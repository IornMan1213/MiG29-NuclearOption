using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // Dumps the built MiG-29 prefab's flight-relevant data (per aero part: mass, physics centre of mass as PhysX computes
    // it from the part's collider, lift settings, MiG skin planform/side area) to MiG29Out/aero.json for tools/fm_sim.py.
    public static class MiG29AeroDump
    {
        const string PrefabPath = "Assets/Blueprinter/Mods/mig29/MiG29.prefab";

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);
        static string V(Vector3 v) => $"[{v.x:F4},{v.y:F4},{v.z:F4}]";

        public static void Dump()
        {
            try { DumpImpl(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void DumpImpl()
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var sb = new StringBuilder("{\"parts\":[\n");
            bool first = true;
            foreach (var part in go.GetComponentsInChildren(T("AeroPart"), true))
            {
                var so = new SerializedObject(part);
                var t = part.transform;
                // PhysX centre of mass from the part's own collider (what CreateRB gets in complex physics)
                var probe = new GameObject("probe");
                probe.transform.SetPositionAndRotation(t.position, t.rotation);
                var mc0 = part.GetComponent<MeshCollider>();
                Vector3 com = t.position;
                if (mc0 != null && mc0.sharedMesh != null)
                {
                    var mc = probe.AddComponent<MeshCollider>(); mc.sharedMesh = mc0.sharedMesh; mc.convex = true;
                    var rb = probe.AddComponent<Rigidbody>();
                    Physics.SyncTransforms();
                    rb.ResetCenterOfMass();
                    com = rb.worldCenterOfMass;
                }
                UnityEngine.Object.DestroyImmediate(probe);

                var ln = so.FindProperty("liftNormal").objectReferenceValue as Transform ?? t;
                // MiG skin pieces of this part: planform (top) and side projected areas
                float top = 0, side = 0;
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name.StartsWith("MiG29_skin_") && m.transform.parent == t || m.name.StartsWith("MiG29_") && m.GetComponentInParent(T("AeroPart")) == part))
                {
                    var mesh = mf.sharedMesh; if (mesh == null) continue;
                    var vs = mesh.vertices.Select(v => mf.transform.TransformPoint(v)).ToArray(); var tr = mesh.triangles;
                    for (int i = 0; i < tr.Length; i += 3)
                    {
                        var c = Vector3.Cross(vs[tr[i + 1]] - vs[tr[i]], vs[tr[i + 2]] - vs[tr[i]]) * 0.5f;
                        top += Mathf.Abs(c.y); side += Mathf.Abs(c.x);
                    }
                }
                if (!first) sb.Append(",\n"); first = false;
                sb.Append($"{{\"name\":\"{part.name}\",\"path\":\"{AnimationUtility.CalculateTransformPath(t, go.transform)}\",\"mass\":{so.FindProperty("mass").floatValue},");
                sb.Append($"\"com\":{V(com)},\"pos\":{V(t.position)},\"wingArea\":{so.FindProperty("wingArea").floatValue},\"dragArea\":{so.FindProperty("dragArea").floatValue},");
                sb.Append($"\"airfoil\":{so.FindProperty("airfoil").intValue},\"wingEffectiveness\":{(so.FindProperty("wingEffectiveness")?.floatValue ?? 1f)},\"centerOfLift\":{V(so.FindProperty("centerOfLift").vector3Value)},");
                sb.Append($"\"lnPos\":{V(ln.position)},\"lnRight\":{V(ln.right)},\"lnUp\":{V(ln.up)},\"lnFwd\":{V(ln.forward)},\"topArea\":{(top / 2f).ToString("F3")},\"sideArea\":{(side / 2f).ToString("F3")}" + "}");
            }
            sb.Append("\n],\"thrust\":[");
            first = true;
            foreach (var n in go.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith("thrustTransform")))
            {
                if (!first) sb.Append(","); first = false;
                sb.Append($"{{\"pos\":{V(n.position)},\"fwd\":{V(n.forward)}}}");
            }
            sb.Append("]}\n");
            Directory.CreateDirectory("MiG29Out");
            File.WriteAllText("MiG29Out/aero.json", sb.ToString());
            Debug.Log("[MiG29] aero dump written");
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
