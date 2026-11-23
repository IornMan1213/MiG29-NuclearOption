using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiG29Tools
{
    // The MiG-29's own damage parts. The prefab starts as the KR-67 Ifrit, and the MiG used to keep the Ifrit's 34 parts: their
    // boxes decided where the MiG's skin broke, so a cockpit came off as a 0.8 m ring behind the radome, a 586 kg "wing root" held a
    // sliver of skin, leading-edge strips fell away on their own and the underside broke in islands (user, 2026-11-11: "the
    // breaklines dont make sense ... make it from scratch not using other planes").
    //
    // Here the MiG gets its own structure: parts on its production joints (radome seam, the frame behind the canopy, the wing
    // frame, the nacelle frame, the root rib, the flap/aileron rib, the tip rib, the tail-boom and fin roots), its own load paths
    // and break strengths, masses from the MiG's own weights, and the flight model's lift moved onto them unchanged
    // (fm_config.json gives each surface's lift point in the aircraft frame). The game's systems on the old parts (engines, gear,
    // radar, pylons, fuel tanks, lights) stay on the part they are on, and the objects themselves are reused: every serialized
    // reference to them stays valid. Each break line is a plane, so the skin is cut exactly along it (MiG29Cut).
    //
    // All positions are the prefab frame (MiG model + MiG29Builder.ModelOffset), metres.
    static class MiG29Parts
    {
        // stations
        const float Radome = 5.565f;     // radome seam ring
        const float Canopy = 2.6f;       // the frame behind the canopy; also the top of the raked intake lips
        const float WingFrame = 0.3f;    // where the LERX meets the wing; the intakes end here
        const float NacelleFrame = -1.9f;
        const float AftFrame = -4.0f;    // aft spine / tail cone
        const float NozzleFrame = -4.86f;
        const float FinRoot = 0.45f;     // y: fin above, boom below
        const float LerxFloor = -0.53f;  // y: between the LERX underside and the intake roof
        const float WingFloor = -0.62f;  // y: under the wing's lower skin
        const float WingTop = -0.13f;    // y: over the wing's upper skin, under the tail-boom tops beside the root
        // butt lines (|x|)
        const float BellyHalf = 0.3f;    // the belly between the intakes
        const float BodyHalf = 0.5f;     // the fuselage sides where the LERX roots are
        const float BoomInner = 1.45f;
        const float RootRib = 1.75f;     // outer wing console joint
        const float FlapAileron = 3.53f; // the rib between flap and aileron
        const float TipRib = 4.8f;
        const float ConsoleOuter = 2.2f; // outboard of this the console reaches back to its trailing edge
        // trailing edge: z = TeRoot + TeSlope (|x| - TeX0)
        const float TeRoot = -3.92f, TeX0 = 1.74f, TeSlope = -0.23f / (4.8f - 1.74f);
        // nozzles: a 12-sided ring of radius NozzleR around each nozzle axis (|x|, y)
        const float NozzleX = 0.75f, NozzleY = -0.68f, NozzleR = 0.66f;
        const int NozzleSides = 12;

        static bool InNozzle(float ax, float y)
        {
            for (int i = 0; i < NozzleSides; i++)
            {
                float a = i * Mathf.PI * 2f / NozzleSides;
                if ((ax - NozzleX) * Mathf.Cos(a) + (y - NozzleY) * Mathf.Sin(a) >= NozzleR) return false;
            }
            return true;
        }

        internal static float Te(float ax) => TeRoot + TeSlope * (ax - TeX0);

        /// <summary>The part a point of the skin belongs to.</summary>
        public static string Label(Vector3 p)
        {
            float ax = Mathf.Abs(p.x), y = p.y, z = p.z;
            string S(string n) => n + (p.x < 0 ? "_L" : "_R");
            if (ax > 1.2f && ax < 2.4f && y > FinRoot && z < -2.5f) return S("fin");
            if (z > WingFrame)
            {
                if (z > Radome) return "radome";
                // ahead of the frame behind the canopy the LERX's forward corners (the gun bay on the left, equipment on the
                // right) are built into the nose section, so they go with the cockpit, not as a block of their own (user, 2026-11-12)
                if (z > Canopy) return "cockpit_section";
                if (y <= LerxFloor && ax >= BellyHalf) return S("intake");
                if (ax < BodyHalf) return "forward_fuselage";
                return S("lerx");
            }
            if (z <= NozzleFrame && InNozzle(ax, y)) return S("nozzle");
            if (z <= AftFrame && ax < BodyHalf) return "tail_cone";
            if (ax >= RootRib && (z > Te(ax) || ax > ConsoleOuter) && y > WingFloor && y < WingTop)
                return S(ax < FlapAileron ? "wing_inner" : ax < TipRib ? "wing_outer" : "wingtip");
            if (z <= NacelleFrame)
            {
                if (ax < BodyHalf) return "aft_spine";
                return S(ax < BoomInner ? "nacelle" : "boom");
            }
            return ax < BodyHalf ? "MiG29" : S("centre_wing");
        }

        /// <summary>
        /// The part a point of the skin belongs to, with torn edges: the point is moved by a jagged noise field (a few centimetres,
        /// in coarse and fine waves) before it is labelled, so every break follows its joint but wanders and tears along it the way
        /// ripped metal does, where a clean plane read as boxy (user, 2026-11-11). The same point always gets the same part.
        /// </summary>
        public static string TornLabel(Vector3 p, Vector3 n)
        {
            // along the skin only: a boundary that runs along a surface (the LERX root on the fuselage side, the wing's height
            // limits) stays put, where moving across it scattered islands of one part over the other
            var w = Tear(p);
            if (n.sqrMagnitude > 1e-6f) { n.Normalize(); w -= n * Vector3.Dot(w, n); }
            return Label(p + w);
        }

        /// <summary>Whether a joint is within reach (the labels round the point differ): where a torn edge can run.</summary>
        public static bool NearSeam(Vector3 p)
        {
            const float R = 0.22f;   // the tear reaches 0.21 m
            var l = Label(p);
            return Label(p + Vector3.right * R) != l || Label(p - Vector3.right * R) != l || Label(p + Vector3.up * R) != l
                || Label(p - Vector3.up * R) != l || Label(p + Vector3.forward * R) != l || Label(p - Vector3.forward * R) != l;
        }

        /// <summary>A jagged offset for ragged patch outlines (the shards along the breaks).</summary>
        public static Vector3 Jag(Vector3 p) => Tear(p * 1.7f) * 0.6f;

        static Vector3 Tear(Vector3 p)
        {
            // value noise on two lattices; the fine one is folded (1 - |n|) into sharp ridges, the teeth of a tear (the user's pick
            // of two previews: about 15 cm waves)
            Vector3 coarse = new Vector3(Noise(p * 4f, 1), Noise(p * 4f, 2), Noise(p * 4f, 3)) * 0.15f;
            Vector3 fine = new Vector3(Ridge(p * 14f, 4), Ridge(p * 14f, 5), Ridge(p * 14f, 6)) * 0.06f;
            return coarse + fine;
        }
        static float Ridge(Vector3 p, int seed) => 1f - 2f * Mathf.Abs(Noise(p, seed));
        static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ seed * 2654435761u);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffffff) / (float)0xffffff * 2f - 1f;
            }
        }
        static float Noise(Vector3 p, int seed)   // trilinear value noise, -1..1
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            float L(float a, float b, float t) => a + (b - a) * t;
            float c00 = L(Hash(x, y, z, seed), Hash(x + 1, y, z, seed), fx), c10 = L(Hash(x, y + 1, z, seed), Hash(x + 1, y + 1, z, seed), fx);
            float c01 = L(Hash(x, y, z + 1, seed), Hash(x + 1, y, z + 1, seed), fx), c11 = L(Hash(x, y + 1, z + 1, seed), Hash(x + 1, y + 1, z + 1, seed), fx);
            return L(L(c00, c10, fy), L(c01, c11, fy), fz);
        }

        /// <summary>Every plane Label's regions are bounded by (the cuts MiG29Cut makes).</summary>
        public static List<MiG29Cut.Plane> Planes()
        {
            var pl = new List<MiG29Cut.Plane>();
            foreach (var z in new[] { Radome, Canopy, WingFrame, NacelleFrame, AftFrame, NozzleFrame, -2.5f }) pl.Add(new MiG29Cut.Plane(2, z));
            foreach (var y in new[] { FinRoot, LerxFloor, WingFloor, WingTop }) pl.Add(new MiG29Cut.Plane(1, y));
            foreach (var s in new[] { -1f, 1f })
            {
                foreach (var x in new[] { BellyHalf, BodyHalf, BoomInner, RootRib, FlapAileron, TipRib, ConsoleOuter, 1.2f, 2.4f }) pl.Add(new MiG29Cut.Plane(0, s * x));
                // z - TeSlope (s x - TeX0) = TeRoot
                pl.Add(new MiG29Cut.Plane(new Vector3(-TeSlope * s, 0f, 1f), TeRoot - TeSlope * TeX0));
                for (int i = 0; i < NozzleSides; i++)
                {
                    float a = i * Mathf.PI * 2f / NozzleSides;
                    var n = new Vector3(s * Mathf.Cos(a), Mathf.Sin(a), 0f);   // (|x| - X) cos + (y - Y) sin = R on this side
                    pl.Add(new MiG29Cut.Plane(n, NozzleR + NozzleX * Mathf.Cos(a) + NozzleY * Mathf.Sin(a)));
                }
            }
            return pl;
        }

        // ---- the parts ------------------------------------------------------------------------------------------------------

        class Joint { public string to; public float force, torque; public Joint(string to, float f, float t) { this.to = to; force = f; torque = t; } }
        class Def
        {
            public string name, from;          // from: the object it is built on (keeps the systems on it), null = new
            public float mass, fuel;           // mass: MiG-29 9.12 empty weight share, kg; fuel: share of the internal fuel
            public Vector3 pivot;              // centre of its skin (prefab frame); null pivot (NaN) = leave
            public Joint[] joints;             // the first is the one it hangs from
        }

        static Def D(string name, string from, float mass, float fuel, Vector3 pivot, params Joint[] joints) =>
            new Def { name = name, from = from, mass = mass, fuel = fuel, pivot = pivot, joints = joints };
        static Joint J(string to, float kN, float kNm = -1f) => new Joint(to, kN * 1000f, (kNm < 0 ? kN : kNm) * 1000f);
        static readonly Vector3 Keep = new Vector3(float.NaN, 0f, 0f);

        // Masses: a MiG-29 9.12 empty-weight breakdown (about 11 t; RD-33 1,055 kg each, the N019 radar in the radome, the main gear
        // in the centre wing), scaled to the aircraft's empty mass. Fuel: the fuselage tanks 1-3 (forward fuselage, centre, aft
        // spine) and the wing tanks (centre wing and inner consoles). Joint strengths in kN: the fuselage frames and wing root the
        // strongest, tips, radome and tail cone the weakest.
        static Dictionary<string, string[]> holds;
        /// <summary>The parts each part is fastened to by its own joints (they stay when it breaks off).</summary>
        public static string[] HeldBy(string part)
        {
            holds ??= Table().ToDictionary(d => d.name, d => (d.joints ?? new Joint[0]).Select(j => j.to).ToArray());
            return holds.TryGetValue(part, out var h) ? h : new string[0];
        }

        static List<Def> Table()
        {
            var t = new List<Def>
            {
                D("MiG29", "MiG29", 900, 0.30f, Keep),
                D("forward_fuselage", "fuselage_F", 700, 0.22f, new Vector3(0f, -0.027f, 1.525f), J("MiG29", 200)),
                D("cockpit_section", "cockpit", 1000, 0, new Vector3(0f, -0.187f, 4.064f), J("forward_fuselage", 200)),
                D("radome", "nose", 250, 0, new Vector3(0f, -0.39f, 6.653f), J("cockpit_section", 40)),
                D("aft_spine", "fuselage_R", 500, 0.16f, new Vector3(0f, -0.598f, -2.957f), J("MiG29", 200)),
                D("tail_cone", "tail", 250, 0, new Vector3(0f, -0.539f, -4.645f), J("aft_spine", 50)),
            };
            foreach (var s in new[] { "L", "R" })
            {
                float sx = s == "L" ? -1f : 1f;
                Vector3 P(float x, float y, float z) => new Vector3(sx * x, y, z);
                t.AddRange(new[]
                {
                    D("lerx_" + s, "wingRoot_" + s, 275, 0, P(0.92f, -0.25f, 2.19f), J("forward_fuselage", 150), J("centre_wing_" + s, 150), J("intake_" + s, 100)),
                    D("intake_" + s, "intake_" + s, 300, 0, P(0.69f, -0.906f, 1.3f), J("forward_fuselage", 60), J("centre_wing_" + s, 100)),
                    D("centre_wing_" + s, "gearbay_" + s, 650, 0.08f, P(1.05f, -0.621f, -0.768f), J("MiG29", 200)),
                    D("wing_inner_" + s, "wing1_" + s, 300, 0.08f, P(2.525f, -0.32f, -1.717f), J("centre_wing_" + s, 200)),
                    D("wing_outer_" + s, "wing2_" + s, 130, 0, P(4.102f, -0.423f, -2.593f), J("wing_inner_" + s, 100)),
                    D("wingtip_" + s, "wingtip_" + s, 45, 0, P(5.143f, -0.473f, -3.453f), J("wing_outer_" + s, 50)),
                    D("flap_" + s, "flap_" + s, 40, 0, Keep, J("wing_inner_" + s, 100)),
                    D("aileron_" + s, "aileron_" + s, 30, 0, Keep, J("wing_outer_" + s, 100)),
                    D("nacelle_" + s, "engine_" + s, 1350, 0, P(1.029f, -0.679f, -3.565f), J("centre_wing_" + s, 200), J("aft_spine", 200)),
                    D("nozzle_" + s, "nozzle_" + s, 120, 0, P(0.768f, -0.547f, -5.25f), J("nacelle_" + s, 80, 400), J("tail_cone", 80)),
                    D("boom_" + s, "tail_" + s, 250, 0, P(1.672f, -0.154f, -4.438f), J("nacelle_" + s, 120), J("centre_wing_" + s, 120)),
                    D("fin_" + s, null, 120, 0, P(1.838f, 1.221f, -5.471f), J("boom_" + s, 100)),
                    D("stabilator_" + s, "elevator_" + s, 120, 0, Keep, J("boom_" + s, 100)),
                });
            }
            return t;
        }

        // A missile's blast reaches each part along one line from the burst to the part's centre, and any other part on that line
        // shields it. The MiG's booms, fins, LERX, intakes, wing panels and radome stand out to the side with nothing in front of
        // them, so a side burst took them all where the KR-67's wingtip shields its own (blast tests, 2026-11-14: 11 parts off at
        // 7 m against the KR-67's 5). Their blast tolerance (the KR-67 part each is built from) is raised by half. The plugin also aims
        // those lines deeper into each part (MiG29Instruments/BlastAim.cs); with both, a 7 m side burst takes 7 parts.
        static readonly string[] BlastHardened = { "boom_", "fin_", "lerx_", "intake_", "wing_inner_", "wing_outer_", "centre_wing_", "radome" };
        const float BlastToughness = 1.5f;

        // the KR-67's leading-edge strips: the MiG's leading edge is part of the wing panel it is on
        static readonly string[] Dropped = { "wing1_FL", "wing1_FR", "wing2_FL", "wing2_FR" };

        /// <summary>The names of the parts that carry airframe skin (all but the control surfaces).</summary>
        public static readonly HashSet<string> Skinned = new HashSet<string>(Table().Where(d => !d.name.StartsWith("flap") && !d.name.StartsWith("aileron") && !d.name.StartsWith("stabilator")).Select(d => d.name));

        static Type T(string n) => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp").Select(a => a.GetType(n)).First(t => t != null);
        static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name && t.GetComponent(T("UnitPart")) != null)
            ?? throw new Exception($"[MiG29] part {name} not found");

        /// <summary>Turns the KR-67's parts into the MiG's. Runs before the skin is split onto them.</summary>
        public static void Restructure(Transform root, float dryMass, float fuelScale, int solverIterations)
        {
            var upType = T("UnitPart"); var tankType = T("FuelTank");
            var defs = Table();
            // fuel: the KR-67's total, scaled to the MiG's, shared out below
            float fuelTotal = root.GetComponentsInChildren(tankType, true).Sum(c => new SerializedObject(c).FindProperty("fuelCapacity").floatValue) * fuelScale;

            // 1. the leading-edge strips go (nothing hangs on them but their own hidden KR-67 meshes)
            foreach (var n in Dropped)
            {
                var d = Find(root, n);
                foreach (var c in d.Cast<Transform>().ToList()) if (c.GetComponent(upType) != null) c.SetParent(d.parent, true);
                UnityEngine.Object.DestroyImmediate(d.gameObject);
            }
            // the inner wings' HighLiftDevice moved those strips as slats: its entries for them now point at nothing, and the game
            // animated a missing transform every physics step (two NullReferenceExceptions a step per wing). Only those entries go.
            foreach (var hld in root.GetComponentsInChildren(T("HighLiftDevice"), true))
            {
                var hso = new SerializedObject(hld); var mps = hso.FindProperty("movingParts");
                if (mps == null) continue;
                int pruned = 0;
                for (int i = mps.arraySize - 1; i >= 0; i--)
                    if (mps.GetArrayElementAtIndex(i).FindPropertyRelative("transform")?.objectReferenceValue == null) { mps.DeleteArrayElementAtIndex(i); pruned++; }
                if (pruned > 0) { hso.ApplyModifiedPropertiesWithoutUndo(); Debug.Log($"[MiG29] {hld.name}: {pruned} slat entries removed with the leading-edge strips"); }
            }
            // 2. rename, and the fins as parts of their own (their rudders and lift move onto them; the booms carry the stabilators)
            var objs = new Dictionary<string, Transform>();
            foreach (var d in defs.Where(d => d.from != null)) { var tr = Find(root, d.from); tr.name = d.name; objs[d.name] = tr; }
            foreach (var d in defs.Where(d => d.from == null))
            {
                var boom = objs[d.joints[0].to];
                var go = new GameObject(d.name) { layer = boom.gameObject.layer, tag = boom.gameObject.tag };
                go.transform.SetParent(boom, false);
                var part = go.AddComponent(upType.Assembly.GetType("AeroPart"));
                EditorUtility.CopySerialized(boom.GetComponent(upType), part);   // the game's part settings (armour, sounds, damage shading)
                var so = new SerializedObject(part);
                so.FindProperty("damageMaterial.renderers").arraySize = 0;
                so.FindProperty("damageMaterial.indices").arraySize = 0;
                so.FindProperty("disintegrationEffects").arraySize = 0;
                so.FindProperty("disintegrateObjects").arraySize = 0;
                so.FindProperty("centerOfMass").objectReferenceValue = null;
                // the fin's sideways lift: the boom's lift normal (turned to the fin's plane) is the fin's now
                var bso = new SerializedObject(boom.GetComponent(upType));
                if (bso.FindProperty("liftNormal").objectReferenceValue is Transform ln && ln != boom) ln.SetParent(go.transform, true);
                else so.FindProperty("liftNormal").objectReferenceValue = null;
                bso.FindProperty("liftNormal").objectReferenceValue = null; bso.FindProperty("centerOfLift").vector3Value = Vector3.zero;
                bso.ApplyModifiedPropertiesWithoutUndo();
                so.FindProperty("wingArea").floatValue = 0f; so.FindProperty("dragArea").floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();
                var bc = boom.GetComponent<MeshCollider>();
                var mc = go.AddComponent<MeshCollider>(); mc.convex = true; mc.sharedMaterial = bc.sharedMaterial;
                objs[d.name] = go.transform;
            }
            // 3. hierarchy along the load paths, pivots at the centre of each part's skin (children keep their place)
            foreach (var d in defs.Where(d => d.joints.Length > 0)) objs[d.name].SetParent(objs[d.joints[0].to], true);
            foreach (var d in defs.Where(d => !float.IsNaN(d.pivot.x)))
            {
                var tr = objs[d.name];
                var kids = tr.Cast<Transform>().ToList();
                foreach (var k in kids) k.SetParent(null, true);
                tr.position = d.pivot;
                foreach (var k in kids) k.SetParent(tr, true);
            }
            // 4. joints, masses, fuel
            float massSum = defs.Sum(d => d.mass);
            var tanks = new List<Component>();
            foreach (var d in defs)
            {
                var part = objs[d.name].GetComponent(upType);
                var so = new SerializedObject(part);
                var js = so.FindProperty("joints");
                var sound = js.arraySize > 0 ? js.GetArrayElementAtIndex(0).FindPropertyRelative("breakSound").objectReferenceValue : null;
                js.arraySize = d.joints.Length;
                for (int i = 0; i < d.joints.Length; i++)
                {
                    var j = js.GetArrayElementAtIndex(i); var to = objs[d.joints[i].to];
                    var anchor = new GameObject($"{d.name}_joint_{d.joints[i].to}").transform;
                    anchor.SetParent(objs[d.name], false);
                    anchor.position = (objs[d.name].position + to.position) / 2f;   // moved to the seam once the skin is cut (Anchors)
                    j.FindPropertyRelative("connectedPart").objectReferenceValue = to.GetComponent(upType);
                    j.FindPropertyRelative("tensor").objectReferenceValue = null;
                    j.FindPropertyRelative("solverIterations").intValue = solverIterations;
                    j.FindPropertyRelative("breakForce").floatValue = d.joints[i].force;
                    j.FindPropertyRelative("breakTorque").floatValue = d.joints[i].torque;
                    j.FindPropertyRelative("anchor").objectReferenceValue = anchor;
                    if (j.FindPropertyRelative("breakSound").objectReferenceValue == null) j.FindPropertyRelative("breakSound").objectReferenceValue = sound ?? FindSound(root);
                }
                so.FindProperty("mass").floatValue = d.mass * dryMass / massSum;
                if (BlastHardened.Any(d.name.StartsWith))
                {
                    var tol = so.FindProperty("armorProperties.blastTolerance");
                    tol.floatValue *= BlastToughness;
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                var tank = objs[d.name].GetComponent(tankType);
                if (d.fuel > 0f)
                {
                    if (tank == null) throw new Exception($"[MiG29] {d.name}: no fuel tank to fill");
                    var ts = new SerializedObject(tank);
                    ts.FindProperty("fuelCapacity").floatValue = fuelTotal * d.fuel;
                    ts.ApplyModifiedPropertiesWithoutUndo();
                    tanks.Add(tank);
                }
                else if (tank != null) UnityEngine.Object.DestroyImmediate(tank);   // the KR-67 had tanks in its wing roots and outer wings
            }
            // the tanks that remain feed each other as before, minus the ones that went
            foreach (var tank in tanks)
            {
                var ts = new SerializedObject(tank); var ct = ts.FindProperty("connectedTanks");
                for (int i = ct.arraySize - 1; i >= 0; i--)
                    if (ct.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    {
                        ct.GetArrayElementAtIndex(i).objectReferenceValue = null; ct.DeleteArrayElementAtIndex(i);
                    }
                ts.ApplyModifiedPropertiesWithoutUndo();
            }
            // every part bursts into debris when it breaks (the game's own disintegration effect; the KR-67 had it on 14 of its 34)
            UnityEngine.Object debris = null;
            foreach (var d in defs)
            {
                var de = new SerializedObject(objs[d.name].GetComponent(upType)).FindProperty("disintegrationEffects");
                if (de.arraySize > 0 && de.GetArrayElementAtIndex(0).objectReferenceValue != null) { debris = de.GetArrayElementAtIndex(0).objectReferenceValue; break; }
            }
            int withDebris = 0;
            if (debris != null)
                foreach (var d in defs)
                {
                    var so = new SerializedObject(objs[d.name].GetComponent(upType)); var de = so.FindProperty("disintegrationEffects");
                    if (de.arraySize == 0) { de.arraySize = 1; de.GetArrayElementAtIndex(0).objectReferenceValue = debris; so.ApplyModifiedPropertiesWithoutUndo(); withDebris++; }
                }
            Debug.Log($"[MiG29] parts: debris burst added to {withDebris} more parts");
            Debug.Log($"[MiG29] parts: {defs.Count} MiG-29 parts ({string.Join(", ", defs.Select(d => d.name))}); "
                + $"fuel {fuelTotal:F0} kg in {tanks.Count} tanks; KR-67 leading-edge parts removed");
        }

        static UnityEngine.Object FindSound(Transform root)
        {
            foreach (var c in root.GetComponentsInChildren(T("UnitPart"), true))
            {
                var js = new SerializedObject(c).FindProperty("joints");
                for (int i = 0; i < js.arraySize; i++) { var s = js.GetArrayElementAtIndex(i).FindPropertyRelative("breakSound").objectReferenceValue; if (s != null) return s; }
            }
            return null;
        }

        /// <summary>
        /// A real airframe is fastened along every seam, not only along its main load path: every two parts that share a cut get
        /// a joint (on the part further down the load path, at the weaker of the two parts' own strengths). Its tensor is the
        /// other part, so the joint lets go the moment that part breaks off (AeroPart.Part_OnTensorBreak), and damage to the part
        /// it is on weakens it like the rest of its joints: neither side can hold a broken part on. Joined parts do not collide,
        /// so their hitboxes can meet (MiG29Hitbox.Fit). Returns every joined pair.
        /// </summary>
        public static HashSet<(string, string)> Fasten(IList<Component> parts, Dictionary<int, HashSet<((int, int, int), (int, int, int))>> cuts, Dictionary<int, MiG29Cut.Bucket> buckets, Transform root, int solverIterations)
        {
            var upType = T("UnitPart");
            var all = root.GetComponentsInChildren(upType, true).ToList();
            var pairs = new HashSet<(string, string)>();
            int Depth(Component c) { int d = 0; for (var t = c.transform; t != root && t != null; t = t.parent) d++; return d; }
            float Strength(Component c, out float torque)
            {
                var js = new SerializedObject(c).FindProperty("joints"); torque = float.MaxValue;
                if (js.arraySize == 0) return float.MaxValue;
                torque = js.GetArrayElementAtIndex(0).FindPropertyRelative("breakTorque").floatValue;
                return js.GetArrayElementAtIndex(0).FindPropertyRelative("breakForce").floatValue;
            }
            foreach (var c in all)
            {
                var js = new SerializedObject(c).FindProperty("joints");
                for (int i = 0; i < js.arraySize; i++)
                    if (js.GetArrayElementAtIndex(i).FindPropertyRelative("connectedPart").objectReferenceValue is Component o) { pairs.Add((c.name, o.name)); pairs.Add((o.name, c.name)); }
            }
            int added = 0;
            var keys = cuts.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
                for (int j = i + 1; j < keys.Count; j++)
                {
                    Component a = parts[keys[i]], b = parts[keys[j]];
                    if (pairs.Contains((a.name, b.name))) continue;
                    // a seam, a corner, or skins whose boxes overlap: parts that close together would otherwise need a gap between their
                    // hitboxes, and the plane that makes it ran the length of both (rounds went through the jet along the spine's sides,
                    // seam probe, 2026-11-11)
                    var shared = cuts[keys[i]].Where(cuts[keys[j]].Contains).ToList();
                    if (shared.Count == 0 && !Overlap(buckets[keys[i]], buckets[keys[j]])) continue;
                    var on = Depth(a) >= Depth(b) ? a : b; var to = on == a ? b : a;
                    float f = Mathf.Min(Strength(a, out float ta), Strength(b, out float tb)), tq = Mathf.Min(ta, tb);
                    var so = new SerializedObject(on); var js = so.FindProperty("joints");
                    var sound = js.arraySize > 0 ? js.GetArrayElementAtIndex(0).FindPropertyRelative("breakSound").objectReferenceValue : null;
                    js.arraySize++;
                    var jj = js.GetArrayElementAtIndex(js.arraySize - 1);
                    Vector3 V((int, int, int) k) => new Vector3(k.Item1, k.Item2, k.Item3) * 1e-4f;
                    var anchor = new GameObject($"{on.name}_joint_{to.name}").transform;
                    anchor.SetParent(on.transform, false);
                    anchor.position = shared.Count > 0 ? shared.Aggregate(Vector3.zero, (s, e) => s + (V(e.Item1) + V(e.Item2)) / 2f) / shared.Count
                        : (on.transform.position + to.transform.position) / 2f;
                    jj.FindPropertyRelative("connectedPart").objectReferenceValue = to;
                    jj.FindPropertyRelative("tensor").objectReferenceValue = to;
                    jj.FindPropertyRelative("solverIterations").intValue = solverIterations;
                    jj.FindPropertyRelative("breakForce").floatValue = f;
                    jj.FindPropertyRelative("breakTorque").floatValue = tq;
                    jj.FindPropertyRelative("anchor").objectReferenceValue = anchor;
                    jj.FindPropertyRelative("breakSound").objectReferenceValue = sound;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    pairs.Add((a.name, b.name)); pairs.Add((b.name, a.name));
                    added++;
                }
            Debug.Log($"[MiG29] parts: {added} seam joints between touching parts");
            return pairs;
        }

        // the two skins' boxes touch or come within the hitbox gap of each other every way (parts meeting at a corner)
        static bool Overlap(MiG29Cut.Bucket a, MiG29Cut.Bucket b)
        {
            Bounds Box(MiG29Cut.Bucket k) { var bb = new Bounds(k.v[0], Vector3.zero); foreach (var v in k.v) bb.Encapsulate(v); return bb; }
            Bounds A = Box(a), B = Box(b);
            var lo = Vector3.Max(A.min, B.min); var hi = Vector3.Min(A.max, B.max);
            return hi.x - lo.x > -MiG29Hitbox.Gap && hi.y - lo.y > -MiG29Hitbox.Gap && hi.z - lo.z > -MiG29Hitbox.Gap;
        }

        /// <summary>Each joint's anchor at the middle of the seam between its two parts (the shared cut edges), where there is one.</summary>
        public static void Anchors(IList<Component> parts, Dictionary<int, MiG29Cut.Bucket> buckets, Dictionary<int, HashSet<((int, int, int), (int, int, int))>> cuts)
        {
            int moved = 0;
            var index = parts.Select((p, i) => (p, i)).ToDictionary(x => x.p.name, x => x.i);
            foreach (var part in parts)
            {
                var so = new SerializedObject(part); var js = so.FindProperty("joints");
                for (int i = 0; i < js.arraySize; i++)
                {
                    var to = js.GetArrayElementAtIndex(i).FindPropertyRelative("connectedPart").objectReferenceValue as Component;
                    var anchor = js.GetArrayElementAtIndex(i).FindPropertyRelative("anchor").objectReferenceValue as Transform;
                    if (to == null || anchor == null || !index.TryGetValue(part.name, out int a) || !index.TryGetValue(to.name, out int b)) continue;
                    if (!cuts.TryGetValue(a, out var ca) || !cuts.TryGetValue(b, out var cb)) continue;
                    var shared = ca.Where(cb.Contains).ToList();
                    if (shared.Count == 0) continue;
                    Vector3 V((int, int, int) k) => new Vector3(k.Item1, k.Item2, k.Item3) * 1e-4f;
                    anchor.position = shared.Aggregate(Vector3.zero, (s, e) => s + (V(e.Item1) + V(e.Item2)) / 2f) / shared.Count;
                    moved++;
                }
            }
            Debug.Log($"[MiG29] parts: {moved} joint anchors on their seams");
        }

        /// <summary>
        /// Holds the empty aircraft's centre of gravity along the jet where the flight model was tuned (cgZ): every part's mass times
        /// 1 + a + b z, total unchanged. Its height is the MiG's own, from its parts' weights and hitboxes, and is returned (the
        /// thrust line goes there). Runs once the hitboxes (and so each part's centre of mass) are final.
        /// </summary>
        public const float TunedCgY = -0.0026f;   // the empty aircraft's centre of gravity height the flight model was tuned with
        public static float Balance(Transform root, float cgZ)
        {
            var parts = root.GetComponentsInChildren(T("UnitPart"), true).Select(p => (part: p, so: new SerializedObject(p))).Where(x => x.so.FindProperty("mass").floatValue > 0f).ToList();
            double m0 = 0, mz = 0, my = 0, mzz = 0, myy = 0, mzy = 0;
            var at = new List<Vector3>();
            foreach (var (part, so) in parts)
            {
                var com = MiG29Hitbox.Com(part); at.Add(com);
                double m = so.FindProperty("mass").floatValue;
                m0 += m; mz += m * com.z; my += m * com.y; mzz += m * com.z * com.z; myy += m * com.y * com.y; mzy += m * com.z * com.y;
            }
            double cz = mz / m0, cy = my / m0;
            // sum m f = m0, sum m f z = m0 zT: f = 1 + a + b z
            double det = m0 * mzz - mz * mz, r1 = m0 * cgZ - mz;
            double a = -mz * r1 / det, b = m0 * r1 / det;
            double lo = double.MaxValue, hi = double.MinValue, my2 = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                double f = 1 + a + b * at[i].z;
                my2 += parts[i].so.FindProperty("mass").floatValue * f * at[i].y;
                lo = Math.Min(lo, f); hi = Math.Max(hi, f);
                var mass = parts[i].so.FindProperty("mass");
                mass.floatValue = (float)(mass.floatValue * f);
                parts[i].so.ApplyModifiedPropertiesWithoutUndo();
            }
            float cgY = (float)(my2 / m0);
            // The flight model was tuned with the centre of gravity at TunedCgY and gives its lift points for that. The MiG's own
            // weights put it lower; moved with it, every lift point keeps its height over the centre of gravity, so the forces and
            // moments are the ones it was tuned with (left where they were, they stood 0.43 m higher over it: the sustained turn fell
            // and the roll bled off sooner, user flight 2026-11-11).
            foreach (var (part, so) in parts)
            {
                var cl = so.FindProperty("centerOfLift");
                if (cl == null || cl.vector3Value == Vector3.zero) continue;
                var ln = so.FindProperty("liftNormal").objectReferenceValue as Transform ?? part.transform;
                cl.vector3Value += Quaternion.Inverse(ln.rotation) * new Vector3(0f, cgY - TunedCgY, 0f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Debug.Log($"[MiG29] parts: {m0:F0} kg empty, centre of gravity z {cz:F3} y {cy:F3} from the MiG weights, held at z {cgZ:F3} (y {cgY:F3}); masses x {lo:F2}..{hi:F2}");
            if (lo < 0.5 || hi > 2.0) throw new Exception($"[MiG29] part mass balance out of range: factors {lo:F2}..{hi:F2}");
            return cgY;
        }

        /// <summary>
        /// Pylons, wing-tip rails, nav lights and flare/chaff points on the part they hang from, the one with the nearest hitbox
        /// (they were on the KR-67's, by its boundaries). Pylon transforms move with them, so a pylon leaves with its wing panel.
        /// </summary>
        public static void Reassign(Transform root)
        {
            var upType = T("UnitPart");
            // the part whose hitbox is nearest: pylons and rails hang below the skin, outside every part's region
            var hulls = root.GetComponentsInChildren(upType, true).Select(p => (part: p, cols: p.GetComponents<MeshCollider>().Where(c => c.sharedMesh != null && c.convex).ToArray()))
                .Where(x => x.cols.Length > 0).ToList();
            Physics.SyncTransforms();
            Component PartAt(Vector3 p) => hulls.OrderBy(h => h.cols.Min(c => (c.ClosestPoint(p) - p).sqrMagnitude)).Select(h => h.part).FirstOrDefault();
            int n = 0;
            void Fix(SerializedProperty el, string tfField, bool reparent)
            {
                var o = el.FindPropertyRelative(tfField)?.objectReferenceValue;
                var tf = o as Transform ?? (o as Component)?.transform;
                if (tf == null) return;
                var part = PartAt(tf.position);
                var pp = el.FindPropertyRelative("part");
                if (part == null || pp == null || pp.objectReferenceValue == null || pp.objectReferenceValue == part) return;
                pp.objectReferenceValue = part;
                if (reparent) tf.SetParent(part.transform, true);
                n++;
            }
            foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (c == null) continue;
                var so = new SerializedObject(c);
                switch (c.GetType().Name)
                {
                    case "WeaponManager":
                        var sets = so.FindProperty("hardpointSets");
                        for (int s = 0; s < sets.arraySize; s++)
                        {
                            var hps = sets.GetArrayElementAtIndex(s).FindPropertyRelative("hardpoints");
                            for (int h = 0; h < hps.arraySize; h++) Fix(hps.GetArrayElementAtIndex(h), "transform", true);
                        }
                        break;
                    case "NavLights":
                        var nl = so.FindProperty("navLights");
                        for (int i = 0; i < nl.arraySize; i++) Fix(nl.GetArrayElementAtIndex(i), "renderer", false);
                        break;
                    case "FlareEjector": case "ChaffEjector":
                        var ep = so.FindProperty("ejectionPoints");
                        for (int i = 0; i < ep.arraySize; i++) Fix(ep.GetArrayElementAtIndex(i), "transform", false);
                        break;
                    default: continue;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Debug.Log($"[MiG29] parts: {n} pylons, lights and dispensers moved onto the part they sit in");
        }
    }
}
