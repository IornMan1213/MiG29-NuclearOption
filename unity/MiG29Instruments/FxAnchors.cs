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
            var t = __instance.transform; var a = FxAnchors.Of(t);
            if (a == null) return;
            var d = a.position - t.position;
            for (int i = __state.own; i < t.childCount; i++) t.GetChild(i).position += d;
            if (Datum.origin != null) for (int i = __state.world; i < Datum.origin.childCount; i++) Datum.origin.GetChild(i).position += d;
        }
    }
}
