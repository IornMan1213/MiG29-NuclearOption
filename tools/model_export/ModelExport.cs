using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Dumps the built MiG prefabs (their visible close-up meshes, hierarchy, materials and textures) for tools/model_export/build_model.py,
// which turns each into an FBX and a ready-to-paint .blend (liveries/model). Copy into the Blueprinter Editor project's
// Assets/Editor, then: Unity -batchmode -quit -projectPath <project> -executeMethod ModelExport.Run   (MODEL_EXPORT_DIR = output folder)
public static class ModelExport
{
    static readonly (string prefab, string name)[] Jets =
    {
        ("Assets/Blueprinter/Mods/mig29/MiG29.prefab", "MiG-29"),
        ("Assets/Blueprinter/Mods/mig29smt/MiG29SMT.prefab", "MiG-29SMT"),
    };

    public static void Run()
    {
        var outDir = System.Environment.GetEnvironmentVariable("MODEL_EXPORT_DIR");
        foreach (var (path, name) in Jets)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) { Debug.LogWarning($"[ModelExport] no {path}: build that jet first"); continue; }
            var dir = Path.Combine(outDir, name); Directory.CreateDirectory(dir);
            Dump(root, dir);
            Debug.Log($"[ModelExport] {name} done");
        }
    }

    static bool Active(Transform t, Transform root) { for (; t != null; t = t.parent) { if (!t.gameObject.activeSelf) return false; if (t == root) break; } return true; }

    static void Dump(GameObject root, string dir)
    {
        // only the close-up model: renderers that appear only in the far LODs are left out
        var far = new HashSet<Renderer>();
        foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = g.GetLODs(); if (lods.Length == 0) continue;
            var near = new HashSet<Renderer>(lods[0].renderers.Where(r => r != null));
            foreach (var lod in lods.Skip(1)) foreach (var r in lod.renderers) if (r != null && !near.Contains(r)) far.Add(r);
        }
        var keep = new List<(Transform t, Mesh m, Material[] mats)>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (far.Contains(r) || !r.enabled || !Active(r.transform, root.transform)) continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // particles, trails, lines
            Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
            if (m == null || m.vertexCount == 0) continue;
            keep.Add((r.transform, m, r.sharedMaterials));
        }
        // nodes: every transform on the way from the root to a kept renderer
        var nodes = new List<Transform>();
        void Add(Transform t) { if (t == null || nodes.Contains(t)) return; if (t != root.transform) Add(t.parent); nodes.Add(t); }
        foreach (var k in keep) Add(k.t);
        var texIds = new Dictionary<Texture, string>();
        var sb = new StringBuilder("{\"nodes\":[");
        for (int i = 0; i < nodes.Count; i++)
        {
            var t = nodes[i];
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"name\":\"{Esc(t.name)}\",\"parent\":{(t == root.transform ? -1 : nodes.IndexOf(t.parent))},");
            sb.Append($"\"p\":{V(t.localPosition)},\"r\":[{F(t.localRotation.x)},{F(t.localRotation.y)},{F(t.localRotation.z)},{F(t.localRotation.w)}],\"s\":{V(t.localScale)}");
            var k = keep.FirstOrDefault(x => x.t == t);
            if (k.m != null)
            {
                var m = k.m;
                File.WriteAllText(Path.Combine(dir, $"mesh_{i}.json"), MeshJson(m));
                sb.Append($",\"mesh\":\"mesh_{i}.json\",\"mats\":[");
                for (int j = 0; j < m.subMeshCount; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(MatJson(j < k.mats.Length ? k.mats[j] : null, dir, texIds));
                }
                sb.Append(']');
            }
            sb.Append('}');
        }
        sb.Append("]}");
        File.WriteAllText(Path.Combine(dir, "scene.json"), sb.ToString());
    }

    static string MatJson(Material mat, string dir, Dictionary<Texture, string> texIds)
    {
        if (mat == null) return "{\"name\":\"none\"}";
        string Tex(string prop)
        {
            if (!mat.HasProperty(prop) || !(mat.GetTexture(prop) is Texture2D t)) return null;
            if (!texIds.TryGetValue(t, out var file))
            {
                file = $"{Safe(t.name)}_{texIds.Count}.png";
                bool normal = prop.IndexOf("bump", System.StringComparison.OrdinalIgnoreCase) >= 0 || prop.IndexOf("normal", System.StringComparison.OrdinalIgnoreCase) >= 0;
                SavePng(t, Path.Combine(dir, file), normal); texIds[t] = file;
            }
            return file;
        }
        // URP Lit names first, then the game's shader graphs (_Basecolor, _Normal)
        string Find(params string[] keys) => mat.GetTexturePropertyNames().Where(pn => keys.Any(k => pn.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)).Select(Tex).FirstOrDefault(f => f != null);
        var col = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        var bc = Tex("_BaseMap") ?? Tex("_MainTex") ?? Find("albedo", "base", "main", "diffuse", "color");
        var nm = Tex("_BumpMap") ?? Find("normal", "bump");
        float sm = mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.3f, me = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
        bool clear = mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f;
        return $"{{\"name\":\"{Esc(mat.name)}\",\"color\":[{F(col.r)},{F(col.g)},{F(col.b)},{F(col.a)}],\"base\":{(bc == null ? "null" : $"\"{bc}\"")},\"normal\":{(nm == null ? "null" : $"\"{nm}\"")},\"smooth\":{F(sm)},\"metal\":{F(me)},\"transparent\":{(clear ? "true" : "false")}}}";
    }

    // any texture, readable or not, compressed or not: drawn into a render texture and read back
    static void SavePng(Texture2D t, string path, bool normal)
    {
        var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, normal ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
        Graphics.Blit(t, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var o = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, normal);
        o.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); o.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(path, o.EncodeToPNG());
        Object.DestroyImmediate(o);
    }

    static string MeshJson(Mesh m)
    {
        var sb = new StringBuilder("{\"v\":[");
        sb.Append(string.Join(",", m.vertices.Select(v => $"{F(v.x)},{F(v.y)},{F(v.z)}")));
        sb.Append("],\"n\":[");
        sb.Append(string.Join(",", m.normals.Select(v => $"{F(v.x)},{F(v.y)},{F(v.z)}")));
        sb.Append("],\"uv\":[");
        var uv = m.uv; sb.Append(uv.Length == m.vertexCount ? string.Join(",", uv.Select(v => $"{F(v.x)},{F(v.y)}")) : "");
        sb.Append("],\"sub\":[");
        for (int j = 0; j < m.subMeshCount; j++) { if (j > 0) sb.Append(','); sb.Append('[').Append(string.Join(",", m.GetTriangles(j))).Append(']'); }
        sb.Append("]}");
        return sb.ToString();
    }

    static string F(float f) => f.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
}
