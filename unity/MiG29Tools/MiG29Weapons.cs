using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MiG29Tools
{
    // R-73 (AA-11 Archer) and R-27R (AA-10 Alamo-A) for the MiG-29.
    // Models: tools/missile_gen.py (procedural, from scratch) -> MiG29Source/missiles.json.
    // Missiles are cloned from stock prefabs for their effects/networking and re-tuned:
    //   R-73  <- AAM1 (MMR-S3, IR + thrust vectoring)
    //   R-27R <- AAM2 (AAM-29, two-stage motor) with its ARH seeker replaced by the SARH seeker of SAM_Radar1
    //            (SARHSeeker guides on missile.owner.radar, so the launching MiG must keep the target illuminated).
    public static class MiG29Weapons
    {
        const string ModDir = "Assets/Blueprinter/Mods/mig29";
        const string Dir = ModDir + "/weapons";
        const string GO = "Assets/Blueprinter/_donotship/GameObject/";
        const string MB = "Assets/Blueprinter/_donotship/MonoBehaviour/";

        [Serializable] class PartDump { public string name; public float[] vertices, normals, uvs; public int[] triangles; }
        [Serializable] class Dump { public PartDump[] parts; }

        public class Result { public ScriptableObject r73Mount, r27Mount; }

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
            var r73 = MakeMissile("AAM1", "mig29_R73", meshes["R73"], mat, length: 2.90f, radius: 0.085f, seekerSwapFrom: null);
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
            var r27 = MakeMissile("AAM2", "mig29_R27R", meshes["R27R"], mat, length: 4.08f, radius: 0.115f, seekerSwapFrom: "SAM_Radar1");
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
            Debug.Log("[MiG29] weapons built: R-73, R-27R");
            return new Result { r73Mount = r73Mount, r27Mount = r27Mount };
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

        static GameObject MakeMissile(string basePrefab, string name, Mesh mesh, Material mat, float length, float radius, string seekerSwapFrom)
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

            if (seekerSwapFrom != null)
            {
                foreach (var s in go.GetComponents(T("MissileSeeker"))) UnityEngine.Object.DestroyImmediate(s, true);
                var donor = AssetDatabase.LoadAssetAtPath<GameObject>(GO + seekerSwapFrom + "_PLACEHOLDER.prefab");
                ComponentUtility.CopyComponent(donor.GetComponent(T("SARHSeeker")));
                ComponentUtility.PasteComponentAsNew(go);
                // the copy still points at objects inside the SAM prefab (its radar etc.); those are runtime-assigned anyway
                var donorPath = AssetDatabase.GetAssetPath(donor);
                var sso = new SerializedObject(go.GetComponent(T("SARHSeeker")));
                var it = sso.GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null && AssetDatabase.GetAssetPath(it.objectReferenceValue) == donorPath)
                        it.objectReferenceValue = null;
                sso.ApplyModifiedPropertiesWithoutUndo();
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
            foreach (var bc in pylon.GetComponents<BoxCollider>()) { bc.center = launcher.bounds.center; bc.size = launcher.bounds.size; }

            var msl = Child(go.transform, missileChild);
            msl.localPosition = new Vector3(0, missileY, 0); msl.localRotation = Quaternion.identity; msl.localScale = Vector3.one;
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
            AddOptions(inner, w.r27Mount, w.r73Mount);
            sets.GetArrayElementAtIndex(middle).FindPropertyRelative("name").stringValue = "Middle Pylons";
            AddOptions(middle, w.r73Mount);

            // new outer pair (R-73 only), inserted right after the middle pylons
            int outer = middle + 1;
            sets.InsertArrayElementAtIndex(middle);
            var os = sets.GetArrayElementAtIndex(outer);
            os.FindPropertyRelative("name").stringValue = "Outer Pylons";
            var oo = os.FindPropertyRelative("weaponOptions");
            oo.arraySize = 2;
            oo.GetArrayElementAtIndex(0).objectReferenceValue = null;
            oo.GetArrayElementAtIndex(1).objectReferenceValue = w.r73Mount;
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
