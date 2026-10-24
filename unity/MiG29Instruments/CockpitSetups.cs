using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using MiG29Tools;
using UnityEngine;

namespace MiG29Instruments
{
    // Cockpit setups (.cpit files from the cockpit editor, Documents\CockpitEditor): put them in the "Cockpits" folder next to
    // this DLL and pick one in Configuration Manager (Cockpit > Cockpit layout), or cycle them with the key. A setup moves, turns,
    // scales or hides the cockpit's instrument groups (built as their own objects MiG29_ck_g_<id>), adds its own decals and
    // screens (the build's own are hidden while it is active) and, on the SMT, its own MFD pages. "Default" is the cockpit as
    // built. Anything wrong with a file (unreadable, another jet's, broken data) or any error while applying it puts the
    // default cockpit back and says why in the log.
    public static class CockpitSetups
    {
        public const string Default = "Default";
        const string Jet = "mig29";   // the editor's jet id this plugin's setups are for
        static ConfigEntry<string> selected;
        static ConfigEntry<KeyboardShortcut> nextKey;
        static string dir;
        static bool pending;
        internal static CockpitSetupState Current;   // the player's cockpit, while flying this jet

        // called with the setup (null = default) after it is applied or reset: the SMT plugin re-sets up its screens
        public static Action<Transform, object> ScreensHook;

        public static void Init(ConfigFile cfg)
        {
            try
            {
                dir = Path.Combine(Path.GetDirectoryName(typeof(CockpitSetups).Assembly.Location) ?? "", "Cockpits");
                Directory.CreateDirectory(dir);
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogWarning($"Cockpits folder: {e.Message}"); }
            var names = new List<string> { Default };
            names.AddRange(Scan());
            selected = cfg.Bind("Cockpit", "Cockpit layout", Default, new ConfigDescription(
                "Cockpit setup made in the cockpit editor (.cpit files in the Cockpits folder next to the plugin; restart the game to list new files). " +
                "Default = the cockpit as built. A file that cannot be used falls back to Default.", new AcceptableValueList<string>(names.ToArray())));
            nextKey = cfg.Bind("Cockpit", "Next cockpit layout key", KeyboardShortcut.Empty, "Switches to the next cockpit setup in flight (Default, then each .cpit file)");
            selected.SettingChanged += (s, e) => pending = true;
            MiG29InstrumentsPlugin.Log.LogInfo($"cockpit setups: {names.Count - 1} in {dir}");
        }

        static List<string> Scan()
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.cpit").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        /// <summary>The player's cockpit is ready (instruments bound): record its default pose and apply the selected setup.</summary>
        public static void Attach(Transform root)
        {
            var st = root.GetComponent<CockpitSetupState>() ?? root.gameObject.AddComponent<CockpitSetupState>();
            st.Init(root);
            Current = st;
            ApplySelected();
        }

        /// <summary>Called every frame by the plugin: the cycle key and changed settings.</summary>
        public static void Update()
        {
            if (selected == null) return;
            if (nextKey != null && nextKey.Value.IsDown())
            {
                var names = new List<string> { Default }; names.AddRange(Scan());
                int i = names.FindIndex(n => string.Equals(n, selected.Value, StringComparison.OrdinalIgnoreCase));
                string next = names[(i + 1) % names.Count];
                if (!names.Contains(selected.Value)) next = Default;
                try { selected.Value = next; } catch { selected.Value = Default; }   // a file added after start is not in the list: Default
                pending = true;
            }
            if (pending && Current != null) { pending = false; ApplySelected(); }
        }

        static void ApplySelected()
        {
            var st = Current;
            if (st == null) return;
            string name = selected?.Value ?? Default;
            object setup = null;
            try
            {
                st.ResetDefault();
                if (!string.Equals(name, Default, StringComparison.OrdinalIgnoreCase))
                {
                    setup = Load(Path.Combine(dir, name + ".cpit"));
                    st.Apply(setup);
                    MiG29InstrumentsPlugin.Log.LogInfo($"cockpit setup \"{name}\" applied");
                }
                ScreensHook?.Invoke(st.transform, setup);
            }
            catch (Exception e)
            {
                MiG29InstrumentsPlugin.Log.LogWarning($"cockpit setup \"{name}\" not used ({e.Message}): default cockpit");
                try { st.ResetDefault(); ScreensHook?.Invoke(st.transform, null); }
                catch (Exception e2) { MiG29InstrumentsPlugin.Log.LogError($"cockpit setup reset: {e2}"); }
            }
        }

