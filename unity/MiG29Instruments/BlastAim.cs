using HarmonyLib;
using UnityEngine;

namespace MiG29Instruments
{
    // Where a missile's blast hits each part. The game traces one line from the burst to each part's pivot, and the parts that line
    // passes through on the way shield the part (DamageEffects.FragTrace). The MiG's pivots sit in the middle of each part's skin, so
    // from the side the lines to the booms, LERX, intakes and radome passed through nothing and a near miss took a whole side off at
    // once; the lines to the ailerons, flaps and stabilators aimed at their hinges, outside their hulls, and never hit them at all.
    // For the MiG's parts the line now aims at the point of the part's hull nearest the jet's centre of mass, a fifth of the way in
    // towards the hull's middle: a burst outside the jet has to get through the parts in front first. Other aircraft are left alone.
    // Damage is worked out on the host, so this applies when the host runs this version.
    [HarmonyPatch(typeof(DamageEffects), nameof(DamageEffects.FragTrace))]
    static class BlastAim
    {
        const float Inward = 0.2f;

        static void Prefix(Vector3 origin, ref Vector3 fragVector, Collider fragTarget)
        {
            try
            {
                if (fragTarget == null || !fragTarget.TryGetComponent<UnitPart>(out var part)) return;
                var ac = part.parentUnit;
                if (!Holes.IsMiG(ac) || ac.rb == null) return;
                var aim = Vector3.Lerp(fragTarget.ClosestPoint(ac.rb.worldCenterOfMass), fragTarget.bounds.center, Inward);
                var dir = aim - origin;
                if (dir.sqrMagnitude < 1e-6f) return;
                fragVector = dir.normalized * fragVector.magnitude;
            }
            catch { }
        }
    }
}
