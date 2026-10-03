using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MiG29Tools
{
    // MiG-29 (9.12) weapons: R-73 (AA-11 Archer), R-27R / R-27T (AA-10 Alamo-A / -B), R-60M (AA-8 Aphid), GSh-30-1 cannon.
    // Models: tools/missile_gen.py (procedural, from scratch) -> MiG29Source/missiles.json.
    // Missiles are cloned from stock prefabs for their effects/networking and re-tuned:
    //   R-73  <- AAM1 (MMR-S3, IR + thrust vectoring)
    //   R-27R <- AAM2 (AAM-29, two-stage motor) with its ARH seeker replaced by the SARH seeker of SAM_Radar1
    //            (SARHSeeker guides on missile.owner.radar, so the launching MiG must keep the target illuminated).
    //   R-27T <- AAM2 with the IR seeker of AAM1
    //   R-60M <- AAM1 without thrust vectoring
    //   GSh-30-1 <- gun_27mm_internal (150 rounds, ~1650 rpm, 870 m/s), muzzle moved to the left LERX root.
    public static class MiG29Weapons
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string Dir = ModDir + "/weapons";
        const string GO = "Assets/Blueprinter/_donotship/GameObject/";
        const string MB = "Assets/Blueprinter/_donotship/MonoBehaviour/";

        [Serializable] class PartDump { public string name; public float[] vertices, normals, uvs; public int[] triangles; }
        [Serializable] class Dump { public PartDump[] parts; }

        public class Result { public ScriptableObject r73Mount, r27Mount, r27tMount, r60Mount, gunMount, ptb1500Mount, ptb1150Mount; }

        public delegate T Saver<T>(T obj, string path) where T : UnityEngine.Object;

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);

        static Transform Child(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t == null) throw new Exception($"[MiG29] {root.name}: child not found: {name}");
            return t;
        }

        static Func<UnityEngine.Object, string, UnityEngine.Object> save;

        public static Result Build(Func<UnityEngine.Object, string, UnityEngine.Object> saveAsset, Material skinTemplate)
        {
            save = saveAsset;
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder(ModDir, "weapons");
            var dump = JsonUtility.FromJson<Dump>(File.ReadAllText("MiG29Source/missiles.json"));
            var meshes = dump.parts.ToDictionary(p => p.name, p => (Mesh)save(ToMesh(p), $"{Dir}/MiG29_{p.name}.asset"));
            var mat = (Material)save(MissileMaterial(skinTemplate), $"{Dir}/MiG29_missiles.mat");

            // ---------- R-73 ----------
            var r73 = MakeMissile("AAM1", "mig29_R73", meshes["R73"], mat, length: 2.90f, radius: 0.085f, seekerFrom: null, seekerType: null);
            Tune(r73, so =>
            {
                so.FindProperty("mass").floatValue = 105f;
                so.FindProperty("finArea").floatValue = 0.16f;
                so.FindProperty("gLimit").floatValue = 45f;       // ~40-60 g quoted for the R-73
                so.FindProperty("maxTurnRate").floatValue = 90f;  // gas-vane thrust vectoring: very nimble off the rail
                so.FindProperty("torque").floatValue = 4f;
                Motor(so, 0, thrust: 26000f, burn: 3.5f, fuel: 38f, tvc: 15f);
            });
            TuneSeeker(r73, "IRSeeker", so => so.FindProperty("flareRejection").floatValue = 2.2f);
            var r73Prefab = SavePrefab(r73, $"{Dir}/mig29_R73.prefab");

            var r73Def = CloneDef("AAM1", "mig29_R73_def", d =>
            {
                d.FindProperty("jsonKey").stringValue = "mig29_R73";
                d.FindProperty("unitName").stringValue = "R-73";
                d.FindProperty("description").stringValue = "R-73 (AA-11 Archer). Short-range, all-aspect infrared missile with gas-vane thrust vectoring and a wide off-boresight seeker, the MiG-29's dogfight weapon.";
                d.FindProperty("mass").floatValue = 105f;
                d.FindProperty("unitPrefab").objectReferenceValue = r73Prefab;
            });
            SetDefinition(r73Prefab, r73Def);
            var r73Info = CloneInfo("AAM1", "mig29_R73_info", r73Prefab, "R-73", "R-73",
                "Short-range infrared missile. Thrust vectoring and a 45 degree off-boresight seeker; best inside 15 km.",
                minRange: 300f, maxRange: 20000f, minAlignment: 45f, cost: 0.30f, massPerRound: 105f);

            // ---------- R-27R ----------
            var r27 = MakeMissile("AAM2", "mig29_R27R", meshes["R27R"], mat, length: 4.08f, radius: 0.115f, seekerFrom: "SAM_Radar1", seekerType: "SARHSeeker");
            Tune(r27, so =>
            {
                so.FindProperty("mass").floatValue = 253f;
                so.FindProperty("finArea").floatValue = 0.7f;
                so.FindProperty("gLimit").floatValue = 25f;
                so.FindProperty("maxTurnRate").floatValue = 25f;
                so.FindProperty("torque").floatValue = 1.5f;
                Motor(so, 0, thrust: 52000f, burn: 2.5f, fuel: 55f, tvc: 0f);   // boost
                Motor(so, 1, thrust: 16000f, burn: 6.0f, fuel: 40f, tvc: 0f);   // sustain
            });
            TuneSeeker(r27, "SARHSeeker", so =>
            {
                so.FindProperty("seekerAngle").floatValue = 50f;
                so.FindProperty("armDelay").floatValue = 1f;
                so.FindProperty("guidanceDelay").floatValue = 0.5f;
                so.FindProperty("lockPersistence").floatValue = 2f;
            });
            var r27Prefab = SavePrefab(r27, $"{Dir}/mig29_R27R.prefab");

            var r27Def = CloneDef("AAM2", "mig29_R27R_def", d =>
            {
                d.FindProperty("jsonKey").stringValue = "mig29_R27R";
                d.FindProperty("unitName").stringValue = "R-27R";
                d.FindProperty("description").stringValue = "R-27R (AA-10 Alamo-A). Medium-range semi-active radar homing missile. The launching aircraft must keep its radar on the target until impact.";
                d.FindProperty("mass").floatValue = 253f;
                d.FindProperty("unitPrefab").objectReferenceValue = r27Prefab;
            });
            SetDefinition(r27Prefab, r27Def);
            var r27Info = CloneInfo("AAM2", "mig29_R27R_info", r27Prefab, "R-27R", "R-27R",
                "Medium-range semi-active radar missile. Keep the target locked with your radar until impact.",
                minRange: 1000f, maxRange: 50000f, minAlignment: 30f, cost: 0.70f, massPerRound: 253f);

            // ---------- launchers / mounts ----------
            var r73MountPrefab = MakeMount("AAM1_single", "mig29_R73_APU73", "pylon", "aam1", meshes["APU73"], meshes["R73"], mat,
                missileY: -0.10f - 0.085f, r73Info, 2.90f, 0.085f, $"{Dir}/mig29_R73_APU73.prefab");
            var r27MountPrefab = MakeMount("AAM2_single", "mig29_R27R_AKU470", "pylon", "aam2", meshes["AKU470"], meshes["R27R"], mat,
                missileY: -0.14f - 0.115f, r27Info, 4.08f, 0.115f, $"{Dir}/mig29_R27R_AKU470.prefab");

            var r73Mount = CloneMount("AAM1_single", "mig29_R73_mount", r73MountPrefab, r73Info, "mig29_R73_single", "R-73", mass: 145f, emptyMass: 40f, drag: 0.04f);
            var r27Mount = CloneMount("AAM2_single", "mig29_R27R_mount", r27MountPrefab, r27Info, "mig29_R27R_single", "R-27R", mass: 333f, emptyMass: 80f, drag: 0.07f);

            // ---------- R-27T: R-27 airframe, infrared seeker ----------
            var r27t = MakeMissile("AAM2", "mig29_R27T", meshes["R27T"], mat, length: 3.80f, radius: 0.115f, seekerFrom: "AAM1", seekerType: "IRSeeker");
            Tune(r27t, so =>
            {
                so.FindProperty("mass").floatValue = 245f;
                so.FindProperty("finArea").floatValue = 0.7f;
                so.FindProperty("gLimit").floatValue = 25f;
                so.FindProperty("maxTurnRate").floatValue = 25f;
                so.FindProperty("torque").floatValue = 1.5f;
                Motor(so, 0, thrust: 52000f, burn: 2.5f, fuel: 55f, tvc: 0f);
                Motor(so, 1, thrust: 16000f, burn: 6.0f, fuel: 40f, tvc: 0f);
            });
            TuneSeeker(r27t, "IRSeeker", so => so.FindProperty("flareRejection").floatValue = 1.6f);
            var r27tPrefab = SavePrefab(r27t, $"{Dir}/mig29_R27T.prefab");
            var r27tDef = CloneDef("AAM2", "mig29_R27T_def", d =>
            {
                d.FindProperty("jsonKey").stringValue = "mig29_R27T";
                d.FindProperty("unitName").stringValue = "R-27T";
                d.FindProperty("description").stringValue = "R-27T (AA-10 Alamo-B). The R-27 airframe with an infrared seeker: fire-and-forget at medium range, but the seeker must lock before launch.";
                d.FindProperty("mass").floatValue = 245f;
                d.FindProperty("unitPrefab").objectReferenceValue = r27tPrefab;
            });
            SetDefinition(r27tPrefab, r27tDef);
            var r27tInfo = CloneInfo("AAM1", "mig29_R27T_info", r27tPrefab, "R-27T", "R-27T",
                "Medium-range infrared missile. Fire-and-forget; lock the target's heat signature before launch.",
                minRange: 800f, maxRange: 30000f, minAlignment: 25f, cost: 0.60f, massPerRound: 245f);
            var r27tMountPrefab = MakeMount("AAM2_single", "mig29_R27T_AKU470", "pylon", "aam2", meshes["AKU470"], meshes["R27T"], mat,
                missileY: -0.14f - 0.115f, r27tInfo, 3.80f, 0.115f, $"{Dir}/mig29_R27T_AKU470.prefab");
            var r27tMount = CloneMount("AAM2_single", "mig29_R27T_mount", r27tMountPrefab, r27tInfo, "mig29_R27T_single", "R-27T", mass: 325f, emptyMass: 80f, drag: 0.07f);

            // ---------- R-60M: small dogfight missile ----------
            var r60 = MakeMissile("AAM1", "mig29_R60M", meshes["R60M"], mat, length: 2.09f, radius: 0.06f, seekerFrom: null, seekerType: null);
            Tune(r60, so =>
            {
                so.FindProperty("mass").floatValue = 44f;
                so.FindProperty("finArea").floatValue = 0.06f;
                so.FindProperty("gLimit").floatValue = 40f;
                so.FindProperty("maxTurnRate").floatValue = 55f;
                so.FindProperty("torque").floatValue = 2.5f;
                Motor(so, 0, thrust: 9500f, burn: 3.0f, fuel: 12f, tvc: 0f);
            });
            TuneSeeker(r60, "IRSeeker", so => so.FindProperty("flareRejection").floatValue = 1.4f);
            var r60Prefab = SavePrefab(r60, $"{Dir}/mig29_R60M.prefab");
            var r60Def = CloneDef("AAM1", "mig29_R60M_def", d =>
            {
                d.FindProperty("jsonKey").stringValue = "mig29_R60M";
                d.FindProperty("unitName").stringValue = "R-60M";
                d.FindProperty("description").stringValue = "R-60M (AA-8 Aphid). Light, very agile short-range infrared missile. Less range and flare resistance than the R-73, but small and cheap.";
                d.FindProperty("mass").floatValue = 44f;
                d.FindProperty("unitPrefab").objectReferenceValue = r60Prefab;
            });
            SetDefinition(r60Prefab, r60Def);
            var r60Info = CloneInfo("AAM1", "mig29_R60M_info", r60Prefab, "R-60M", "R-60M",
                "Light short-range infrared missile. Very agile; best inside 6 km.",
                minRange: 200f, maxRange: 8000f, minAlignment: 30f, cost: 0.12f, massPerRound: 44f);
            var r60MountPrefab = MakeMount("AAM1_single", "mig29_R60M_APU60", "pylon", "aam1", meshes["APU60"], meshes["R60M"], mat,
                missileY: -0.08f - 0.06f, r60Info, 2.09f, 0.06f, $"{Dir}/mig29_R60M_APU60.prefab");
            var r60Mount = CloneMount("AAM1_single", "mig29_R60M_mount", r60MountPrefab, r60Info, "mig29_R60M_single", "R-60M", mass: 66f, emptyMass: 22f, drag: 0.025f);

            var gunMount = BuildGun();

            // ---------- drop tanks (released like a dumb bomb; the instruments plugin feeds their fuel to the internal tanks) ----------
            var ptb1500Mount = BuildDropTank("PTB1500", "PTB-1500", meshes["PTB1500"], meshes["PTB_PYLON_C"], mat, length: 4.9f, halfHeight: 0.34f, radius: 0.34f,
                pylonDepth: 0.08f, fullMass: 1300f, pylonMass: 35f, drag: 0.10f, rcs: 0.06f,
                desc: "PTB-1500 centreline drop tank: 1,500 litres (about 1,180 kg) of fuel, used before the internal fuel. Select it and fire to drop it, or press the jettison key (Ctrl+J by default).");
            var ptb1150Mount = BuildDropTank("PTB1150", "PTB-1150", meshes["PTB1150"], meshes["PTB_PYLON_W"], mat, length: 4.6f, halfHeight: 0.32f, radius: 0.32f,
                pylonDepth: 0.10f, fullMass: 1000f, pylonMass: 30f, drag: 0.08f, rcs: 0.05f,
                desc: "PTB-1150 wing drop tanks: 1,150 litres (about 905 kg) of fuel each, used before the internal fuel. Select them and fire to drop them, or press the jettison key (Ctrl+J by default).");
            Debug.Log("[MiG29] weapons built: R-73, R-27R, R-27T, R-60M, GSh-30-1, PTB-1500, PTB-1150");
            return new Result { r73Mount = r73Mount, r27Mount = r27Mount, r27tMount = r27tMount, r60Mount = r60Mount, gunMount = gunMount,
                ptb1500Mount = ptb1500Mount, ptb1150Mount = ptb1150Mount };
        }

        // ---------------- drop tanks ----------------

        // The tank is a "bomb" with no steering authority (torque 0) and almost no warhead: on impact it bursts with a small puff of
        // dust or spray. Its weapon info has zero effectiveness against everything, so the AI never picks it as a weapon.
        static ScriptableObject BuildDropTank(string id, string name, Mesh tank, Mesh pylon, Material mat, float length, float halfHeight, float radius,
            float pylonDepth, float fullMass, float pylonMass, float drag, float rcs, string desc)
        {
            var go = MakeMissile("bomb_250_1", "mig29_" + id, tank, mat, length, radius, seekerFrom: null, seekerType: null);
            UnityEngine.Object Fx(string n) => AssetDatabase.LoadAssetAtPath<GameObject>(GO + n + "_PLACEHOLDER.prefab");
            Tune(go, so =>
            {
                so.FindProperty("mass").floatValue = fullMass;
                so.FindProperty("torque").floatValue = 0f;          // no steering: it falls where it is dropped
                so.FindProperty("maxTurnRate").floatValue = 0f;
                so.FindProperty("blastYield").floatValue = 1f;      // a bursting tank, not a bomb
                so.FindProperty("pierceDamage").floatValue = 20f;
                so.FindProperty("impactFuse").boolValue = true;
                var wh = so.FindProperty("warhead");
                wh.FindPropertyRelative("airEffect").objectReferenceValue = Fx("cannonHit_50mm_dusty");
                wh.FindPropertyRelative("armorEffect").objectReferenceValue = Fx("cannonHit_50mm_dusty");
                wh.FindPropertyRelative("terrainEffect").objectReferenceValue = Fx("cannonHit_50mm_dusty");
                wh.FindPropertyRelative("waterSurfaceEffect").objectReferenceValue = Fx("ShellSplash_10kg");
                wh.FindPropertyRelative("underwaterEffect").objectReferenceValue = Fx("ShellSplash_10kg");
                wh.FindPropertyRelative("fizzleEffect").objectReferenceValue = null;
            });
            TuneSeeker(go, "OpticalSeekerBomb", so =>
            {
                so.FindProperty("searchRadius").floatValue = 0f;
                so.FindProperty("tangibleDelay").floatValue = 1.0f;   // clear of the jet before it can hit anything
                so.FindProperty("armDelay").floatValue = 1.5f;
                so.FindProperty("altitudeFuseHeight").floatValue = 0f;
            });
            var prefab = SavePrefab(go, $"{Dir}/mig29_{id}.prefab");

            var def = CloneDef("Bomb_250_1", $"mig29_{id}_def", d =>
            {
                d.FindProperty("jsonKey").stringValue = "mig29_" + id;
                d.FindProperty("unitName").stringValue = name;
                d.FindProperty("description").stringValue = desc;
                d.FindProperty("mass").floatValue = fullMass;
                d.FindProperty("value").floatValue = 0.01f;
                d.FindProperty("unitPrefab").objectReferenceValue = prefab;
            });
            SetDefinition(prefab, def);

            var info = Clone("info_bomb_250_1", $"mig29_{id}_info");
            var iso = new SerializedObject(info);
            iso.FindProperty("weaponPrefab").objectReferenceValue = prefab;
            iso.FindProperty("weaponName").stringValue = name + " Drop Tank";
            iso.FindProperty("shortName").stringValue = name;
            iso.FindProperty("description").stringValue = desc;
            foreach (var e in new[] { "antiSurface", "antiAir", "antiMissile", "antiRadar" })
                iso.FindProperty("effectiveness." + e).floatValue = 0f;
            iso.FindProperty("pierceDamage").floatValue = 20f;
            iso.FindProperty("blastDamage").floatValue = 0f;
            iso.FindProperty("costPerRound").floatValue = 0.01f;
            iso.FindProperty("massPerRound").floatValue = fullMass;
            iso.FindProperty("fireInterval").floatValue = 0.3f;
            iso.ApplyModifiedPropertiesWithoutUndo();

            var mountPrefab = MakeMount("bomb_250_single", $"mig29_{id}_rack", "pylon", "bomb", pylon, tank, mat,
                missileY: -pylonDepth - halfHeight, info, length, radius, $"{Dir}/mig29_{id}_rack.prefab");
            // a short, quick release instead of the bomb rack's slow 0.5 m slide
            var path = AssetDatabase.GetAssetPath(mountPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            var mm = new SerializedObject(root.GetComponentInChildren(T("MountedMissile"), true));
            mm.FindProperty("railLength").floatValue = 0.12f;
            mm.FindProperty("railSpeed").floatValue = 2f;
            mm.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            mountPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var m = CloneMount("bomb_250_single", $"mig29_{id}_mount", mountPrefab, info, $"mig29_{id}", name + " drop tank",
                mass: fullMass + pylonMass, emptyMass: pylonMass, drag: drag);
            var mso = new SerializedObject(m);
            mso.FindProperty("emptyDrag").floatValue = 0.01f;
            mso.FindProperty("RCS").floatValue = rcs;
            mso.FindProperty("emptyRCS").floatValue = 0.004f;
            mso.FindProperty("GearSafety").boolValue = true;     // no release with the gear down or on the ground
            mso.FindProperty("GroundSafety").boolValue = true;
            mso.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        // ---------------- GSh-30-1 ----------------

        // MiG frame: muzzle inside the left LERX root, level with the rear of the canopy (blender ray cast: LERX y -0.02..0.24 at x -1.1, z 5.0)
        public static readonly Vector3 GunMuzzle = new Vector3(-1.10f, 0.11f, 5.0f);

        static ScriptableObject BuildGun()
        {
            var info = Clone("Gun27mm_Autocannon", "mig29_GSh301_info");
            var iso = new SerializedObject(info);
            iso.FindProperty("weaponName").stringValue = "GSh-30-1 Cannon";
            iso.FindProperty("shortName").stringValue = "GUN 30MM";
            iso.FindProperty("description").stringValue = "GSh-30-1: single-barrel 30 mm recoil-operated cannon in the left wing root, 150 rounds. Fires about 1,650 rounds per minute; a one-second burst is a sixth of the magazine, so fire short bursts.";
            iso.FindProperty("muzzleVelocity").floatValue = 870f;
            iso.FindProperty("pierceDamage").floatValue = 600f;
            iso.FindProperty("blastDamage").floatValue = 0.06f;
            iso.FindProperty("massPerRound").floatValue = 0.83f;
            iso.FindProperty("targetRequirements.maxRange").floatValue = 1800f;
            iso.FindProperty("visibilityWhenFired").floatValue = 2500f;
            iso.ApplyModifiedPropertiesWithoutUndo();

            var go = Instance("gun_27mm_internal", "mig29_GSh301");
            var gso = new SerializedObject(go.GetComponentInChildren(T("Gun"), true));
            gso.FindProperty("info").objectReferenceValue = info;
            gso.FindProperty("fireRate").floatValue = 1650f;
            gso.FindProperty("magazineCapacity").intValue = 150;
            gso.FindProperty("ammo").intValue = 150;
            gso.FindProperty("recoilImpulse").floatValue = 420f;
            gso.FindProperty("tracerRatio").intValue = 3;
            gso.FindProperty("bulletSpread").floatValue = 3f;
            gso.ApplyModifiedPropertiesWithoutUndo();
            var prefab = SavePrefab(go, $"{Dir}/mig29_GSh301.prefab");

            var m = Clone("gun_27mm_internal", "mig29_GSh301_mount");
            var mso = new SerializedObject(m);
            mso.FindProperty("prefab").objectReferenceValue = prefab;
            mso.FindProperty("info").objectReferenceValue = info;
            mso.FindProperty("jsonKey").stringValue = "mig29_gsh301_internal";
            mso.FindProperty("mountName").stringValue = "30mm GSh-30-1 (150 rds)";
            mso.FindProperty("ammo").intValue = 150;
            mso.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        // Internal cannon: the GSh-30-1 mount replaces the 27 mm; the hardpoint moves so the muzzle (3 m ahead of it in the gun prefab) is at GunMuzzle.
        public static UnityEngine.Object SetupGun(GameObject go, Vector3 modelOffset, Result w)
        {
            var so = new SerializedObject(go.GetComponentInChildren(T("WeaponManager"), true));
            var sets = so.FindProperty("hardpointSets");
            UnityEngine.Object old = null;
            for (int i = 0; i < sets.arraySize; i++)
            {
                var set = sets.GetArrayElementAtIndex(i);
                if (set.FindPropertyRelative("name").stringValue != "Internal Cannon") continue;
                set.FindPropertyRelative("name").stringValue = "GSh-30-1";
                var opts = set.FindPropertyRelative("weaponOptions");
                for (int k = 0; k < opts.arraySize; k++)
                    if (opts.GetArrayElementAtIndex(k).objectReferenceValue != null)
                    {
                        old = opts.GetArrayElementAtIndex(k).objectReferenceValue;
                        opts.GetArrayElementAtIndex(k).objectReferenceValue = w.gunMount;
                    }
                var hp = set.FindPropertyRelative("hardpoints").GetArrayElementAtIndex(0);
                var t = (Transform)hp.FindPropertyRelative("transform").objectReferenceValue;
                t.position = GunMuzzle + modelOffset - new Vector3(0, 0.038f, 3.0f);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            if (old == null) throw new Exception("[MiG29] internal cannon set not found");
            Debug.Log($"[MiG29] GSh-30-1 installed in place of {old.name}");
            return old;
        }

        public static int ReplaceInLoadouts(SerializedObject pso, UnityEngine.Object from, UnityEngine.Object to)
        {
            int n = 0;
            void Rep(SerializedProperty weapons)
            {
                for (int i = 0; i < weapons.arraySize; i++)
                    if (weapons.GetArrayElementAtIndex(i).objectReferenceValue == from) { weapons.GetArrayElementAtIndex(i).objectReferenceValue = to; n++; }
            }
            var loadouts = pso.FindProperty("loadouts");
            for (int i = 0; i < loadouts.arraySize; i++) Rep(loadouts.GetArrayElementAtIndex(i).FindPropertyRelative("weapons"));
            var std = pso.FindProperty("StandardLoadouts");
            for (int i = 0; i < std.arraySize; i++) Rep(std.GetArrayElementAtIndex(i).FindPropertyRelative("loadout.weapons"));
            return n;
        }

        // ---------------- helpers ----------------

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
            var m = new Mesh { name = "MiG29_" + p.name };
            m.vertices = v; m.normals = nr; m.uv = uv; m.triangles = p.triangles;
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        static Texture2D ImportTex(string file, bool srgb, bool normal)
        {
            var dst = $"{Dir}/{file}";
            File.Copy(Path.Combine("MiG29Source", file), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.filterMode = FilterMode.Point;          // flat colour cells in the atlas
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static Material MissileMaterial(Material skinTemplate)
        {
            var atlas = ImportTex("missile_atlas.png", true, false);
            var m = new Material(skinTemplate) { name = "MiG29_missiles" };
            m.SetTexture("_Basecolor", atlas); m.SetTexture("_Livery", atlas); m.SetTexture("_BasecolorDmg", atlas);
            var nrm = ImportTex("flat_normal.png", false, true);
            m.SetTexture("_Normal", nrm); m.SetTexture("_NormalDmg", nrm);
            m.SetTexture("_Metallic", ImportTex("missile_metallic.png", false, false));
            m.SetTexture("_AO", null);
            return m;
        }

        static GameObject Instance(string basePrefab, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GO + basePrefab + "_PLACEHOLDER.prefab"));
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            return go;
        }

        static GameObject MakeMissile(string basePrefab, string name, Mesh mesh, Material mat, float length, float radius, string seekerFrom, string seekerType)
        {
            var go = Instance(basePrefab, name);
            // the stock missile mesh lives on the root: swap it, drop other stock meshes (fins etc.)
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;
                if (mf.transform == go.transform) { mf.sharedMesh = mesh; r.sharedMaterials = new[] { mat }; }
                else mf.sharedMesh = null;
            }
            // exhaust effects and missile camera to the new tail
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLower();
                if (t == go.transform) continue;
                if (n.Contains("fire") || n.Contains("smoke") || n.Contains("effects")) t.localPosition = new Vector3(0, 0, -length / 2 - 0.05f);
                if (n == "missilecam") t.localPosition = new Vector3(0, 0, -length);
            }
            foreach (var c in go.GetComponentsInChildren<CapsuleCollider>(true)) { c.radius = radius; c.height = length; c.center = Vector3.zero; c.direction = 2; }

            if (seekerFrom != null)
            {
                foreach (var s in go.GetComponents(T("MissileSeeker"))) UnityEngine.Object.DestroyImmediate(s, true);
                var donor = AssetDatabase.LoadAssetAtPath<GameObject>(GO + seekerFrom + "_PLACEHOLDER.prefab");
                ComponentUtility.CopyComponent(donor.GetComponent(T(seekerType)));
                ComponentUtility.PasteComponentAsNew(go);
                // the copy may point at objects inside the donor prefab (e.g. the SAM's radar); those are runtime-assigned anyway
                var donorPath = AssetDatabase.GetAssetPath(donor);
                var sso = new SerializedObject(go.GetComponent(T(seekerType)));
                var it = sso.GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null && AssetDatabase.GetAssetPath(it.objectReferenceValue) == donorPath)
                        it.objectReferenceValue = null;
                // ...except the seeker's own missile: MissileSeeker.missile must point at this missile, or Initialize/Seek throw
                // a NullReferenceException at launch and on every physics tick (v0.5.0 bug, R-27R and R-27T)
                sso.FindProperty("missile").objectReferenceValue = go.GetComponent(T("Missile"));
                sso.ApplyModifiedPropertiesWithoutUndo();
                // the airframe donor (AAM2) is active-radar; take the seeker donor's mode (passive SARH / IR)
                var mso = new SerializedObject(go.GetComponent(T("Missile")));
                mso.FindProperty("seekerMode").enumValueIndex = new SerializedObject(donor.GetComponent(T("Missile"))).FindProperty("seekerMode").enumValueIndex;
                mso.ApplyModifiedPropertiesWithoutUndo();
            }
            return go;
        }

        static void Tune(GameObject go, Action<SerializedObject> edit)
        {
            var so = new SerializedObject(go.GetComponent(T("Missile")));
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void TuneSeeker(GameObject go, string type, Action<SerializedObject> edit)
        {
            var so = new SerializedObject(go.GetComponent(T(type)));
            edit(so);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Motor(SerializedObject so, int i, float thrust, float burn, float fuel, float tvc)
        {
            var m = so.FindProperty("motors").GetArrayElementAtIndex(i);
            m.FindPropertyRelative("thrust").floatValue = thrust;
            m.FindPropertyRelative("burnTime").floatValue = burn;
            m.FindPropertyRelative("fuelMass").floatValue = fuel;
            var tv = m.FindPropertyRelative("thrustVectoring");
            if (tv != null) tv.floatValue = tvc;
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            var p = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return p;
        }

        static void SetDefinition(GameObject prefab, ScriptableObject def)
        {
            var path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            var so = new SerializedObject(root.GetComponent(T("Missile")));
            so.FindProperty("definition").objectReferenceValue = def;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static ScriptableObject Clone(string src, string dstName)
        {
            var copy = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ScriptableObject>(MB + src + "_PLACEHOLDER.asset"));
            copy.name = dstName;
            return (ScriptableObject)save(copy, $"{Dir}/{dstName}.asset");
        }

        static ScriptableObject CloneDef(string src, string name, Action<SerializedObject> edit)
        {
            var d = Clone(src, name);
            var so = new SerializedObject(d); edit(so); so.ApplyModifiedPropertiesWithoutUndo();
            return d;
        }

        static ScriptableObject CloneInfo(string src, string name, GameObject prefab, string weaponName, string shortName, string desc,
            float minRange, float maxRange, float minAlignment, float cost, float massPerRound)
        {
            var info = Clone(src + "_info", name);
            var so = new SerializedObject(info);
            so.FindProperty("weaponPrefab").objectReferenceValue = prefab;
            so.FindProperty("weaponName").stringValue = weaponName;
            so.FindProperty("shortName").stringValue = shortName;
            so.FindProperty("description").stringValue = desc;
            so.FindProperty("targetRequirements.minRange").floatValue = minRange;
            so.FindProperty("targetRequirements.maxRange").floatValue = maxRange;
            so.FindProperty("targetRequirements.minAlignment").floatValue = minAlignment;
            so.FindProperty("costPerRound").floatValue = cost;
            so.FindProperty("massPerRound").floatValue = massPerRound;
            so.ApplyModifiedPropertiesWithoutUndo();
            return info;
        }

        static GameObject MakeMount(string basePrefab, string name, string pylonChild, string missileChild, Mesh launcher, Mesh missile, Material mat,
            float missileY, ScriptableObject info, float length, float radius, string path)
        {
            var go = Instance(basePrefab, name);
            var rootMf = go.GetComponent<MeshFilter>();
            if (rootMf != null) rootMf.sharedMesh = null; // stock "large pylon" fairing on the root

            var pylon = Child(go.transform, pylonChild);
            pylon.localPosition = Vector3.zero; pylon.localRotation = Quaternion.identity; pylon.localScale = Vector3.one; // stock children carry scales for the stock meshes; ours are true size
            pylon.GetComponent<MeshFilter>().sharedMesh = launcher;
            pylon.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
            // rail collider: the launcher's box minus its bottom 3 cm, so it never touches the missile (a launched missile spawning
            // in contact with the rail on the wing is a candidate for the reported "aircraft explodes when firing")
            foreach (var bc in pylon.GetComponents<BoxCollider>())
            {
                var b = launcher.bounds; var size = b.size; size.y = Mathf.Max(size.y - 0.03f, 0.02f);
                bc.center = b.center + Vector3.up * (b.size.y - size.y) / 2; bc.size = size;
            }

            var msl = Child(go.transform, missileChild);
            msl.localPosition = new Vector3(0, missileY - 0.015f, 0); msl.localRotation = Quaternion.identity; msl.localScale = Vector3.one; // 1.5 cm below the rail
            msl.GetComponent<MeshFilter>().sharedMesh = missile;
            msl.GetComponent<MeshRenderer>().sharedMaterials = new[] { mat };
            foreach (var c in msl.GetComponents<CapsuleCollider>()) { c.radius = radius; c.height = length; c.center = Vector3.zero; c.direction = 2; }
            var lod = msl.GetComponent<LODGroup>();
            if (lod != null) lod.RecalculateBounds();

            var so = new SerializedObject(msl.GetComponent(T("MountedMissile")));
            so.FindProperty("info").objectReferenceValue = info;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(go, path);
        }

        static ScriptableObject CloneMount(string src, string name, GameObject prefab, ScriptableObject info,
            string jsonKey, string mountName, float mass, float emptyMass, float drag)
        {
            var m = Clone(src, name);
            var so = new SerializedObject(m);
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("info").objectReferenceValue = info;
            so.FindProperty("jsonKey").stringValue = jsonKey;
            so.FindProperty("mountName").stringValue = mountName;
            so.FindProperty("mass").floatValue = mass;
            so.FindProperty("emptyMass").floatValue = emptyMass;
            so.FindProperty("drag").floatValue = drag;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        // ---------------- MiG pylons: inner / middle / outer per wing ----------------

        // MiG-frame pylon shoe positions, measured from the model (blender/pylon_pos.py)
        public static readonly Vector3 InnerPylon = new Vector3(2.36f, -0.27f, 1.31f);
        public static readonly Vector3 MiddlePylon = new Vector3(3.10f, -0.29f, 0.66f);
        public static readonly Vector3 OuterPylon = new Vector3(3.73f, -0.40f, 0.28f);

        // Returns the index of the new outer pylon set (loadouts need a slot there).
        public static int SetupPylons(GameObject go, Vector3 modelOffset, Result w)
        {
            Transform root = go.transform;
            Vector3 P(Vector3 v, float side) => new Vector3(side * v.x, v.y, v.z) + modelOffset;
            Child(root, "hardpoint_pylon_L1").position = P(InnerPylon, -1);
            Child(root, "hardpoint_pylon_R1").position = P(InnerPylon, 1);
            Child(root, "hardpoint_pylon_L2").position = P(MiddlePylon, -1);
            Child(root, "hardpoint_pylon_R2").position = P(MiddlePylon, 1);

            var so = new SerializedObject(go.GetComponentInChildren(T("WeaponManager"), true));
            var sets = so.FindProperty("hardpointSets");
            int inner = -1, middle = -1;
            for (int i = 0; i < sets.arraySize; i++)
            {
                var n = sets.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                if (n == "Inner Wing Pylons") inner = i;
                if (n == "Outer Wing Pylons") middle = i;
            }
            if (inner < 0 || middle < 0) throw new Exception("[MiG29] wing pylon hardpoint sets not found");

            void AddOptions(int i, params UnityEngine.Object[] mounts)
            {
                var opts = sets.GetArrayElementAtIndex(i).FindPropertyRelative("weaponOptions");
                int at = opts.arraySize > 0 ? 1 : 0; // after the "empty" entry
                foreach (var m in mounts.Reverse())
                {
                    opts.InsertArrayElementAtIndex(at);
                    opts.GetArrayElementAtIndex(at).objectReferenceValue = m;
                }
            }
            sets.GetArrayElementAtIndex(inner).FindPropertyRelative("name").stringValue = "Inner Pylons";
            AddOptions(inner, w.r27Mount, w.r27tMount, w.r73Mount, w.r60Mount, w.ptb1150Mount);
            sets.GetArrayElementAtIndex(middle).FindPropertyRelative("name").stringValue = "Middle Pylons";
            AddOptions(middle, w.r73Mount, w.r60Mount);

            // new outer pair (R-73 / R-60M), inserted right after the middle pylons
            int outer = middle + 1;
            sets.InsertArrayElementAtIndex(middle);
            var os = sets.GetArrayElementAtIndex(outer);
            os.FindPropertyRelative("name").stringValue = "Outer Pylons";
            var oo = os.FindPropertyRelative("weaponOptions");
            oo.arraySize = 3;
            oo.GetArrayElementAtIndex(0).objectReferenceValue = null;
            oo.GetArrayElementAtIndex(1).objectReferenceValue = w.r73Mount;
            oo.GetArrayElementAtIndex(2).objectReferenceValue = w.r60Mount;
            var hps = os.FindPropertyRelative("hardpoints");
            var upType = T("UnitPart");
            for (int i = 0; i < hps.arraySize && i < 2; i++)
            {
                var hp = hps.GetArrayElementAtIndex(i);
                var oldT = hp.FindPropertyRelative("transform").objectReferenceValue as Transform;
                bool left = oldT != null && oldT.position.x < 0;
                var wing = Child(root, left ? "wing2_L" : "wing2_R");
                var t = new GameObject(left ? "hardpoint_pylon_L3" : "hardpoint_pylon_R3").transform;
                t.SetParent(wing, false);
                t.SetPositionAndRotation(P(OuterPylon, left ? -1 : 1), oldT != null ? oldT.rotation : Quaternion.identity);
                hp.FindPropertyRelative("transform").objectReferenceValue = t;
                hp.FindPropertyRelative("part").objectReferenceValue = wing.GetComponent(upType);
                hp.FindPropertyRelative("pylonOptions").arraySize = 0;
                hp.FindPropertyRelative("Pylon").objectReferenceValue = null;
                hp.FindPropertyRelative("Plug").objectReferenceValue = null;
                hp.FindPropertyRelative("bayDoors").arraySize = 0;
                hp.FindPropertyRelative("HardpointIndex").intValue = left ? 8 : 0; // reuse the removed side bays' indices (PylonIndicator arrays stop at 8)
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[MiG29] pylons: inner {inner}, middle {middle}, outer {outer}; {sets.arraySize} hardpoint sets");
            return outer;
        }

        // ---------------- centreline station (PTB-1500) ----------------

        // MiG-frame belly between the engine nacelles: flat at y -0.44 from z 0.5 to 2.0 (blender/analysis/stores_pos.py)
        public static readonly Vector3 CentrelineStation = new Vector3(0f, -0.44f, 1.0f);

        // The KR-67's forward weapon bay becomes the MiG's centreline station: one hardpoint on the belly, no bay doors.
        // Returns the set's index (loadouts keep that slot).
        public static int SetupCentreline(GameObject go, Vector3 modelOffset, Result w)
        {
            var so = new SerializedObject(go.GetComponentInChildren(T("WeaponManager"), true));
            var sets = so.FindProperty("hardpointSets");
            int idx = -1;
            for (int i = 0; i < sets.arraySize; i++)
                if (sets.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == "Forward Weapon Bay") idx = i;
            if (idx < 0) throw new Exception("[MiG29] forward weapon bay set not found");
            var set = sets.GetArrayElementAtIndex(idx);
            set.FindPropertyRelative("name").stringValue = "Centreline";
            var opts = set.FindPropertyRelative("weaponOptions");
            opts.arraySize = 2;
            opts.GetArrayElementAtIndex(0).objectReferenceValue = null;
            opts.GetArrayElementAtIndex(1).objectReferenceValue = w.ptb1500Mount;
            var hps = set.FindPropertyRelative("hardpoints");
            hps.arraySize = 1;
            var hp = hps.GetArrayElementAtIndex(0);
            var t = (Transform)hp.FindPropertyRelative("transform").objectReferenceValue;
            var part = (Component)hp.FindPropertyRelative("part").objectReferenceValue;
            t.name = "hardpoint_centreline";
            t.SetParent(part.transform, true);
            t.SetPositionAndRotation(CentrelineStation + modelOffset, Quaternion.identity);
            t.localScale = Vector3.one;
            hp.FindPropertyRelative("bayDoors").arraySize = 0;
            hp.FindPropertyRelative("pylonOptions").arraySize = 0;
            hp.FindPropertyRelative("Pylon").objectReferenceValue = null;
            hp.FindPropertyRelative("Plug").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[MiG29] centreline station: set {idx} on part {part.name}");
            return idx;
        }

        // the forward bay's KR-67 weapons are not centreline options: empty that slot in every stock loadout
        public static void ClearLoadoutSlot(SerializedObject pso, int index)
        {
            void Clr(SerializedProperty weapons) { if (index < weapons.arraySize) weapons.GetArrayElementAtIndex(index).objectReferenceValue = null; }
            var loadouts = pso.FindProperty("loadouts");
            for (int i = 0; i < loadouts.arraySize; i++) Clr(loadouts.GetArrayElementAtIndex(i).FindPropertyRelative("weapons"));
            var std = pso.FindProperty("StandardLoadouts");
            for (int i = 0; i < std.arraySize; i++) Clr(std.GetArrayElementAtIndex(i).FindPropertyRelative("loadout.weapons"));
        }

        // Default loadout (loadouts[1]: the loadout screen's starting point and the fallback) and the standard loadouts AI-flown
        // MiGs spawn with. Hardpoint sets: 0 gun, 1 centreline, 2 inner, 3 middle, 4 outer, 5 tail hook.
        public static void SetLoadouts(SerializedObject pso, Result w)
        {
            UnityEngine.Object Stock(string n) => AssetDatabase.LoadAssetAtPath<ScriptableObject>(MB + n + "_PLACEHOLDER.asset");
            var airSup = new UnityEngine.Object[] { w.gunMount, null, w.r27Mount, w.r73Mount, w.r73Mount, null };
            var presets = new (string name, float fuel, UnityEngine.Object[] weapons)[]
            {
                ("Air Superiority (R-27R / R-73)", 0.8f, airSup),
                ("Long-Range CAP (PTB-1500 / R-27R / R-73)", 1.0f, new UnityEngine.Object[] { w.gunMount, w.ptb1500Mount, w.r27Mount, w.r73Mount, w.r73Mount, null }),
                ("Dogfight (R-27T / R-73 / R-60M)", 0.6f, new UnityEngine.Object[] { w.gunMount, null, w.r27tMount, w.r73Mount, w.r60Mount, null }),
                ("Strike (FAB-500 / rockets / R-73)", 0.7f, new UnityEngine.Object[] { w.gunMount, null, Stock("bomb_500_single"), Stock("Rocket2_4Pod"), w.r73Mount, null }),
                ("Ferry (3 drop tanks / R-73)", 1.0f, new UnityEngine.Object[] { w.gunMount, w.ptb1500Mount, w.ptb1150Mount, w.r73Mount, null, null }),
            };
            void Fill(SerializedProperty weapons, UnityEngine.Object[] src)
            {
                weapons.arraySize = src.Length;
                for (int i = 0; i < src.Length; i++) weapons.GetArrayElementAtIndex(i).objectReferenceValue = src[i];
            }
            Fill(pso.FindProperty("loadouts").GetArrayElementAtIndex(1).FindPropertyRelative("weapons"), airSup);
            var std = pso.FindProperty("StandardLoadouts");
            std.arraySize = presets.Length;
            for (int i = 0; i < presets.Length; i++)
            {
                var e = std.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("disabled").boolValue = false;
                e.FindPropertyRelative("Name").stringValue = presets[i].name;
                e.FindPropertyRelative("FuelRatio").floatValue = presets[i].fuel;
                Fill(e.FindPropertyRelative("loadout.weapons"), presets[i].weapons);
            }
            Debug.Log($"[MiG29] default loadout + {presets.Length} standard loadouts set");
        }

        // keep loadout.weapons aligned with hardpointSets after inserting the outer pylon set
        public static void InsertLoadoutSlot(SerializedObject pso, int index)
        {
            void Ins(SerializedProperty weapons)
            {
                if (index > weapons.arraySize) return;
                weapons.InsertArrayElementAtIndex(index);
                weapons.GetArrayElementAtIndex(index).objectReferenceValue = null;
            }
            var loadouts = pso.FindProperty("loadouts");
            for (int i = 0; i < loadouts.arraySize; i++) Ins(loadouts.GetArrayElementAtIndex(i).FindPropertyRelative("weapons"));
            var std = pso.FindProperty("StandardLoadouts");
            for (int i = 0; i < std.arraySize; i++) Ins(std.GetArrayElementAtIndex(i).FindPropertyRelative("loadout.weapons"));
        }
    }
}
