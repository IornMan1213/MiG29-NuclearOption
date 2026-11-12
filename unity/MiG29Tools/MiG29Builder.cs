using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // Builds the "mig29" Blueprinter mod from the KR-67 Ifrit (Multirole1):
    // KR-67 flight systems + the Sketchfab MiG-29 visual model (MiG29Source/mig29_mesh.json, exported by blender/export_mig29.py).
    public static class MiG29Builder
    {
        const string ModName = "mig29";
        const string ModDir = "Assets/Blueprinter/Mods/" + ModName;
        const string SourceDir = "MiG29Source";
        const string BasePrefab = "Assets/Blueprinter/_donotship/GameObject/Multirole1_PLACEHOLDER.prefab";
        const string BaseDefinition = "Assets/Blueprinter/_donotship/MonoBehaviour/Multirole1_PLACEHOLDER.asset";
        const string BaseParameters = "Assets/Blueprinter/_donotship/MonoBehaviour/Multirole1_parameters_PLACEHOLDER.asset";
        const string BaseSkin = "Assets/Blueprinter/_donotship/Material/Multirole1_skin_PLACEHOLDER.mat";

        public const string JsonKey = "mig29_Fulcrum";
        public const string DisplayName = "MiG-29 Fulcrum";
        public const string Version = "0.9.4";

        // MiG model frame -> aircraft root. Puts MiG main wheels on the KR-67 main gear and MiG wheels on KR-67 ground line.
        static readonly Vector3 ModelOffset = new Vector3(0f, -0.44f, -2.655f);
        // KR-67 cockpit interior / pilot shifted to sit under the MiG canopy.
        const int JointIterations = 30;   // PartJoint.solverIterations on every part (stock 6), see the mass-scaling step
        const float ThrustLineY = 0.0f;   // prefab frame: a first thrust height; the MiG part balance moves it to the centre of gravity
        static readonly Vector3 CockpitShift = new Vector3(0f, -0.44f, -2.23f);

        // KR-67 renderers that stay visible (moving gear, effects, cockpit interior, pilot).
        static readonly string[] KeepVisible =
        {
            "gear_", "wheel_", "axle_", "bumpstop", "castPoint", "afterburner", "heat_haze", "pilot", "_int", "tacScreen",
            "joystick", "throttle", "warningLights", "EjectionSeat", "gearLight", "chocks",
        };

        // gear arms (drag struts) aim at fixed targets and stick out of the MiG skin once the gear folds
        static readonly string[] HiddenAnyway = { "_arm", "gearDoor", "gearbay", "LOD", "canopy_F", "canopy_R", "canopyFrame_F", "canopyFrame_R" };

        [MenuItem("MiG29/1. Build MiG-29 prefab + definitions")]
        public static void BuildAssets()
        {
            var data = JsonUtility.FromJson<MeshDump>(File.ReadAllText(Path.Combine(SourceDir, "mig29_mesh.json")));
            // the forward main-bay doors are cut out of the trunk skin (tools/main_bay_door.py, original triangle order), then the
            // model's nozzle cans give way to the from-scratch RD-33 nozzles (MiG29Nozzles.Build below)
            foreach (var p in data.parts.Where(p => p.name == "body"))
            {
                MiG29MainGear.CutBodySkin(ref p.vertices, ref p.normals, ref p.uvs, ref p.triangles);
                p.triangles = MiG29Nozzles.StripModelNozzles(p.vertices, p.triangles, out int stripped);
                Debug.Log($"[MiG29] nozzles: {stripped} model nozzle triangles stripped");
                p.triangles = MiG29Duct.StripCaps(p.vertices, p.triangles, out int caps);   // the ducts run on to the engines (MiG29Duct)
                Debug.Log($"[MiG29] intakes: {caps} duct end-cap triangles stripped");
            }
            MoveStabWicks(data);
            AssetDatabase.DeleteAsset(ModDir + "/meshes"); // no stale meshes in the bundle
            EnsureFolder(ModDir); EnsureFolder(ModDir + "/meshes"); EnsureFolder(ModDir + "/textures"); EnsureFolder(ModDir + "/materials");

            var materials = BuildMaterials();
            var meshes = data.parts.ToDictionary(p => p.name, p => BuildMesh(p));
            var weapons = MiG29Weapons.Build((o, path) => CreateOrReplace(o, path), materials.skin);

            // --- prefab ---
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = "MiG29";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var root = go.transform;

            HideBaseExterior(root);
            MiG29NoseGear.Build(root, null, (o, path) => CreateOrReplace(o, path));   // before the nose gear's stow search, which fits its meshes
            MoveGearAndEffects(root, data.info);
            MiG29MainGear.Setup(root, ModelOffset, materials.skin, TypeByName("LandingGear"), (o, path) => CreateOrReplace(o, path));
            MiG29NoseGear.SetMaterial(root, AssetDatabase.LoadAssetAtPath<Material>(ModDir + "/materials/MiG29_gear.mat"));
            LogStowed(root, "wheel_F");
            // from here on the parts are the MiG's own (MiG29Parts)
            MiG29Parts.Restructure(root, 16040f * MassScale, FuelScale, JointIterations);

            var visual = new GameObject("MiG29_visual").transform;
            visual.SetParent(root, false);
            visual.localPosition = ModelOffset;
            // canopy glass rides on the KR-67 cockpit part; the MiG model's own cockpit is replaced by MiG29Cockpit (from scratch)
            var exterior = new List<Renderer>();
            var cockpitPart = Find(root, "cockpit_section");
            Renderer migCanopy = null;
            foreach (var part in data.parts.Where(p => p.name == "canopy"))
            {
                var r = AddMeshObject("MiG29_" + part.name, cockpitPart, meshes[part.name], part.material == "glass" ? materials.glass : materials.skin);
                r.transform.SetPositionAndRotation(visual.position, Quaternion.identity);
                exterior.Add(r);
                AddDamageRenderer(cockpitPart, r);
                if (part.name == "canopy") migCanopy = r;
            }
            var surfaces = new List<(Component part, Renderer renderer)>();
            foreach (var part in data.parts.Where(p => p.pivot != null && p.pivot.Length == 3))
                surfaces.Add(AddControlSurface(root, part, meshes[part.name], materials.skin));

            // airframe split into the KR-67 damage parts: pieces detach with their part, get damage shading,
            // and become that part's hitbox
            var airframe = data.parts.Where(p => (p.pivot == null || p.pivot.Length != 3) && p.name != "canopy" && p.name != "cockpit" && p.name != "gear_doors_closed" && !p.name.StartsWith("pylon_")).ToList();
            SplitAirframe(root, visual, airframe, materials.skin, surfaces);
            // the cockpit part's pivot is in the radome, 2.3 m ahead of the cockpit: its break-up effects go to the cockpit itself
            AddAnchor(cockpitPart, FxAnchor, new Vector3(data.info.cockpit_center[0], data.info.cockpit_center[1], data.info.cockpit_center[2]) + ModelOffset);
            UnityEngine.Object.DestroyImmediate(visual.gameObject);
            var doorsPart = data.parts.First(p => p.name == "gear_doors_closed");
            MiG29Polish.SetupGearDoors(go, ModelOffset, doorsPart.vertices, doorsPart.normals, doorsPart.uvs, doorsPart.triangles, materials.skin,
                (m, path) => CreateOrReplace(m, path), (t, r) => AddDamageRenderer(t, r));
            var fwd = MiG29MainGear.DoorMeshes();
            MiG29Polish.SetupGearDoors(go, ModelOffset, fwd.v, fwd.n, fwd.uv, fwd.t, materials.skin,
                (m, path) => CreateOrReplace(m, path), (t, r) => AddDamageRenderer(t, r), c => c.x < 0 ? "fwd_L" : "fwd_R");
            MiG29Wells.Build(root, ModelOffset, materials.skin, (o, path) => CreateOrReplace(o, path));
            MiG29Airbrakes.Build(go, ModelOffset, materials.skin, (o, path) => CreateOrReplace(o, path), (t, r) => AddDamageRenderer(t, r));
            var fm = MiG29FlightModel.Load();
            MiG29FlightModel.ApplyToPrefab(root, fm, (n, c) => CubeMesh("MiG29_col_" + n, c, 0.3f));

            MiG29Nozzles.Build(root, ModelOffset, materials.skin, (o, path) => CreateOrReplace(o, path));
            MoveCockpit(root);
            MiG29Polish.SetupCanopy(go, ModelOffset, migCanopy, (m, path) => CreateOrReplace(m, path), exterior);
            AddFodDoors(root);
            MiG29Cockpit.Build(go, cockpitPart, ModelOffset, materials.skin, materials.glass, exterior, (o, path) => CreateOrReplace(o, path), (t, r) => AddDamageRenderer(t, r));
            AppendExteriorRenderers(go, exterior);
            TuneToMiG(root);
            // thrust acts at the centre of gravity's height (see MoveGearAndEffects): the MiG's own, now that its parts are
            var cgY = MiG29Parts.Balance(root, DryCgZ);
            foreach (var side in new[] { "L", "R" })
            {
                var tt = Find(root, "thrustTransform_" + side);
                var kids = tt.Cast<Transform>().ToList(); foreach (var k in kids) k.SetParent(tt.parent, true);
                tt.position = new Vector3(tt.position.x, cgY, tt.position.z);
                foreach (var k in kids) k.SetParent(tt, true);
            }
            TuneRadar(root);
            MiG29Nozzles.RoundAfterburners(root, (o, path) => CreateOrReplace(o, path));
            bayIndices = BayIndices(go);
            RemoveInternalBays(go);
            var centreSet = MiG29Weapons.SetupCentreline(go, ModelOffset, weapons);
            var outerPylonSet = MiG29Weapons.SetupPylons(go, ModelOffset, weapons);
            MiG29Parts.Reassign(root);
            MiG29Weapons.SetupPylonFairings(go, ModelOffset, meshes.Where(kv => kv.Key.StartsWith("pylon_")).ToDictionary(kv => kv.Key, kv => kv.Value), materials.skin,
                (t, r) => AddDamageRenderer(t, r));
            var wingtipSet = MiG29Weapons.SetupWingtips(go, ModelOffset, weapons, outerPylonSet);
            MiG29Countermeasures.Setup(go, ModelOffset);
            MiG29Parts.Reassign(root);
            var oldGun = MiG29Weapons.SetupGun(go, ModelOffset, weapons);
            var displays = MiG29Polish.BuildDisplays(go);
            MiG29Cleanup.RemoveDeadShells(go, new HashSet<string>(src.GetComponentsInChildren<Transform>(true).Select(t => t.name)), MiG29Cleanup.GameStrings());   // the KR-67's dead leftovers, before the LOD levels are rebuilt
            MiG29Lod.Apply(go);   // last: every renderer exists now

            var prefabPath = ModDir + "/MiG29.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);

            // --- definitions ---
            var parameters = CloneAsset(BaseParameters, ModDir + "/MiG29_parameters.asset");
            var pso = new SerializedObject(parameters);
            pso.FindProperty("aircraftName").stringValue = "MiG-29";
            pso.FindProperty("rankRequired").intValue = 2;   // rank and price: the 9.12 rank 2 / $50 M, the SMT rank 3 / $90 M (user, 0.9.2 / SMT 0.2.0)
            RemoveBayLoadouts(pso);
            MiG29Weapons.ClearLoadoutSlot(pso, centreSet);
            MiG29Weapons.InsertLoadoutSlot(pso, outerPylonSet);
            MiG29Weapons.InsertLoadoutSlot(pso, wingtipSet);
            Debug.Log($"[MiG29] loadouts: {MiG29Weapons.ReplaceInLoadouts(pso, oldGun, weapons.gunMount)} gun entries switched to GSh-30-1");
            MiG29Weapons.SetLoadouts(pso, weapons);
            MiG29Polish.SetLiveries(pso);
            pso.FindProperty("StatusDisplay").objectReferenceValue = displays.statusDisplay;
            MiG29FlightModel.ApplyToParameters(pso, fm);
            pso.ApplyModifiedPropertiesWithoutUndo();

            var definition = CloneAsset(BaseDefinition, ModDir + "/MiG29.asset");
            var dso = new SerializedObject(definition);
            dso.FindProperty("jsonKey").stringValue = JsonKey;
            dso.FindProperty("unitName").stringValue = DisplayName;
            dso.FindProperty("code").stringValue = "MiG-29";
            dso.FindProperty("description").stringValue =
                "A twin-engine, fourth-generation air superiority fighter. The MiG-29 Fulcrum trades range and payload for agility: " +
                "a high thrust-to-weight ratio and a blended lifting body give it superb low-speed handling in close-range dogfights.";
            dso.FindProperty("length").floatValue = 17.32f;
            dso.FindProperty("width").floatValue = 11.36f;
            dso.FindProperty("height").floatValue = 4.73f;
            dso.FindProperty("value").floatValue = 50f;
            // radar size on the game's scale (stock: KR-67 0.0015 stealth, T/A-30 0.12, A-19 0.6); real MiG-29 about 5 m2
            dso.FindProperty("radarSize").floatValue = 0.2f;
            dso.FindProperty("unitPrefab").objectReferenceValue = prefab;
            dso.FindProperty("aircraftParameters").objectReferenceValue = parameters;
            dso.FindProperty("mapIcon").objectReferenceValue = displays.mapIcon;
            dso.FindProperty("mass").floatValue = 16040f * MassScale;
            dso.FindProperty("aircraftInfo.emptyWeight").floatValue = 11500f;
            dso.FindProperty("aircraftInfo.maxSpeed").floatValue = 2400f;
            dso.FindProperty("aircraftInfo.stallSpeed").floatValue = 210f;
            dso.FindProperty("aircraftInfo.maneuverability").floatValue = 9f;
            dso.FindProperty("aircraftInfo.maxWeight").floatValue = 21000f;
            dso.ApplyModifiedPropertiesWithoutUndo();

            // prefab's Unit.definition -> our definition
            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            var unit = prefabRoot.GetComponents<MonoBehaviour>().First(c => c != null && new SerializedObject(c).FindProperty("definition") != null && c.GetType().Name == "Aircraft");
            var uso = new SerializedObject(unit);
            uso.FindProperty("definition").objectReferenceValue = definition;
            uso.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);

            BuildHangarOp();
            BuildMissionOp();
            MiG29Polish.BuildLoadingScreens(); MiG29Polish.BuildHints(); MiG29Polish.BuildAryxWeapons(ModDir, new[] { (JsonKey, false) });
            ModInfo(DisplayName, Version);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MiG29] assets built");
        }

        [MenuItem("MiG29/2. Build .nobp into BepInEx/plugins")]
        public static void BuildMod()
        {
            var output = Environment.GetEnvironmentVariable("MIG29_OUT");
            if (string.IsNullOrEmpty(output))
                output = Path.GetFullPath("MiG29Build");
            Directory.CreateDirectory(output);
            Blueprinter.ModBuilder.Build(ModName, DisplayName, Version, output);
            Debug.Log("[MiG29] mod built -> " + output);
        }

        // Batchmode entry: everything in one go.
        public static void BuildAll()
        {
            try
            {
                BuildAssets();
                BuildMod();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------

        class Mats { public Material skin, glass; }

        static Mats BuildMaterials()
        {
            var tex = new Dictionary<string, Texture2D>();
            foreach (var name in new[] { "mig29_basecolor", "mig29_basecolor_dmg", "mig29_normal", "mig29_normal_dmg", "mig29_metallic" })
            {
                var dst = $"{ModDir}/textures/{name}.png";
                File.Copy(Path.Combine(SourceDir, name + ".png"), dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
                imp.maxTextureSize = 2048;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.textureType = name.Contains("normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = name.Contains("basecolor");
                imp.SaveAndReimport();
                tex[name] = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            }

            var skin = new Material(AssetDatabase.LoadAssetAtPath<Material>(BaseSkin)) { name = "MiG29_skin" };
            skin.SetTexture("_Basecolor", tex["mig29_basecolor"]);
            skin.SetTexture("_Livery", tex["mig29_basecolor"]);
            skin.SetTexture("_BasecolorDmg", tex["mig29_basecolor_dmg"]);
            skin.SetTexture("_Normal", tex["mig29_normal"]);
            // stock-format damage maps (tools/damage_textures.py): soot overlay + burn mask in alpha, dented normals
            skin.SetTexture("_NormalDmg", tex["mig29_normal_dmg"]);
            skin.SetTexture("_Metallic", tex["mig29_metallic"]);
            skin.SetTexture("_AO", null);
            skin.SetFloat("_Glossiness", 0f);
            CreateOrReplace(skin, ModDir + "/materials/MiG29_skin.mat");

            // canopy glass: reuse the KR-67's own glass material (a game asset, restored at runtime)
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
            var canopy = basePrefab.GetComponentsInChildren<Renderer>(true).First(r => r.name == "canopy_R");
            return new Mats { skin = AssetDatabase.LoadAssetAtPath<Material>(ModDir + "/materials/MiG29_skin.mat"), glass = canopy.sharedMaterial };
        }

        static Mesh BuildMesh(PartDump p)
        {
            var m = new Mesh { name = "MiG29_" + p.name };
            int n = p.vertices.Length / 3;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]);
                nr[i] = new Vector3(p.normals[i * 3], p.normals[i * 3 + 1], p.normals[i * 3 + 2]);
                uv[i] = new Vector2(p.uvs[i * 2], p.uvs[i * 2 + 1]);
            }
            m.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.vertices = v; m.normals = nr; m.uv = uv;
            m.triangles = p.triangles;
            m.RecalculateBounds();
            m.RecalculateTangents();
            return CreateOrReplace(m, $"{ModDir}/meshes/MiG29_{p.name}.asset");
        }

        static Renderer AddMeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var o = new GameObject(name);
            o.transform.SetParent(parent, false);
            o.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = o.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }

        static void HideBaseExterior(Transform root)
        {
            int hidden = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var n = mf.name;
                bool keep = KeepVisible.Any(k => n.Contains(k)) && !HiddenAnyway.Any(h => n.Contains(h) && !n.Contains("_int"));
                if (keep) continue;
                mf.sharedMesh = null;
                hidden++;
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (KeepVisible.Any(k => smr.name.Contains(k))) continue;
                smr.sharedMesh = null;
                hidden++;
            }
            Debug.Log($"[MiG29] hid {hidden} KR-67 exterior meshes");
        }

        // The static dischargers on the stabilator trailing edges (two per side, a base and a thin needle each, 12-20 cm) are loose
        // pieces of the model's body mesh, so they stayed put while the stabilators moved (user screenshot, v0.8.7). Small separate
        // body pieces inside a stabilator's bounds, out past the fuselage, go into that stabilator's mesh (stored about its pivot).
        static void MoveStabWicks(MeshDump data)
        {
            var body = data.parts.First(p => p.name == "body");
            var V = body.vertices; var T = body.triangles;
            int nv = V.Length / 3;
            Vector3 Vert(int i) => new Vector3(V[i * 3], V[i * 3 + 1], V[i * 3 + 2]);

            // connected pieces: union-find over welded positions
            var weld = new Dictionary<Vector3Int, int>(); var canon = new int[nv];
            for (int i = 0; i < nv; i++)
            {
                var v = Vert(i); var k = new Vector3Int(Mathf.RoundToInt(v.x * 1e4f), Mathf.RoundToInt(v.y * 1e4f), Mathf.RoundToInt(v.z * 1e4f));
                if (!weld.TryGetValue(k, out canon[i])) { weld[k] = i; canon[i] = i; }
            }
            var parent = Enumerable.Range(0, nv).ToArray();
            int Root(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int t = 0; t < T.Length; t += 3)
            {
                int a = Root(canon[T[t]]);
                parent[Root(canon[T[t + 1]])] = a; parent[Root(canon[T[t + 2]])] = a;
            }
            var pieces = new Dictionary<int, List<int>>();   // root -> triangle starts
            for (int t = 0; t < T.Length; t += 3)
            {
                int r = Root(canon[T[t]]);
                if (!pieces.TryGetValue(r, out var l)) pieces[r] = l = new List<int>();
                l.Add(t);
            }

            var removed = new HashSet<int>();
            foreach (var side in new[] { "L", "R" })
            {
                var stab = data.parts.First(p => p.name == "stab_" + side);
                var piv = new Vector3(stab.pivot[0], stab.pivot[1], stab.pivot[2]);
                var sb = new Bounds(new Vector3(stab.vertices[0], stab.vertices[1], stab.vertices[2]) + piv, Vector3.zero);
                for (int i = 0; i < stab.vertices.Length; i += 3) sb.Encapsulate(new Vector3(stab.vertices[i], stab.vertices[i + 1], stab.vertices[i + 2]) + piv);
                sb.Expand(0.4f);
                var sv = stab.vertices.ToList(); var sn = stab.normals.ToList(); var su = stab.uvs.ToList(); var st = stab.triangles.ToList();
                int moved = 0;
                foreach (var tris in pieces.Values)
                {
                    if (tris.Count > 100) continue;
                    var pb = new Bounds(Vert(T[tris[0]]), Vector3.zero);
                    foreach (var t in tris) for (int k = 0; k < 3; k++) pb.Encapsulate(Vert(T[t + k]));
                    if (pb.size.magnitude > 0.3f || !sb.Contains(pb.center) || Mathf.Abs(pb.center.x) < 2.5f) continue;
                    foreach (var t in tris)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            int vi = T[t + k];
                            st.Add(sv.Count / 3);
                            var p = Vert(vi) - piv;
                            sv.Add(p.x); sv.Add(p.y); sv.Add(p.z);
                            sn.Add(body.normals[vi * 3]); sn.Add(body.normals[vi * 3 + 1]); sn.Add(body.normals[vi * 3 + 2]);
                            su.Add(body.uvs[vi * 2]); su.Add(body.uvs[vi * 2 + 1]);
                        }
                        removed.Add(t);
                    }
                    moved++;
                }
                stab.vertices = sv.ToArray(); stab.normals = sn.ToArray(); stab.uvs = su.ToArray(); stab.triangles = st.ToArray();
                Debug.Log($"[MiG29] stab_{side}: {moved} static discharger pieces moved from the body");
            }
            body.triangles = Enumerable.Range(0, T.Length / 3).Where(i => !removed.Contains(i * 3)).SelectMany(i => new[] { T[i * 3], T[i * 3 + 1], T[i * 3 + 2] }).ToArray();
        }

        static Transform Find(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t == null) throw new Exception("[MiG29] transform not found: " + name);
            return t;
        }

        static void MoveWorld(Transform root, string name, Vector3 delta) => Find(root, name).position += delta;

        static void SetWorld(Transform root, string name, Vector3 world) => Find(root, name).position = world;

        static void MoveGearAndEffects(Transform root, InfoDump info)
        {
            // nose gear: KR-67 wheel_F z -> MiG nose wheel z
            var noseZ = info.nose_wheel_contact[2] + ModelOffset.z;
            var dNose = new Vector3(0, 0, noseZ - Find(root, "wheel_F").position.z);
            MoveWorld(root, "gearHinge_F", dNose);
            MoveWorld(root, "gear_F_armTarget", dNose);

            // main gear: lateral track to the MiG's
            foreach (var s in new[] { "L", "R" })
            {
                var c = s == "L" ? info.main_wheel_contact_L : info.main_wheel_contact_R;
                var d = new Vector3(c[0] - Find(root, "wheel_" + s).position.x, 0, 0);
                MoveWorld(root, "gearMount_" + s, d);
                MoveWorld(root, $"gear_{s}_armTarget", d);
            }

            // main legs, their stow and their doors: MiG29MainGear (called after this)

            PlaceLandingLight(root);
            // KR-67 nose wheel (0.66 m) fits the thin centre fuselage here: top 0.63 vs skin 0.66 at x 0.32 (blender ray scan).
            // Folded the stock 92 deg, the KR-67 strut slopes down toward its hinge and, once shifted so the wheel stows, its upper end
            // hung 15 cm below the closed bay doors (stowed check: y -0.32 at z 4.5-5.2; user screenshot, v0.8.1). The fold angle and
            // stow point are searched against the leg's real vertices, landing light included (StowGear searchBay).
            StowGear(root, "wheel_F", new Vector3(0f, 0.30f, 3.5f) + ModelOffset, 0f, searchBay: true);

            // nozzles (thrust + afterburner effects) to the MiG nozzles
            SetWorld(root, "nozzle_L", new Vector3(-0.866f, -0.292f, -3.768f) + ModelOffset);
            SetWorld(root, "nozzle_R", new Vector3(0.866f, -0.292f, -3.768f) + ModelOffset);
            // Thrust acts at thrustTransform (JetNozzle: AddForceAtPosition). At the MiG nozzles it sat 0.73 m below the centre of
            // mass, so full afterburner (~170 kN) pitched the nose up harder than the weight on the main wheels could hold: the jet
            // flipped over backwards on the runway above ~70 % throttle and pitched up uncontrollably at low speed (v0.5.1 report).
            // The thrust point goes to CG height (dry CG y ~0.00, MiG29AeroDump); the heat haze stays at the visible nozzle.
            foreach (var side in new[] { "L", "R" })
            {
                var tt = Find(root, "thrustTransform_" + side);
                foreach (var fx in tt.Cast<Transform>().ToList()) fx.SetParent(tt.parent, true);
                tt.position = new Vector3(tt.position.x, ThrustLineY, tt.position.z);
                // ...and so does the smoke of a failed engine: JetNozzle.FailureEffect spawns at the thrust point; the plugin moves it
                // to this anchor at the nozzle centre (FxAnchors.cs)
                AddAnchor(tt, FxAnchor, Find(root, "nozzle_" + side).position);
            }

            // wing pylons to the MiG rails: inner/outer
            SetWorld(root, "hardpoint_pylon_L1", new Vector3(-2.40f, -0.27f, 0.90f) + ModelOffset);
            SetWorld(root, "hardpoint_pylon_R1", new Vector3(2.40f, -0.27f, 0.90f) + ModelOffset);
            SetWorld(root, "hardpoint_pylon_L2", new Vector3(-3.10f, -0.29f, 0.24f) + ModelOffset);
            SetWorld(root, "hardpoint_pylon_R2", new Vector3(3.10f, -0.29f, 0.24f) + ModelOffset);

            // Navigation lights (red left, green right) on the MiG wingtips. The tip edge runs x 5.53-5.57, y ~-0.05, from the leading
            // edge at z ~-0.40 aft (MiG frame, mig29_mesh.json). They used to sit 5 cm outboard of and ahead of the leading-edge corner
            // with a 0.6 m glow, so the glow hung in the air off the tip (user screenshots, v0.8.1): now on the tip edge, 0.3 m aft of
            // the leading edge, with a lamp-sized glow. Vortex trails leave the trailing tip corner.
            foreach (var s in new[] { "L", "R" })
            {
                float sx = s == "L" ? -1 : 1;
                var nav = Find(root, "navlight_" + s + "_effects");
                nav.position = new Vector3(sx * 5.55f, -0.045f, -0.70f) + ModelOffset;
                foreach (var ps in nav.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.startSizeMultiplier *= 0.45f; }
                SetWorld(root, "wingtipvortex_" + s, new Vector3(sx * 5.56f, -0.05f, -1.65f) + ModelOffset);
            }
            MiG29Vapor.Apply(root, ModelOffset);
        }

        // Landing light. The KR-67 hangs it on its front gear door, 3.6 m ahead of the MiG's nose gear; NavLights swaps its flat lamp
        // panel to a glowing material whenever the gear is down. Moved to the strut, it kept the door's angle and hung beside the leg:
        // a bright panel floating in the air that lit nothing (user screenshots, v0.9.0). Now the panel sits on the front of the nose
        // strut facing ahead, the beam aimed 9 deg down to light the runway ahead, the glare sprite kept small.
        static void PlaceLandingLight(Transform root)
        {
            var lamp = Find(root, "gearLight_F");
            var sprung = Find(root, "gear_F_sprung");
            lamp.SetParent(sprung, true);
            var mf = lamp.GetComponent<MeshFilter>();
            var lr = lamp.rotation;
            var n0 = mf.sharedMesh.normals.Aggregate(Vector3.zero, (a, n) => a + lr * n);
            if (n0.sqrMagnitude < 1e-6f) n0 = -lamp.up;
            var face = new Vector3(0f, -Mathf.Sin(8f * Mathf.Deg2Rad), Mathf.Cos(8f * Mathf.Deg2Rad));
            lamp.rotation = Quaternion.FromToRotation(n0.normalized, face) * lamp.rotation;
            // strut front at lamp height (MiG frame y -0.45: on the sprung part, above the shock strut, below the bay)
            const float y = -0.45f;
            // (from the strut's axis at that height: the MiG barrel is a plain cylinder with no vertices part-way up, so the old search
            // for strut vertices at lamp height found none and left the lamp on the axis, inside the barrel)
            var up = sprung.up;
            var axisPt = sprung.position + up * ((y + ModelOffset.y - sprung.position.y) / up.y) - ModelOffset;
            float front = axisPt.z + MiG29NoseGear.BarrelRadius * sprung.lossyScale.x;
            var centre = lamp.TransformPoint(mf.sharedMesh.bounds.center);
            var target = new Vector3(axisPt.x, y, front + 0.025f) + ModelOffset;
            lamp.position += target - centre;
            var beam = Find(lamp, "gearLight_F_effects");
            var aim = new Vector3(0f, -Mathf.Sin(9f * Mathf.Deg2Rad), Mathf.Cos(9f * Mathf.Deg2Rad));
            beam.SetPositionAndRotation(target + face * 0.03f, Quaternion.LookRotation(aim, Vector3.up));
            foreach (var ps in beam.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startSizeMultiplier *= 0.3f;      // the glare is a camera-facing sprite: kept small so it doesn't glow in every direction
            }
            foreach (var l in beam.GetComponentsInChildren<Light>(true))
                l.spotAngle = 30f;
            MiG29NoseGear.Lamp(lamp, sprung, face, AssetDatabase.LoadAssetAtPath<Material>(ModDir + "/materials/MiG29_gear.mat"), (o, path) => CreateOrReplace(o, path));
            Debug.Log($"[MiG29] landing light on the nose strut front (z {front:F2}), lamp {lamp.position - ModelOffset:F3}, normal was {n0.normalized:F2}, strut axis at {axisPt:F2}");
        }

        // Poses a leg as LandingGear does when retracted (hinge + foldDegrees, + hingeFoldMotion) and logs where each visible piece
        // ends up, MiG frame, so a stowed leg poking out of the skin shows in the build log.
        static void LogStowed(Transform root, string wheelName)
        {
            var wheel = Find(root, wheelName);
            foreach (var lg in root.GetComponentsInChildren(TypeByName("LandingGear"), true))
            {
                var so = new SerializedObject(lg);
                var hinge = so.FindProperty("gearHinge").objectReferenceValue as Transform;
                if (hinge == null || !wheel.IsChildOf(hinge)) continue;
                var baseRot = hinge.localEulerAngles; var basePos = hinge.localPosition;
                hinge.localEulerAngles = baseRot + new Vector3(so.FindProperty("foldDegrees").floatValue, 0f, 0f);
                hinge.localPosition = basePos + so.FindProperty("hingeFoldMotion").vector3Value;
                foreach (var r in hinge.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || !r.enabled) continue;
                    var m = r.transform.localToWorldMatrix;
                    var pts = mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v) - ModelOffset).ToArray();
                    var low = pts.OrderBy(p => p.y).First();
                    var worst = r.name.Contains("chock") ? 0f : pts.Min(p => BayMargin(p));
                    Debug.Log($"[MiG29]   {wheelName} stowed {r.name}: vertices y {pts.Min(p => p.y):F2}..{pts.Max(p => p.y):F2}, z {pts.Min(p => p.z):F2}..{pts.Max(p => p.z):F2}, lowest at z {low.z:F2}, inside the skin by {worst * 100f:F1} cm");
                }
                hinge.localEulerAngles = baseRot; hinge.localPosition = basePos;
                return;
            }
        }

        // Nose bay envelope (tools/nose_bay.py): per (x, z) cell the outer skin from below (closed doors / belly) and from above. The
        // model's bay-well walls in between are hidden behind the closed doors, so a stowed leg may cross them but not the skin.
        [Serializable] class BayGrid { public float x0, dx, z0, dz; public int nx, nz; public float[] floor, roof; }
        static BayGrid bay;
        const float BayClearance = 0.015f;

        // margin of a MiG-frame point inside the bay skin (negative = outside)
        static float BayMargin(Vector3 p)
        {
            if (bay == null) bay = JsonUtility.FromJson<BayGrid>(File.ReadAllText(Path.Combine(SourceDir, "nose_bay.json")));
            int i = Mathf.RoundToInt((p.z - bay.z0) / bay.dz), j = Mathf.RoundToInt((p.x - bay.x0) / bay.dx);
            if (i < 0 || j < 0 || i >= bay.nz || j >= bay.nx) return -1f;
            int k = i * bay.nx + j;
            return Mathf.Min(p.y - bay.floor[k], bay.roof[k] - p.y) - BayClearance;
        }

        static void StowGear(Transform root, string wheelName, Vector3 stowedWorld, float extraFold = 0f, bool searchBay = false)
        {
            var wheel = Find(root, wheelName);
            var lgType = TypeByName("LandingGear");
            foreach (var lg in root.GetComponentsInChildren(lgType, true))
            {
                var so = new SerializedObject(lg);
                var hinge = so.FindProperty("gearHinge").objectReferenceValue as Transform;
                if (hinge == null || !wheel.IsChildOf(hinge))
                    continue;
                var baseRot = hinge.localEulerAngles;
                if (searchBay)
                {
                    // try fold angles and stow points; keep the pose whose real vertices stay inside the bay with the most margin
                    float stock = so.FindProperty("foldDegrees").floatValue;
                    float best = float.MinValue; float bestFold = stock; Vector3 bestStow = stowedWorld;
                    var basePos = hinge.localPosition;
                    var rends = hinge.GetComponentsInChildren<MeshFilter>(true).Where(mf => mf.sharedMesh != null && !mf.name.Contains("chock"))
                                     .Select(mf => (mf, verts: mf.sharedMesh.vertices)).ToList();
                    for (float ef = -40f; ef <= 30f; ef += 1f)
                        for (float dy = -0.16f; dy <= 0.12f; dy += 0.02f)
                            for (float dz = -0.40f; dz <= 0.60f; dz += 0.05f)
                            {
                                var stow = stowedWorld + new Vector3(0f, dy, dz);
                                hinge.localEulerAngles = baseRot + new Vector3(stock + ef, 0f, 0f);
                                hinge.localPosition = basePos;
                                var f = wheel.position;
                                hinge.localPosition = basePos + hinge.parent.InverseTransformVector(stow - f);
                                float score = float.MaxValue;
                                foreach (var (mf, verts) in rends)
                                {
                                    var m = mf.transform.localToWorldMatrix;
                                    foreach (var v in verts)
                                    {
                                        float mg = BayMargin(m.MultiplyPoint3x4(v) - ModelOffset); if (mg < score) score = mg;
                                        if (score <= best) break;   // this pose can no longer beat the best: same result, sooner
                                    }
                                    if (score <= best) break;
                                }
                                if (score > best) { best = score; bestFold = stock + ef; bestStow = stow; }
                            }
                    hinge.localEulerAngles = baseRot; hinge.localPosition = basePos;
                    extraFold = bestFold - stock; stowedWorld = bestStow;
                    Debug.Log($"[MiG29] {wheelName}: bay search -> fold {bestFold:F0} deg (stock {stock:F0}), stow {bestStow - ModelOffset}, margin {best * 100f:F1} cm");
                }
                var fold = so.FindProperty("foldDegrees").floatValue + extraFold;
                so.FindProperty("foldDegrees").floatValue = fold;
                hinge.localEulerAngles = baseRot + new Vector3(fold, 0f, 0f);
                var folded = wheel.position;
                hinge.localEulerAngles = baseRot;
                var motion = hinge.parent.InverseTransformVector(stowedWorld - folded);
                so.FindProperty("hingeFoldMotion").vector3Value = motion;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[MiG29] {wheelName}: fold {fold:F1} deg, folded at {folded}, fold motion {motion}");
                return;
            }
            throw new Exception("[MiG29] no LandingGear drives " + wheelName);
        }

        static void MoveCockpit(Transform root)
        {
            foreach (var n in new[] { "pilot", "cockpit_int", "tacScreen", "canopyFrame_F", "canopyHinge" })
                MoveWorld(root, n, CockpitShift);
            MoveWorld(root, "pilot", MiG29Cockpit.PilotShift);
        }

        // Parent part (UnitPart) each MiG control surface hangs from, and its visual-only ControlSurface ranges.
        static readonly Dictionary<string, (string parent, float pitch, float roll, float yaw, bool flap, float speed)> Surfaces =
            new Dictionary<string, (string, float, float, float, bool, float)>
            {
                ["aileron_L"] = ("aileron_L", 0, -20, 0, false, 45),
                ["aileron_R"] = ("aileron_R", 0, 20, 0, false, 45),
                ["flap_L"] = ("flap_L", 25, 0, 0, true, 15),
                ["flap_R"] = ("flap_R", 25, 0, 0, true, 15),
                ["stab_L"] = ("stabilator_L", -30, -8, 0, false, 60), // all-moving stabilator: pitch + differential roll
                ["stab_R"] = ("stabilator_R", -30, 8, 0, false, 60),
                ["rudder_L"] = ("fin_L", 0, 0, -25, false, 60),
                ["rudder_R"] = ("fin_R", 0, 0, -25, false, 60),
            };

        static (Component part, Renderer renderer) AddControlSurface(Transform root, PartDump part, Mesh mesh, Material mat)
        {
            var cfg = Surfaces[part.name];
            var parent = Find(root, cfg.parent);
            var axis = new Vector3(part.axis[0], part.axis[1], part.axis[2]).normalized;
            var fwd = (Vector3.forward - Vector3.Dot(Vector3.forward, axis) * axis).normalized;
            var up = Vector3.Cross(fwd, axis);

            var pivot = new GameObject("MiG29_" + part.name + "_pivot").transform;
            pivot.SetParent(parent, true);
            pivot.SetPositionAndRotation(new Vector3(part.pivot[0], part.pivot[1], part.pivot[2]) + ModelOffset, Quaternion.LookRotation(fwd, up));

            var r = AddMeshObject("MiG29_" + part.name, pivot, mesh, mat);
            r.transform.SetPositionAndRotation(pivot.position, Quaternion.identity);

            var csType = TypeByName("ControlSurface");
            var unitPartType = TypeByName("UnitPart");
            var unitPart = parent.GetComponentInParent(unitPartType);
            var cs = pivot.gameObject.AddComponent(csType);
            var so = new SerializedObject(cs);
            so.FindProperty("pitchRange").floatValue = cfg.pitch;
            so.FindProperty("rollRange").floatValue = cfg.roll;
            so.FindProperty("yawRange").floatValue = cfg.yaw;
            so.FindProperty("flap").boolValue = cfg.flap;
            so.FindProperty("servoSpeed").floatValue = cfg.speed;
            so.FindProperty("maxSplit").floatValue = 0;
            so.FindProperty("splitDrag").floatValue = 0;
            so.FindProperty("visibleMesh").objectReferenceValue = pivot.gameObject;
            so.FindProperty("attachedSurface").objectReferenceValue = unitPart;
            so.ApplyModifiedPropertiesWithoutUndo();
            AddDamageRenderer(unitPart.transform, r);
            return (unitPart, r);
        }

        static void AddDamageRenderer(Transform partTransform, Renderer r)
        {
            var part = partTransform.GetComponent(TypeByName("UnitPart"));
            if (part == null) part = partTransform.GetComponentInParent(TypeByName("UnitPart"));
            var so = new SerializedObject(part);
            var arr = so.FindProperty("damageMaterial.renderers");
            arr.arraySize++;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = r;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // The intake FOD doors (tools/fod_door.py -> MiG29Source/fod_door.json). On the real jet each door is a panel of the duct roof,
        // hinged along its front edge: in flight it lies flush with the roof, on the ground it swings down until its free edge meets the
        // duct floor and seals the duct (the engines then breathe through the louvres on top of the wing roots). The hinge follows the
        // roof's roll (~6.5 deg) and the closed door is the duct liner's own cross-section in its plane (MiG29Duct), so it seals wall
        // to wall: the old door, outlined level in a rolled duct, gapped at one corner and clipped at the other, swung out of the black
        // panel that stood in for the duct and hid in flight (in-game shots, 0.9.4). Built open; the MiG29Instruments plugin closes it
        // by turning the hinge MiG29InstrumentMath.FodClosedDeg about its x axis. The duct's own material, like the roof it is part of.
        // Not damage renderers, as the duct.
        // Also the brake chute's material: the flat-colour missile atlas on a renderer of its own (ChuteDriver finds it by name).
        static void AddFodDoors(Transform root)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ModDir + "/duct/MiG29_duct.mat");
            var d = JsonUtility.FromJson<FodDump>(File.ReadAllText(Path.Combine(SourceDir, "fod_door.json")));
            Vector3 V(float[] a, int i = 0) => new Vector3(a[i], a[i + 1], a[i + 2]);
            foreach (var side in new[] { -1f, 1f })
            {
                // the left door as measured; the right one mirrored (x flipped; the hinge axis turned back to +x, so the same turn
                // closes both)
                Vector3 M(Vector3 v) => side < 0 ? v : new Vector3(-v.x, v.y, v.z);
                var axis = side < 0 ? V(d.axis) : -M(V(d.axis));
                var fwd = Vector3.Cross(axis, Vector3.up).normalized;
                var rot = Quaternion.LookRotation(fwd, Vector3.Cross(fwd, axis));   // local x along the hinge
                var part = Find(root, side < 0 ? "intake_L" : "intake_R");
                var hinge = new GameObject(side < 0 ? "MiG29_fod_L" : "MiG29_fod_R").transform;
                hinge.SetParent(part, false);
                hinge.SetPositionAndRotation(M(V(d.hinge)) + ModelOffset, rot);
                var ls = part.lossyScale;                                       // the door is measured in metres: undo any part scale
                hinge.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                var door = new GameObject("MiG29_fod_door").transform;
                door.SetParent(hinge, false);
                door.gameObject.AddComponent<MeshFilter>().sharedMesh = CreateOrReplace(FodDoorMesh(d, M(V(d.hinge)), rot, M(V(d.normal)), M), $"{ModDir}/meshes/MiG29_fod_door_{(side < 0 ? "L" : "R")}.asset");
                door.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            var chute = new GameObject("MiG29_chute_mat");
            chute.transform.SetParent(root, false);
            var cr = chute.AddComponent<MeshRenderer>();
            cr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ModDir + "/weapons/MiG29_missiles.mat"); cr.enabled = false;
        }

        [Serializable] class FodDump { public float[] hinge, axis, normal, outline; public float closed_deg, thickness; }

        // The door in its hinge's frame, open pose: the closed outline turned back up by -FodClosedDeg about the hinge. Front (toward the
        // intake mouth when closed, the duct's underside when open) at the lip's grey, back and edges at the darker grey further in.
        static Mesh FodDoorMesh(FodDump d, Vector3 hinge, Quaternion rot, Vector3 normal, Func<Vector3, Vector3> mirror)
        {
            if (Mathf.Abs(d.closed_deg + MiG29InstrumentMath.FodClosedDeg) > 0.01f)
                Debug.LogError($"[MiG29] fod_door.json is built for {d.closed_deg} deg, the plugin closes the doors {-MiG29InstrumentMath.FodClosedDeg} deg");
            var toOpen = Quaternion.Inverse(Quaternion.Euler(MiG29InstrumentMath.FodClosedDeg, 0f, 0f)) * Quaternion.Inverse(rot);
            Vector3 Local(Vector3 p) => toOpen * (p - hinge);
            var front = new List<Vector3>();
            for (int i = 0; i + 2 < d.outline.Length; i += 3) front.Add(mirror(new Vector3(d.outline[i], d.outline[i + 1], d.outline[i + 2])));
            var back = front.Select(p => p - normal * d.thickness).ToList();
            var n = toOpen * normal;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            Vector2 light = new Vector2(0.5f, 0.05f), dark = new Vector2(0.5f, 0.35f);   // duct_basecolor.png: v = depth / 6 m
            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 nn, Vector2 uv)
            {
                int i0 = verts.Count;
                verts.AddRange(new[] { a, b, c }); norms.AddRange(new[] { nn, nn, nn }); uvs.AddRange(new[] { uv, uv, uv });
                // Unity front face: cross(b - a, c - a) along the normal (as the model's own triangles)
                tris.AddRange(Vector3.Dot(Vector3.Cross(b - a, c - a), nn) >= 0f ? new[] { i0, i0 + 1, i0 + 2 } : new[] { i0, i0 + 2, i0 + 1 });
            }
            Vector3 cf = Local(front.Aggregate(Vector3.zero, (s, p) => s + p) / front.Count), cb = cf - n * d.thickness;
            for (int i = 0; i < front.Count; i++)
            {
                int j = (i + 1) % front.Count;
                Vector3 f0 = Local(front[i]), f1 = Local(front[j]), b0 = Local(back[i]), b1 = Local(back[j]);
                Tri(cf, f0, f1, n, light);
                Tri(cb, b0, b1, -n, dark);
                var side = Vector3.Cross(f1 - f0, n).normalized;
                if (Vector3.Dot(side, f0 - cf) < 0f) side = -side;               // edges face out of the panel
                Tri(f0, f1, b1, side, dark); Tri(f0, b1, b0, side, dark);
            }
            var m = new Mesh { name = "MiG29_fod_door" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        static void AppendExteriorRenderers(GameObject go, List<Renderer> renderers)
        {
            var aircraft = go.GetComponent(TypeByName("Aircraft"));
            var so = new SerializedObject(aircraft);
            var arr = so.FindProperty("exteriorRenderers");
            foreach (var r in renderers)
            {
                arr.arraySize++;
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = r;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- airframe split / hitboxes ----------------

        static Bounds WorldBounds(Bounds b, Matrix4x4 m)
        {
            var wb = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                wb.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return wb;
        }

        // MiG coordinates -> KR-67 coordinates: the KR-67 wing reaches 7.1 m, the MiG's 5.68 m.
        static Vector3 ToKR67(Vector3 c)
        {
            float ax = Mathf.Abs(c.x);
            float m = ax <= 1.5f ? ax : 1.5f + (ax - 1.5f) * 1.34f;
            return new Vector3(Mathf.Sign(c.x) * m, c.y, c.z);
        }

        static Vector3 FromKR67(Vector3 k)
        {
            float ax = Mathf.Abs(k.x);
            float m = ax <= 1.5f ? ax : 1.5f + (ax - 1.5f) / 1.34f;
            return new Vector3(Mathf.Sign(k.x) * m, k.y, k.z);
        }

        static void SplitAirframe(Transform root, Transform visual, List<PartDump> pieces, Material mat, List<(Component part, Renderer renderer)> surfaces)
        {
            // the MiG's own parts (MiG29Parts): each skin triangle goes to the part its points lie in, and one straddling a joint
            // is cut along that joint's plane, so a part breaks off along a straight frame or rib line and is what was hit
            var parts = root.GetComponentsInChildren(TypeByName("UnitPart"), true).Where(p => MiG29Parts.Skinned.Contains(p.name)).ToList();
            var partIndex = parts.Select((p, i) => (p.name, i)).ToDictionary(x => x.name, x => x.i);
            var hulls = new MiG29Cut.Hulls { faces = parts.Select(_ => new List<MiG29Cut.Plane>()).ToArray(), fixedPart = c => partIndex[MiG29Parts.Label(c)] };
            hulls.fixedFaces.AddRange(MiG29Parts.Planes());
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var world = pieces.Select(piece =>
            {
                int n = piece.vertices.Length / 3;
                var wv = new Vector3[n]; var wn = new Vector3[n]; var uv = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    wv[i] = visual.TransformPoint(new Vector3(piece.vertices[i * 3], piece.vertices[i * 3 + 1], piece.vertices[i * 3 + 2]));
                    wn[i] = visual.TransformDirection(new Vector3(piece.normals[i * 3], piece.normals[i * 3 + 1], piece.normals[i * 3 + 2]));
                    uv[i] = new Vector2(piece.uvs[i * 2], piece.uvs[i * 2 + 1]);
                }
                return (wv, wn, uv, piece.triangles);
            }).ToList();
            var buckets = MiG29Cut.Split(world, hulls);
            // The skin you see breaks along torn edges (MiG29Parts.TornLabel); everything behind it (the lining, the structure, the
            // frame strips, the joints, the hitboxes) keeps to the straight joints, at a tenth of the triangles.
            var tornHulls = new MiG29Cut.Hulls { faces = hulls.faces, fixedPart = hulls.fixedPart, torn = true,
                nearSeam = MiG29Parts.NearSeam, tornPart = (c, n) => partIndex[MiG29Parts.TornLabel(c, n)] };
            var skins = MiG29Cut.Split(world, tornHulls);
            MiG29Cut.Despeckle(skins, 0.03f);
            var skinCuts = MiG29Cut.CutEdges(skins);
            var cuts = MiG29Cut.CutEdges(buckets); MiG29Cut.frames = 0;
            MiG29Parts.Anchors(parts, buckets, cuts);
            var joined = MiG29Parts.Fasten(parts, cuts, buckets, root, JointIterations);
            Debug.Log($"[MiG29] timing: split and seams {clock.Elapsed.TotalSeconds:F0} s");
            // the stock jets' own interior materials (game assets, restored at runtime like the KR-67 glass; nothing is shipped)
            var structMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Blueprinter/_donotship/Material/aircraft_structure_PLACEHOLDER.mat");
            var honeyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Blueprinter/_donotship/Material/honeycomb_PLACEHOLDER.mat");
            var mechMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Blueprinter/_donotship/Material/jet_mechanical_PLACEHOLDER.mat");
            int innerTris = 0;
            Mesh PartMesh(string name, Transform tr, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Color32> col, List<int> t)
            {
                var pm = new Mesh { name = name };
                pm.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                pm.SetVertices(v.Select(x => tr.InverseTransformPoint(x)).ToList()); pm.SetNormals(n.Select(x => tr.InverseTransformDirection(x)).ToList());
                pm.SetUVs(0, uv); if (col != null) pm.SetColors(col); pm.SetTriangles(t, 0);
                pm.RecalculateBounds(); pm.RecalculateTangents();
                return CreateOrReplace(pm, $"{ModDir}/meshes/{name}.asset");
            }
            // RD-33 bodies forward of the nozzle roots, inside the nacelles
            var axes = new[] { -1f, 1f }.Select(sd => { MiG29Nozzles.EngineAxis(sd, out var r0, out var fw); return (r0 + ModelOffset, fw); }).ToList();
            foreach (var kv in MiG29Cut.Engines(axes, EngineLength, EngineRadius, hulls.Label))
            {
                var part = parts[kv.Key]; var (ev, en, euv, et) = kv.Value;
                MiG29Cut.Dump("mech_" + part.name, ev, et);
                AddMeshObject("MiG29_mech_" + part.name, part.transform, PartMesh("MiG29_mech_" + part.name, part.transform, ev, en, euv, null, et), mechMat);
                innerTris += et.Count / 3;
            }
            // the intake ducts' liners, from the lips to those engine fronts, each piece on the part it lies in (MiG29Duct)
            var ductMat = MiG29Duct.Material(mat, (o, path) => CreateOrReplace(o, path));
            var ducts = new Dictionary<int, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)>();
            int ductSide = 0;
            foreach (var (dv, dn, duv, dt) in MiG29Duct.Liners())
            {
                // the stretch aft of the model's old duct end runs on into the engine and belongs to it, as the stock jets' last
                // stretch of duct does: on the hull it fell to the centre fuselage, and stayed hanging there as a ring once the
                // engine broke off (break-every-part pictures, 2026-11-09)
                var engineName = ductSide++ == 0 ? "nacelle_L" : "nacelle_R";
                int engine = parts.FindIndex(p => p.name == engineName);
                var map = new Dictionary<(int part, int vert), int>();
                for (int i = 0; i < dt.Length; i += 3)
                {
                    var ctr = (dv[dt[i]] + dv[dt[i + 1]] + dv[dt[i + 2]]) / 3f;
                    int k = engine >= 0 && ctr.z < MiG29Duct.CapZ ? engine : hulls.Label(ctr + ModelOffset);
                    if (!ducts.TryGetValue(k, out var m)) ducts[k] = m = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>());
                    for (int c = 0; c < 3; c++)
                    {
                        int j = dt[i + c];
                        if (!map.TryGetValue((k, j), out int at)) { map[(k, j)] = at = m.v.Count; m.v.Add(dv[j] + ModelOffset); m.n.Add(dn[j]); m.uv.Add(duv[j]); }
                        m.t.Add(at);
                    }
                }
            }
            foreach (var kv in ducts)
            {
                var part = parts[kv.Key]; var (dv, dn, duv, dt) = kv.Value;
                AddMeshObject("MiG29_duct_" + part.name, part.transform, PartMesh("MiG29_duct_" + part.name, part.transform, dv, dn, duv, null, dt), ductMat);
                // its outside, in the airframe's structure: once the skin over it is gone the duct reads as a metal trunk, not as the
                // dark inside of its far wall seen through the near one (a black hole in the side, same pictures)
                var ot = new List<int>(); for (int i = 0; i < dt.Count; i += 3) { ot.Add(dt[i]); ot.Add(dt[i + 2]); ot.Add(dt[i + 1]); }
                var ouv = duv.Select(u => new Vector2(u.x * 2.2f * 0.46f, u.y * 6f * 0.46f)).ToList();   // ~2.2 m round, 6 m per v; structure tiles
                AddMeshObject("MiG29_inner_duct_" + part.name, part.transform, PartMesh("MiG29_inner_duct_" + part.name, part.transform, dv, dn.Select(x => -x).ToList(), ouv,
                    Enumerable.Repeat(new Color32(0x57, 0x57, 0x57, 255), dv.Count).ToList(), ot), structMat);
                innerTris += dt.Count / 3 * 2;
            }
            // behind the radome, on the part that stays when it goes: a bulkhead across the seam and a radar dish ahead of it, as the
            // stock jets show a bay where their radome was. Inside the radome otherwise, out of sight.
            {
                var seam = new Vector3(0f, 0.18f, RadomeSeamZ - 0.01f) + ModelOffset;
                var holder = parts[hulls.Label(seam - Vector3.forward * 0.15f)];
                var bv = new List<Vector3>(); var bn = new List<Vector3>(); var buv = new List<Vector2>(); var bc = new List<Color32>(); var bt = new List<int>();
                void Disc(Vector3 c, float rx, float ry, float bulge, Color32 tint)
                {
                    const int Sides = 32; int c0 = bv.Count;
                    bv.Add(c + Vector3.forward * bulge); bn.Add(Vector3.forward); buv.Add(new Vector2(0.5f, 0.5f)); bc.Add(tint);
                    for (int i = 0; i < Sides; i++)
                    {
                        float a = i * Mathf.PI * 2f / Sides;
                        bv.Add(c + new Vector3(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry, 0f)); bn.Add(Vector3.forward);
                        buv.Add(new Vector2(0.5f + Mathf.Cos(a) * rx * 0.46f, 0.5f + Mathf.Sin(a) * ry * 0.46f)); bc.Add(tint);
                    }
                    for (int i = 0; i < Sides; i++) bt.AddRange(new[] { c0, c0 + 1 + i, c0 + 1 + (i + 1) % Sides });   // facing forward: cross(b - a, c - a) along +z
                }
                // bulkhead: the seam ring, traced from the skin's own section there two centimetres inside it (an ellipse fitted to the
                // nose's width and height stuck out above and below it, where the section narrows: user screenshot, 2026-11-12)
                {
                    // the section: the skin's edges cut by the seam plane, as 2D segments round the seam centre; each side of the
                    // bulkhead reaches along its ray to the nearest of them (points alone let the straight sides cut outside where
                    // the underside curves in between them)
                    const int Sides = 96; var radius = new float[Sides];
                    var segs = new List<(Vector2 a, Vector2 b)>();
                    foreach (var (wv, _, _, tris) in world)
                        for (int k = 0; k + 2 < tris.Length; k += 3)
                        {
                            var cut = new List<Vector2>(2);
                            for (int e = 0; e < 3; e++)
                            {
                                Vector3 p = wv[tris[k + e]], q = wv[tris[k + (e + 1) % 3]];
                                if ((p.z - seam.z) * (q.z - seam.z) > 0f || Mathf.Abs(q.z - p.z) < 1e-6f) continue;
                                var d = p + (q - p) * ((seam.z - p.z) / (q.z - p.z)) - seam;
                                cut.Add(new Vector2(d.x, d.y));
                            }
                            if (cut.Count >= 2) segs.Add((cut[0], cut[1]));
                        }
                    for (int i = 0; i < Sides; i++)
                    {
                        float a = i * Mathf.PI * 2f / Sides; var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        radius[i] = float.MaxValue;
                        foreach (var (sa, sb) in segs)
                        {
                            var e = sb - sa; float den = dir.x * e.y - dir.y * e.x;
                            if (Mathf.Abs(den) < 1e-9f) continue;
                            float t = (sa.x * e.y - sa.y * e.x) / den, u = (sa.x * dir.y - sa.y * dir.x) / den;
                            if (t > 0.05f && u >= 0f && u <= 1f) radius[i] = Mathf.Min(radius[i], t);
                        }
                    }
                    int c0 = bv.Count; var tint = new Color32(0x57, 0x57, 0x57, 255);
                    bv.Add(seam); bn.Add(Vector3.forward); buv.Add(new Vector2(0.5f, 0.5f)); bc.Add(tint);
                    for (int i = 0; i < Sides; i++)
                    {
                        float a = i * Mathf.PI * 2f / Sides;
                        float ellipse = 1f / Mathf.Sqrt(Mathf.Pow(Mathf.Cos(a) / 0.47f, 2) + Mathf.Pow(Mathf.Sin(a) / 0.62f, 2));
                        float r = radius[i] < 2f ? radius[i] - 0.02f : ellipse;   // no skin in that direction: the old ellipse
                        bv.Add(seam + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f)); bn.Add(Vector3.forward);
                        buv.Add(new Vector2(0.5f + Mathf.Cos(a) * r * 0.46f, 0.5f + Mathf.Sin(a) * r * 0.46f)); bc.Add(tint);
                    }
                    for (int i = 0; i < Sides; i++) bt.AddRange(new[] { c0, c0 + 1 + i, c0 + 1 + (i + 1) % Sides });
                }
                Disc(seam + new Vector3(0f, -0.03f, 0.2f), 0.36f, 0.36f, -0.06f, new Color32(0x8a, 0x8a, 0x8a, 255));   // radar dish, dished
                AddMeshObject("MiG29_inner_bulkhead", holder.transform, PartMesh("MiG29_inner_bulkhead", holder.transform, bv, bn, buv, bc, bt), structMat);
                Debug.Log($"[MiG29] radome: bulkhead and radar dish on {holder.name}");
            }
            Debug.Log($"[MiG29] intakes: duct liners on {ducts.Count} parts ({string.Join(", ", ducts.Keys.Select(k => parts[k].name))})");

            // airframe thickness, for the inner hull's inset: a ray inward from the skin to its far side (the whole skin as one
            // temporary collider on its own layer, back faces included)
            var rayGo = new GameObject("MiG29_tmp_thickness") { layer = 31 };
            var allV = new List<Vector3>(); var allT = new List<int>();
            foreach (var bk in buckets.Values) { int o = allV.Count; allV.AddRange(bk.v); allT.AddRange(bk.t.Select(x => x + o)); }
            var rayMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; rayMesh.SetVertices(allV); rayMesh.SetTriangles(allT, 0);
            rayGo.AddComponent<MeshCollider>().sharedMesh = rayMesh;
            Physics.SyncTransforms();
            bool backfaces = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
            // No inner hull inside the cockpit (MiG frame: between the sills from the floor up through the canopy, the canopy's rear
            // edge to the windscreen): the windscreen frame and sills fell to the front fuselage in the hull cut, and their hull, a few
            // cm in from the skin, showed in the cockpit view as a streaky arch under the bow and slabs along the sills (user
            // screenshot, SMT 0.2.0); the game hides only the cockpit part's skin there. The cockpit tub closes that space anyway.
            bool InCockpit(Vector3 p) { var q = p - ModelOffset; return Mathf.Abs(q.x) < 0.46f && q.y > 0.30f && q.y < 1.45f && q.z > 5.62f && q.z < 7.95f; }
            float Depth(Vector3 p, Vector3 dir) => Physics.Raycast(p + dir * 0.003f, dir, out var hit, 3f, 1 << 31, QueryTriggerInteraction.Ignore) ? hit.distance + 0.003f : -1f;   // -1: no far side
            // outside the airframe: skin just above the point and nothing below it, or the reverse
            bool Outside(Vector3 p)
            {
                bool Ray(Vector3 dir, float max) => Physics.Raycast(p, dir, out _, max, 1 << 31, QueryTriggerInteraction.Ignore);   // the hit-info form, as Depth (back faces)
                return Ray(Vector3.up, 0.3f) && !Ray(Vector3.down, 5f) || Ray(Vector3.down, 0.3f) && !Ray(Vector3.up, 5f);
            }
            int outsideDropped = 0;
            // inside an intake duct (both liners as one temporary collider on a layer of their own): near the engine fronts the ducts run
            // within 5-10 cm of the top skin, closer than the inner hull's inset, which would show as structure in the duct there
            var ductGo = new GameObject("MiG29_tmp_ducts") { layer = 30 };
            var ductV = new List<Vector3>(); var ductT = new List<int>();
            foreach (var d in ducts.Values) { int o = ductV.Count; ductV.AddRange(d.v); ductT.AddRange(d.t.Select(x => x + o)); }
            var ductMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; ductMesh.SetVertices(ductV); ductMesh.SetTriangles(ductT, 0);
            ductGo.AddComponent<MeshCollider>().sharedMesh = ductMesh;
            Physics.SyncTransforms();
            bool InDuct(Vector3 p) => new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right }.All(dir => Physics.Raycast(p, dir, out _, 1.2f, 1 << 30, QueryTriggerInteraction.Ignore));
            int ductDropped = 0;

            // ribs, spars and frames inside the airframe (MiG29Structure), on the part each piece lies in
            {
                var structure = MiG29Structure.Build(31, p => InCockpit(p) || InDuct(p) || Outside(p), ax => LeZ(ax) + ModelOffset.z, MiG29Parts.Te,
                    axes, 0.46f, new Color32(0x57, 0x57, 0x57, 255), out var structReport);
                foreach (var kv in structure)
                {
                    var tr = parts[partIndex[kv.Key]].transform; var pc = kv.Value;
                    AddMeshObject("MiG29_inner_struct_" + kv.Key, tr, PartMesh("MiG29_inner_struct_" + kv.Key, tr, pc.v, pc.n, pc.uv, pc.c, pc.t), structMat);
                    innerTris += pc.t.Count / 3;
                }
                Debug.Log("[MiG29] structure: " + structReport);
            }
            // which part is across each torn edge (the exact twins; the rest are matched by the shards' nearest edge)
            var across = new Dictionary<((int, int, int), (int, int, int)), List<int>>();
            foreach (var kv in skinCuts) foreach (var e in kv.Value) { if (!across.TryGetValue(e, out var l)) across[e] = l = new List<int>(); l.Add(kv.Key); }
            int skinTris = 0;
            int shardCount = 0;
            foreach (var kv in buckets)
            {
                var part = parts[kv.Key]; var b = kv.Value; var tr = part.transform;
                MiG29Cut.Dump("skin_" + part.name, b.v, b.t);
                // shards: the skin within ShardReach of a break line, in ragged patches. Each triangle carries its patch in UV3 (x:
                // patch number + 1, y: FxAnchors.PartCode of the part across the break); the plugin cuts the patches out of the mesh and
                // throws them off when that part breaks (FxAnchors.Shred), so the break shreds instead of parting cleanly
                if (!skins.TryGetValue(kv.Key, out var sb)) { Debug.LogWarning($"[MiG29] {part.name}: no torn skin, the straight one instead"); sb = b; }
                var (mainT, shards) = Shards(sb, skinCuts.TryGetValue(kv.Key, out var sc) ? sc : new HashSet<((int, int, int), (int, int, int))>(), across, kv.Key, k => parts[k].name);
                var map = new Dictionary<(int, int), int>(); var sv = new List<Vector3>(); var sn = new List<Vector3>(); var suv = new List<Vector2>(); var s3 = new List<Vector2>(); var st = new List<int>();
                void Tri(List<int> tris, int at, int id, int code)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int i0 = tris[at + c];
                        if (!map.TryGetValue((i0, id), out int jv)) { map[(i0, id)] = jv = sv.Count; sv.Add(tr.InverseTransformPoint(sb.v[i0])); sn.Add(tr.InverseTransformDirection(sb.n[i0])); suv.Add(sb.uv[i0]); s3.Add(new Vector2(id, code)); }
                        st.Add(jv);
                    }
                }
                for (int ti = 0; ti < mainT.Count; ti += 3) Tri(mainT, ti, 0, 0);
                for (int si = 0; si < shards.Count; si++) for (int ti = 0; ti < shards[si].tris.Count; ti += 3) Tri(shards[si].tris, ti, si + 1, PartCode(shards[si].across));
                var m = new Mesh { name = "MiG29_part_" + part.name, indexFormat = sv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(sv); m.SetNormals(sn); m.SetUVs(0, suv); m.SetUVs(3, s3); m.SetTriangles(st, 0);
                m.RecalculateBounds(); m.RecalculateTangents();
                m = CreateOrReplace(m, $"{ModDir}/meshes/MiG29_part_{part.name}.asset");
                skinTris += st.Count / 3; shardCount += shards.Count;
                var r = AddMeshObject("MiG29_skin_" + part.name, tr, m, mat);
                AddDamageRenderer(tr, r);
                // beyond close range the straight split (MiG29Lod)
                var lm = new Mesh { name = "MiG29_lodpart_" + part.name, indexFormat = b.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                lm.SetVertices(b.v.Select(v => tr.InverseTransformPoint(v)).ToList()); lm.SetNormals(b.n.Select(v => tr.InverseTransformDirection(v)).ToList());
                lm.SetUVs(0, b.uv); lm.SetTriangles(b.t, 0); lm.RecalculateBounds(); lm.RecalculateTangents();
                AddDamageRenderer(tr, AddMeshObject("MiG29_lodskin_" + part.name, tr, CreateOrReplace(lm, $"{ModDir}/meshes/{lm.name}.asset"), mat));

                // Fire, fireballs, fuel leaks and break-up effects spawn at the part's own transform: the KR-67's pivots, up to 2 m
                // off the MiG's skin (the outer wing tanks burned in mid-air ahead of the wing). Anchors on the part's own skin, top
                // for fire and bottom for leaks, which the plugin moves the effects to (FxAnchors.cs). The nozzle pivots are fine.
                if (!part.name.StartsWith("nozzle") && (part.GetComponent(TypeByName("FuelTank")) != null || new SerializedObject(part).FindProperty("disintegrationEffects").arraySize > 0))
                {
                    var cens = new List<Vector3>(); Vector3 mid = Vector3.zero; float area = 0f, top = float.MinValue, bottom = float.MaxValue;
                    for (int i = 0; i + 2 < b.t.Count; i += 3)
                    {
                        Vector3 p0 = b.v[b.t[i]], p1 = b.v[b.t[i + 1]], p2 = b.v[b.t[i + 2]];
                        float a = Vector3.Cross(p1 - p0, p2 - p0).magnitude / 2f; var c = (p0 + p1 + p2) / 3f;
                        cens.Add(c); mid += c * a; area += a; top = Mathf.Max(top, c.y); bottom = Mathf.Min(bottom, c.y);
                    }
                    if (area > 0f)
                    {
                        mid /= area;
                        Vector3 Nearest(Vector3 q) => cens.OrderBy(c => (c - q).sqrMagnitude).First();
                        AddAnchor(tr, FxAnchor, Nearest(new Vector3(mid.x, top, mid.z)));
                        AddAnchor(tr, FxAnchor + "_leak", Nearest(new Vector3(mid.x, bottom, mid.z)));
                    }
                }

                // the inside behind the skin (stock structure; honeycomb in the radome, at the stock tints and texel densities); not
                // a damage renderer: the damage shader would punch the airframe's burn mask through it too
                bool radome = part.name == "radome";
                var (iv, inr, iuv, icol, it) = MiG29Cut.Interior(b, radome ? 0.61f : 0.46f, radome ? new Color32(0x81, 0x56, 0x43, 255) : new Color32(0x57, 0x57, 0x57, 255), Depth, InCockpit,
                    p => { bool o = Outside(p); if (o) outsideDropped++; else if (InDuct(p)) { ductDropped++; o = true; } return o; }, cuts[kv.Key]);
                MiG29Cut.Dump("inner_" + part.name, iv, it);
                AddMeshObject("MiG29_inner_" + part.name, tr, PartMesh("MiG29_inner_" + part.name, tr, iv, inr, iuv, icol, it), radome ? honeyMat : structMat);
                innerTris += it.Count / 3;
                Debug.Log($"[MiG29] part {part.name}: {b.t.Count / 3} tris");
            }
            Debug.Log($"[MiG29] inner hull: {outsideDropped} inset points outside the skin dropped, {ductDropped} inside the intake ducts");
            Physics.queriesHitBackfaces = backfaces; UnityEngine.Object.DestroyImmediate(rayGo); UnityEngine.Object.DestroyImmediate(ductGo);
            Debug.Log($"[MiG29] skin: {skinTris} triangles with torn edges, {shardCount} patches along the breaks that tear away");
            Debug.Log($"[MiG29] interior: {innerTris} triangles (inner hull, radome honeycomb, engines), {MiG29Cut.frames} frame strips on {cuts.Values.Sum(c => c.Count)} cut edges");

            // Hitboxes fitted to each part's own skin and cut apart from every neighbour (MiG29Hitbox): the part a round hits is the
            // part whose skin it hit
            var hitParts = parts.Concat(surfaces.Select(x => x.part)).Distinct().ToList();
            var sources = hitParts.Where(p => !p.name.EndsWith("_R")).ToDictionary(p => p.name, p => new MiG29Hitbox.Source());
            void AddSkin(string partName, IList<Vector3> v, IList<int> t)
            {
                bool right = partName.EndsWith("_R");
                var src = sources[right ? MiG29Hitbox.Mirror(partName) : partName];
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    if (right) { a.x = -a.x; b.x = -b.x; c.x = -c.x; }
                    src.pts.Add(a); src.pts.Add(b); src.pts.Add(c);
                    src.cen.Add((a + b + c) / 3f); src.area.Add(Vector3.Cross(b - a, c - a).magnitude / 2f);
                }
            }
            foreach (var kv in buckets) AddSkin(parts[kv.Key].name, kv.Value.v, kv.Value.t);
            foreach (var (part, renderer) in surfaces)
            {
                var sm = renderer.GetComponent<MeshFilter>().sharedMesh;
                AddSkin(part.name, sm.vertices.Select(v => renderer.transform.TransformPoint(v)).ToArray(), sm.triangles);
            }
            foreach (var kv in sources) if (kv.Value.cen.Count == 0) throw new Exception($"[MiG29] hitbox {kv.Key}: no skin");
            Debug.Log($"[MiG29] timing: inner hulls {clock.Elapsed.TotalSeconds:F0} s");
            foreach (var kv in MiG29Hitbox.Fit(sources, joined))
            {
                var part = hitParts.FirstOrDefault(p => p.name == kv.Key) ?? throw new Exception($"[MiG29] hitbox {kv.Key}: no such part");
                var t = part.transform;
                var m = new Mesh { name = "MiG29_col_" + kv.Key, vertices = kv.Value.v.Select(v => t.InverseTransformPoint(v)).ToArray(), triangles = kv.Value.t };
                m.RecalculateBounds();
                SetHitbox(part, CreateOrReplace(m, $"{ModDir}/meshes/{m.name}.asset"));
            }
            Debug.Log($"[MiG29] timing: hitboxes {clock.Elapsed.TotalSeconds:F0} s");
        }

        // Shards: triangles whose centre is within ShardReach of a cut edge, grouped by a ragged ShardSize grid (the cell of the
        // centre moved by a jagged offset), labelled with the part across the nearest cut edge. Patches under ShardMin m2 stay on
        // the part. Returns the part's remaining triangles and the shards.
        const float ShardReach = 0.35f, ShardSize = 0.28f, ShardMin = 0.006f;
        // a part's name as a number a float holds exactly (FNV-1a, 20 bits); the plugin's FxAnchors.PartCode is the same
        static int PartCode(string name) { uint h = 2166136261; foreach (char c in name) { h ^= c; h *= 16777619; } return (int)(h & 0xFFFFF); }
        static (List<int> main, List<(string across, List<int> tris)> shards) Shards(MiG29Cut.Bucket b, HashSet<((int, int, int), (int, int, int))> cutEdges,
            Dictionary<((int, int, int), (int, int, int)), List<int>> across, int self, Func<int, string> partName)
        {
            Vector3 V((int, int, int) q) => new Vector3(q.Item1, q.Item2, q.Item3) * 1e-4f;
            const float Cell = 0.4f;
            (int, int, int) C(Vector3 p, float s) => (Mathf.FloorToInt(p.x / s), Mathf.FloorToInt(p.y / s), Mathf.FloorToInt(p.z / s));
            var grid = new Dictionary<(int, int, int), List<(Vector3 at, string other)>>();
            foreach (var e in cutEdges)
            {
                var other = across.TryGetValue(e, out var l) ? l.FirstOrDefault(x => x != self) : -1;
                if (other < 0) continue;   // a T-junction piece: the nearest exact edge names the neighbour
                var mid = (V(e.Item1) + V(e.Item2)) / 2f; var c = C(mid, Cell);
                if (!grid.TryGetValue(c, out var g)) grid[c] = g = new List<(Vector3, string)>();
                g.Add((mid, partName(other)));
            }
            var main = new List<int>(); var groups = new Dictionary<(string, (int, int, int)), List<int>>(); var area = new Dictionary<(string, (int, int, int)), float>();
            for (int i = 0; i + 2 < b.t.Count; i += 3)
            {
                Vector3 a = b.v[b.t[i]], bb = b.v[b.t[i + 1]], cc = b.v[b.t[i + 2]];
                var cen = (a + bb + cc) / 3f; var cc0 = C(cen, Cell);
                float best = ShardReach * ShardReach; string other = null;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    if (grid.TryGetValue((cc0.Item1 + dx, cc0.Item2 + dy, cc0.Item3 + dz), out var g))
                        foreach (var (at, o) in g) { float d = (at - cen).sqrMagnitude; if (d < best) { best = d; other = o; } }
                if (other == null) { main.Add(b.t[i]); main.Add(b.t[i + 1]); main.Add(b.t[i + 2]); continue; }
                var key = (other, C(cen + MiG29Parts.Jag(cen), ShardSize));
                if (!groups.TryGetValue(key, out var tl)) { groups[key] = tl = new List<int>(); area[key] = 0f; }
                tl.Add(b.t[i]); tl.Add(b.t[i + 1]); tl.Add(b.t[i + 2]);
                area[key] += Vector3.Cross(bb - a, cc - a).magnitude / 2f;
            }
            var shards = new List<(string, List<int>)>();
            foreach (var kv in groups)
                if (area[kv.Key] < ShardMin) main.AddRange(kv.Value); else shards.Add((kv.Key.Item1, kv.Value));
            return (main, shards);
        }

        const float RadomeSeamZ = 8.22f;
        // MiG frame: the wing's straight leading edge, z at |x| (sliced from the model: 2.845 at 2.1, 0.018 at 5.1), and the flap band
        const float LeRootX = 2.1f, LeTipX = 5.6f, LeSweep = 0.9433f, LeFlapChord = 0.35f;
        static float LeZ(float ax) => 2.845f - LeSweep * (ax - LeRootX);   // MiG frame: the ring where the radome meets the forward fuselage (dense vertex ring at z 8.21)
        const float EngineLength = 2.6f, EngineRadius = 0.42f;   // RD-33 body forward of the nozzle root, inside the nacelle

        const string FxAnchor = "MiG29_fx";
        static void AddAnchor(Transform parent, string name, Vector3 at)
        {
            var a = parent.Find(name) ?? new GameObject(name).transform;
            a.SetParent(parent, false);
            a.position = at;
        }

        static void SetHitbox(Component part, Mesh mesh)
        {
            var mc = part.GetComponent<MeshCollider>();
            if (mc == null) return;
            mc.sharedMesh = mesh;
            mc.convex = true;
        }

        static Mesh CubeMesh(string name, Vector3 c, float s)
        {
            var h = s / 2;
            var v = new List<Vector3>();
            for (int i = 0; i < 8; i++) v.Add(c + new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h));
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 }, 0);
            m.RecalculateBounds();
            return CreateOrReplace(m, $"{ModDir}/meshes/{name}.asset");
        }

        // ---------------- MiG-29 weights and engines ----------------

        // the empty aircraft's centre of gravity along the jet, as the flight model was tuned (tools/fm_sim.py), prefab frame
        const float DryCgZ = -0.7624f;
        const float MassScale = 0.72f;   // KR-67 16.0 t empty -> 11.5 t (MiG-29: ~11 t)
        const float FuelScale = 0.45f;   // 8.2 t internal fuel -> 3.7 t (MiG-29: ~3.5 t)

        // The 9.12's N019 in place of the KR-67's radar (the SMT has its Zhuk-ME). The game's radar sees a target while
        // (maxRange / distance) x RCS^0.25 - clutter x clutterFactor stays above minSignal, inside radarCone (half-angle, deg) of
        // the nose (RadarParams.GetSignalStrength, Radar.RadarCheck), so:
        //   maxRange 50 -> 45 km   the N019 sees roughly 0.6x as far as the SMT's Zhuk-ME (~70 against ~120 km against a fighter);
        //                          on the game's scale 45 km against the SMT's 70
        //   radarCone 30 -> 50 deg it scans wider than the KR-67 (about +-67 deg on the real set), less than the SMT
        //   clutterFactor 0.12 -> 0.16   its look-down / shoot-down was weak: low targets drop out sooner
        //   jamTolerance 0.7 -> 0.6      older ECCM: easier to jam, and (for the R-27R it guides) easier to chaff
        // The R-27R's seeker takes its range from this radar too (SARHSeeker.Initialize).
        const float RadarRange = 45000f, RadarCone = 50f, RadarClutter = 0.16f, RadarJamTolerance = 0.6f;

        static void TuneRadar(Transform root)
        {
            var radar = root.GetComponentInChildren(TypeByName("Radar"), true);
            if (radar == null) { Debug.LogError("[MiG29] radar: not found"); return; }
            var so = new SerializedObject(radar);
            so.FindProperty("RadarParameters.maxRange").floatValue = RadarRange;
            so.FindProperty("radarCone").floatValue = RadarCone;
            so.FindProperty("RadarParameters.clutterFactor").floatValue = RadarClutter;
            so.FindProperty("jamTolerance").floatValue = RadarJamTolerance;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[MiG29] radar: N019, {RadarRange / 1000:0} km, +-{RadarCone:0} deg, clutter {RadarClutter}, jam tolerance {RadarJamTolerance}");
        }

        static void TuneToMiG(Transform root)
        {
            void Each(string typeName, Action<SerializedObject> edit)
            {
                foreach (var c in root.GetComponentsInChildren(TypeByName(typeName), true))
                {
                    var so = new SerializedObject(c);
                    edit(so);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            // part masses and fuel capacities are the MiG's own (MiG29Parts.Restructure)
            // The aircraft you fly has one rigidbody per part, held together by FixedJoints (AeroPart.CreateJoints). At the stock 6
            // solver iterations the joints give under load: in a high-g pull the wings visibly rose off the fuselage and the airframe
            // looked to come apart (user video, v0.9.0). The parts form one PhysX island, which is solved with the highest count of
            // any of its bodies, so this stiffens the whole airframe. Break forces are unchanged.
            Each("UnitPart", so =>
            {
                var joints = so.FindProperty("joints");
                if (joints == null) return;
                for (int i = 0; i < joints.arraySize; i++) joints.GetArrayElementAtIndex(i).FindPropertyRelative("solverIterations").intValue = JointIterations;
            });
            StiffenNoseGear(root);
        }

        // The nose leg keeps the KR-67's spring (300 kN/m, 30 kN s/m), but on the MiG the nose wheel sits 3.6 m ahead of the mains
        // with the CG 0.7 m ahead of them, so the leg carries ~20 % of the weight. The KR-67 spread that load over a longer
        // wheelbase. On the MiG the nose sagged at rest, dived hard under braking and bobbed afterwards (user report, 0.9.2). The
        // real MiG's nose strut barely moves, so the spring is stiffer and damped near critical for the pitch-on-gear mode
        // (about 7 t at the nose).
        const float NoseSpringRate = 480000f, NoseDampingRate = 75000f;

        static void StiffenNoseGear(Transform root)
        {
            var wheel = Find(root, "wheel_F");
            foreach (var lg in root.GetComponentsInChildren(TypeByName("LandingGear"), true))
            {
                var so = new SerializedObject(lg);
                var hinge = so.FindProperty("gearHinge").objectReferenceValue as Transform;
                if (hinge == null || !wheel.IsChildOf(hinge)) continue;
                so.FindProperty("springRate").floatValue = NoseSpringRate;
                so.FindProperty("dampingRate").floatValue = NoseDampingRate;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[MiG29] nose gear {lg.name}: spring {NoseSpringRate}, damping {NoseDampingRate}");
                return;
            }
            throw new Exception("[MiG29] no LandingGear drives wheel_F");
        }

        // ---------------- internal bays: the MiG has none ----------------

        // (the forward bay's set is kept and becomes the centreline drop-tank station: MiG29Weapons.SetupCentreline)
        static readonly string[] BayNames = { "Rear Weapon Bay", "Side Weapon Bays" };
        static List<int> bayIndices = new List<int>();

        static List<int> BayIndices(GameObject go)
        {
            var wm = go.GetComponentInChildren(TypeByName("WeaponManager"), true);
            var sets = new SerializedObject(wm).FindProperty("hardpointSets");
            var idx = new List<int>();
            for (int i = 0; i < sets.arraySize; i++)
                if (BayNames.Contains(sets.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue))
                    idx.Add(i);
            return idx;
        }

        static void DeleteAt(SerializedProperty arr, int i)
        {
            var size = arr.arraySize;
            var el = arr.GetArrayElementAtIndex(i);
            if (el.propertyType == SerializedPropertyType.ObjectReference)
                el.objectReferenceValue = null;
            arr.DeleteArrayElementAtIndex(i);
            if (arr.arraySize == size) arr.DeleteArrayElementAtIndex(i);
        }

        // WeaponManager requires loadout.weapons.Count == hardpointSets.Length, so loadouts drop the same indices.
        static void RemoveInternalBays(GameObject go)
        {
            var wm = go.GetComponentInChildren(TypeByName("WeaponManager"), true);
            var so = new SerializedObject(wm);
            var sets = so.FindProperty("hardpointSets");
            foreach (var i in bayIndices.OrderByDescending(x => x))
                DeleteAt(sets, i);
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[MiG29] removed bay hardpoint sets {string.Join(",", bayIndices)}; {sets.arraySize} left");
        }

        static void RemoveBayLoadouts(SerializedObject pso)
        {
            void Drop(SerializedProperty weapons)
            {
                foreach (var i in bayIndices.OrderByDescending(x => x))
                    if (i < weapons.arraySize)
                        DeleteAt(weapons, i);
            }
            var loadouts = pso.FindProperty("loadouts");
            for (int i = 0; i < loadouts.arraySize; i++)
                Drop(loadouts.GetArrayElementAtIndex(i).FindPropertyRelative("weapons"));
            var std = pso.FindProperty("StandardLoadouts");
            for (int i = 0; i < std.arraySize; i++)
                Drop(std.GetArrayElementAtIndex(i).FindPropertyRelative("loadout.weapons"));
        }

        // Liveries are Addressables and must live inside the mod: one MiG livery per faction the KR-67 had.
        static void SetLiveries(SerializedObject pso)
        {
            var path = ModDir + "/MiG29_livery.asset";
            var livery = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (livery == null)
            {
                livery = ScriptableObject.CreateInstance(TypeByName("LiveryData"));
                AssetDatabase.CreateAsset(livery, path);
            }
            var lso = new SerializedObject(livery);
            lso.FindProperty("Texture").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(ModDir + "/textures/mig29_basecolor.png");
            lso.FindProperty("Glossiness").floatValue = 0f;
            var colors = lso.FindProperty("Colors");
            colors.arraySize = 1;
            colors.GetArrayElementAtIndex(0).FindPropertyRelative("Color").colorValue = new Color32(170, 178, 178, 255);
            colors.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 1;
            lso.ApplyModifiedPropertiesWithoutUndo();
            var guid = AssetDatabase.AssetPathToGUID(path);

            var liveries = pso.FindProperty("liveries");
            var seen = new HashSet<UnityEngine.Object>();
            for (int i = liveries.arraySize - 1; i >= 0; i--)
            {
                var faction = liveries.GetArrayElementAtIndex(i).FindPropertyRelative("faction").objectReferenceValue;
                if (!seen.Add(faction))
                    liveries.DeleteArrayElementAtIndex(i);
            }
            for (int i = 0; i < liveries.arraySize; i++)
            {
                var e = liveries.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = "Fulcrum Grey";
                e.FindPropertyRelative("assetReference.m_AssetGUID").stringValue = guid;
                e.FindPropertyRelative("assetReference.m_SubObjectName").stringValue = string.Empty;
            }
        }

        static void BuildHangarOp()
        {
            var path = ModDir + "/OpAddAircraftToHangars.asset";
            var op = AssetDatabase.LoadAssetAtPath<Blueprinter.OpAddAircraftToHangars>(path);
            if (op == null)
            {
                op = ScriptableObject.CreateInstance<Blueprinter.OpAddAircraftToHangars>();
                AssetDatabase.CreateAsset(op, path);
            }
            op.aircraftJsonKey = JsonKey;
            op.hangars = new List<Blueprinter.OpAddAircraftToHangars.HangarTarget>
            {
                new Blueprinter.OpAddAircraftToHangars.HangarTarget { hangarUnitJsonKey = "hangar_med", hangarNames = new List<string> { "hangar_med" } },
                new Blueprinter.OpAddAircraftToHangars.HangarTarget { hangarUnitJsonKey = "shelter1", hangarNames = new List<string> { "shelter1" } },
                new Blueprinter.OpAddAircraftToHangars.HangarTarget { hangarUnitJsonKey = "revetment1", hangarNames = new List<string> { "revetment1" } },
                // Hyperion Class Carrier: the three aft deck hangars that carry the KR-67 (hangar_F is helicopters only)
                new Blueprinter.OpAddAircraftToHangars.HangarTarget { hangarUnitJsonKey = "FleetCarrier1", hangarNames = new List<string> { "hangar_R1", "hangar_R2", "hangar_R3" } },
            };
            EditorUtility.SetDirty(op);
        }

        // The missions that ship with the mod (<repo>/mod/missions, copied to <source>/missions by build_mig29.ps1): each a TextAsset
        // in the mod, listed in the game's Missions tab by Blueprinter's OpAddMissions. "Operation Fulcrum (SEAD)" is by @calensk.11.
        static void BuildMissionOp()
        {
            var dir = ModDir + "/missions";
            var srcDir = Path.Combine(SourceDir, "missions");
            var files = Directory.Exists(srcDir) ? Directory.GetFiles(srcDir, "*.json").OrderBy(f => f).ToArray() : new string[0];
            AssetDatabase.DeleteAsset(dir);
            if (files.Length == 0) return;
            AssetDatabase.CreateFolder(ModDir, "missions");
            var missions = new List<TextAsset>();
            foreach (var f in files)
            {
                var dst = $"{dir}/{Path.GetFileName(f)}";
                File.Copy(f, dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(dst);
                if (asset == null) throw new Exception("[MiG29] mission did not import: " + dst);
                missions.Add(asset);
            }
            var op = ScriptableObject.CreateInstance<Blueprinter.Editor.Ops.OpAddMissions>();
            op.Missions = missions.ToArray();
            op.MissionGroups = new[] { Blueprinter.Editor.Ops.MissionGroup.Missions };
            AssetDatabase.CreateAsset(op, dir + "/OpAddMissions.asset");
            Debug.Log($"[MiG29] {missions.Count} mission(s): {string.Join(", ", missions.Select(m => m.name))}");
        }

        static void ModInfo(string displayName, string version) => Blueprinter.ModInfo.Save(ModName, displayName, version);

        // ------------------------------------------------------------------

        static Type TypeByName(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("Assembly-CSharp")) continue;
                var t = asm.GetType(name);
                if (t != null) return t;
            }
            throw new Exception("[MiG29] game type not found: " + name);
        }

        static T CreateOrReplace<T>(T obj, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(obj, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        static ScriptableObject CloneAsset(string srcPath, string dstPath)
        {
            var src = AssetDatabase.LoadAssetAtPath<ScriptableObject>(srcPath);
            var copy = UnityEngine.Object.Instantiate(src);
            copy.name = Path.GetFileNameWithoutExtension(dstPath);
            return CreateOrReplace(copy, dstPath);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [Serializable] class MeshDump { public PartDump[] parts; public InfoDump info; }
        [Serializable] class PartDump { public string name, material; public float[] vertices, normals, uvs, pivot, axis; public int[] triangles; }
        [Serializable] class InfoDump { public float[] nose_wheel_contact, main_wheel_contact_L, main_wheel_contact_R, canopy_center, cockpit_center; public float canopy_top_y, tail_min_x_u, scale; }
    }
}
