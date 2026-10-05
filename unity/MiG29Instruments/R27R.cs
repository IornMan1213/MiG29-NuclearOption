using HarmonyLib;
using UnityEngine;

namespace MiG29Instruments
{
    // R-27R fired with no target. The game lets a missile launch with an empty target list (WeaponManager.Fire passes target null),
    // and SARHSeeker.Seek detonates a missile that has no target as soon as its warhead is armed. On the SAM it was made for that is
    // harmless, but it blew the R-27R up at the pylon (user report, v0.9.0; MiG29Weapons now also spawns it unarmed). Like the
    // radar and IR missiles, an R-27R without a target now flies straight ahead unguided: it arms after its arm delay and
    // SARHSeeker.SlowChecks still self-destructs it once the motor is out and it slows down. With a target, the stock seeker runs.
    [HarmonyPatch(typeof(SARHSeeker), "Seek")]
    static class R27RNoTarget
    {
        const float ArmDelay = 1f;     // MiG29Weapons TuneSeeker armDelay for the R-27R
        static readonly AccessTools.FieldRef<MissileSeeker, Missile> MissileOf = AccessTools.FieldRefAccess<MissileSeeker, Missile>("missile");
        static readonly AccessTools.FieldRef<MissileSeeker, Unit> TargetOf = AccessTools.FieldRefAccess<MissileSeeker, Unit>("targetUnit");

        static bool Prefix(SARHSeeker __instance)
        {
            var m = MissileOf(__instance);
            if (m == null || !m.name.StartsWith("mig29_R27R") || TargetOf(__instance) != null) return true;
            if (!m.IsArmed() && m.timeSinceSpawn > ArmDelay) m.Arm();
            m.SetAimpoint(m.GlobalPosition() + m.transform.forward * 10000f, Vector3.zero);
            return false;
        }
    }
}
