using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MiG29Instruments
{
    // Chaff that behaves like chaff. The game scores a chaff cartridge once, as it leaves the jet: the angle between the missile's
    // line of sight and the direction from the cartridge to the jet (a vector set mostly by how far the jet moved in that frame and
    // where the dispenser sits), times how close the missile is, added to the seeker's jam total, which is capped at 1 and decays
    // within half a second. A missile only drops its target after that total stays above its tolerance for its whole lock
    // persistence (2 s for the R-27R, 2-4 s for active missiles), so one press never broke a lock, and the cloud itself never
    // mattered (game code, 2026-11). Only these jets carry chaff in the stock game, so this changes nothing for anyone else.
    //
    //   Programmes: one press of chaff runs a burst (Configuration Manager > Countermeasures > Chaff programme), as real dispensers
    //     do: Single 1 pair, Short 4, Standard 8 (about 2 s, the semi-active lock time), Long 12 (for active missiles).
    //   The cloud keeps working: each cartridge counts for 4 s while its cloud is still in the missile's range gate (within 150 m of
    //     the jet along the line of sight, fading out by 300 m) and inside its beam (6 degrees, fading out by 12). Beaming keeps the jet
    //     in that gate; running away from your own chaff does not. A fresh cloud takes hold within about a second.
    //   Notching: from the jet's velocity against the line of sight, squared: beaming counts fully, 60 degrees off the line a
    //     quarter, 45 degrees under a tenth, flying straight at or away from the missile nothing, at any frame rate.
    //   Seduction: while the jam total is over the missile's tolerance, an active missile is steered at the clouds (Decoy), in its
    //     own homing and on its datalink alike.
    // Measured with real AAM2s fired by an AI KR-67 (tests/ingame/chaff_run.txt, 2026-11-04): beaming with no chaff, every missile
    // reached the jet; beaming with one Standard press at 8 km, it lost lock at 4.3 km and missed by 134 m; with Long, 2 of 3
    // missed (the third came from 2.9 km head-on); at 45 degrees the jam total peaked at 0.62 and the missile hit; head-on chaff
    // added nothing. The first settings (a 1.5 s build-up, a 4 degree beam, no seduction) never made a missile miss.
    //   Seekers: semi-active ones (the R-27R, the game's SAMs) are fooled more easily than active ones (x0.7).
    //   Ground clutter: below 1,000 m, with the missile looking down at the jet, chaff counts up to 50% more (full at 10 degrees down
    //     and at ground level). A missile below or level with the jet gets no bonus.
    // The game's own rules stay: the range effect (closer counts more), the jam total and its decay, lock persistence, and the jammer
    // and chaff feeding the same total.
    public static class Chaff
    {
        const float ProgrammeGap = 0.31f;           // the dispenser refuses faster than its 0.3 s ejection interval
        const float CloudLife = 4f, NormTime = 0.35f, Bloom = 0.3f;
        const float RangeGate = 150f, RangeGateOut = 300f, BeamGate = 6f, BeamGateOut = 12f;
        const float ActiveFactor = 0.7f, ClutterBonus = 0.5f, ClutterAlt = 1000f, ClutterLookDown = 10f;

        public static int Pairs(string programme)
        {
            switch (programme) { case "Short": return 4; case "Standard": return 8; case "Long": return 12; default: return 1; }
        }

        static readonly AccessTools.FieldRef<Countermeasure, Aircraft> AircraftOf = AccessTools.FieldRefAccess<Countermeasure, Aircraft>("aircraft");
        static readonly AccessTools.FieldRef<RadarChaff, Aircraft> ChaffAircraft = AccessTools.FieldRefAccess<RadarChaff, Aircraft>("aircraft");
        static readonly AccessTools.FieldRef<MissileSeeker, Missile> MissileOf = AccessTools.FieldRefAccess<MissileSeeker, Missile>("missile");
        static readonly AccessTools.FieldRef<MissileSeeker, Unit> TargetOf = AccessTools.FieldRefAccess<MissileSeeker, Unit>("targetUnit");
        static readonly AccessTools.FieldRef<SARHSeeker, float> SarhJam = AccessTools.FieldRefAccess<SARHSeeker, float>("jamAccumulation");
        static readonly AccessTools.FieldRef<SARHSeeker, float> SarhTol = AccessTools.FieldRefAccess<SARHSeeker, float>("jamTolerance");
        static readonly AccessTools.FieldRef<SARHSeeker, RadarParams> SarhRadar = AccessTools.FieldRefAccess<SARHSeeker, RadarParams>("radarParams");
        static readonly AccessTools.FieldRef<ARHSeeker, float> ArhJam = AccessTools.FieldRefAccess<ARHSeeker, float>("jamAccumulation");
        static readonly AccessTools.FieldRef<ARHSeeker, float> ArhTol = AccessTools.FieldRefAccess<ARHSeeker, float>("jamTolerance");
        static readonly AccessTools.FieldRef<ARHSeeker, RadarParams> ArhRadar = AccessTools.FieldRefAccess<ARHSeeker, RadarParams>("radarParameters");
        static readonly System.Reflection.FieldInfo SeekerMode = typeof(Missile).GetField("seekerMode", AccessTools.all);

        internal static bool Ours(Aircraft ac) => Holes.IsMiG(ac);

        // ---- programmes ----

        class Run { public ChaffEjector ejector; public int left; public float next; }
        static readonly List<Run> runs = new List<Run>();
        static bool firing;
        static float lastReport;

        internal static bool Busy(ChaffEjector e) { foreach (var r in runs) if (r.ejector == e) return true; return false; }

        // the dispenser's own Fire: a press starts a programme (or does nothing while one runs: holding the button repeats it)
        internal static bool OnFire(ChaffEjector e)
        {
            if (firing) return true;
            var ac = AircraftOf(e);
            if (!Ours(ac)) return true;
            int pairs = Pairs(MiG29InstrumentsPlugin.ChaffProgramme.Value);
            if (pairs <= 1) return true;
            if (Busy(e)) return false;
            runs.Add(new Run { ejector = e, left = pairs, next = 0f });
            if (GameManager.IsLocalAircraft(ac) && Time.time - lastReport > 0.5f)
            {
                lastReport = Time.time;
                SceneSingleton<AircraftActionsReport>.i?.ReportText($"Chaff programme: {pairs} pairs", 1.5f);
            }
            Tick();   // the first pair now
            return false;
        }

        internal static bool ProgrammeRunning(Aircraft ac)
        {
            foreach (var r in runs) if (r.ejector != null && AircraftOf(r.ejector) == ac) return true;
            return false;
        }

        // ---- clouds ----

        class Cloud { public MissileSeeker seeker; public bool active; public RadarChaff chaff; public float born; }
        static readonly List<Cloud> clouds = new List<Cloud>();

        // the seekers' chaff handlers: chaff from these jets is scored here, over the cloud's life; any other chaff the stock way
        internal static bool OnChaff(MissileSeeker seeker, RadarChaff chaff, bool active)
        {
            if (chaff == null || !Ours(ChaffAircraft(chaff))) return true;
            clouds.Add(new Cloud { seeker = seeker, active = active, chaff = chaff, born = Time.time });
            return false;
        }

        // Updated by ChaffDriver once a frame
        internal static void Tick()
        {
            float now = Time.timeSinceLevelLoad;
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                var r = runs[i];
                if (r.ejector == null || r.left <= 0 || r.ejector.GetAmmo() <= 0) { runs.RemoveAt(i); continue; }
                if (now < r.next) continue;
                firing = true;
                try { r.ejector.Fire(); }
                finally { firing = false; }
                r.left--; r.next = now + ProgrammeGap;
            }

            float dt = Time.deltaTime;
            for (int i = clouds.Count - 1; i >= 0; i--)
            {
                var c = clouds[i];
                float age = Time.time - c.born;
                if (c.seeker == null || c.chaff == null || age > CloudLife) { clouds.RemoveAt(i); continue; }
                float add = Rate(c, age) * dt;
                if (add <= 0f) continue;
                if (c.active) { var s = (ARHSeeker)c.seeker; ArhJam(s) = Mathf.Min(1f, ArhJam(s) + add); }
                else { var s = (SARHSeeker)c.seeker; SarhJam(s) = Mathf.Min(1f, SarhJam(s) + add); }
            }
        }

        // Seduction: while a missile's jam total is over its tolerance and clouds still count against it, it is steered at their
        // centre (weighted by how much each counts), in active and in datalink mode. In the game a jammed active missile otherwise
        // switches to its datalink, which hands it the jet's true position whenever anything on its side tracks the jet, so chaff
        // never made it miss (measured: jam pinned at 1, missiles still within 4-12 m). On the real thing the midcourse updates come
        // from the launcher's radar, which a notching jet's chaff fools as well. When the clouds fade it may find the jet again.
        internal static bool Decoy(MissileSeeker seeker, out Vector3 aim)
        {
            aim = Vector3.zero;
            float tol, jam;
            if (seeker is ARHSeeker a) { tol = ArhTol(a); jam = ArhJam(a); }
            else if (seeker is SARHSeeker sa) { tol = SarhTol(sa); jam = SarhJam(sa); }
            else return false;
            if (jam <= tol) return false;
            float w = 0f; Vector3 sum = Vector3.zero;
            foreach (var c in clouds)
            {
                if (c.seeker != seeker || c.chaff == null) continue;
                float r = Rate(c, Time.time - c.born);
                if (r <= 0f) continue;
                sum += c.chaff.transform.position * r; w += r;
            }
            if (w <= 0f) return false;
            aim = sum / w;
            return true;
        }

        internal static void Seduce(MissileSeeker seeker)
        {
            if (!Decoy(seeker, out var aim)) return;
            var m = MissileOf(seeker);
            if (m != null) m.SetAimpoint(aim.ToGlobalPosition(), Vector3.zero);
        }

        // for the in-game tests: the clouds counting against a seeker now, their summed jam per second, and the mean of each factor
        public static string Stats(MissileSeeker seeker)
        {
            int n = 0, live = 0; float total = 0f, gR = 0f, gB = 0f, notch = 0f;
            foreach (var c in clouds)
            {
                if (c.seeker != seeker) continue;
                n++;
                float r = Rate(c, Time.time - c.born, out var f);
                if (r > 0f) { live++; total += r; gR += f.x; gB += f.y; notch += f.z; }
            }
            return live > 0 ? $"{live}/{n} clouds, {total:F2}/s, range gate {gR / live:F2}, beam gate {gB / live:F2}, notch {notch / live:F2}" : $"0/{n} clouds";
        }

        static float Rate(Cloud c, float age) => Rate(c, age, out _);

        // jam added per second by one cloud against one seeker (factors: range gate, beam gate, notch)
        static float Rate(Cloud c, float age, out Vector3 factors)
        {
            factors = Vector3.zero;
            var m = MissileOf(c.seeker);
            var target = TargetOf(c.seeker);
            if (m == null || target == null || m.targetID.NotValid || SeekerMode == null) return 0f;
            int mode = Convert.ToInt32(SeekerMode.GetValue(m));   // 1 active lock, 2 active search (datalink), 3 passive
            if (mode != 1 && mode != 2) return 0f;
            Vector3 msl = c.seeker.transform.position, tgt = target.transform.position, cloud = c.chaff.transform.position;
            Vector3 los = tgt - msl;
            float d = los.magnitude;
            if (d < 1f) return 0f;
            los /= d;

            float tol = c.active ? ArhTol((ARHSeeker)c.seeker) : SarhTol((SARHSeeker)c.seeker);
            float maxRange = c.active ? ArhRadar((ARHSeeker)c.seeker).maxRange : SarhRadar((SARHSeeker)c.seeker).maxRange;
            float range = maxRange > 0f ? Mathf.Clamp01(1f - d / maxRange) : 0f;

            // notch: the jet's speed toward or away from the missile is what a Doppler seeker sees; side-on it vanishes into the chaff
            var rb = target.rb;
            Vector3 v = rb != null ? rb.velocity : target.transform.forward * target.speed;
            float side = v.sqrMagnitude > 25f ? 1f - Mathf.Abs(Vector3.Dot(los, v.normalized)) : 1f;
            float notch = side * side;   // a Doppler seeker only loses you near a true beam: 1 at 90 deg, 0.55 at 75, 0.25 at 60, 0.09 at 45

            // the cloud is only a decoy while it shares the jet's range cell and the seeker's beam
            float alongLos = Mathf.Abs(Vector3.Dot(cloud - tgt, los));
            float gateR = 1f - Mathf.Clamp01((alongLos - RangeGate) / (RangeGateOut - RangeGate));
            float beamDeg = Vector3.Angle(cloud - msl, tgt - msl);
            float gateB = 1f - Mathf.Clamp01((beamDeg - BeamGate) / (BeamGateOut - BeamGate));
            float bloom = Mathf.Clamp01(age / Bloom);

            // ground clutter: only when the missile looks down on a low jet
            float lookDown = Mathf.Asin(Mathf.Clamp(-los.y, -1f, 1f)) * Mathf.Rad2Deg;
            float clutter = 1f + ClutterBonus * Mathf.Clamp01(lookDown / ClutterLookDown) * Mathf.Clamp01(1f - target.radarAlt / ClutterAlt);

            factors = new Vector3(gateR, gateB, notch);
            float strength = range * notch * (c.active ? ActiveFactor : 1f) * clutter / (1f + tol);
            return strength * gateR * gateB * bloom / NormTime;
        }
    }

    // runs the programmes and scores the clouds
    public class ChaffDriver : MonoBehaviour
    {
        int errors;
        void Update()
        {
            try { Chaff.Tick(); }
            catch (Exception e) { if (errors++ < 3) MiG29InstrumentsPlugin.Log.LogWarning($"chaff: {e}"); }
        }
    }

    [HarmonyPatch(typeof(ChaffEjector), "Fire")]
    static class ChaffProgrammePatch
    {
        static bool Prefix(ChaffEjector __instance) => Chaff.OnFire(__instance);
    }

    [HarmonyPatch(typeof(ARHSeeker), "DatalinkMode")]
    static class ChaffDatalinkPatch
    {
        static void Postfix(ARHSeeker __instance) => Chaff.Seduce(__instance);
    }

    [HarmonyPatch(typeof(ARHSeeker), "TerminalMode")]
    static class ChaffTerminalPatch
    {
        static void Postfix(ARHSeeker __instance) => Chaff.Seduce(__instance);
    }

    [HarmonyPatch(typeof(SARHSeeker), "SARHSeeker_OnChaff")]
    static class ChaffSarhPatch
    {
        static bool Prefix(SARHSeeker __instance, RadarChaff __0) => Chaff.OnChaff(__instance, __0, false);
    }

    [HarmonyPatch(typeof(ARHSeeker), "ARHSeeker_OnChaff")]
    static class ChaffArhPatch
    {
        static bool Prefix(ARHSeeker __instance, RadarChaff __0) => Chaff.OnChaff(__instance, __0, true);
    }
}
