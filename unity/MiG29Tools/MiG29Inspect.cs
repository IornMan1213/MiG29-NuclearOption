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

        // Do mounted missiles (and their rails) overlap the aircraft's own colliders?
        public static void RunMissileOverlap()
        {
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/mig29/MiG29.prefab"));
                var plane = go.GetComponentsInChildren<Collider>(true).Where(c => c.enabled).ToList();
                var sb = new StringBuilder();
                foreach (var (hp, mount) in new[] { ("hardpoint_pylon_L1", "mig29_R27R_AKU470"), ("hardpoint_pylon_L2", "mig29_R73_APU73"), ("hardpoint_pylon_L3", "mig29_R73_APU73"), ("hardpoint_pylon_L1", "mig29_R27T_AKU470"), ("hardpoint_pylon_L3", "mig29_R60M_APU60") })
                {
                    var t = go.GetComponentsInChildren<Transform>(true).First(x => x.name == hp);
                    var m = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Blueprinter/Mods/mig29/weapons/{mount}.prefab"), t);
                    m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
                    foreach (var mc in m.GetComponentsInChildren<Collider>(true))
                        foreach (var pc in plane)
                            if (Physics.ComputePenetration(mc, mc.transform.position, mc.transform.rotation, pc, pc.transform.position, pc.transform.rotation, out var dir, out var dist))
                                sb.AppendLine($"{hp}/{mount}/{mc.name} ({mc.GetType().Name}) overlaps {Path(pc.transform, go.transform)} ({pc.GetType().Name}) by {dist:F3} m");
                    sb.AppendLine($"checked {hp}/{mount}: missile at {m.GetComponentsInChildren<Collider>(true).Select(c => c.bounds.center.ToString("F2")).FirstOrDefault()}");
                }
                File.WriteAllText("MiG29Out/missile_overlap.txt", sb.ToString());
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // Which components reference the cockpit-interior objects (to know what the game animates), plus the posed pilot mesh as OBJ (MiG frame).
        public static void RunCockpitRefs()
        {
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/mig29/MiG29.prefab"));
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var sb = new StringBuilder();
                var cockpit = go.GetComponentsInChildren<Transform>(true).First(t => t.name == "cockpit");
                var targets = cockpit.GetComponentsInChildren<Transform>(true).ToDictionary(t => (UnityEngine.Object)t, t => Path(t, go.transform));
                foreach (var t in cockpit.GetComponentsInChildren<Transform>(true))
                {
                    var r = t.GetComponent<Renderer>(); var mf = t.GetComponent<MeshFilter>();
                    sb.AppendLine($"NODE {Path(t, go.transform)} pos={t.position:F3} rot={t.eulerAngles:F1} scale={t.lossyScale:F2} comps={string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name))}"
                        + (r ? $" bounds c={r.bounds.center:F3} s={r.bounds.size:F3} mats={string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "/" + m.shader.name : "null"))}" : "")
                        + (mf && mf.sharedMesh ? $" mesh={mf.sharedMesh.name} v={mf.sharedMesh.vertexCount}" : ""));
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c is Transform || c is Renderer || c is MeshFilter) continue;
                        sb.AppendLine($"  COMPONENT {c.GetType().Name}"); DumpObject(sb, c, "    ");
                    }
                }
                foreach (var c in go.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform) continue;
                    var so = new SerializedObject(c); var it = so.GetIterator();
                    while (it.Next(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                        {
                            var o = it.objectReferenceValue; var key = o is Component oc ? oc.transform : o;
                            if (key is Transform kt && targets.ContainsKey(kt) && !c.transform.IsChildOf(kt))
                                sb.AppendLine($"REF {Path(c.transform, go.transform)}:{c.GetType().Name}.{it.propertyPath} -> {targets[kt]} ({o.GetType().Name})");
                        }
                }
                File.WriteAllText("MiG29Out/cockpit_refs.txt", sb.ToString());
                // posed pilot + KR-67 seat as OBJ in the MiG frame (prefab frame minus ModelOffset)
                var off = new Vector3(0f, -0.44f, -2.655f);
                var obj = new StringBuilder(); int baseIdx = 1;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true).Where(r => r.transform.IsChildOf(cockpit.Find("pilot"))))
                {
                    Mesh m; Matrix4x4 mat;
                    if (r is SkinnedMeshRenderer smr) { m = new Mesh(); smr.BakeMesh(m, true); mat = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one); }
                    else if (r.TryGetComponent<MeshFilter>(out var mf2) && mf2.sharedMesh) { m = mf2.sharedMesh; mat = r.transform.localToWorldMatrix; }
                    else continue;
                    obj.AppendLine($"o {r.name}");
                    foreach (var v in m.vertices) { var w = mat.MultiplyPoint3x4(v) - off; obj.AppendLine($"v {w.x:F4} {w.y:F4} {w.z:F4}"); }
                    var tr = m.triangles;
                    for (int i = 0; i < tr.Length; i += 3) obj.AppendLine($"f {tr[i] + baseIdx} {tr[i + 1] + baseIdx} {tr[i + 2] + baseIdx}");
                    baseIdx += m.vertexCount;
                }
                File.WriteAllText("MiG29Out/pilot_mig.obj", obj.ToString());
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // Mesh + material details of the functional cockpit pieces (tac screen, warning lights) on the stock KR-67 prefab.
        public static void RunCockpitMeshes()
        {
            try
            {
                var sb = new StringBuilder();
                var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/mig29/MiG29.prefab");
                foreach (var n in new[] { "tacScreen", "warningLights", "joystick", "throttle" })
                {
                    var t = go.GetComponentsInChildren<Transform>(true).First(x => x.name == n);
                    var r = t.GetComponent<Renderer>(); var m = t.GetComponent<MeshFilter>().sharedMesh;
                    sb.AppendLine($"== {n} local pos {t.localPosition:F3} mesh {m.name} v={m.vertexCount} subMeshes={m.subMeshCount}");
                    foreach (var mat in r.sharedMaterials)
                    {
                        sb.AppendLine($"  mat {mat.name} shader {mat.shader.name} keywords {string.Join(" ", mat.shaderKeywords)}");
                        var sh = mat.shader;
                        for (int i = 0; i < sh.GetPropertyCount(); i++)
                        {
                            var pn = sh.GetPropertyName(i); var pt = sh.GetPropertyType(i);
                            string v = pt == UnityEngine.Rendering.ShaderPropertyType.Texture ? (mat.GetTexture(pn) ? mat.GetTexture(pn).name + " " + mat.GetTexture(pn).width + "x" + mat.GetTexture(pn).height : "null")
                                : pt == UnityEngine.Rendering.ShaderPropertyType.Color ? mat.GetColor(pn).ToString() : pt == UnityEngine.Rendering.ShaderPropertyType.Vector ? mat.GetVector(pn).ToString() : mat.GetFloat(pn).ToString();
                            sb.AppendLine($"    {pn} ({pt}) = {v}");
                        }
                    }
                    if (n == "tacScreen" || n == "warningLights")
                    {
                        var v = m.vertices; var uv = m.uv; var cols = m.colors;
                        for (int i = 0; i < v.Length; i++) sb.AppendLine($"  v{i} {v[i]:F4} uv {(uv.Length > i ? uv[i].ToString("F3") : "-")} col {(cols.Length > i ? cols[i].ToString() : "-")} uv2 {(m.uv2.Length > i ? m.uv2[i].ToString("F3") : "-")}");
                    }
                }
                foreach (var c in go.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "CockpitWarningLights"))
                    DumpObject(sb, c, "  W ");
                File.WriteAllText("MiG29Out/cockpit_meshes.txt", sb.ToString());
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
