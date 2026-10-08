using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // MiG-29 speed brakes: two panels on the flat tail section between the engines, ahead of the brake-chute housing; the upper one
    // opens up, the lower one down, both hinged at their front edge. tools/airbrake_cut.py cuts them out of the body skin (with a
    // shallow well floor under each); here they hang on hinges driven by the game's own Airbrake component, as on the stock fighters:
    // it opens them with the throttle at idle (the HUD's AIRBRAKE detent), adds their drag and plays the stock airbrake sound.
    public static class MiG29Airbrakes
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string SourceDir = "MiG29Source";
        const string StockFighter = "Assets/Blueprinter/_donotship/GameObject/Fighter1_PLACEHOLDER.prefab";

        // drag = DragAmount * air density * v^2 at full opening (the stock fighter's is 1): two ~0.5 m2 panels
        const float DragAmount = 0.6f, MaxAngle = 50f, OpenSpeed = 1f;

        [Serializable] class Piece { public float[] vertices, normals, uvs; public int[] triangles; }
        [Serializable] class Pair { public Piece upper, lower; }
        [Serializable] class Hinges { public float[] upper, lower; }
        [Serializable] class Dump { public int[] remove; public Piece skin; public Pair panels, wells; public Hinges hinges; }

        static Dump dump;
        static Dump D => dump ?? (dump = JsonUtility.FromJson<Dump>(File.ReadAllText(Path.Combine(SourceDir, "airbrakes.json"))));

        // body triangles (original order) the panels were cut from, and the skin pieces around them
        public static IEnumerable<int> RemovedTriangles => D.remove;
        public static (float[] v, float[] n, float[] uv, int[] t) SkinPieces => (D.skin.vertices, D.skin.normals, D.skin.uvs, D.skin.triangles);

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        static Mesh MakeMesh(string name, Piece p, Func<Vector3, Vector3> toLocal, Func<Vector3, Vector3> dirToLocal)
        {
            int n = p.vertices.Length / 3;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = toLocal(new Vector3(p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]));
                nr[i] = dirToLocal(new Vector3(p.normals[i * 3], p.normals[i * 3 + 1], p.normals[i * 3 + 2]));
                uv[i] = new Vector2(p.uvs[i * 2], p.uvs[i * 2 + 1]);
            }
            var m = new Mesh { name = name, vertices = v, normals = nr, uv = uv, triangles = p.triangles };
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        // the damage part whose skin is nearest a point (the panels and wells ride on the part that carries the tail section)
        static Transform PartAt(Transform root, Vector3 world)
        {
            Transform best = null; float bestD = float.MaxValue;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.name.StartsWith("MiG29_skin_") || mf.sharedMesh == null) continue;
                var tr = mf.transform;
                foreach (var p in mf.sharedMesh.vertices)
                {
                    float d = (tr.TransformPoint(p) - world).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = tr.parent; }
                }
            }
            return best;
        }

        public static void Build(GameObject go, Vector3 modelOffset, Material skin, Func<UnityEngine.Object, string, UnityEngine.Object> save,
            Action<Transform, Renderer> addDamage)
        {
            var root = go.transform;
            var wellMat = AssetDatabase.LoadAssetAtPath<Material>(ModDir + "/weapons/MiG29_missiles.mat");   // flat dark grey atlas cell
            var hinges = new List<Transform>();
            Transform part = null;
            foreach (var (name, piece, well, h) in new[] { ("upper", D.panels.upper, D.wells.upper, D.hinges.upper), ("lower", D.panels.lower, D.wells.lower, D.hinges.lower) })
            {
                var hingeWorld = new Vector3(h[0], h[1], h[2]) + modelOffset;
                part = PartAt(root, hingeWorld);
                // closed = identity; the Airbrake turns each hinge about its own +x by up to MaxAngle, which lifts the panel's trailing
                // edge: the lower hinge is rolled 180 deg so the same turn swings its panel down
                var hinge = new GameObject("MiG29_airbrake_" + name).transform;
                hinge.SetParent(part, false);
                hinge.SetPositionAndRotation(hingeWorld, name == "upper" ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f));
                var pm = (Mesh)save(MakeMesh("MiG29_airbrake_" + name, piece, p => hinge.InverseTransformPoint(p + modelOffset), hinge.InverseTransformDirection),
                    $"{ModDir}/meshes/MiG29_airbrake_{name}.asset");
                var panel = new GameObject("MiG29_airbrake_" + name + "_panel");
                panel.transform.SetParent(hinge, false);
                panel.AddComponent<MeshFilter>().sharedMesh = pm;
                var pr = panel.AddComponent<MeshRenderer>(); pr.sharedMaterial = skin;
                addDamage(part, pr);
                hinges.Add(hinge);

                var wm = (Mesh)save(MakeMesh("MiG29_airbrake_well_" + name, well, p => part.InverseTransformPoint(p + modelOffset), part.InverseTransformDirection),
                    $"{ModDir}/meshes/MiG29_airbrake_well_{name}.asset");
                var w = new GameObject("MiG29_airbrake_well_" + name);
                w.transform.SetParent(part, false);
                w.AddComponent<MeshFilter>().sharedMesh = wm;
                w.AddComponent<MeshRenderer>().sharedMaterial = wellMat;
            }

            // the game's Airbrake, set up like the stock fighter's (its sound copied over)
            var abType = T("Airbrake");
            var holder = new GameObject("MiG29_airbrakes");
            holder.transform.SetParent(part, false);
            var ab = holder.AddComponent(abType);
            var so = new SerializedObject(ab);
            var tr = so.FindProperty("transforms"); tr.arraySize = hinges.Count;
            for (int i = 0; i < hinges.Count; i++) tr.GetArrayElementAtIndex(i).objectReferenceValue = hinges[i];
            so.FindProperty("dragAmount").floatValue = DragAmount;
            so.FindProperty("maxAngle").floatValue = MaxAngle;
            so.FindProperty("openSpeed").floatValue = OpenSpeed;
            so.FindProperty("part").objectReferenceValue = part.GetComponent(T("UnitPart")) ?? part.GetComponentInParent(T("UnitPart"));
            so.FindProperty("aircraft").objectReferenceValue = go.GetComponent(T("Aircraft"));
            so.FindProperty("constraints").arraySize = 0;
            so.FindProperty("volumeMultiplier").floatValue = 0.7f;
            var stock = AssetDatabase.LoadAssetAtPath<GameObject>(StockFighter);
            var stockAb = stock != null ? stock.GetComponentInChildren(abType, true) : null;
            var stockSound = stockAb != null ? new SerializedObject(stockAb).FindProperty("airbrakeSound").objectReferenceValue as AudioSource : null;
            if (stockSound != null)
            {
                var src = holder.AddComponent<AudioSource>();
                EditorUtility.CopySerialized(stockSound, src);
                so.FindProperty("airbrakeSound").objectReferenceValue = src;
            }
            else Debug.LogWarning("[MiG29] airbrakes: stock airbrake sound not found, silent");
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[MiG29] airbrakes on part {part.name}: {hinges.Count} panels, drag {DragAmount}, {MaxAngle} deg, sound {(stockSound != null)}");
        }
    }
}