        static object Load(string file)
        {
            if (!File.Exists(file)) throw new Exception("file not found");
            var root = MiG29Json.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
            if (root == null) throw new Exception("not a cockpit setup");
            if (MiG29Json.Str(root, "format") != "cpit") throw new Exception("not a cockpit setup (format)");
            if (MiG29Json.Num(root, "version", 0f) > 1f) throw new Exception("made by a newer cockpit editor");
            string jet = MiG29Json.Str(root, "jet");
            if (jet != Jet) throw new Exception($"made for the {(jet == "mig29" ? "MiG-29" : jet == "smt" ? "MiG-29SMT" : "unknown jet")}");
            return root;
        }
    }

    // One cockpit's default pose and what a setup added to it.
    public class CockpitSetupState : MonoBehaviour
    {
        class Pose { public Transform t; public Vector3 p, s; public Quaternion r; }
        readonly Dictionary<string, Pose> groups = new Dictionary<string, Pose>();
        Pose tac;
        Transform origin, cockpitPart;
        readonly List<Renderer> buildItems = new List<Renderer>();
        readonly List<GameObject> created = new List<GameObject>();
        Material ckMat, tacMat;
        Texture2D metal;
        int layer;
        bool inited;

        public void Init(Transform root)
        {
            if (inited) return;
            inited = true;
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t.name.StartsWith("MiG29_ck_g_")) groups[t.name.Substring("MiG29_ck_g_".Length)] = new Pose { t = t, p = t.localPosition, r = t.localRotation, s = t.localScale };
                else if (t.name == "MiG29_ck_origin") origin = t;
                else if (t.name == "tacScreen") tac = new Pose { t = t, p = t.localPosition, r = t.localRotation, s = t.localScale };
                else if ((t.name.StartsWith("MiG29_ck_decal_") || t.name.StartsWith("MiG29_ck_cs_") || t.name.StartsWith("MiG29_ck_cbz_")) && t.TryGetComponent<Renderer>(out var r))
                    buildItems.Add(r);
            }
            var tub = all.FirstOrDefault(t => t.name == "MiG29_ck_tub");
            cockpitPart = tub != null ? tub.parent : root;
            if (tub != null && tub.TryGetComponent<Renderer>(out var tr)) { ckMat = tr.sharedMaterial; layer = tub.gameObject.layer; }
            if (tac?.t != null && tac.t.TryGetComponent<Renderer>(out var tcr)) tacMat = tcr.sharedMaterial;
        }

        public void ResetDefault()
        {
            foreach (var o in created) if (o != null) Destroy(o);
            created.Clear();
            foreach (var g in groups.Values) if (g.t != null) { g.t.localPosition = g.p; g.t.localRotation = g.r; g.t.localScale = g.s; }
            if (tac?.t != null) { tac.t.localPosition = tac.p; tac.t.localRotation = tac.r; tac.t.localScale = tac.s; }
            foreach (var r in buildItems) if (r != null) r.enabled = true;
        }

        public void Apply(object setup)
        {
            var lay = MiG29Json.Obj(MiG29Json.Obj(setup, "layout"), "groups");
            if (lay != null && lay.Count > 0)
            {
                if (origin == null) throw new Exception("this build has no movable instruments (rebuild the mod with the cockpit editor)");
                foreach (var kv in lay)
                {
                    if (!groups.TryGetValue(kv.Key, out var g) || g.t == null) { MiG29InstrumentsPlugin.Log.LogWarning($"cockpit setup: no instrument \"{kv.Key}\" in this build, skipped"); continue; }
                    var pos = MiG29Json.Floats(kv.Value, "pos"); var n = MiG29Json.Floats(kv.Value, "normal"); var u = MiG29Json.Floats(kv.Value, "up");
                    if (pos.Length != 3 || n.Length != 3 || u.Length != 3) throw new Exception($"instrument \"{kv.Key}\": bad position");
                    var N = new Vector3(n[0], n[1], n[2]); var U = new Vector3(u[0], u[1], u[2]);
                    if (N.sqrMagnitude < 1e-6f || U.sqrMagnitude < 1e-6f || Vector3.Cross(N, U).sqrMagnitude < 1e-8f) throw new Exception($"instrument \"{kv.Key}\": bad direction");
                    var P = new Vector3(pos[0], pos[1], pos[2]);
                    if (float.IsNaN(P.x + P.y + P.z) || P.magnitude > 50f) throw new Exception($"instrument \"{kv.Key}\": bad position");
                    float s = Mathf.Clamp(MiG29Json.Num(kv.Value, "scale", 1f), 0.05f, 5f);
                    Place(g.t, P + origin.localPosition, Quaternion.LookRotation(N, U), MiG29Json.Bool(kv.Value, "hidden") ? 1e-4f : s);
                }
            }
            // the setup's own decals and screens replace the build's
            foreach (var r in buildItems) if (r != null) r.enabled = false;
            var parts = MiG29Json.Arr(setup, "parts");
            if (parts.Count == 0) return;
            if (origin == null || ckMat == null) throw new Exception("this build cannot take added decals or screens");
            var textures = MiG29Json.Obj(setup, "textures") ?? new Dictionary<string, object>();
            foreach (var po in parts)
            {
                string name = MiG29Json.Str(po, "name"), mat = MiG29Json.Str(po, "material");
                if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0) throw new Exception("a part without a proper name");
                var mesh = MakeMesh(po, origin.localPosition, "MiG29_ck_" + name);
                Material m = ckMat;
                if (mat == "decal")
                {
                    string tn = MiG29Json.Str(po, "texture");
                    if (!(textures.TryGetValue(tn, out var b64) && b64 is string str)) throw new Exception($"decal {name}: picture missing");
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "MiG29_cpit_" + tn, filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp, ignoreMipmapLimit = true };
                    if (!ImageConversion.LoadImage(tex, Convert.FromBase64String(str))) throw new Exception($"decal {name}: bad picture");
                    if (metal == null) { metal = new Texture2D(4, 4, TextureFormat.RGBA32, false); var px = new Color32[16]; for (int k = 0; k < 16; k++) px[k] = new Color32(0, 0, 0, 70); metal.SetPixels32(px); metal.Apply(); }
                    m = new Material(ckMat) { name = "MiG29_cpit_" + name };
                    m.SetTexture("_Basecolor", tex); m.SetTexture("_Livery", tex); m.SetTexture("_BasecolorDmg", tex); m.SetTexture("_Metallic", metal);
                }
                else if (mat == "tac" && tacMat != null) m = tacMat;
                var o = new GameObject("MiG29_ck_" + name);
                o.transform.SetParent(cockpitPart, false);
                o.transform.localPosition = Vector3.zero; o.transform.localRotation = Quaternion.identity; o.transform.localScale = Vector3.one;
                o.layer = layer;
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = o.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                created.Add(o);
            }
        }

        // a group to a pose (cockpit-part frame) and scale; the tactical screen, if it follows the group, keeps its place on it
        void Place(Transform g, Vector3 pos, Quaternion rot, float scale)
        {
            var t = tac?.t;
            bool follow = t != null && g.Find("MiG29_follow_tacScreen") != null;
            Vector3 rp = Vector3.zero, rs = Vector3.one; Quaternion rr = Quaternion.identity; float s0 = g.localScale.x;
            if (follow) { rp = g.InverseTransformPoint(t.position); rr = Quaternion.Inverse(g.rotation) * t.rotation; rs = t.localScale; }
            g.localPosition = pos; g.localRotation = rot; g.localScale = Vector3.one * scale;
            if (follow) { t.position = g.TransformPoint(rp); t.rotation = g.rotation * rr; t.localScale = rs * (scale / Mathf.Max(s0, 1e-6f)); }
        }

        static Mesh MakeMesh(object p, Vector3 offset, string name)
        {
            var v = MiG29Json.Floats(p, "vertices"); var nr = MiG29Json.Floats(p, "normals"); var uv = MiG29Json.Floats(p, "uvs"); var tri = MiG29Json.Ints(p, "triangles");
            int n = v.Length / 3;
            if (n == 0 || v.Length != n * 3 || nr.Length != n * 3 || uv.Length != n * 2 || tri.Length % 3 != 0 || tri.Any(i => i < 0 || i >= n)) throw new Exception($"{name}: broken mesh data");
            var vs = new Vector3[n]; var ns = new Vector3[n]; var us = new Vector2[n];
            for (int i = 0; i < n; i++) { vs[i] = new Vector3(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]) + offset; ns[i] = new Vector3(nr[i * 3], nr[i * 3 + 1], nr[i * 3 + 2]); us[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]); }
            var m = new Mesh { name = name, indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.vertices = vs; m.normals = ns; m.uv = us; m.triangles = tri;
            m.RecalculateBounds();
            return m;
        }

        void OnDestroy() { if (CockpitSetups.Current == this) CockpitSetups.Current = null; }
    }
}
