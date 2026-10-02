using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // MiG-29 flight model, tuned offline with tools/fm_sim.py (a re-implementation of the game's AeroJob_Math / Turbojet
    // force model) and exported to MiG29Source/fm_unity.json by tools/fm_export.py.
    public static class MiG29FlightModel
    {
        [Serializable] public class Key { public float t, v; }
        [Serializable] public class Airfoil { public string name; public Key[] CL, CD; }
        [Serializable] public class Engine { public float dryN, abN, maxSpeed, minDensity, fuelConsumptionMin, fuelConsumptionMax, abFuelConsumption; }
        [Serializable] public class Part { public string name; public float wingArea, dragArea; public bool hasCom, hasCenterOfLift; public float[] com, centerOfLift; }
        [Serializable] public class Config { public float fuelKg; public Engine engine; public Key[] altitudeThrust; public Airfoil[] airfoils; public Part[] parts; }

        public static Config Load() => JsonUtility.FromJson<Config>(File.ReadAllText("MiG29Source/fm_unity.json"));

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        static Transform Find(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t == null) throw new Exception("[MiG29] transform not found: " + name);
            return t;
        }

        // Each physics surface's lift normal follows the MiG's own animated surface, so the visible deflection is the aerodynamic one.
        static readonly (string part, string pivot, bool flatten)[] LiftOwners =
        {
            ("elevator_L", "MiG29_stab_L_pivot", true), ("elevator_R", "MiG29_stab_R_pivot", true),
            ("aileron_L", "MiG29_aileron_L_pivot", false), ("aileron_R", "MiG29_aileron_R_pivot", false),
            ("flap_L", "MiG29_flap_L_pivot", false), ("flap_R", "MiG29_flap_R_pivot", false),
            ("tail_L", "MiG29_rudder_L_pivot", false), ("tail_R", "MiG29_rudder_R_pivot", false),
        };

        public static void ApplyToPrefab(Transform root, Config cfg, Func<string, Vector3, Mesh> cubeAt)
        {
            var aeroType = T("AeroPart");
            foreach (var p in cfg.parts)
            {
                var part = Find(root, p.name).GetComponent(aeroType);
                var so = new SerializedObject(part);
                if (p.wingArea >= 0) so.FindProperty("wingArea").floatValue = p.wingArea;
                if (p.dragArea >= 0) so.FindProperty("dragArea").floatValue = p.dragArea;
                if (p.hasCenterOfLift) so.FindProperty("centerOfLift").vector3Value = new Vector3(p.centerOfLift[0], p.centerOfLift[1], p.centerOfLift[2]);
                so.ApplyModifiedPropertiesWithoutUndo();
                // (centre-of-mass overrides are gone: lift points are placed with centerOfLift instead; hitboxes stay the KR-67's)
            }

            foreach (var (partName, pivotName, flatten) in LiftOwners)
            {
                var part = Find(root, partName).GetComponent(aeroType);
                var so = new SerializedObject(part);
                var ln = so.FindProperty("liftNormal").objectReferenceValue as Transform;
                var pivot = Find(root, pivotName);
                if (ln == null || ln == part.transform)
                {
                    ln = new GameObject(partName + "_liftNormal_MiG29").transform;
                    ln.SetPositionAndRotation(pivot.position, part.transform.rotation);
                }
                if (flatten) ln.rotation = Quaternion.identity; // MiG stabilators are flat (KR-67 tails were canted ~40 deg)
                ln.SetParent(pivot, true);
                ln.position = pivot.position;
                so.FindProperty("liftNormal").objectReferenceValue = ln;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // RD-33 engines
            foreach (var tj in root.GetComponentsInChildren(T("Turbojet"), true))
            {
                var so = new SerializedObject(tj);
                so.FindProperty("maxThrust").floatValue = cfg.engine.dryN;
                so.FindProperty("maxSpeed").floatValue = cfg.engine.maxSpeed;
                so.FindProperty("minDensity").floatValue = cfg.engine.minDensity;
                so.FindProperty("fuelConsumptionMin").floatValue = cfg.engine.fuelConsumptionMin;
                so.FindProperty("fuelConsumptionMax").floatValue = cfg.engine.fuelConsumptionMax;
                so.FindProperty("thrustVectoring").vector3Value = Vector3.zero; // no TVC on the standard MiG-29
                so.FindProperty("altitudeThrust").animationCurveValue = ToCurve(cfg.altitudeThrust, smooth: true);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var nz in root.GetComponentsInChildren(T("JetNozzle"), true))
            {
                var so = new SerializedObject(nz);
                var abs = so.FindProperty("afterburners");
                for (int i = 0; i < abs.arraySize; i++)
                {
                    abs.GetArrayElementAtIndex(i).FindPropertyRelative("thrust").floatValue = cfg.engine.abN;
                    abs.GetArrayElementAtIndex(i).FindPropertyRelative("fuelConsumption").floatValue = cfg.engine.abFuelConsumption;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Debug.Log("[MiG29] flight model applied to prefab");
        }

        public static void ApplyToParameters(SerializedObject pso, Config cfg)
        {
            var afs = pso.FindProperty("airfoils");
            for (int i = 0; i < cfg.airfoils.Length && i < afs.arraySize; i++)
            {
                var a = afs.GetArrayElementAtIndex(i);
                a.FindPropertyRelative("name").stringValue = cfg.airfoils[i].name;
                a.FindPropertyRelative("liftCoef").animationCurveValue = ToCurve(cfg.airfoils[i].CL, odd: true);
                a.FindPropertyRelative("dragCoef").animationCurveValue = ToCurve(cfg.airfoils[i].CD, even: true);
            }
            pso.FindProperty("cornerSpeed").floatValue = 180f;   // ~650 km/h
            pso.FindProperty("aircraftGLimit").floatValue = 9f;
            pso.FindProperty("maxSpeed").floatValue = 600f;
        }

        // Airfoil curves are authored for alpha >= 0 and mirrored over [-pi, pi]; linear tangents (the simulator interpolates linearly).
        static AnimationCurve ToCurve(Key[] keys, bool odd = false, bool even = false, bool smooth = false)
        {
            var list = keys.Select(k => new Keyframe(k.t, k.v)).ToList();
            if (odd || even)
                foreach (var k in keys.Where(k => k.t > 0).Reverse())
                    list.Insert(0, new Keyframe(-k.t, odd ? -k.v : k.v));
            var c = new AnimationCurve(list.ToArray());
            for (int i = 0; i < c.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Linear);
            }
            return c;
        }
    }
}
