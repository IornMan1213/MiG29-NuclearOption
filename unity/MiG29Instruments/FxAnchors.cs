using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MiG29Instruments
{
    // Fire, fuel leaks, engine smoke and break-up effects where the MiG's skin is. The game spawns them at the part's own transform
    // (or the thrust point), and the MiG's parts keep the KR-67's pivots: up to 2 m off the MiG's skin, so an outer wing tank burned
    // in mid-air ahead of the wing and a hit cockpit broke up in the radome. The build puts an anchor on the skin of each such part
    // (MiG29Builder: "MiG29_fx", and "MiG29_fx_leak" on the underside for leaks); these patches move each effect to it as it spawns.
    // Other aircraft have no anchors and are left alone. Looks only: the fire's damage radius stays where the game has it.
    static class FxAnchors
    {
        const string Anchor = "MiG29_fx", LeakAnchor = "MiG29_fx_leak";
        static readonly AccessTools.FieldRef<FuelTank, GameObject> Fire = AccessTools.FieldRefAccess<FuelTank, GameObject>("fireEffectSpawn");
        static readonly AccessTools.FieldRef<FuelTank, GameObject> Ball = AccessTools.FieldRefAccess<FuelTank, GameObject>("fireballSpawn");
        internal static readonly AccessTools.FieldRef<FuelTank, ParticleSystem> Leak = AccessTools.FieldRefAccess<FuelTank, ParticleSystem>("leakSystem");
        internal static readonly AccessTools.FieldRef<JetNozzle, Transform> ThrustPoint = AccessTools.FieldRefAccess<JetNozzle, Transform>("thrustTransform");

        internal static Transform Of(Transform part, bool leak = false) => part == null ? null : (leak ? part.Find(LeakAnchor) : null) ?? part.Find(Anchor);

        // The skin along each break line is in ragged patches (MiG29Builder: UV3 x = patch number + 1, y = PartCode of the part
        // across the break). When a part breaks off, about ShredShare of the patches along its breaks are cut out of the skin, its
        // own and the parts' it was joined to, and thrown off as pieces, so the break shreds instead of parting cleanly. Nothing is
        // made until a part breaks. Looks only: each client tears its own.
        const float ShredShare = 0.6f, ShardLife = 7f;
        const int MaxShards = 24;   // per break, the pieces thrown off
        static readonly List<Vector2> uv3 = new List<Vector2>();
        static readonly List<int> tris = new List<int>();

        internal static int PartCode(string name) { uint h = 2166136261; foreach (char c in name) { h ^= c; h *= 16777619; } return (int)(h & 0xFFFFF); }
        static string Bare(string n) => n.EndsWith("(Clone)") ? n.Substring(0, n.Length - 7) : n;

        internal static void Shred(UnitPart part)
        {
            try
            {
                var ac = part.parentUnit;
                if (ac == null || !Holes.IsMiG(ac)) return;
                int me = PartCode(Bare(part.name)), thrown = 0;
                foreach (var p in ac.partLookup)
                {
                    if (p == null || thrown >= MaxShards) continue;
                    MeshFilter mf = null;
                    foreach (Transform ch in p.transform) if (ch.name.StartsWith("MiG29_skin_")) { mf = ch.GetComponent<MeshFilter>(); break; }
                    if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    bool mine = p == part;
                    var mesh = mf.sharedMesh;
                    mesh.GetUVs(3, uv3);
                    if (uv3.Count == 0) continue;
                    // the patches along this break, and the ones to tear
                    var take = new HashSet<int>(); var seen = new HashSet<int>();
                    foreach (var u in uv3)
                    {
                        int id = (int)u.x;
                        if (id == 0 || !seen.Add(id)) continue;
                        if ((mine || (int)u.y == me) && Random.value < ShredShare && thrown + take.Count < MaxShards) take.Add(id);
                    }
                    if (take.Count == 0) continue;
                    if (!ReferenceEquals(mf.sharedMesh, mf.mesh)) mesh = mf.mesh;   // this aircraft's own copy, once
                    mesh.GetTriangles(tris, 0);
                    var keep = new List<int>(tris.Count); var pieces = new Dictionary<int, List<int>>();
                    for (int i = 0; i + 2 < tris.Count; i += 3)
                    {
                        int id = (int)uv3[tris[i]].x;
                        if (take.Contains(id)) { if (!pieces.TryGetValue(id, out var l)) pieces[id] = l = new List<int>(); l.Add(tris[i]); l.Add(tris[i + 1]); l.Add(tris[i + 2]); }
                        else { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); }
                    }
                    mesh.SetTriangles(keep, 0);
                    var r = mf.GetComponent<MeshRenderer>();
                    var verts = mesh.vertices; var normals = mesh.normals; var uvs = mesh.uv;
                    foreach (var kv in pieces) { Throw(mf.transform, r, verts, normals, uvs, kv.Value, p.rb); thrown++; }
                }
                MiG29InstrumentsPlugin.Log.LogInfo($"shred {Bare(part.name)}: {thrown} pieces");
            }
            catch (System.Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); }
        }

        // A part that breaks off stops colliding with the jet it left: joined neighbours' hitboxes overlap at the seam (so no
        // round slips between them), and that overlap would otherwise hold the part wedged in place (the tail cone between
        // the nozzles)
        internal static void Release(UnitPart part)
        {
            try
            {
                var ac = part.parentUnit;
                if (ac == null || !Holes.IsMiG(ac)) return;
                var own = part.GetComponentsInChildren<Collider>(true).Where(c => c.GetComponentInParent<UnitPart>() == part).ToList();
                if (own.Count == 0) return;
                foreach (var p in ac.partLookup)
                {
                    if (p == null || p == part) continue;
                    foreach (var c in p.GetComponentsInChildren<Collider>(true))
                    {
                        if (c.GetComponentInParent<UnitPart>() != p) continue;
                        foreach (var o in own) Physics.IgnoreCollision(o, c);
                    }
                }
            }
            catch (System.Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); }
        }

        static void Throw(Transform skin, MeshRenderer r, Vector3[] verts, Vector3[] normals, Vector2[] uvs, List<int> idx, Rigidbody from)
        {
            var map = new Dictionary<int, int>(); var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            foreach (var i in idx) { if (!map.TryGetValue(i, out int j)) { map[i] = j = v.Count; v.Add(verts[i]); n.Add(normals[i]); uv.Add(uvs[i]); } t.Add(j); }
            // centred on itself: with no collider its body turns about its own origin
            var c = Vector3.zero; foreach (var q in v) c += q; c /= v.Count;
            for (int i = 0; i < v.Count; i++) v[i] -= c;
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
            var go = new GameObject("MiG29_shard");
            var at = skin.TransformPoint(c);
            go.transform.SetPositionAndRotation(at, skin.rotation); go.transform.localScale = skin.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = r.sharedMaterials;
            go.transform.SetParent(Datum.origin, true);
            var body = go.AddComponent<Rigidbody>();
            body.mass = 3f; body.drag = 0.8f; body.angularDrag = 0.2f;
            body.velocity = (from != null ? from.GetPointVelocity(at) : Vector3.zero) + Random.onUnitSphere * Random.Range(3f, 9f);
            body.angularVelocity = Random.insideUnitSphere * 12f;
            Object.Destroy(go, ShardLife);
            Object.Destroy(m, ShardLife + 0.1f);
        }

        // the game puts both at the tank's origin, so this can run on every status update
        internal static void TankFire(FuelTank tank)
        {
            var a = Of(tank.transform);
            if (a == null) return;
            foreach (var fx in new[] { Fire(tank), Ball(tank) })
                if (fx != null && fx.transform.parent == tank.transform) fx.transform.localPosition = a.localPosition;
        }
    }

    [HarmonyPatch(typeof(FuelTank), nameof(FuelTank.UpdateStatus))]
    static class FxTankFire
    {
        static void Postfix(FuelTank __instance) => FxAnchors.TankFire(__instance);
    }

    // the leak keeps its effect's own offset and is made once per tank
    [HarmonyPatch(typeof(FuelTank), nameof(FuelTank.PunctureTank))]
    static class FxTankLeak
    {
        static void Prefix(FuelTank __instance, out bool __state) => __state = FxAnchors.Leak(__instance) == null;
        static void Postfix(FuelTank __instance, bool __state)
        {
            var leak = FxAnchors.Leak(__instance);
            var a = FxAnchors.Of(__instance.transform, leak: true);
            if (__state && leak != null && a != null) leak.transform.localPosition += a.localPosition;
            FxAnchors.TankFire(__instance);
        }
    }

    [HarmonyPatch(typeof(JetNozzle), nameof(JetNozzle.FailureEffect))]
    static class FxEngineFail
    {
        static void Postfix(JetNozzle __instance)
        {
            var tt = FxAnchors.ThrustPoint(__instance);
            var a = FxAnchors.Of(tt);
            if (a == null) return;
            var fx = tt.GetChild(tt.childCount - 1);   // the effect just spawned
            if (fx != a) fx.localPosition = a.localPosition;
        }
    }

    // Break-up effects: DamageParticles stay on the part, the rest are handed to the world origin. Both are new last children.
    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.SpawnFragments))]
    static class FxFragments
    {
        static void Prefix(UnitPart __instance, out (int own, int world) __state) =>
            __state = (__instance.transform.childCount, Datum.origin != null ? Datum.origin.childCount : 0);

        static void Postfix(UnitPart __instance, (int own, int world) __state)
        {
            FxAnchors.Shred(__instance);
            FxAnchors.Release(__instance);
            var t = __instance.transform; var a = FxAnchors.Of(t);
            if (a == null) return;
            var d = a.position - t.position;
            for (int i = __state.own; i < t.childCount; i++) t.GetChild(i).position += d;
            if (Datum.origin != null) for (int i = __state.world; i < Datum.origin.childCount; i++) Datum.origin.GetChild(i).position += d;
        }
    }
}
