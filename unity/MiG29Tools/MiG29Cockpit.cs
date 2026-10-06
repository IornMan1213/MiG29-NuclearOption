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
        [Serializable] class Part { public string name, material; public float[] vertices, normals, uvs, pivot, axis, up; public int[] triangles; public float[] fold; }

        public static void Build(GameObject go, Transform cockpitPart, Vector3 modelOffset, Material skin, Material glass, List<Renderer> exterior,
                                 Func<UnityEngine.Object, string, UnityEngine.Object> save, Action<Transform, Renderer> addDamage)
        {
            var root = go.transform;
            var dump = JsonUtility.FromJson<Dump>(File.ReadAllText(Path.Combine(SourceDir, "cockpit_mesh.json")));
            var parts = dump.parts.ToDictionary(p => p.name);
            var mat = (Material)save(CockpitMaterial(skin), ModDir + "/materials/MiG29_cockpit.mat");

            // MiG canopy + windscreen glass show in both views
            int glassCount = exterior.RemoveAll(r => r != null && (r.name.StartsWith("MiG29_canopy") || r.name.Contains("windscreen")));
            int hidden = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => KrHidden.Contains(t.name)))
                if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) { mf.sharedMesh = null; hidden++; }

            // static pieces, in the MiG frame (the cockpit part sits at the prefab origin)
            foreach (var (name, m) in new[] { ("tub", mat), ("glass", glass) })
            {
                var mesh = (Mesh)save(MakeMesh(parts[name], v => v + modelOffset, n => n, "MiG29_ck_" + name), $"{ModDir}/meshes/MiG29_ck_{name}.asset");
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
            BuildInstruments(cockpitPart, parts.Values, modelOffset, mat, save);
            // The cockpit view draws the interior on the stock interior's layer with its own near-clip camera; anything on the default
            // layer closer than ~1 m to the eye is clipped away. Everything new in the cockpit goes on that layer.
            int layer = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "cockpit_int").gameObject.layer;
            int moved = 0;
            foreach (var t in cockpitPart.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MiG29_ck_") || t.name.StartsWith("MiG29_ins_") || t.name.StartsWith("MiG29_lamp_") || t.name == "needle"))
            { t.gameObject.layer = layer; moved++; }
            // The main (outside) camera does not draw that layer: an exterior copy of the interior on the default layer, which the
            // game hides in the cockpit view (exterior renderer), keeps the cockpit visible through the canopy from outside.
            {
                var src = cockpitPart.Find("MiG29_ck_tub");
                var ext = UnityEngine.Object.Instantiate(src.gameObject, src.parent);
                ext.name = "MiG29_ck_tub_ext"; ext.layer = 0;
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
