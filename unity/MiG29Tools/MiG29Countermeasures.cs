using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // MiG-29 9.12 countermeasures: two BVP-30-26M dispensers in the tail booms, just inboard of the fins, each with 30 26 mm
    // cartridges fired upward. One dispenser load is PPI-26 infrared flares, the other PPR-26 chaff (60 cartridges in all).
    //   IR Flares   <- the KR-67's FlareEjector, moved from its engine tops to the MiG's dispensers, 30 flares
    //   Radar Chaff <- the game's own ChaffEjector (no stock aircraft carries one) with a chaff cloud built here: RadarChaff + particles.
    //                  Chaff decoys active and semi-active radar missiles; it works best when the missile sees you side-on (beaming)
    //                  and when it is close, exactly how the game's ARH / SARH seekers weigh a chaff burst.
    //   Radar ECM   <- the KR-67's jammer, kept (the 9.13's internal Gardeniya)
    // Names sort IR Flares < Radar Chaff < Radar ECM: the game pops "flares" from the first station, so flares must stay first.
    public static class MiG29Countermeasures
    {
        const string Dir = "Assets/Blueprinter/Mods/mig29/weapons";
        const string Mat = "Assets/Blueprinter/_donotship/Material/";

        public const int Flares = 30, Chaff = 30;

        // MiG frame: top of the tail booms at x 1.5 (ray cast, blender/analysis/stores_pos.py): y 0.11 at z -2.0, 0.07 at z -2.5
        static readonly Vector3 FlarePoint = new Vector3(1.50f, 0.15f, -1.9f);
        static readonly Vector3 ChaffPoint = new Vector3(1.50f, 0.10f, -2.4f);
        // cartridges leave upward, a little outboard and aft
        static Vector3 EjectDir(float side) => new Vector3(side * 0.27f, 1f, -0.17f).normalized;

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        static Transform Find(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t == null) throw new Exception("[MiG29] not found: " + name);
            return t;
        }

        public static void Setup(GameObject go, Vector3 modelOffset)
        {
            var root = go.transform;
            var aircraft = go.GetComponent(T("Aircraft"));
            var flares = go.GetComponentInChildren(T("FlareEjector"), true);
            var jammer = go.GetComponentInChildren(T("RadarJammer"), true);

            // flares: the MiG's dispensers, 30 rounds
            var fso = new SerializedObject(flares);
            fso.FindProperty("ammo").intValue = Flares;
            var pts = fso.FindProperty("ejectionPoints");
            for (int i = 0; i < pts.arraySize; i++)
            {
                var t = (Transform)pts.GetArrayElementAtIndex(i).FindPropertyRelative("transform").objectReferenceValue;
                float side = t.name.EndsWith("_L") ? -1f : 1f;
                t.SetPositionAndRotation(new Vector3(side * FlarePoint.x, FlarePoint.y, FlarePoint.z) + modelOffset, Quaternion.LookRotation(EjectDir(side), Vector3.forward));
            }
            fso.FindProperty("flareDoors").arraySize = 0;   // the KR-67's (hidden) flare doors
            fso.ApplyModifiedPropertiesWithoutUndo();

            // chaff: a new ejector on the aircraft root, ejection points on the same engine parts as the flares
            var chaff = go.AddComponent(T("ChaffEjector"));
            var cso = new SerializedObject(chaff);
            cso.FindProperty("displayName").stringValue = "Radar Chaff";
            cso.FindProperty("displayImage").objectReferenceValue = new SerializedObject(jammer).FindProperty("displayImage").objectReferenceValue;
            cso.FindProperty("chargeable").boolValue = false;
            cso.FindProperty("ammo").intValue = Chaff;
            cso.FindProperty("aircraft").objectReferenceValue = aircraft;
            cso.FindProperty("chaffPrefab").objectReferenceValue = BuildChaffPrefab();
            cso.FindProperty("ejectionVelocity").floatValue = 25f;
            cso.FindProperty("ejectionVelocityVariance").floatValue = 0.3f;
            cso.FindProperty("ejectionInterval").floatValue = 0.3f;
            cso.FindProperty("ejectionGrouping").intValue = 2;     // one cartridge from each dispenser
            cso.FindProperty("ejectionSound").objectReferenceValue = fso.FindProperty("ejectionSound").objectReferenceValue;
            cso.FindProperty("ejectionVolume").floatValue = 0.8f;
            cso.FindProperty("flareDoors").arraySize = 0;
            var cpts = cso.FindProperty("ejectionPoints");
            cpts.arraySize = 2;
            var upType = T("UnitPart");
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var part = Find(root, side < 0 ? "engine_L" : "engine_R");
                var t = new GameObject(side < 0 ? "chaffEjector_L" : "chaffEjector_R").transform;
                t.SetParent(part, false);
                t.SetPositionAndRotation(new Vector3(side * ChaffPoint.x, ChaffPoint.y, ChaffPoint.z) + modelOffset, Quaternion.LookRotation(EjectDir(side), Vector3.forward));
                var e = cpts.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("part").objectReferenceValue = part.GetComponent(upType);
                e.FindPropertyRelative("transform").objectReferenceValue = t;
            }
            cso.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[MiG29] countermeasures: {Flares} flares + {Chaff} chaff (BVP-30-26M), ECM kept");
        }

        // One chaff cartridge: a RadarChaff (slows down fast, then drifts and falls for 6 s) carrying a cloud of glinting foil and a
        // faint grey bloom. Particles live in the chaff object's space so they move with it (and with the game's floating origin).
        static GameObject BuildChaffPrefab()
        {
            var go = new GameObject("mig29_chaff");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ConfigureGlints(ps);

            var haze = new GameObject("bloom");
            haze.transform.SetParent(go.transform, false);
            ConfigureBloom(haze.AddComponent<ParticleSystem>());

            var rc = go.AddComponent(T("RadarChaff"));
            var so = new SerializedObject(rc);
            so.FindProperty("chaffTime").floatValue = 6f;
            so.FindProperty("drag").floatValue = 0.03f;     // ~250 m/s -> drifting in well under a second; stable at low frame rates
            so.FindProperty("chaffParticles").objectReferenceValue = ps;
            so.FindProperty("emitFrequency").floatValue = 0f;
            so.FindProperty("minSpeed").floatValue = 20f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var path = Dir + "/mig29_chaff.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static void ConfigureGlints(ParticleSystem ps)
        {
            var main = ps.main;
            // looping, one burst: the system keeps "playing" until RadarChaff stops it, which is what destroys the object
            main.loop = true;
            main.duration = 30f;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.87f, 0.9f, 0.9f), new Color(1f, 1f, 1f, 1f));
            main.gravityModifier = 0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 160;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70), new ParticleSystem.Burst(0.08f, 40) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.4f;
            var vel = ps.velocityOverLifetime;   // the foil spreads, then mostly hangs
            vel.enabled = false;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1.2f;
            limit.dampen = 0.25f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.8f, 0.82f, 0.85f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = ps.noise;                // foil strips flutter and twinkle
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 1.5f;
            noise.scrollSpeed = 1f;
            noise.sizeAmount = 0.8f;
            noise.separateAxes = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat + "sparks_PLACEHOLDER.mat");
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static void ConfigureBloom(ParticleSystem ps)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 1f;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startColor = new Color(0.78f, 0.8f, 0.82f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 12;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.5f;
            var size = ps.sizeOverLifetime;      // the cloud blooms to ~10 m
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.25f, 3.5f), new Keyframe(1f, 5f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Mat + "smoke_midground_PLACEHOLDER.mat");
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }
}
