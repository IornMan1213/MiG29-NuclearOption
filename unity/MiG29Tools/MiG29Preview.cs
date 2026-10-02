using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // Batchmode (with graphics) check renders of the built MiG-29 prefab, in the poses the game animates:
    //  flight_* : gear folded (foldDegrees + hingeFoldMotion), gear doors closed, canopy closed
    //  ground_* : gear down, gear doors open, canopy open
    //  parts_*  : every MiG piece tinted by the damage part it belongs to
    // KR-67 leftovers that stay visible are drawn orange; MiG pieces use the MiG base texture.
    public static class MiG29Preview
    {
        const string PrefabPath = "Assets/Blueprinter/Mods/mig29/MiG29.prefab";
        const string BaseTex = "Assets/Blueprinter/Mods/mig29/textures/mig29_basecolor.png";

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        public static void RenderAll()
        {
            try
            {
                Directory.CreateDirectory("MiG29Out");
                Render("flight", tintParts: false, gearUp: true, open: false);
                Render("ground", tintParts: false, gearUp: false, open: true);
                Render("parts", tintParts: true, gearUp: true, open: false);
                Render("armed", tintParts: false, gearUp: true, open: false, armed: true);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void Render(string prefix, bool tintParts, bool gearUp, bool open, bool armed = false)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            foreach (var lg in go.GetComponentsInChildren(T("LandingGear"), true))
            {
                var so = new SerializedObject(lg);
                var hinge = so.FindProperty("gearHinge").objectReferenceValue as Transform;
                if (hinge != null && gearUp)
                {
                    hinge.localEulerAngles += new Vector3(so.FindProperty("foldDegrees").floatValue, 0, 0);
                    hinge.localPosition += so.FindProperty("hingeFoldMotion").vector3Value;
                }
                var doors = so.FindProperty("gearDoors");
                for (int i = 0; i < doors.arraySize; i++)
                {
                    var d = doors.GetArrayElementAtIndex(i);
                    var t = d.FindPropertyRelative("transform").objectReferenceValue as Transform;
                    if (t != null) t.localEulerAngles = d.FindPropertyRelative(open ? "openAngle" : "closedAngle").vector3Value;
                }
            }
            if (open)
            {
                var cso = new SerializedObject(go.GetComponentInChildren(T("Canopy"), true));
                var hinges = cso.FindProperty("canopyHinges");
                for (int i = 0; i < hinges.arraySize; i++)
                {
                    var h = hinges.GetArrayElementAtIndex(i);
                    var t = h.FindPropertyRelative("transform").objectReferenceValue as Transform;
                    if (t != null) t.localEulerAngles = Vector3.right * h.FindPropertyRelative("hingeAngle").floatValue;
                }
            }

            if (armed)
            {
                // hang the mounts the way WeaponManager does: mount prefab at the hardpoint transform
                var r27 = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/mig29/weapons/mig29_R27R_AKU470.prefab");
                var r73 = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Blueprinter/Mods/mig29/weapons/mig29_R73_APU73.prefab");
                foreach (var (hp, prefab) in new[] { ("hardpoint_pylon_L1", r27), ("hardpoint_pylon_R1", r27), ("hardpoint_pylon_L2", r73), ("hardpoint_pylon_R2", r73), ("hardpoint_pylon_L3", r73), ("hardpoint_pylon_R3", r73) })
                {
                    var t = go.GetComponentsInChildren<Transform>(true).First(x => x.name == hp);
                    var m = (GameObject)UnityEngine.Object.Instantiate(prefab, t);
                    m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
                    foreach (var r in m.GetComponentsInChildren<Renderer>(true))
                    {
                        Debug.Log($"[MiG29] mount {hp}/{r.name}: active={r.gameObject.activeInHierarchy} enabled={r.enabled} bounds c={r.bounds.center:F3} s={r.bounds.size:F3} hp={t.position:F3}");
                        r.gameObject.name = "MiG29_weapon_" + r.gameObject.name;
                    }
                }
            }
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseTex);
            var upType = T("UnitPart");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null)) continue;
                bool mig = r.name.StartsWith("MiG29_");
                bool glass = r.name.Contains("canopy_glass") || r.name.Contains("windscreen") || r.name == "MiG29_canopy";
                Material m;
                if (tintParts && mig)
                {
                    var part = r.GetComponentInParent(upType);
                    var h = Mathf.Abs((part ? part.name : r.name).GetHashCode()) % 1000 / 1000f;
                    m = new Material(unlit); m.SetColor("_BaseColor", Color.HSVToRGB(h, 0.75f, 0.95f));
                }
                else if (r.name.StartsWith("MiG29_weapon_"))
                {
                    m = new Material(unlit); m.SetColor("_BaseColor", r.name.Contains("pylon") ? new Color(1f, 0.85f, 0.2f) : new Color(0.35f, 0.75f, 1f)); // rails yellow, missiles blue (unlit)
                }
                else if (mig && glass)
                {
                    m = new Material(lit); m.SetColor("_BaseColor", new Color(0.25f, 0.35f, 0.45f));
                }
                else if (mig)
                {
                    m = new Material(lit); m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0.35f);
                }
                else
                {
                    m = new Material(lit); m.SetColor("_BaseColor", new Color(1f, 0.35f, 0.1f));
                }
                r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();
                r.enabled = true;
            }

            var lightGo = new GameObject("light"); var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.4f; lightGo.transform.rotation = Quaternion.Euler(25, 70, 0);
            RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.52f);
            var fillGo = new GameObject("fill"); var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 1.0f; fillGo.transform.rotation = Quaternion.Euler(-60, 20, 0); // from below
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;

            void Shot(string name, Vector3 pos, Vector3 look, Vector3 up, bool ortho = true, float size = 6.5f)
            {
                cam.orthographic = ortho; cam.orthographicSize = size; cam.fieldOfView = 30;
                camGo.transform.position = pos; camGo.transform.LookAt(look, up);
                cam.Render();
                RenderTexture.active = rt;
                var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); img.Apply();
                File.WriteAllBytes($"MiG29Out/{prefix}_{name}.png", img.EncodeToPNG());
                RenderTexture.active = null;
            }
            var c = new Vector3(0, -0.4f, 0.5f);
            Shot("below", c + Vector3.down * 30, c, Vector3.forward);
            Shot("side", c + Vector3.left * 30, c, Vector3.up);
            Shot("above", c + Vector3.up * 30, c, Vector3.forward);
            Shot("front", c + Vector3.forward * 30 + Vector3.down * 2, c, Vector3.up);
            Shot("quarter", c + new Vector3(-22, 9, 24), c, Vector3.up, ortho: false);
            Shot("nosegear", new Vector3(-5, -1.6f, 6), new Vector3(0, -1.3f, 1.8f), Vector3.up, ortho: false);
            if (armed)
            {
                Shot("wing_front", new Vector3(-3.2f, -0.9f, 6f), new Vector3(-3.2f, -0.9f, -2f), Vector3.up, ortho: true, size: 1.4f);
                Shot("wing_side", new Vector3(-12f, -1.0f, -1.8f), new Vector3(-3f, -1.0f, -1.8f), Vector3.up, ortho: true, size: 1.6f);
                Shot("armed_quarter", c + new Vector3(-16, -6, 18), c, Vector3.up, ortho: false);
            }
            Shot("bays", new Vector3(-6, -7, 9), new Vector3(0, -0.8f, 1.5f), Vector3.up, ortho: false);
            Shot("cockpit", new Vector3(-4, 2.2f, 6.5f), new Vector3(0, 0.4f, 3.6f), Vector3.up, ortho: false);
            Debug.Log($"[MiG29] preview {prefix} rendered");
            UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(lightGo); UnityEngine.Object.DestroyImmediate(fillGo);
        }
    }
}
