using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // The KR-67's dead leftovers, deleted. The MiG is built on a copy of the KR-67 prefab, and 298 of its 299 objects stayed in the
    // MiG (object count, 2026-10-30): most carry the game's systems or the points they use and must stay, but many are empty shells
    // whose mesh the builder removed (HideBaseExterior) or bare pivots nothing uses any more. An object goes only if all of these hold:
    //   - it has no children and nothing but a Transform, a MeshFilter with no mesh and a MeshRenderer (or just the Transform);
    //   - no component in the prefab points at it or at its components, except the LOD group (taken out of the LOD lists here) and
    //     transform parent links;
    //   - it is one of the KR-67's own objects (its name is in the base prefab; the MiG's own objects include plain names the plugin
    //     finds them by: "needle", "folded", "off"), and its name appears as no string in the game's code (the game may find an
    //     object by name, which no reference shows).
    // Run before MiG29Lod.Apply (which rebuilds the LOD levels from the KR-67's).
    static class MiG29Cleanup
    {
        public static void RemoveDeadShells(GameObject go, HashSet<string> krNames, HashSet<string> gameStrings)
        {
            if (gameStrings == null) return;
            var root = go.transform;
            var referenced = new HashSet<GameObject>();
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is LODGroup) continue;
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var o = it.objectReferenceValue;
                    var g = o is GameObject og ? og : o is Component oc ? oc.gameObject : null;
                    if (g != null && g != c.gameObject) referenced.Add(g);
                }
            }

            bool Dead(Transform t)
            {
                if (t == root || t.childCount > 0) return false;
                var n = t.name;
                if (!krNames.Contains(n) || gameStrings.Contains(n)) return false;   // the KR-67's own objects only: the MiG's have names the plugin finds them by ("needle", "folded", "off")
                if (referenced.Contains(t.gameObject)) return false;
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c is Transform || c is MeshRenderer) continue;
                    if (c is MeshFilter mf && mf.sharedMesh == null) continue;
                    return false;   // a mesh still drawn, a collider, a game component...
                }
                return true;
            }

            // leaves first: a pivot whose only children were dead shells becomes a dead leaf itself on the next pass
            var removed = new List<string>();
            for (int pass = 0; pass < 4; pass++)
            {
                var dead = root.GetComponentsInChildren<Transform>(true).Where(Dead).ToList();
                if (dead.Count == 0) break;
                foreach (var t in dead) { removed.Add(t.name); Object.DestroyImmediate(t.gameObject); }
            }

            var lg = go.GetComponent<LODGroup>();
            if (lg != null)
            {
                var lods = lg.GetLODs();
                for (int i = 0; i < lods.Length; i++) lods[i].renderers = lods[i].renderers.Where(r => r != null).ToArray();
                lg.SetLODs(lods);
            }
            Debug.Log($"[MiG29] cleanup: {removed.Count} dead KR-67 objects removed ({string.Join(", ", removed.Take(12))}{(removed.Count > 12 ? ", ..." : "")})");
        }

        // The game's string literals (names it may look objects up by), from the installed game's Assembly-CSharp (the editor
        // project's copy of the game code may not hold them). Not found: null, and RemoveDeadShells removes nothing.
        public static HashSet<string> GameStrings()
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            var paths = new[]
            {
                System.Environment.GetEnvironmentVariable("NO_GAME_DLL"),
                System.IO.Path.Combine(home, ".steam/steam/steamapps/common/Nuclear Option/NuclearOption_Data/Managed/Assembly-CSharp.dll"),
                @"C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\NuclearOption_Data\Managed\Assembly-CSharp.dll",
            };
            var path = paths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && System.IO.File.Exists(p));
            if (path == null) { Debug.LogWarning("[MiG29] cleanup: the game's Assembly-CSharp.dll not found (set NO_GAME_DLL): nothing removed"); return null; }
            // user strings are UTF-16: scan the file at both byte alignments for printable runs (finds them all, plus some noise)
            var set = new HashSet<string>();
            var b = System.IO.File.ReadAllBytes(path);
            var sb = new System.Text.StringBuilder();
            for (int start = 0; start < 2; start++)
            {
                sb.Clear();
                for (int i = start; i + 1 < b.Length; i += 2)
                {
                    char ch = (char)(b[i] | (b[i + 1] << 8));
                    if (ch >= 0x20 && ch < 0x7f) { sb.Append(ch); continue; }
                    if (sb.Length >= 3) set.Add(sb.ToString());
                    sb.Clear();
                }
            }
            return set;
        }
    }
}
