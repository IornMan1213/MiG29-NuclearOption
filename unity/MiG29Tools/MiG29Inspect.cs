using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // Dev helper: dumps weapon-related setup of a prefab (hardpoint sets, guns, mounts) to MiG29Out/inspect.txt.
    public static class MiG29Inspect
    {
        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        static string Path(Transform t, Transform root) => t == root ? root.name : Path(t.parent, root) + "/" + t.name;

        static void DumpObject(StringBuilder sb, UnityEngine.Object o, string indent, int depth = 0)
        {
            var so = new SerializedObject(o);
            var it = so.GetIterator();
            bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = it.propertyType == SerializedPropertyType.Generic && it.depth < 5 && !it.isArray || (it.isArray && it.arraySize < 40 && it.depth < 5);
                if (it.name == "m_Script") continue;
                string v;
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Float: v = it.floatValue.ToString("G5"); break;
                    case SerializedPropertyType.Integer: v = it.intValue.ToString(); break;
                    case SerializedPropertyType.Boolean: v = it.boolValue.ToString(); break;
                    case SerializedPropertyType.String: v = it.stringValue; break;
                    case SerializedPropertyType.Enum: v = it.enumValueIndex >= 0 && it.enumValueIndex < it.enumDisplayNames.Length ? it.enumDisplayNames[it.enumValueIndex] : it.intValue.ToString(); break;
                    case SerializedPropertyType.Vector3: v = it.vector3Value.ToString("F3"); break;
                    case SerializedPropertyType.ObjectReference:
                        var r = it.objectReferenceValue; v = r == null ? "null" : $"{r.name} ({r.GetType().Name}) {AssetDatabase.GetAssetPath(r)}"; break;
                    default: v = it.isArray ? $"[{it.arraySize}]" : ""; break;
                }
                sb.AppendLine($"{indent}{new string(' ', it.depth * 2)}{it.propertyPath} = {v}");
            }
        }

        public static void Dump(string prefabPath, string outFile)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var sb = new StringBuilder();
            sb.AppendLine("PREFAB " + prefabPath);
            foreach (var typeName in new[] { "WeaponManager", "Gun", "Hardpoint", "ControlsFilter" })
            {
                var t = T(typeName);
                if (t == null || !typeof(Component).IsAssignableFrom(t)) continue;
                foreach (var c in go.GetComponentsInChildren(t, true))
                {
                    sb.AppendLine($"== {typeName} on {Path(c.transform, go.transform)} pos={c.transform.position:F3}");
                    DumpObject(sb, c, "  ");
                }
            }
            Directory.CreateDirectory("MiG29Out");
            File.WriteAllText(outFile, sb.ToString());
            Debug.Log("[MiG29] inspect written " + outFile);
        }

        public static void RunVisible()
        {
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/mig29/MiG29.prefab"));
                var sb = new StringBuilder();
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.StartsWith("MiG29_")) continue;
                    if (!(r is SkinnedMeshRenderer) && (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null)) continue;
                    if (r is SkinnedMeshRenderer smr && smr.sharedMesh == null) continue;
                    sb.AppendLine($"{Path(r.transform, go.transform)} c={r.bounds.center:F2} s={r.bounds.size:F2} {r.GetType().Name} active={r.gameObject.activeInHierarchy}");
                }
                foreach (var l in go.GetComponentsInChildren<Light>(true)) sb.AppendLine($"LIGHT {Path(l.transform, go.transform)} pos={l.transform.position:F2} type={l.type} range={l.range:F1}");
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) sb.AppendLine($"PS {Path(ps.transform, go.transform)} pos={ps.transform.position:F2}");
                foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => { var n = t.name.ToLower(); return n.Contains("light") || n.Contains("strobe") || n.Contains("nav") || n.Contains("beacon") || n.Contains("lamp") || n.Contains("vortex") || n.Contains("contrail") || n.Contains("trail"); }))
                    sb.AppendLine($"NAMED {Path(t, go.transform)} pos={t.position:F2} comps={string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name))}");
                var vp = new SerializedObject(go.GetComponent(T("Unit"))).FindProperty("cockpitViewPoint").objectReferenceValue as Transform;
                sb.AppendLine($"cockpitViewPoint: {(vp ? Path(vp, go.transform) + " " + vp.position.ToString("F3") : "null")}");
                foreach (var n in new[] { "pilot", "EjectionSeat", "canopyHinge" })
                {
                    var t = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                    if (t) sb.AppendLine($"T {Path(t, go.transform)} pos={t.position:F3}");
                }
                File.WriteAllText("MiG29Out/visible_kr67.txt", sb.ToString());
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void RunParams()
        {
            try
            {
                var sb = new StringBuilder();
                var a = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Blueprinter/Mods/mig29/MiG29_parameters.asset");
                var so = new SerializedObject(a);
                foreach (var name in new[] { "loadouts", "StandardLoadouts" })
                {
                    var arr = so.FindProperty(name);
                    sb.AppendLine($"{name} [{arr.arraySize}]");
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var e = arr.GetArrayElementAtIndex(i);
                        var it = e.Copy(); var end = e.GetEndProperty();
                        bool enter = true;
                        while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
                        {
                            enter = true;
                            string v = it.propertyType == SerializedPropertyType.ObjectReference ? (it.objectReferenceValue ? it.objectReferenceValue.name : "null")
                                : it.propertyType == SerializedPropertyType.String ? it.stringValue
                                : it.propertyType == SerializedPropertyType.Float ? it.floatValue.ToString()
                                : it.propertyType == SerializedPropertyType.Integer ? it.intValue.ToString()
                                : it.propertyType == SerializedPropertyType.Boolean ? it.boolValue.ToString() : "";
                            sb.AppendLine($"  {it.propertyPath} = {v}");
                        }
                    }
                }
                File.WriteAllText("MiG29Out/inspect_params.txt", sb.ToString());
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void Run()
        {
            try
            {
                Dump("Assets/Blueprinter/Mods/mig29/MiG29.prefab", "MiG29Out/inspect_mig29.txt");
                var sb = new StringBuilder();
                foreach (var path in new[] {
                    "Assets/Blueprinter/_donotship/GameObject/gun_27mm_internal_PLACEHOLDER.prefab",
                    "Assets/Blueprinter/_donotship/GameObject/gun_30mm_rotary_pod_PLACEHOLDER.prefab",
                    "Assets/Blueprinter/_donotship/MonoBehaviour/gun_27mm_internal_PLACEHOLDER.asset",
                    "Assets/Blueprinter/_donotship/MonoBehaviour/Gun27mm_Autocannon_PLACEHOLDER.asset",
                    "Assets/Blueprinter/_donotship/MonoBehaviour/Gun30mm_Rotary_PLACEHOLDER.asset" })
                {
                    sb.AppendLine("#### " + path);
                    if (path.EndsWith(".prefab"))
                    {
                        var g = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        foreach (var t in g.GetComponentsInChildren<Transform>(true)) sb.AppendLine($"  T {Path(t, g.transform)} lp={t.localPosition:F3} ls={t.localScale:F3}");
                        foreach (var c in g.GetComponentsInChildren<MonoBehaviour>(true)) { sb.AppendLine($"  == {c.GetType().Name} on {Path(c.transform, g.transform)}"); DumpObject(sb, c, "    "); }
                    }
                    else foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o != null) { sb.AppendLine($"  == {o.GetType().Name} {o.name}"); DumpObject(sb, o, "    "); }
                }
                File.WriteAllText("MiG29Out/inspect_gun.txt", sb.ToString());
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
