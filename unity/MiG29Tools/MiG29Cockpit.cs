using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // The MiG-29 cockpit, modelled from scratch: blender/cockpit_build.py writes MiG29Source/cockpit_mesh.json (MiG frame),
    // tools/cockpit_atlas.py draws its texture. Static interior pieces become their own renderers, visible from inside and outside.
    // The parts the game drives keep the stock objects and only get new meshes: stick -> joystick, throttle levers -> throttle,
    // display -> tacScreen (tactical render texture), master warning lamps -> warningLights, seat -> EjectionSeat.
    public static class MiG29Cockpit
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string SourceDir = "MiG29Source";

        // The stock pilot sat ~0.2 m forward of the MiG seat position (eye 0.4 m behind the windscreen bow); this puts the head
        // against the new headrest. cockpitViewPoint (the pilot's helmetCamPoint) moves with it.
        public static readonly Vector3 PilotShift = new Vector3(0f, 0f, -0.17f);

        // KR-67 interior meshes that the new cockpit replaces (components on them stay: Cockpit, CockpitWarningLights).
        static readonly string[] KrHidden = { "cockpit_int", "cockpit_int_simple", "canopy_F_int", "canopy_R_int", "canopyFrame_F_int", "canopyFrame_R_int",
                                              "canopyFrame_F_int_simple", "canopyFrame_R_int_simple" };

        [Serializable] class Dump { public Part[] parts; }
        [Serializable] class Part { public string name, material, texture; public float[] vertices, normals, uvs, pivot, axis, up; public int[] triangles; public float[] fold, glass; }

        public static void Build(GameObject go, Transform cockpitPart, Vector3 modelOffset, Material skin, Material glass, List<Renderer> exterior,
                                 Func<UnityEngine.Object, string, UnityEngine.Object> save, Action<Transform, Renderer> addDamage)
        {
            var root = go.transform;
            var dump = JsonUtility.FromJson<Dump>(File.ReadAllText(Path.Combine(SourceDir, "cockpit_mesh.json")));
            var parts = dump.parts.ToDictionary(p => p.name);
            var mat = (Material)save(CockpitMaterial(skin), ModDir + "/materials/MiG29_cockpit.mat");
            mirrorGrey = null;

            // MiG canopy + windscreen glass show in both views; their inside layers (*_int) only outside (MiG29Polish.SetupCanopy)
            int glassCount = exterior.RemoveAll(r => r != null && (r.name.StartsWith("MiG29_canopy") || r.name.Contains("windscreen")) && !r.name.EndsWith("_int"));
            int hidden = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => KrHidden.Contains(t.name)))
                if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) { mf.sharedMesh = null; hidden++; }

            // static pieces, in the MiG frame (the cockpit part sits at the prefab origin); the tub without the cockpit editor's
            // movable groups, which become their own objects (BuildGroups)
            var groups = ReadGroups();
            foreach (var (name, m) in new[] { ("tub", mat), ("glass", glass) })
            {
                var src = name == "tub" ? TubWithoutGroups(parts["tub"], groups) : parts[name];
                var mesh = (Mesh)save(MakeMesh(src, v => v + modelOffset, n => n, "MiG29_ck_" + name), $"{ModDir}/meshes/MiG29_ck_{name}.asset");
                var o = new GameObject("MiG29_ck_" + name);
                o.transform.SetParent(cockpitPart, false);
                o.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                o.AddComponent<MeshRenderer>().sharedMaterial = m;
            }

            // game-driven pieces: move the pivots, bake the meshes into their local frames
            Attach(root, "joystick", parts["stick"], modelOffset, mat, save, movePivot: true);
            Attach(root, "throttle", parts["throttle"], modelOffset, mat, save, movePivot: true);
            Attach(root, "EjectionSeat", parts["seat"], modelOffset, mat, save, movePivot: false);
            Attach(root, "tacScreen", parts["screen"], modelOffset, null, save, movePivot: false);
            Attach(root, "warningLights", parts["lamps"], modelOffset, null, save, movePivot: false);
            BuildCustom(root, cockpitPart, modelOffset, mat, save);
            BuildInstruments(cockpitPart, parts.Values, modelOffset, mat, save);
            BuildGroups(root, cockpitPart, parts["tub"], groups, modelOffset, mat, save);
            // The cockpit view draws the interior on the stock interior's layer with its own near-clip camera; anything on the default
            // layer closer than ~1 m to the eye is clipped away. Everything new in the cockpit goes on that layer.
            int layer = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "cockpit_int").gameObject.layer;
            int moved = 0;
            foreach (var t in cockpitPart.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MiG29_ck_") || t.name.StartsWith("MiG29_ins_") || t.name.StartsWith("MiG29_lamp_") || t.name == "needle" || t.name.StartsWith("NOMirror_")))
            { t.gameObject.layer = layer; moved++; }
            // The main (outside) camera does not draw that layer: an exterior copy of the interior on the default layer, which the
            // game hides in the cockpit view (exterior renderer), keeps the cockpit visible through the canopy from outside.
            {
                var mesh = (Mesh)save(MakeMesh(parts["tub"], v => v + modelOffset, n => n, "MiG29_ck_tub_ext"), $"{ModDir}/meshes/MiG29_ck_tub_ext.asset");
                var ext = new GameObject("MiG29_ck_tub_ext");
                ext.transform.SetParent(cockpitPart, false);
                ext.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                ext.AddComponent<MeshFilter>().sharedMesh = mesh;
                ext.AddComponent<MeshRenderer>().sharedMaterial = mat;
                ext.layer = 0;
                exterior.Add(ext.GetComponent<Renderer>());
            }
            // The MiG model's own windscreen bow and sills (skin UVs, livery and damage): outside view only. Up close they are low-poly
            // and blotchy, so inside the cockpit the tub's from-scratch bow and mirrors replace them. Only these take part damage: the
            // damage shader samples the livery/damage maps in the airframe UV layout, which punches holes in anything on the atlas.
            {
                var mesh = (Mesh)save(MakeMesh(parts["frames"], v => v + modelOffset, n => n, "MiG29_ck_frames"), $"{ModDir}/meshes/MiG29_ck_frames.asset");
                var o = new GameObject("MiG29_ck_frames_ext");
                o.transform.SetParent(cockpitPart, false);
                o.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = o.AddComponent<MeshRenderer>(); r.sharedMaterial = skin;
                addDamage(cockpitPart, r);
                exterior.Add(r);
            }
            Debug.Log($"[MiG29] cockpit: {moved} objects on the interior layer {layer} ({LayerMask.LayerToName(layer)})");
            Debug.Log($"[MiG29] cockpit: from-scratch interior ({parts["tub"].triangles.Length / 3} tris), {glassCount} MiG glass renderers in both views, {hidden} KR-67 interior meshes hidden");
        }

        // Moving instrument parts (ins_*): a fixed mount "MiG29_ins_<id>" (z into the panel, y up) with a child "needle" that the
        // MiG29Instruments plugin turns; posed here at the engine-off reading. Lamps (lamp_*): "MiG29_lamp_<id>", off until the plugin
        // lights them.
        static void BuildInstruments(Transform cockpitPart, IEnumerable<Part> parts, Vector3 modelOffset, Material mat, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            var rest = new FlightState { hydraulic = 0, oxygen = 150 };
            int ins = 0, lamps = 0;
            foreach (var p in parts)
            {
                if (p.name.StartsWith("ins_"))
                {
                    var mount = new GameObject("MiG29_" + p.name).transform;
                    mount.SetParent(cockpitPart, false);
                    var axis = new Vector3(p.axis[0], p.axis[1], p.axis[2]); var up = new Vector3(p.up[0], p.up[1], p.up[2]);
                    mount.SetPositionAndRotation(new Vector3(p.pivot[0], p.pivot[1], p.pivot[2]) + modelOffset, Quaternion.LookRotation(-axis, up));
                    var needle = new GameObject("needle").transform;
                    needle.SetParent(mount, false);
                    var mesh = (Mesh)save(MakeMesh(p, v => needle.InverseTransformPoint(v + modelOffset), n => needle.InverseTransformDirection(n), "MiG29_ck_" + p.name),
                                          $"{ModDir}/meshes/MiG29_ck_{p.name}.asset");
                    needle.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    needle.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    if (p.fold != null && p.fold.Length == 4)   // folding mirrors: the folded pose for the plugin (cockpit_build.py finds it against the glass)
                    {
                        var folded = new GameObject("folded").transform;
                        folded.SetParent(mount, false);
                        folded.localRotation = new Quaternion(p.fold[0], p.fold[1], p.fold[2], p.fold[3]);
                    }
                    if (p.glass != null && p.glass.Length == 11)
                        MirrorGlass(needle, p, modelOffset, mat, save);
                    var id = p.name.Substring(4);
                    if (MiG29InstrumentMath.Pose(id, rest, out var lp, out var lr)) { needle.localPosition = lp; needle.localRotation = lr; }
                    else needle.localRotation = id == "adi_ball" ? MiG29InstrumentMath.Ball(rest)
                        : MiG29InstrumentMath.Needles.TryGetValue(id, out var f) ? Quaternion.Euler(0f, 0f, -f(rest)) : Quaternion.identity;
                    ins++;
                }
                else if (p.name.StartsWith("lamp_"))
                {
                    var o = new GameObject("MiG29_" + p.name);
                    o.transform.SetParent(cockpitPart, false);
                    o.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var mesh = (Mesh)save(MakeMesh(p, v => v + modelOffset, n => n, "MiG29_ck_" + p.name), $"{ModDir}/meshes/MiG29_ck_{p.name}.asset");
                    o.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = o.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.enabled = false;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lamps++;
                }
            }
            Debug.Log($"[MiG29] instruments: {ins} moving parts, {lamps} lamps");
        }

        static void Attach(Transform root, string node, Part p, Vector3 modelOffset, Material mat, Func<UnityEngine.Object, string, UnityEngine.Object> save, bool movePivot)
        {
            var t = root.GetComponentsInChildren<Transform>(true).First(x => x.name == node);
            if (movePivot) t.position = new Vector3(p.pivot[0], p.pivot[1], p.pivot[2]) + modelOffset;
            var mesh = (Mesh)save(MakeMesh(p, v => t.InverseTransformPoint(v + modelOffset), n => t.InverseTransformDirection(n), "MiG29_ck_" + p.name),
                                  $"{ModDir}/meshes/MiG29_ck_{p.name}.asset");
            t.GetComponent<MeshFilter>().sharedMesh = mesh;
            if (mat != null)
            {
                var r = t.GetComponent<Renderer>();
                r.sharedMaterials = Enumerable.Repeat(mat, 1).ToArray();
            }
        }

        // ------------------------------------------------------------------------------------------------ cockpit editor groups
        // blender/cockpit_build.py marks each instrument, display, plate and switch panel as a movable group (cockpit_mesh.json
        // "groups": its faces as a vertex / triangle range of the tub, its frame, the parts it owns). Each becomes its own object
        // MiG29_ck_g_<id>: its faces as a mesh in its own frame (pivot at the group's centre, z = its normal toward the pilot,
        // y = its up), the parts it owns (needle mounts, lamps, plugin screens) parented under it, the game's tactical screen
        // following it (marker MiG29_follow_tacScreen). Moving the object moves the whole instrument; the instruments plugins do
        // that at runtime for the cockpit setups (.cpit). The build's own layout (the editor's layout.json, copied to
        // <source>/custom) is set the same way. MiG29_ck_origin marks the model offset (the editor's coordinates are the MiG frame).
        class Group { public string id; public int v0, v1, t0, t1; public Vector3 c, n, u; public List<string> owned = new List<string>(); }

        static List<Group> ReadGroups()
        {
            var list = new List<Group>();
            var root = MiG29Json.Parse(File.ReadAllText(Path.Combine(SourceDir, "cockpit_mesh.json")));
            foreach (var g in MiG29Json.Arr(root, "groups"))
            {
                var v = MiG29Json.Ints(g, "verts"); var tr = MiG29Json.Ints(g, "tris"); var fr = MiG29Json.Arr(g, "frame");
                if (v.Length < 2 || tr.Length < 2 || fr.Count < 4) continue;
                var c = MiG29Json.Floats(fr[0]); var n = MiG29Json.Floats(fr[1]); var u = MiG29Json.Floats(fr[3]);
                var grp = new Group { id = MiG29Json.Str(g, "id"), v0 = v[0], v1 = v[1], t0 = tr[0], t1 = tr[1], c = new Vector3(c[0], c[1], c[2]), n = new Vector3(n[0], n[1], n[2]).normalized, u = new Vector3(u[0], u[1], u[2]).normalized };
                foreach (var p in MiG29Json.Arr(g, "parts")) if (p is string s) grp.owned.Add(s);
                list.Add(grp);
            }
            return list;
        }

        static Part TubWithoutGroups(Part tub, List<Group> groups)
        {
            if (groups.Count == 0) return tub;
            int v1 = groups.Min(g => g.v0), t1 = groups.Min(g => g.t0);
            return new Part { name = tub.name, material = tub.material, vertices = tub.vertices.Take(v1 * 3).ToArray(), normals = tub.normals.Take(v1 * 3).ToArray(),
                              uvs = tub.uvs.Take(v1 * 2).ToArray(), triangles = tub.triangles.Take(t1).ToArray() };
        }

        static void BuildGroups(Transform root, Transform cockpitPart, Part tub, List<Group> groups, Vector3 modelOffset, Material mat, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            if (groups.Count == 0) return;
            var origin = new GameObject("MiG29_ck_origin").transform;
            origin.SetParent(cockpitPart, false);
            origin.SetPositionAndRotation(modelOffset, Quaternion.identity);
            Directory.CreateDirectory($"{ModDir}/meshes/groups");
            var tac = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "tacScreen");
            var made = new Dictionary<string, Transform>();
            int owned = 0;
            foreach (var g in groups)
            {
                var o = new GameObject("MiG29_ck_g_" + g.id);
                var tr = o.transform;
                tr.SetParent(cockpitPart, false);
                var rot = Quaternion.LookRotation(g.n, g.u);
                tr.SetPositionAndRotation(g.c + modelOffset, rot);
                int nv = g.v1 - g.v0;
                var p = new Part { name = "g_" + g.id, material = "cockpit", vertices = new float[nv * 3], normals = new float[nv * 3], uvs = new float[nv * 2], triangles = new int[g.t1 - g.t0] };
                Array.Copy(tub.vertices, g.v0 * 3, p.vertices, 0, nv * 3); Array.Copy(tub.normals, g.v0 * 3, p.normals, 0, nv * 3); Array.Copy(tub.uvs, g.v0 * 2, p.uvs, 0, nv * 2);
                for (int k = 0; k < p.triangles.Length; k++) p.triangles[k] = tub.triangles[g.t0 + k] - g.v0;
                var mesh = (Mesh)save(MakeMesh(p, v => tr.InverseTransformPoint(v + modelOffset), n => tr.InverseTransformDirection(n), "MiG29_ck_g_" + g.id), $"{ModDir}/meshes/groups/MiG29_ck_g_{g.id}.asset");
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                o.AddComponent<MeshRenderer>().sharedMaterial = mat;
                foreach (var name in g.owned)
                {
                    if (name == "screen") { if (tac != null) new GameObject("MiG29_follow_tacScreen").transform.SetParent(tr, false); continue; }
                    var t = cockpitPart.Find("MiG29_" + name) ?? cockpitPart.Find("MiG29_ck_" + name);
                    if (t != null) { t.SetParent(tr, true); owned++; }
                }
                made[g.id] = tr;
            }
            // the build's own layout (the cockpit editor's layout.json): the groups' transforms
            int laid = 0;
            var lf = Path.Combine(SourceDir, "custom", "layout.json");
            if (File.Exists(lf))
            {
                var lay = MiG29Json.Obj(MiG29Json.Parse(File.ReadAllText(lf)), "groups");
                if (lay != null)
                    foreach (var kv in lay)
                    {
                        if (!made.TryGetValue(kv.Key, out var tr)) continue;
                        var pos = MiG29Json.Floats(kv.Value, "pos"); var n = MiG29Json.Floats(kv.Value, "normal"); var u = MiG29Json.Floats(kv.Value, "up");
                        bool hide = MiG29Json.Bool(kv.Value, "hidden"); float s = MiG29Json.Num(kv.Value, "scale", 1f);
                        var g = groups.First(x => x.id == kv.Key);
                        Vector3 P = pos.Length == 3 ? new Vector3(pos[0], pos[1], pos[2]) : g.c, N = n.Length == 3 ? new Vector3(n[0], n[1], n[2]) : g.n, U = u.Length == 3 ? new Vector3(u[0], u[1], u[2]) : g.u;
                        PlaceGroup(tr, P + modelOffset, Quaternion.LookRotation(N, U), hide ? 1e-4f : Mathf.Max(0.05f, s), tac);
                        laid++;
                    }
            }
            Debug.Log($"[MiG29] cockpit editor groups: {made.Count} movable objects ({owned} parts under them), {laid} placed by the layout");
        }

        // a group to a pose (cockpit-part frame) and scale; the tactical screen, if it follows the group, keeps its place on it
        static void PlaceGroup(Transform g, Vector3 pos, Quaternion rot, float scale, Transform tac)
        {
            bool follow = tac != null && g.Find("MiG29_follow_tacScreen") != null;
            Vector3 rp = Vector3.zero; Quaternion rr = Quaternion.identity; Vector3 rs = Vector3.one; float s0 = g.localScale.x;
            if (follow) { rp = g.InverseTransformPoint(tac.position); rr = Quaternion.Inverse(g.rotation) * tac.rotation; rs = tac.localScale; }
            g.localPosition = pos; g.localRotation = rot; g.localScale = Vector3.one * scale;
            if (follow) { tac.position = g.TransformPoint(rp); tac.rotation = g.rotation * rr; tac.localScale = rs * (scale / Mathf.Max(s0, 1e-6f)); }
        }

        static Mesh MakeMesh(Part p, Func<Vector3, Vector3> pos, Func<Vector3, Vector3> dir, string name)
        {
            int n = p.vertices.Length / 3;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = pos(new Vector3(p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]));
                nr[i] = dir(new Vector3(p.normals[i * 3], p.normals[i * 3 + 1], p.normals[i * 3 + 2])).normalized;
                uv[i] = new Vector2(p.uvs[i * 2], p.uvs[i * 2 + 1]);
            }
            var m = new Mesh { name = name, indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.vertices = v; m.normals = nr; m.uv = uv;
            m.triangles = p.triangles;
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        // ------------------------------------------------------------------------------------------------ cockpit editor elements
        // Decals (painted stencils, each a mesh of just its painted pixels with its own texture on the cockpit shader, which has
        // no transparency) and custom screens with bezels, placed by the user in the cockpit editor (Documents\CockpitEditor). It
        // writes <repo>\cockpit\build\custom_mesh.json in this file's Part format; the build script copies it to <source>\custom.
        [Serializable] class CustomDump { public Part[] parts; public string[] hide; }

        static string[] CustomHidden()
        {
            var file = Path.Combine(SourceDir, "custom", "custom_mesh.json");
            if (!File.Exists(file)) return new string[0];
            return JsonUtility.FromJson<CustomDump>(File.ReadAllText(file)).hide ?? new string[0];
        }

        static void BuildCustom(Transform root, Transform cockpitPart, Vector3 modelOffset, Material mat, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            var file = Path.Combine(SourceDir, "custom", "custom_mesh.json");
            if (!File.Exists(file)) return;
            var dump = JsonUtility.FromJson<CustomDump>(File.ReadAllText(file));
            if (dump.parts == null || dump.parts.Length == 0) return;
            Directory.CreateDirectory($"{ModDir}/textures/custom");
            Directory.CreateDirectory($"{ModDir}/meshes/custom");
            Directory.CreateDirectory($"{ModDir}/materials/custom");
            // the game's tactical display material: a custom "tac" screen shows the same picture (experimental)
            var tac = root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "tacScreen");
            Texture2D metal = null;
            int decals = 0, screens = 0;
            foreach (var p in dump.parts)
            {
                Material m = mat;
                if (p.material == "decal" && !string.IsNullOrEmpty(p.texture))
                {
                    if (metal == null) metal = ImportTex("custom/decal_metallic.png", false);
                    var tex = ImportTex("custom/" + p.texture, true);
                    m = new Material(mat) { name = "MiG29_ck_" + p.name };
                    m.SetTexture("_Basecolor", tex); m.SetTexture("_Livery", tex); m.SetTexture("_BasecolorDmg", tex);
                    m.SetTexture("_Metallic", metal);
                    m = (Material)save(m, $"{ModDir}/materials/custom/MiG29_ck_{p.name}.mat");
                    decals++;
                }
                else if (p.material == "tac")
                {
                    if (tac != null) m = tac.sharedMaterial;
                    screens++;
                }
                else if (p.material == "screen") screens++;
                var mesh = (Mesh)save(MakeMesh(p, v => v + modelOffset, n => n, "MiG29_ck_" + p.name), $"{ModDir}/meshes/custom/MiG29_ck_{p.name}.asset");
                var o = new GameObject("MiG29_ck_" + p.name);
                o.transform.SetParent(cockpitPart, false);
                o.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = o.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Debug.Log($"[MiG29] cockpit editor: {decals} decals, {screens} custom screens, {dump.parts.Length} parts" +
                      (dump.hide != null && dump.hide.Length > 0 ? $", switched off: {string.Join(", ", dump.hide)}" : ""));
        }

        static Texture2D ImportTex(string file, bool srgb)
        {
            var dst = $"{ModDir}/textures/{file}";
            File.Copy(Path.Combine(SourceDir, file), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;          // console decals are seen at grazing angles
            imp.maxTextureSize = 4096;   // tools/cockpit_atlas.py draws a 4096 atlas (K = 2) so switch legends and rivets stay sharp up close
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        // A mirror's reflecting face for the NO Mirrors plugin (github.com/IornMan1213/NuclearOption-Mirrors): a quad named
        // "NOMirror_<c|l|r>" on the mirror's folding part, local Z its normal, X across, Y up, UVs 0..1 as seen from the front. With
        // the plugin it shows the reflection; without it, plain mirror grey.
        static Material mirrorGrey;

        static void MirrorGlass(Transform needle, Part p, Vector3 modelOffset, Material cockpit, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            var g = p.glass;
            var n = new Vector3(g[3], g[4], g[5]); var up = new Vector3(g[6], g[7], g[8]);
            float w = g[9], h = g[10];
            string id = p.name.Substring("ins_mirror_".Length);
            var o = new GameObject($"NOMirror_{id}_res320_fov{(id == "c" ? "2.5" : "3")}");   // convex: a flat mirror this size shows ~10-20 deg
            o.transform.SetParent(needle, false);
            o.transform.SetPositionAndRotation(new Vector3(g[0], g[1], g[2]) + modelOffset, Quaternion.LookRotation(-n, up));  // Z away from the eye

            var mesh = new Mesh { name = "MiG29_mirror_glass_" + id };
            mesh.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(w / 2, -h / 2, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };   // clockwise as seen from the eye
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            o.AddComponent<MeshFilter>().sharedMesh = (Mesh)save(mesh, $"{ModDir}/meshes/MiG29_mirror_glass_{id}.asset");

            if (mirrorGrey == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "MiG29_mirror_grey" };
                tex.SetPixels(Enumerable.Repeat(new Color(0.55f, 0.58f, 0.62f), 16).ToArray()); tex.Apply();
                tex = (Texture2D)save(tex, $"{ModDir}/materials/MiG29_mirror_grey.asset");
                var m = new Material(cockpit) { name = "MiG29_mirror" };
                m.SetTexture("_Basecolor", tex); m.SetTexture("_Livery", tex); m.SetTexture("_BasecolorDmg", tex);
                m.SetTexture("_Metallic", null);
                mirrorGrey = (Material)save(m, $"{ModDir}/materials/MiG29_mirror.mat");
            }
            o.AddComponent<MeshRenderer>().sharedMaterial = mirrorGrey;
        }

        static Material CockpitMaterial(Material skin)
        {
            var atlas = ImportTex("cockpit_atlas.png", true);
            var metal = ImportTex("cockpit_metallic.png", false);
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/weapons/flat_normal.png");
            var m = new Material(skin) { name = "MiG29_cockpit" };
            m.SetTexture("_Basecolor", atlas); m.SetTexture("_Livery", atlas); m.SetTexture("_BasecolorDmg", atlas);
            m.SetTexture("_Normal", nrm); m.SetTexture("_NormalDmg", nrm);
            m.SetTexture("_Metallic", metal);
            m.SetTexture("_AO", null);
            return m;
        }
    }
}
