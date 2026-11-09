using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // Intake ducts that reach the engines (tools/intake_duct.py -> MiG29Source/intake_duct.json). The model's ducts ended 3.4 m in on
    // a flat cap, a metre short of the RD-33 front, and a black panel half a metre behind each lip hid even that (it was there for the
    // KR-67 wheels, which no longer stow in the ducts). Now the cap goes, and a liner 1.5 cm inside the model walls runs from the lip to
    // the engine front, where the engine's own fan disc (MiG29Cut.Engines) closes it, as on the stock jets whose ducts reach the engine
    // (T/A-30, Darkreach, Alkyon). It darkens toward the back as theirs do, and carries no livery: painters never see it on the
    // template. Not damage renderers (the burn mask would punch through), and only drawn up close (MiG29Lod).
    public static class MiG29Duct
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string Dir = ModDir + "/duct";
        const string SourceDir = "MiG29Source";

        [Serializable] class Side { public float[] vertices, normals, uvs; public int[] triangles; }
        [Serializable] class Dump { public float cap_z; public Side L, R; }

        static Dump dump;
        public static float CapZ => D.cap_z;   // the model's old duct end (MiG frame z)
        static Dump D => dump ?? (dump = JsonUtility.FromJson<Dump>(File.ReadAllText(Path.Combine(SourceDir, "intake_duct.json"))));

        // The model's duct end caps (MiG frame): the 20 triangles lying in the cap plane.
        public static int[] StripCaps(float[] v, int[] tris, out int removed)
        {
            var keep = new List<int>(tris.Length);
            removed = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                bool cap = true;
                for (int k = 0; k < 3 && cap; k++) cap = Mathf.Abs(v[tris[i + k] * 3 + 2] - D.cap_z) < 0.005f;
                if (cap) { removed++; continue; }
                keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
            }
            return keep.ToArray();
        }

        // Both liners in the MiG frame, as one triangle soup each (vertices, normals, uvs, triangles).
        public static IEnumerable<(Vector3[] v, Vector3[] n, Vector2[] uv, int[] t)> Liners()
        {
            foreach (var s in new[] { D.L, D.R })
            {
                int n = s.vertices.Length / 3;
                var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    v[i] = new Vector3(s.vertices[i * 3], s.vertices[i * 3 + 1], s.vertices[i * 3 + 2]);
                    nr[i] = new Vector3(s.normals[i * 3], s.normals[i * 3 + 1], s.normals[i * 3 + 2]);
                    uv[i] = new Vector2(s.uvs[i * 2], s.uvs[i * 2 + 1]);
                }
                yield return (v, nr, uv, s.triangles);
            }
        }

        // The skin shader with depth gradients for colour (no livery slot of its own: _Livery is the same gradient) and ambient
        // occlusion (the game lights a duct's inside like the outside; stock ducts get their dark from their AO maps), flat normals
        // and a low gloss (sky reflections turned the walls blue-white, in-game shots).
        public static Material Material(Material skinTemplate, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(ModDir, "duct");
            var tex = ImportTex("duct_basecolor.png", true, false);
            var m = new Material(skinTemplate) { name = "MiG29_duct" };
            m.SetTexture("_Basecolor", tex); m.SetTexture("_Livery", tex); m.SetTexture("_BasecolorDmg", tex);
            var nrm = ImportTex("flat_normal.png", false, true);
            m.SetTexture("_Normal", nrm); m.SetTexture("_NormalDmg", nrm);
            m.SetTexture("_Metallic", ImportTex("duct_metallic.png", false, false));
            m.SetTexture("_AO", ImportTex("duct_ao.png", false, false));
            return (Material)save(m, $"{Dir}/MiG29_duct.mat");
        }

        static Texture2D ImportTex(string file, bool srgb, bool normal)
        {
            var dst = $"{Dir}/{file}";
            File.Copy(Path.Combine(SourceDir, file), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;   // a 4 x 256 gradient: block compression would band it
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }
    }
}
