using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MiG29Tools
{
    // Presentation polish for the MiG-29: opening canopy, MiG damage display + map icon (rasterised from the MiG's own
    // per-part geometry), liveries, loading screens.
    public static class MiG29Polish
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string UiDir = ModDir + "/ui";
        const string StatusBase = "Assets/Blueprinter/_donotship/GameObject/StatusDisplay_Multirole1_PLACEHOLDER.prefab";

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        static Transform Find(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t == null) throw new Exception("[MiG29] transform not found: " + name);
            return t;
        }

        static void EnsureUiDir()
        {
            if (!AssetDatabase.IsValidFolder(UiDir)) AssetDatabase.CreateFolder(ModDir, "ui");
        }

        // ---------------- canopy ----------------

        // MiG frame (before ModelOffset): windscreen arch and rear canopy hinge, measured from the model (blender/canopy_frame.py)
        const float WindscreenZ = 7.15f;
        static readonly Vector3 CanopyHinge = new Vector3(0f, 0.80f, 5.66f);
        const float CanopyOpenDeg = -42f; // negative: front of the canopy lifts (Unity +X rotation tilts forward down)

        public static void SetupCanopy(GameObject go, Vector3 modelOffset, Renderer migGlass, Func<Mesh, string, Mesh> saveMesh, List<Renderer> exterior)
        {
            var root = go.transform;
            var canopy = go.GetComponentInChildren(T("Canopy"), true);
            var so = new SerializedObject(canopy);
            var hinges = so.FindProperty("canopyHinges");
            var krHinge = hinges.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue as Transform;
            var frameR = Find(root, "canopyFrame_R"); // the Canopy's ejectionTransform

            // split the glass: windscreen stays fixed, canopy opens
            var mf = migGlass.GetComponent<MeshFilter>();
            var src = mf.sharedMesh;
            var v = src.vertices; var n = src.normals; var uv = src.uv; var tri = src.triangles;
            var front = new List<int>(); var back = new List<int>();
            float splitWorldZ = WindscreenZ + modelOffset.z;
            for (int i = 0; i < tri.Length; i += 3)
            {
                var c = migGlass.transform.TransformPoint((v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f);
                (c.z > splitWorldZ ? front : back).AddRange(new[] { tri[i], tri[i + 1], tri[i + 2] });
            }
            Mesh Sub(List<int> t, string name)
            {
                var m = new Mesh { name = name, vertices = v, normals = n, uv = uv, triangles = t.ToArray() };
                m.RecalculateBounds();
                return saveMesh(m, $"{ModDir}/meshes/{name}.asset");
            }
            mf.sharedMesh = Sub(front, "MiG29_windscreen");
            var glass = new GameObject("MiG29_canopy_glass");
            glass.AddComponent<MeshFilter>().sharedMesh = Sub(back, "MiG29_canopy_moving");
            var glassR = glass.AddComponent<MeshRenderer>(); glassR.sharedMaterials = migGlass.sharedMaterials;

            // pivot on the rear sill: canopyHinge (now static) > MiG pivot > canopyFrame_R (ejects) > MiG glass
            var pivot = new GameObject("MiG29_canopyHinge").transform;
            pivot.SetParent(krHinge, false);
            pivot.SetPositionAndRotation(CanopyHinge + modelOffset, Quaternion.identity);
            frameR.SetParent(pivot, true);
            glass.transform.SetParent(frameR, false);
            glass.transform.SetPositionAndRotation(migGlass.transform.position, migGlass.transform.rotation);

            hinges.GetArrayElementAtIndex(0).FindPropertyRelative("transform").objectReferenceValue = pivot;
            hinges.GetArrayElementAtIndex(0).FindPropertyRelative("hingeAngle").floatValue = CanopyOpenDeg;
            var glassList = so.FindProperty("glassRenderers");
            foreach (var r in new[] { migGlass, glassR })
            {
                glassList.arraySize++;
                glassList.GetArrayElementAtIndex(glassList.arraySize - 1).objectReferenceValue = r;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            exterior.Add(glassR);
            Debug.Log($"[MiG29] canopy: {back.Count / 3} moving / {front.Count / 3} windscreen tris, hinge {pivot.position}");
        }

        // ---------------- gear doors ----------------

        // MiG frame door hinges (measured from the model's closed-door mesh, blender/doors.py): (name, hinge, open Z angle, gear wheel)
        static readonly (string name, Vector3 hinge, float openZ, string wheel)[] Doors =
        {
            ("nose_L", new Vector3(-0.29f, -0.16f, 4.9f), -90f, "wheel_F"),
            ("nose_R", new Vector3(0.29f, -0.16f, 4.9f), 90f, "wheel_F"),
            ("main_L", new Vector3(-1.65f, 0.06f, 1.33f), -80f, "wheel_L"),
            ("main_R", new Vector3(1.65f, 0.06f, 1.33f), 80f, "wheel_R"),
        };

        static string DoorOf(Vector3 c)
        {
            float ax = Mathf.Abs(c.x);
            if (ax < 0.4f && c.z > 3.4f) return c.x < 0 ? "nose_L" : "nose_R";
            if (ax > 1.0f && ax < 1.8f && c.z > -0.3f && c.z < 2.95f) return c.x < 0 ? "main_L" : "main_R";
            return null; // tiny fittings: dropped
        }

        // The MiG's own doors open with the gear (LandingGear.gearDoors: localEulerAngles lerp closed -> open).
        public static void SetupGearDoors(GameObject go, Vector3 modelOffset, float[] verts, float[] norms, float[] uvs, int[] tris, Material mat,
            Func<Mesh, string, Mesh> saveMesh, Action<Transform, Renderer> addDamage)
        {
            var root = go.transform;
            var lgType = T("LandingGear");
            int n = verts.Length / 3;
            var V = new Vector3[n]; var N = new Vector3[n]; var U = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                V[i] = new Vector3(verts[i * 3], verts[i * 3 + 1], verts[i * 3 + 2]);
                N[i] = new Vector3(norms[i * 3], norms[i * 3 + 1], norms[i * 3 + 2]);
                U[i] = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
            }
            var groups = new Dictionary<string, List<int>>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                var d = DoorOf((V[tris[i]] + V[tris[i + 1]] + V[tris[i + 2]]) / 3f);
                if (d == null) continue;
                if (!groups.TryGetValue(d, out var l)) groups[d] = l = new List<int>();
                l.AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
            }
            foreach (var (name, hinge, openZ, wheel) in Doors)
            {
                if (!groups.TryGetValue(name, out var t)) continue;
                // mesh relative to the hinge
                var hv = V.Select(p => p - hinge).ToArray();
                var m = new Mesh { name = "MiG29_door_" + name, vertices = hv, normals = N, uv = U, triangles = t.ToArray() };
                m.RecalculateBounds(); m.RecalculateTangents();
                m = saveMesh(m, $"{ModDir}/meshes/MiG29_door_{name}.asset");

                var wheelT = Find(root, wheel);
                var lg = root.GetComponentsInChildren(lgType, true).First(c =>
                {
                    var h = new SerializedObject(c).FindProperty("gearHinge").objectReferenceValue as Transform;
                    return h != null && wheelT.IsChildOf(h);
                });
                var lso = new SerializedObject(lg);
                var part = lso.FindProperty("attachedPart").objectReferenceValue as Component;
                var parent = part != null ? part.transform : lg.transform.parent;

                var pivot = new GameObject("MiG29_door_" + name + "_hinge").transform;
                pivot.SetParent(parent, false);
                pivot.SetPositionAndRotation(hinge + modelOffset, Quaternion.identity);
                // LandingGear.GearDoor lerps localEulerAngles component-wise and snaps doors to localEulerAngles zero once retracted, so
                // closed must be zero and open a signed angle: as eulerAngles (0..360) the left doors opened to 270/280 deg and swung the
                // long way round, up through the fuselage (user report, v0.9.0)
                if (Quaternion.Angle(parent.rotation, Quaternion.identity) > 0.01f)
                    throw new Exception($"[MiG29] door {name}: parent {parent.name} is rotated, door angles assume an aligned parent");
                pivot.localRotation = Quaternion.identity;
                var closed = Vector3.zero;
                var open = new Vector3(0f, 0f, openZ);

                var door = new GameObject("MiG29_door_" + name);
                door.transform.SetParent(pivot, false);
                door.AddComponent<MeshFilter>().sharedMesh = m;
                var r = door.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                addDamage(parent, r);

                var doors = lso.FindProperty("gearDoors");
                doors.arraySize++;
                var e = doors.GetArrayElementAtIndex(doors.arraySize - 1);
                e.FindPropertyRelative("transform").objectReferenceValue = pivot;
                e.FindPropertyRelative("closedAngle").vector3Value = closed;
                e.FindPropertyRelative("openAngle").vector3Value = open;
                lso.ApplyModifiedPropertiesWithoutUndo();
            }
            Debug.Log($"[MiG29] gear doors: {string.Join(", ", groups.Select(g => $"{g.Key} {g.Value.Count / 3} tris"))}");
        }

        // ---------------- damage display + map icon ----------------

        class Raster
        {
            public readonly int N; public readonly float[] depth; public readonly int[] id;
            readonly Vector2 center; readonly float half;
            public Raster(int n, Vector2 c, float h) { N = n; center = c; half = h; depth = Enumerable.Repeat(float.MinValue, n * n).ToArray(); id = Enumerable.Repeat(-1, n * n).ToArray(); }
            Vector2 P(Vector3 w) => new Vector2(((w.x - center.x) / half * 0.5f + 0.5f) * N, ((w.z - center.y) / half * 0.5f + 0.5f) * N);
            public void Tri(Vector3 a, Vector3 b, Vector3 c, int part)
            {
                Vector2 pa = P(a), pb = P(b), pc = P(c);
                int x0 = Mathf.Max(0, (int)Mathf.Floor(Mathf.Min(pa.x, pb.x, pc.x))), x1 = Mathf.Min(N - 1, (int)Mathf.Ceil(Mathf.Max(pa.x, pb.x, pc.x)));
                int y0 = Mathf.Max(0, (int)Mathf.Floor(Mathf.Min(pa.y, pb.y, pc.y))), y1 = Mathf.Min(N - 1, (int)Mathf.Ceil(Mathf.Max(pa.y, pb.y, pc.y)));
                float area = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
                if (Mathf.Abs(area) < 1e-6f) return;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float w0 = ((pb.x - p.x) * (pc.y - p.y) - (pb.y - p.y) * (pc.x - p.x)) / area;
                        float w1 = ((pc.x - p.x) * (pa.y - p.y) - (pc.y - p.y) * (pa.x - p.x)) / area;
                        float w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        int k = y * N + x;
                        float h = w0 * a.y + w1 * b.y + w2 * c.y; // per-pixel height: part borders follow the real surface intersections
                        if (h > depth[k]) { depth[k] = h; id[k] = part; }
                    }
            }
        }

        static Sprite SaveSprite(Color32[] px, int n, string name)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            var path = $"{UiDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.maxTextureSize = n;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void SmoothParts(int[] id, int n, int partCount, int radius)
        {
            var best = new int[n * n]; var bestScore = new int[n * n];
            for (int k = 0; k < best.Length; k++) { best[k] = id[k]; bestScore[k] = -1; }
            var sat = new int[(n + 1) * (n + 1)];
            for (int pi = 0; pi < partCount; pi++)
            {
                bool any = false;
                for (int y = 0; y < n; y++)
                {
                    int row = 0;
                    for (int x = 0; x < n; x++)
                    {
                        if (id[y * n + x] == pi) { row++; any = true; }
                        sat[(y + 1) * (n + 1) + x + 1] = sat[y * (n + 1) + x + 1] + row;
                    }
                }
                if (!any) continue;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int k = y * n + x;
                        if (id[k] < 0) continue;
                        int x0 = Mathf.Max(0, x - radius), x1 = Mathf.Min(n, x + radius + 1), y0 = Mathf.Max(0, y - radius), y1 = Mathf.Min(n, y + radius + 1);
                        int c = sat[y1 * (n + 1) + x1] - sat[y0 * (n + 1) + x1] - sat[y1 * (n + 1) + x0] + sat[y0 * (n + 1) + x0];
                        if (c > bestScore[k]) { bestScore[k] = c; best[k] = pi; }
                    }
            }
            Array.Copy(best, id, id.Length);
        }

        public class Displays { public Sprite mapIcon; public GameObject statusDisplay; }

        public static Displays BuildDisplays(GameObject go)
        {
            EnsureUiDir();
            var upType = T("UnitPart");
            var parts = go.GetComponentsInChildren(upType, true).ToList();
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c, int part)>();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("MiG29_")))
            {
                if (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;
                var part = r.GetComponentInParent(upType);
                int pi = parts.IndexOf(part);
                var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                    tris.Add((r.transform.TransformPoint(v[t[i]]), r.transform.TransformPoint(v[t[i + 1]]), r.transform.TransformPoint(v[t[i + 2]]), pi));
            }
            float xmin = tris.Min(q => Mathf.Min(q.a.x, q.b.x, q.c.x)), xmax = tris.Max(q => Mathf.Max(q.a.x, q.b.x, q.c.x));
            float zmin = tris.Min(q => Mathf.Min(q.a.z, q.b.z, q.c.z)), zmax = tris.Max(q => Mathf.Max(q.a.z, q.b.z, q.c.z));
            var center = new Vector2((xmin + xmax) / 2, (zmin + zmax) / 2);
            float half = Mathf.Max(xmax - xmin, zmax - zmin) / 2 * 1.06f;

            // status: one id map at 1024 (outline) and per-part masks at 256
            const int big = 1024, small = 256;
            var ids = new Raster(big, center, half);
            foreach (var q in tris) ids.Tri(q.a, q.b, q.c, q.part);
            // smooth part borders: the airframe split follows the model's triangulation, which zigzags along the wing roots.
            // Each inside pixel takes the part that covers most of a (2R+1)^2 box around it (integral images, one per part);
            // the silhouette itself is unchanged.
            SmoothParts(ids.id, big, parts.Count, radius: 9);
            SmoothParts(ids.id, big, parts.Count, radius: 5);
            var outline = new Color32[big * big];
            for (int y = 0; y < big; y++)
                for (int x = 0; x < big; x++)
                {
                    int k = y * big + x, me = ids.id[k];
                    bool edge = false;
                    for (int dy = -2; dy <= 2 && !edge; dy++)
                        for (int dx = -2; dx <= 2 && !edge; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            int other = (xx < 0 || yy < 0 || xx >= big || yy >= big) ? -1 : ids.id[yy * big + xx];
                            if (other != me && (me >= 0 || other >= 0)) edge = true;
                        }
                    outline[k] = edge ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            var outlineSprite = SaveSprite(outline, big, "MiG29_status_outline");

            // per-part images: downsampled from the filtered id map (same borders as the outline), coverage as alpha
            var partSprites = new Dictionary<string, Sprite>();
            const int f = big / small;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var px = new Color32[small * small]; int count = 0;
                for (int y = 0; y < small; y++)
                    for (int x = 0; x < small; x++)
                    {
                        int hits = 0;
                        for (int dy = 0; dy < f; dy++)
                            for (int dx = 0; dx < f; dx++)
                                if (ids.id[(y * f + dy) * big + x * f + dx] == pi) hits++;
                        if (hits == 0) continue;
                        px[y * small + x] = new Color32(220, 220, 220, (byte)(255 * hits / (f * f)));
                        count++;
                    }
                if (count > 0) partSprites[parts[pi].name] = SaveSprite(px, small, "MiG29_status_" + parts[pi].name);
            }

            // map icon: whole silhouette, nose up, white
            const int icon = 128;
            var sil = new Raster(icon, center, half);
            foreach (var q in tris) sil.Tri(q.a, q.b, q.c, 0);
            var ipx = sil.id.Select(i => i >= 0 ? new Color32(245, 245, 245, 255) : new Color32(0, 0, 0, 0)).ToArray();
            var mapIcon = SaveSprite(ipx, icon, "MiG29_mapIcon");

            // status display prefab: KR-67 layout, MiG art, every part image full-frame over the outline
            var disp = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StatusBase));
            PrefabUtility.UnpackPrefabInstance(disp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            disp.name = "StatusDisplay_MiG29";
            var rootRect = (RectTransform)disp.transform;
            disp.GetComponent<Image>().sprite = outlineSprite;
            foreach (var img in disp.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject == disp) continue;
                if (img.name.StartsWith("ENGINE FIRE") || img.transform.parent != disp.transform) continue;
                if (img.name == "Multirole1") img.gameObject.name = go.name; // root part name
                var rt = (RectTransform)img.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero; rt.sizeDelta = rootRect.sizeDelta; rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
                if (partSprites.TryGetValue(img.name, out var s)) { img.sprite = s; img.enabled = true; }
                else { img.sprite = null; img.enabled = false; }
            }
            // engine fire lamps over the MiG engines
            foreach (var side in new[] { "L", "R" })
            {
                var lamp = disp.transform.Find("ENGINE FIRE " + side) as RectTransform;
                var eng = Find(go.transform, "engine_" + side);
                if (lamp == null) continue;
                var p = new Vector2(eng.position.x - center.x, eng.position.z - center.y) / half * 0.5f;
                lamp.anchoredPosition = new Vector2(p.x * rootRect.sizeDelta.x, p.y * rootRect.sizeDelta.y);
            }
            var path = $"{UiDir}/StatusDisplay_MiG29.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(disp, path);
            UnityEngine.Object.DestroyImmediate(disp);
            Debug.Log($"[MiG29] damage display: {partSprites.Count} part images; map icon built");
            return new Displays { mapIcon = mapIcon, statusDisplay = prefab };
        }

        // ---------------- liveries ----------------

        static Texture2D ImportColor(string file)
        {
            EnsureUiDir();
            var dst = $"{ModDir}/textures/{file}";
            File.Copy(Path.Combine("MiG29Source", file), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.maxTextureSize = 2048; imp.sRGBTexture = true; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static string Livery(string name, Texture2D tex, Color32 weaponColor)
        {
            var path = $"{ModDir}/{name}.asset";
            var livery = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (livery == null) { livery = ScriptableObject.CreateInstance(T("LiveryData")); AssetDatabase.CreateAsset(livery, path); }
            var lso = new SerializedObject(livery);
            lso.FindProperty("Texture").objectReferenceValue = tex;
            lso.FindProperty("Glossiness").floatValue = 0f;
            var colors = lso.FindProperty("Colors");
            colors.arraySize = 1;
            colors.GetArrayElementAtIndex(0).FindPropertyRelative("Color").colorValue = weaponColor;
            colors.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 1;
            lso.ApplyModifiedPropertiesWithoutUndo();
            return AssetDatabase.AssetPathToGUID(path);
        }

        // Four schemes, all selectable by either faction; Boscali defaults to grey, PALA to desert.
        public static void SetLiveries(SerializedObject pso)
        {
            var grey = Livery("MiG29_livery", AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/textures/mig29_basecolor.png"), new Color32(170, 178, 178, 255));
            var desert = Livery("MiG29_livery_desert", ImportColor("mig29_basecolor_desert.png"), new Color32(196, 172, 128, 255));
            var digital = Livery("MiG29_livery_digital", ImportColor("mig29_basecolor_digital.png"), new Color32(150, 160, 170, 255));
            var display = Livery("MiG29_livery_display", ImportColor("mig29_basecolor_display.png"), new Color32(40, 70, 140, 255));

            var liveries = pso.FindProperty("liveries");
            var factions = new List<(UnityEngine.Object faction, bool pala)>();
            for (int i = 0; i < liveries.arraySize; i++)
            {
                var e = liveries.GetArrayElementAtIndex(i);
                var f = e.FindPropertyRelative("faction").objectReferenceValue;
                if (factions.Any(x => x.faction == f)) continue;
                factions.Add((f, e.FindPropertyRelative("name").stringValue.ToUpperInvariant().Contains("PALA")));
            }
            liveries.arraySize = factions.Count * 4;
            int k = 0;
            foreach (var (faction, pala) in factions)
            {
                var order = pala ? new[] { ("Fulcrum Desert Tan", desert), ("Fulcrum Two-Tone Grey", grey), ("Fulcrum Digital Grey", digital), ("Fulcrum Display Blue", display) }
                                 : new[] { ("Fulcrum Two-Tone Grey", grey), ("Fulcrum Desert Tan", desert), ("Fulcrum Digital Grey", digital), ("Fulcrum Display Blue", display) };
                foreach (var (name, guid) in order)
                {
                    var e = liveries.GetArrayElementAtIndex(k++);
                    e.FindPropertyRelative("name").stringValue = name;
                    e.FindPropertyRelative("faction").objectReferenceValue = faction;
                    e.FindPropertyRelative("assetReference.m_AssetGUID").stringValue = guid;
                    e.FindPropertyRelative("assetReference.m_SubObjectName").stringValue = string.Empty;
                }
            }
        }

        // ---------------- loading screens ----------------

        public static void BuildLoadingScreens()
        {
            EnsureUiDir();
            var sprites = new List<Sprite>();
            foreach (var file in new[] { "loading_mig29_bank.png", "loading_mig29_climb.png", "loading_mig29_digital.png", "loading_mig29_display.png" })
            {
                var dst = $"{UiDir}/{file}";
                File.Copy(Path.Combine("MiG29Source", file), dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.mipmapEnabled = false; imp.maxTextureSize = 2048; imp.npotScale = TextureImporterNPOTScale.None;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
                sprites.Add(AssetDatabase.LoadAssetAtPath<Sprite>(dst));
            }
            var path = ModDir + "/OpAddLoadingScreens.asset";
            var op = AssetDatabase.LoadAssetAtPath<Blueprinter.Editor.Ops.OpAddLoadingScreens>(path);
            if (op == null) { op = ScriptableObject.CreateInstance<Blueprinter.Editor.Ops.OpAddLoadingScreens>(); AssetDatabase.CreateAsset(op, path); }
            op.images = sprites.ToArray();
            EditorUtility.SetDirty(op);
        }
    }
}
