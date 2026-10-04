using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // From-scratch RD-33 exhaust nozzles (tools/nozzle_gen.py -> MiG29Source/nozzles.json). The MiG model's own nozzle cans are
    // low-poly and lumpy: their triangles are stripped from the airframe and replaced, per engine, by
    //   MiG29_nozzle_<L|R>                 frame on the engine axis at the nacelle seam (+z forward, toes out/down ~2 deg like the model)
    //     MiG29_nozstatic_<L|R>            duct, throat ring, root band, afterburner flame holders, turbine cone
    //     MiG29_noz_<L|R>_<flap|seal|inner>_<i>   one hinged petal each (16 flaps, 16 seals, 16 inner petals)
    // The MiG29Instruments plugin (NozzleDriver) opens and closes the petals with the engine like the real convergent-divergent
    // nozzle; without it they rest at the as-built opening. Petal pose: localRotation = AngleAxis(phi, forward) * Euler(theta, 0, 0).
    public static class MiG29Nozzles
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string Dir = ModDir + "/nozzles";
        const string SourceDir = "MiG29Source";

        [Serializable] class PartDump { public string name; public float[] vertices, normals, uvs; public int[] triangles; }
        [Serializable] class Layout { public float[] axis_root, axis_end; public int count; public float r_root, l_flap, seal_inset, z_throat, r_throat, l_inner, exit_rest; }
        [Serializable] class Dump { public PartDump[] parts; public Layout layout; }

        public const float StripRadius = 0.535f;   // keep in step with blender/analysis/nozzle_preview.py

        static Layout layout;
        static Layout L => layout ?? (layout = JsonUtility.FromJson<Dump>(File.ReadAllText(Path.Combine(SourceDir, "nozzles.json"))).layout);

        static void Axis(float side, out Vector3 root, out Vector3 fwd)
        {
            root = new Vector3(side * L.axis_root[0], L.axis_root[1], L.axis_root[2]);
            var end = new Vector3(side * L.axis_end[0], L.axis_end[1], L.axis_end[2]);
            fwd = (root - end).normalized;
        }

        // Removes the model's nozzle triangles (MiG frame): everything aft of 2 cm ahead of the seam within StripRadius of either engine axis.
        public static int[] StripModelNozzles(float[] v, int[] tris, out int removed)
        {
            var keep = new System.Collections.Generic.List<int>(tris.Length);
            removed = 0;
            Axis(-1, out var rl, out var fl); Axis(1, out var rr, out var fr);
            for (int i = 0; i < tris.Length; i += 3)
            {
                var c = Vector3.zero;
                for (int k = 0; k < 3; k++) { int j = tris[i + k] * 3; c += new Vector3(v[j], v[j + 1], v[j + 2]); }
                c /= 3f;
                if (In(c, rl, fl) || In(c, rr, fr)) { removed++; continue; }
                keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
            }
            return keep.ToArray();
        }

        static bool In(Vector3 c, Vector3 root, Vector3 fwd)
        {
            var rel = c - root;
            float along = Vector3.Dot(rel, fwd);
            if (-along <= -0.02f) return false;                       // more than 2 cm ahead of the seam
            return (rel - along * fwd).magnitude < StripRadius;
        }

        public static void Build(Transform root, Vector3 modelOffset, Material skinTemplate, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(ModDir, "nozzles");
            var dump = JsonUtility.FromJson<Dump>(File.ReadAllText(Path.Combine(SourceDir, "nozzles.json")));
            layout = dump.layout;
            var meshes = dump.parts.ToDictionary(p => p.name, p => (Mesh)save(ToMesh(p), $"{Dir}/MiG29_noz_{p.name}.asset"));
            var mat = (Material)save(NozzleMaterial(skinTemplate), $"{Dir}/MiG29_nozzle.mat");
            int petals = 0;
            foreach (var (side, s) in new[] { (-1f, "L"), (1f, "R") })
            {
                Axis(side, out var r, out var fwd);
                var up = Vector3.up - Vector3.Dot(Vector3.up, fwd) * fwd;
                var parent = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "nozzle_" + s);
                var frame = new GameObject("MiG29_nozzle_" + s).transform;
                frame.SetParent(parent, true);
                frame.SetPositionAndRotation(r + modelOffset, Quaternion.LookRotation(fwd, up));
                Add(frame, "MiG29_nozstatic_" + s, meshes["static"], mat, Vector3.zero, Quaternion.identity);
                float alpha = Mathf.Asin((L.r_root - L.exit_rest) / L.l_flap) * Mathf.Rad2Deg;
                float beta = Mathf.Asin((L.exit_rest - 0.015f - L.r_throat) / L.l_inner) * Mathf.Rad2Deg;
                for (int i = 0; i < L.count; i++)
                {
                    foreach (var (kind, phase, radius, z, theta) in new[] {
                        ("flap", 0f, L.r_root, 0f, -alpha),
                        ("seal", 0.5f, L.r_root - L.seal_inset, 0f, -alpha),
                        ("inner", 0.25f, L.r_throat, -L.z_throat, beta) })
                    {
                        float phi = 360f * (i + phase) / L.count;
                        var q = Quaternion.AngleAxis(phi, Vector3.forward);
                        Add(frame, $"MiG29_noz_{s}_{kind}_{i:00}", meshes[kind], mat, q * new Vector3(0f, radius, z), q * Quaternion.Euler(theta, 0f, 0f));
                        petals++;
                    }
                }
            }
            Debug.Log($"[MiG29] nozzles: 2 RD-33 nozzles, {petals} hinged petals, rest exit radius {L.exit_rest:F3} m");
        }

        static void Add(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot)
        {
            var o = new GameObject(name);
            o.transform.SetParent(parent, false);
            o.transform.localPosition = localPos; o.transform.localRotation = localRot;
            o.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = o.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
        }

        static Mesh ToMesh(PartDump p)
        {
            int n = p.vertices.Length / 3;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]);
                nr[i] = new Vector3(p.normals[i * 3], p.normals[i * 3 + 1], p.normals[i * 3 + 2]);
                uv[i] = new Vector2(p.uvs[i * 2], p.uvs[i * 2 + 1]);
            }
            var m = new Mesh { name = "MiG29_noz_" + p.name };
            m.vertices = v; m.normals = nr; m.uv = uv; m.triangles = p.triangles;
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        static Texture2D ImportTex(string file, bool srgb, bool normal)
        {
            var dst = $"{Dir}/{file}";
            File.Copy(Path.Combine(SourceDir, file), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static Material NozzleMaterial(Material skinTemplate)
        {
            var atlas = ImportTex("nozzle_atlas.png", true, false);
            var m = new Material(skinTemplate) { name = "MiG29_nozzle" };
            m.SetTexture("_Basecolor", atlas); m.SetTexture("_Livery", atlas); m.SetTexture("_BasecolorDmg", atlas);
            var nrm = ImportTex("flat_normal.png", false, true);
            m.SetTexture("_Normal", nrm); m.SetTexture("_NormalDmg", nrm);
            m.SetTexture("_Metallic", ImportTex("nozzle_metallic.png", false, false));
            m.SetTexture("_AO", null);
            return m;
        }
    }
}
