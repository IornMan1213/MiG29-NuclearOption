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
        public const string Version = "0.8.1";

        // MiG model frame -> aircraft root. Puts MiG main wheels on the KR-67 main gear and MiG wheels on KR-67 ground line.
        static readonly Vector3 ModelOffset = new Vector3(0f, -0.44f, -2.655f);
        // KR-67 cockpit interior / pilot shifted to sit under the MiG canopy.
        const float ThrustLineY = 0.0f;   // prefab frame: the aircraft's centre-of-mass height
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
            MoveGearAndEffects(root, data.info);
            // landing light: the KR-67 hangs it on its front gear door, 3.6 m ahead of the MiG's nose gear. On the MiG it sits on the
            // nose-gear strut, so it rides on the strut (retracting with it), just ahead of and above the wheel.
            var landingLight = Find(root, "gearLight_F");
            landingLight.SetParent(Find(root, "gear_F_sprung"), true);
            landingLight.position = Find(root, "wheel_F").position + new Vector3(0f, 0.72f, 0.42f);

            var visual = new GameObject("MiG29_visual").transform;
            visual.SetParent(root, false);
            visual.localPosition = ModelOffset;
            // canopy glass rides on the KR-67 cockpit part; the MiG model's own cockpit is replaced by MiG29Cockpit (from scratch)
            var exterior = new List<Renderer>();
            var cockpitPart = Find(root, "cockpit");
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
            var airframe = data.parts.Where(p => (p.pivot == null || p.pivot.Length != 3) && p.name != "canopy" && p.name != "cockpit" && p.name != "gear_doors_closed").ToList();
            SplitAirframe(root, visual, airframe, materials.skin, surfaces);
            UnityEngine.Object.DestroyImmediate(visual.gameObject);
            var doorsPart = data.parts.First(p => p.name == "gear_doors_closed");
            MiG29Polish.SetupGearDoors(go, ModelOffset, doorsPart.vertices, doorsPart.normals, doorsPart.uvs, doorsPart.triangles, materials.skin,
                (m, path) => CreateOrReplace(m, path), (t, r) => AddDamageRenderer(t, r));
            var fm = MiG29FlightModel.Load();
            MiG29FlightModel.ApplyToPrefab(root, fm, (n, c) => CubeMesh("MiG29_col_" + n, c, 0.3f));

            MoveCockpit(root);
            MiG29Polish.SetupCanopy(go, ModelOffset, migCanopy, (m, path) => CreateOrReplace(m, path), exterior);
            AddIntakeBlockers(root);
            MiG29Cockpit.Build(go, cockpitPart, ModelOffset, materials.skin, materials.glass, exterior, (o, path) => CreateOrReplace(o, path), (t, r) => AddDamageRenderer(t, r));
            AppendExteriorRenderers(go, exterior);
            TuneToMiG(root);
            bayIndices = BayIndices(go);
            RemoveInternalBays(go);
            var centreSet = MiG29Weapons.SetupCentreline(go, ModelOffset, weapons);
            var outerPylonSet = MiG29Weapons.SetupPylons(go, ModelOffset, weapons);
            MiG29Countermeasures.Setup(go, ModelOffset);
            var oldGun = MiG29Weapons.SetupGun(go, ModelOffset, weapons);
            var displays = MiG29Polish.BuildDisplays(go);

            var prefabPath = ModDir + "/MiG29.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);

            // --- definitions ---
            var parameters = CloneAsset(BaseParameters, ModDir + "/MiG29_parameters.asset");
            var pso = new SerializedObject(parameters);
            pso.FindProperty("aircraftName").stringValue = "MiG-29";
            RemoveBayLoadouts(pso);
            MiG29Weapons.ClearLoadoutSlot(pso, centreSet);
            MiG29Weapons.InsertLoadoutSlot(pso, outerPylonSet);
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
            dso.FindProperty("value").floatValue = 95f;
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
            MiG29Polish.BuildLoadingScreens();
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
            foreach (var name in new[] { "mig29_basecolor", "mig29_basecolor_dmg", "mig29_normal", "mig29_metallic" })
            {
                var dst = $"{ModDir}/textures/{name}.png";
                File.Copy(Path.Combine(SourceDir, name + ".png"), dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
                imp.maxTextureSize = 2048;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.textureType = name.EndsWith("normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                imp.sRGBTexture = name.Contains("basecolor");
                imp.SaveAndReimport();
                tex[name] = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            }

            var skin = new Material(AssetDatabase.LoadAssetAtPath<Material>(BaseSkin)) { name = "MiG29_skin" };
            skin.SetTexture("_Basecolor", tex["mig29_basecolor"]);
            skin.SetTexture("_Livery", tex["mig29_basecolor"]);
            skin.SetTexture("_BasecolorDmg", tex["mig29_basecolor_dmg"]);
            skin.SetTexture("_Normal", tex["mig29_normal"]);
            skin.SetTexture("_NormalDmg", tex["mig29_normal"]);
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

            // stowed wheels: KR-67 gear folds into KR-67 bays and pokes out of the MiG skin. LandingGear lerps
            // gearHinge.localPosition by hingeFoldMotion while folding, so translate each gear to a spot inside
            // the MiG body (found by blender/gearbay.py; MiG frame).
            StowGear(root, "wheel_L", new Vector3(-0.23f, -0.08f, 2.9f) + ModelOffset, MainGearExtraFold); // wheel mesh sits 0.27 m outboard of wheel_L
            StowGear(root, "wheel_R", new Vector3(0.23f, -0.08f, 2.9f) + ModelOffset, MainGearExtraFold);
            StowGear(root, "wheel_F", new Vector3(0f, 0.30f, 3.5f) + ModelOffset); // KR-67 nose wheel (0.66 m) fits the thin centre fuselage here: top 0.63 vs skin 0.66 at x 0.32 (blender ray scan)

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
            }

            // wing pylons to the MiG rails: inner/outer
            SetWorld(root, "hardpoint_pylon_L1", new Vector3(-2.40f, -0.27f, 0.90f) + ModelOffset);
            SetWorld(root, "hardpoint_pylon_R1", new Vector3(2.40f, -0.27f, 0.90f) + ModelOffset);
            SetWorld(root, "hardpoint_pylon_L2", new Vector3(-3.10f, -0.29f, 0.24f) + ModelOffset);
            SetWorld(root, "hardpoint_pylon_R2", new Vector3(3.10f, -0.29f, 0.24f) + ModelOffset);

            // wingtip light glow + vortex trails at the MiG wingtips (KR-67 meshes there are hidden; the glow sits
            // 0.7 m outboard of its parent, so place the effect objects themselves)
            foreach (var s in new[] { "L", "R" })
            {
                float sx = s == "L" ? -1 : 1;
                SetWorld(root, "navlight_" + s + "_effects", new Vector3(sx * 5.62f, -0.45f, -3.0f));
                SetWorld(root, "wingtipvortex_" + s, new Vector3(sx * 5.65f, -0.45f, -3.6f));
            }
        }

        // extra fold on the main legs so the KR-67 strut tips (stowed angled inboard) stay inside the belly tunnel
        static readonly float MainGearExtraFold = float.TryParse(Environment.GetEnvironmentVariable("MIG29_MAINFOLD") ?? "12", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 12f;

        static void StowGear(Transform root, string wheelName, Vector3 stowedWorld, float extraFold = 0f)
        {
            var wheel = Find(root, wheelName);
            var lgType = TypeByName("LandingGear");
            foreach (var lg in root.GetComponentsInChildren(lgType, true))
            {
                var so = new SerializedObject(lg);
                var hinge = so.FindProperty("gearHinge").objectReferenceValue as Transform;
                if (hinge == null || !wheel.IsChildOf(hinge))
                    continue;
                var fold = so.FindProperty("foldDegrees").floatValue + extraFold;
                so.FindProperty("foldDegrees").floatValue = fold;
                var baseRot = hinge.localEulerAngles;
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
                ["stab_L"] = ("elevator_L", -30, -8, 0, false, 60), // all-moving stabilator: pitch + differential roll
                ["stab_R"] = ("elevator_R", -30, 8, 0, false, 60),
                ["rudder_L"] = ("tail_L", 0, 0, -25, false, 60),
                ["rudder_R"] = ("tail_R", 0, 0, -25, false, 60),
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

        // The KR-67 main wheels (0.85 m) only fit inside the MiG intake ducts, where they show from dead ahead. A black panel 0.5 m
        // behind each intake lip reads as the dark duct. Intake mouth (MiG frame, blender ray scan): x 0.45..0.95, y -0.85..-0.32,
        // lip z ~4.1; the stowed wheels sit at z 2.45..3.4. Not a damage renderer: liveries retexture those (atlas UVs would pick up camo).
        static void AddIntakeBlockers(Transform root)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ModDir + "/weapons/MiG29_missiles.mat");
            var mesh = CreateOrReplace(AtlasBox("MiG29_intake_blocker", new Vector3(0.56f, 0.58f, 0.02f), 0.625f, 0.375f), ModDir + "/meshes/MiG29_intake_blocker.asset"); // atlas black
            foreach (var side in new[] { -1f, 1f })
            {
                var part = Find(root, side < 0 ? "intake_L" : "intake_R");
                var t = new GameObject(side < 0 ? "MiG29_intake_blocker_L" : "MiG29_intake_blocker_R").transform;
                t.SetParent(part, false);
                t.SetPositionAndRotation(new Vector3(side * 0.70f, -0.59f, 3.6f) + ModelOffset, Quaternion.identity);
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        // Box mesh centred on the origin, every vertex mapped to one flat-colour cell of the missile atlas (0.625, 0.875 = dgrey).
        static Mesh AtlasBox(string name, Vector3 size, float u, float v)
        {
            var e = size / 2;
            var faces = new[] { (Vector3.forward, Vector3.up), (Vector3.back, Vector3.up), (Vector3.left, Vector3.up), (Vector3.right, Vector3.up), (Vector3.up, Vector3.forward), (Vector3.down, Vector3.forward) };
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            foreach (var (n, up) in faces)
            {
                var side = Vector3.Cross(up, n);
                int b0 = verts.Count;
                foreach (var (a, c) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    verts.Add(Vector3.Scale(n + side * a + up * c, e)); norms.Add(n);
                }
                tris.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 }); // cross(b-a, c-a) along n: Unity front face (see missile_gen.py)
            }
            var m = new Mesh { name = name };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, Enumerable.Repeat(new Vector2(u, v), verts.Count).ToList()); m.SetTriangles(tris, 0);
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

        static Component AssignPart(Vector3 c, Dictionary<Component, Bounds> boxes, Transform root)
        {
            // MiG vertical fins sit where the KR-67 has nothing: give them to the tail parts
            if (Mathf.Abs(c.x) > 1.2f && Mathf.Abs(c.x) < 2.4f && c.y > 0.45f && c.z < -2.5f)
                return Find(root, c.x < 0 ? "tail_L" : "tail_R").GetComponent(TypeByName("UnitPart"));
            var k = ToKR67(c);
            Component best = null; float bestVol = float.MaxValue;
            foreach (var kv in boxes)
            {
                var b = kv.Value; b.Expand(0.1f);
                if (!b.Contains(k)) continue;
                var vol = b.size.x * b.size.y * b.size.z;
                if (vol < bestVol) { bestVol = vol; best = kv.Key; }
            }
            if (best != null) return best;
            float bestD = float.MaxValue;
            foreach (var kv in boxes)
            {
                var d = kv.Value.SqrDistance(k);
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
            return best;
        }

        class Bucket { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>(); }

        static void SplitAirframe(Transform root, Transform visual, List<PartDump> pieces, Material mat, List<(Component part, Renderer renderer)> surfaces)
        {
            var upType = TypeByName("UnitPart");
            // rudders hang off the tail parts, which also carry fin skin: only pure control-surface parts are excluded
            var surfaceParts = new HashSet<Component>(surfaces.Select(s => s.part).Where(p => !p.name.StartsWith("tail")));
            var boxes = new Dictionary<Component, Bounds>();
            foreach (var p in root.GetComponentsInChildren(upType, true))
            {
                if (surfaceParts.Contains(p)) continue;
                var mc = p.GetComponent<MeshCollider>();
                if (mc == null || mc.sharedMesh == null) continue;
                boxes[p] = WorldBounds(mc.sharedMesh.bounds, p.transform.localToWorldMatrix);
            }

            var buckets = new Dictionary<Component, Bucket>();
            foreach (var piece in pieces)
            {
                int n = piece.vertices.Length / 3;
                var wv = new Vector3[n]; var wn = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    wv[i] = visual.TransformPoint(new Vector3(piece.vertices[i * 3], piece.vertices[i * 3 + 1], piece.vertices[i * 3 + 2]));
                    wn[i] = visual.TransformDirection(new Vector3(piece.normals[i * 3], piece.normals[i * 3 + 1], piece.normals[i * 3 + 2]));
                }
                var remap = new Dictionary<(Component, int), int>();
                for (int t = 0; t < piece.triangles.Length; t += 3)
                {
                    var c = (wv[piece.triangles[t]] + wv[piece.triangles[t + 1]] + wv[piece.triangles[t + 2]]) / 3f;
                    var part = AssignPart(c, boxes, root);
                    if (!buckets.TryGetValue(part, out var b)) buckets[part] = b = new Bucket();
                    for (int k = 0; k < 3; k++)
                    {
                        int oi = piece.triangles[t + k];
                        if (!remap.TryGetValue((part, oi), out var ni))
                        {
                            ni = b.v.Count;
                            remap[(part, oi)] = ni;
                            b.v.Add(part.transform.InverseTransformPoint(wv[oi]));
                            b.n.Add(part.transform.InverseTransformDirection(wn[oi]));
                            b.uv.Add(new Vector2(piece.uvs[oi * 2], piece.uvs[oi * 2 + 1]));
                        }
                        b.t.Add(ni);
                    }
                }
            }

            foreach (var kv in buckets)
            {
                var part = kv.Key; var b = kv.Value;
                var m = new Mesh { name = "MiG29_part_" + part.name };
                m.indexFormat = b.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                m.SetVertices(b.v); m.SetNormals(b.n); m.SetUVs(0, b.uv); m.SetTriangles(b.t, 0);
                m.RecalculateBounds(); m.RecalculateTangents();
                m = CreateOrReplace(m, $"{ModDir}/meshes/MiG29_part_{part.name}.asset");
                var r = AddMeshObject("MiG29_skin_" + part.name, part.transform, m, mat);
                AddDamageRenderer(part.transform, r);
                Debug.Log($"[MiG29] part {part.name}: {b.t.Count / 3} tris");
            }

            // Hitboxes: the KR-67's own part colliders (designed not to overlap each other, which matters because in complex
            // physics every part is its own rigidbody and only jointed neighbours ignore each other) scaled to the MiG:
            // x * 0.8 puts the 7.1 m KR-67 semi-span on the MiG's 5.7 m and keeps every hull convex and disjoint.
            // (v0.2-0.4 used MiG-shaped hulls; overlapping hulls and tiny cube colliders blew the aircraft apart on spawn.)
            foreach (var kv in boxes.Keys.Concat(surfaces.Select(x => x.part)).Distinct())
            {
                var mc = kv.GetComponent<MeshCollider>();
                if (mc == null || mc.sharedMesh == null) continue;
                var t = kv.transform;
                var src = mc.sharedMesh;
                var verts = src.vertices.Select(v => { var w = t.TransformPoint(v); w.x *= HitboxWidthScale; return t.InverseTransformPoint(w); }).ToArray();
                var m = new Mesh { name = "MiG29_col_" + kv.name, vertices = verts, triangles = src.triangles };
                m.RecalculateBounds();
                SetHitbox(kv, CreateOrReplace(m, $"{ModDir}/meshes/MiG29_col_{kv.name}.asset"));
            }
        }

        const float HitboxWidthScale = 0.8f;

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

        const float MassScale = 0.72f;   // KR-67 16.0 t empty -> 11.5 t (MiG-29: ~11 t)
        const float FuelScale = 0.45f;   // 8.2 t internal fuel -> 3.7 t (MiG-29: ~3.5 t)

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
            Each("UnitPart", so => so.FindProperty("mass").floatValue *= MassScale);
            Each("FuelTank", so => so.FindProperty("fuelCapacity").floatValue *= FuelScale);
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
                e.FindPropertyRelative("name").stringValue = "Fulcrum Two-Tone Grey";
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
            };
            EditorUtility.SetDirty(op);
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
