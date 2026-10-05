using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // Wheel-well interiors (tools/gear_wells.py -> MiG29Source/gear_wells.json): the model's gear bays are bare holes in the skin that
    // showed black with the doors open (user screenshot, v0.9.0). Each well (walls on the opening rim, ceiling under the outer skin,
    // faces pointing into the bay) hangs from the part its doors hang from. The single-sided MiG doors get a primer-grey inner face
    // too, so an open door no longer turns see-through from the bay side. Not damage renderers: liveries retexture those.
    public static class MiG29Wells
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string Dir = ModDir + "/wells";
        const string SourceDir = "MiG29Source";

        [Serializable] class PartDump { public string name; public float[] vertices, normals, uvs; public int[] triangles; }
        [Serializable] class Dump { public PartDump[] parts; }

        public static void Build(Transform root, Vector3 modelOffset, Material skinTemplate, Func<UnityEngine.Object, string, UnityEngine.Object> save)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(ModDir, "wells");
            var mat = (Material)save(BayMaterial(skinTemplate), $"{Dir}/MiG29_bay.mat");
            var dump = JsonUtility.FromJson<Dump>(File.ReadAllText(Path.Combine(SourceDir, "gear_wells.json")));
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var p in dump.parts)
            {
                // the well hangs from its doors' parent (nose: both nose doors share one)
                var doorName = p.name == "nose" ? "MiG29_door_nose_L_hinge" : $"MiG29_door_{p.name}_hinge";
                var parent = all.First(t => t.name == doorName).parent;
                // MiG frame -> the parent's local frame (the well object sits at the parent's origin)
                var mesh = (Mesh)save(ToMesh(p, q => parent.InverseTransformPoint(q + modelOffset), parent.InverseTransformDirection), $"{Dir}/MiG29_well_{p.name}.asset");
                var o = new GameObject("MiG29_well_" + p.name);
                o.transform.SetParent(parent, false);
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                o.AddComponent<MeshRenderer>().sharedMaterial = mat;
                Debug.Log($"[MiG29] well {p.name}: {p.triangles.Length / 3} tris under {parent.name}");
            }
            // inner faces of the doors: the door mesh with its winding reversed, primer grey
            foreach (var door in all.Where(t => t.name.StartsWith("MiG29_door_") && !t.name.EndsWith("_hinge")).ToList())
            {
                var src = door.GetComponent<MeshFilter>().sharedMesh;
                var tris = src.triangles;
                for (int i = 0; i < tris.Length; i += 3) { var t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
                var v = src.vertices;
                var m = new Mesh
                {
                    name = src.name + "_inner", vertices = v, normals = src.normals.Select(n => -n).ToArray(),
                    uv = v.Select(q => new Vector2((q.x + q.z) / 0.6f, (q.y + q.z) / 0.6f)).ToArray(), triangles = tris,
                };
                m.RecalculateBounds(); m.RecalculateTangents();
                m = (Mesh)save(m, $"{Dir}/{m.name}.asset");
                var o = new GameObject(door.name + "_inner");
                o.transform.SetParent(door, false);
                o.AddComponent<MeshFilter>().sharedMesh = m;
                o.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        static Mesh ToMesh(PartDump p, Func<Vector3, Vector3> point, Func<Vector3, Vector3> dir)
        {
            int n = p.vertices.Length / 3;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = point(new Vector3(p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]));
                nr[i] = dir(new Vector3(p.normals[i * 3], p.normals[i * 3 + 1], p.normals[i * 3 + 2]));
                uv[i] = new Vector2(p.uvs[i * 2], p.uvs[i * 2 + 1]);
            }
            var m = new Mesh { name = "MiG29_well_" + p.name, vertices = v, normals = nr, uv = uv, triangles = p.triangles };
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
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static Material BayMaterial(Material skinTemplate)
        {
            var atlas = ImportTex("bay_atlas.png", true, false);
            var m = new Material(skinTemplate) { name = "MiG29_bay" };
            m.SetTexture("_Basecolor", atlas); m.SetTexture("_Livery", atlas); m.SetTexture("_BasecolorDmg", atlas);
            var nrm = ImportTex("flat_normal.png", false, true);
            m.SetTexture("_Normal", nrm); m.SetTexture("_NormalDmg", nrm);
            m.SetTexture("_Metallic", ImportTex("bay_metallic.png", false, false));
            m.SetTexture("_AO", null);
            return m;
        }
    }
}
