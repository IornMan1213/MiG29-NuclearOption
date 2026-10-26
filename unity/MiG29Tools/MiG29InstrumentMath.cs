using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiG29Tools
{
    // What the MiG-29 instruments show, from one snapshot of the aircraft. Shared by the MiG29Instruments BepInEx plugin (live values)
    // and the editor preview (test values), so the renders check exactly what flies. Angles are degrees clockwise from 12 o'clock,
    // matching the dial faces drawn by tools/cockpit_atlas.py.
    public class FlightState
    {
        public float iasKmh, mach, altM, vsMs, aoaDeg, g = 1f, pitchDeg, rollDeg, headingDeg, radAltM;
        public float rpmL, rpmR, egtL, egtR;          // % and deg C
        public float fuelKg, fuelFrac = 1f;
        public bool ptbEmpty;   // drop tanks carried and empty (MiG29Instruments StoresDriver)
        public float oxygen = 150f, hydraulic, cabinKm, volts, brake, throttle, yaw;
        public float hours, minutes, seconds;
        public bool gearDown = true, gearMoving, onGround = true, canopyOpen, airborne;
        public bool fireL, fireR, damagedL, damagedR, missileIncoming, lockedOn;
        public bool blink;                             // ~2 Hz flasher for warnings
        public float rwrPower;                         // 0..1, strongest radar painting us
        public List<(float bearing, int type)> rwr = new List<(float, int)>();   // bearing from the nose, threat type 0..5 (П З Н Х F О), -1 for a missile
    }

    public static class MiG29InstrumentMath
    {
        // Intake FOD doors (MiG29Builder.AddIntakeBlockers builds them open): closed = the hinge turned this far about its x axis, from
        // lying along the duct roof to sealing the duct down to its floor. Closed on the ground below 200 km/h with an engine running,
        // like the cockpit's ПЗУ light.
        public const float FodClosedDeg = -56.4f;
        public static bool FodClosed(bool onGround, float iasKmh, float rpmL, float rpmR) => onGround && iasKmh < 200f && (rpmL > 40f || rpmR > 40f);

        static float Lin(float v, float lo, float hi, float a0, float a1) => a0 + Mathf.Clamp01((v - lo) / (hi - lo)) * (a1 - a0);

        // vertical speed: 0/50/100/200/300 m/s at 0/40/80/120/160 deg from 9 o'clock
        static float Vsi(float v)
        {
            float a = Mathf.Abs(v);
            float d = a < 50 ? a / 50 * 40 : a < 100 ? 40 + (a - 50) / 50 * 40 : a < 200 ? 80 + (a - 100) / 100 * 40 : 120 + Mathf.Min(a - 200, 100) / 100 * 40;
            return -90 + Mathf.Sign(v) * d;
        }

        public static readonly Dictionary<string, Func<FlightState, float>> Needles = new Dictionary<string, Func<FlightState, float>>
        {
            ["asi_kmh"] = s => Lin(s.iasKmh, 0, 1600, -150, 150),
            ["asi_mach"] = s => Lin(s.mach, 0.4f, 2.4f, -150, 150),
            ["alt_m"] = s => Mathf.Repeat(s.altM, 1000) / 1000 * 360,
            ["alt_km"] = s => Mathf.Repeat(Mathf.Max(s.altM, 0), 10000) / 10000 * 360,
            ["vsi"] = s => Vsi(s.vsMs),
            ["aoa"] = s => Lin(s.aoaDeg, -10, 30, -140, -20),
            ["g"] = s => Lin(s.g, -2, 10, 140, 20),
            ["radalt"] = s => Lin(s.radAltM, 0, 1500, -120, 120),
            ["clock_h"] = s => (Mathf.Repeat(s.hours, 12) + s.minutes / 60) * 30,
            ["clock_m"] = s => (s.minutes + s.seconds / 60) * 6,
            ["clock_s"] = s => Mathf.Floor(s.seconds) * 6,
            ["oxy"] = s => Lin(s.oxygen, 0, 150, -120, 120),
            ["cabin"] = s => Lin(s.cabinKm, 0, 20, -120, 120),
            ["rpm_l"] = s => Lin(s.rpmL, 0, 110, -135, 135),
            ["rpm_r"] = s => Lin(s.rpmR, 0, 110, -135, 135),
            ["egt_l"] = s => Lin(s.egtL, 200, 1000, -135, 135),
            ["egt_r"] = s => Lin(s.egtR, 200, 1000, -135, 135),
            ["fuel"] = s => Lin(s.fuelKg, 0, 5000, -120, 120),
            ["hyd"] = s => Lin(s.hydraulic, 0, 300, -120, 120),
            ["hsi_card"] = s => -s.headingDeg,
        };

        // Attitude ball rotation in its instrument frame (z into the panel, y up): roll about the view axis, then pitch.
        public static Quaternion Ball(FlightState s) => Quaternion.Euler(0f, 0f, s.rollDeg) * Quaternion.Euler(-s.pitchDeg, 0f, 0f);

        // Parts that move other than turning about the dial axis: pedals slide fore/aft with the rudder input, the gear lever swings
        // down (gear down) or up. Returns false for ordinary needles.
        public static bool Pose(string id, FlightState s, out Vector3 localPos, out Quaternion localRot)
        {
            localPos = Vector3.zero; localRot = Quaternion.identity;
            switch (id)
            {
                case "pedal_l": localPos = new Vector3(0f, 0f, -s.yaw * 0.035f); return true;   // mount z points forward (away from the pilot)
                case "pedal_r": localPos = new Vector3(0f, 0f, s.yaw * 0.035f); return true;
                case "gear_lever": localRot = Quaternion.Euler(s.gearDown || (s.gearMoving && s.onGround) ? -145f : -35f, 0f, 0f); return true;   // negative: swings out toward the pilot, never into the panel
            }
            return false;
        }

        // How fast each needle follows its value (1/s): damped like real instruments.
        public static float Damping(string id) => id == "clock_s" ? 1000f : id.StartsWith("vsi") ? 3f : id.StartsWith("egt") ? 1.5f : id.StartsWith("hyd") || id.StartsWith("oxy") ? 2f : 8f;

        static readonly float[] SectorBearings = { -150, -120, -60, -30, 30, 60, 120, 150 };

        // lamp ids built once: the plugin asks for the lit set every frame
        static readonly string[] SpoSector = Ids("spo_s", 8), SpoType = Ids("spo_t", 6), SpoPower = Ids("spo_p", 10);
        static readonly string[] GearRed = { "gear_nose_r", "gear_left_r", "gear_right_r" }, GearGreen = { "gear_nose_g", "gear_left_g", "gear_right_g" };
        static string[] Ids(string prefix, int n) { var a = new string[n]; for (int i = 0; i < n; i++) a[i] = prefix + i; return a; }

        // Every lit lamp for this state: caution panel (cau_*), radar warning receiver (spo_*), gear lights (gear_*).
        public static HashSet<string> Lamps(FlightState s) => Lamps(s, new HashSet<string>());

        // The same into a set the caller keeps (cleared first), without allocating.
        public static HashSet<string> Lamps(FlightState s, HashSet<string> on)
        {
            on.Clear();
            void C(string id, bool v) { if (v) on.Add(id); }
            bool engOffL = s.rpmL < 40, engOffR = s.rpmR < 40;
            C("cau_fire_l", s.fireL && s.blink); C("cau_fire_r", s.fireR && s.blink);
            C("cau_lowalt", s.airborne && !s.gearDown && s.radAltM < 60 && s.vsMs < -2);
            C("cau_aoa", s.airborne && s.aoaDeg > 24);
            C("cau_overg", s.g > 8.5f || s.g < -2.5f);
            C("cau_missile", s.missileIncoming && s.blink);
            C("cau_fuel_low", s.fuelFrac < 0.25f);
            C("cau_fuel_res", s.fuelFrac < 0.10f && s.blink);
            C("cau_gen_l", engOffL); C("cau_gen_r", engOffR);
            C("cau_hyd", s.hydraulic < 150);
            C("cau_lock", s.lockedOn);
            C("cau_pump", engOffL || engOffR);
            C("cau_oil", s.damagedL || s.damagedR);
            C("cau_vib", (s.damagedL && s.rpmL > 60) || (s.damagedR && s.rpmR > 60) || Mathf.Max(s.rpmL, s.rpmR) > 104);
            C("cau_canopy", s.canopyOpen);
            C("cau_gear", s.gearMoving || (s.airborne && !s.gearDown && s.iasKmh < 300 && s.radAltM < 300));
            C("cau_speed", s.iasKmh > 1450 || s.mach > 2.25f);
            C("cau_ptb", s.ptbEmpty);   // drop tanks carried but dry: drop them
            C("cau_flaps", s.gearDown && !s.gearMoving && s.airborne);
            C("cau_brake", s.brake > 0.1f);
            C("cau_fod", FodClosed(s.onGround, s.iasKmh, s.rpmL, s.rpmR));
            C("cau_ab", s.throttle > 0.9f && (s.rpmL > 90 || s.rpmR > 90));
            bool anyWarn = on.Count > 0 && !(on.Count == 1 && (on.Contains("cau_brake") || on.Contains("cau_fod") || on.Contains("cau_ab") || on.Contains("cau_flaps")));
            C("cau_master", anyWarn && (s.airborne || s.fireL || s.fireR) && s.blink);

            // SPO-15: sector lamps nearest each emitter's bearing, power ladder, threat type
            foreach (var (bearing, type) in s.rwr)
            {
                int best = 0; float bestD = 999;
                for (int i = 0; i < SectorBearings.Length; i++) { float d = Mathf.Abs(Mathf.DeltaAngle(bearing, SectorBearings[i])); if (d < bestD) { bestD = d; best = i; } }
                if (!s.missileIncoming || s.blink) on.Add(SpoSector[best]);
                if (type >= 0 && type <= 5) on.Add(SpoType[type]);
            }
            int bars = Mathf.RoundToInt(Mathf.Clamp01(s.rwrPower) * 10);
            for (int i = 0; i < bars && i < SpoPower.Length; i++) on.Add(SpoPower[i]);

            // gear: green down and locked, red in transit
            for (int i = 0; i < 3; i++)
            {
                if (s.gearMoving) on.Add(GearRed[i]);
                else if (s.gearDown) on.Add(GearGreen[i]);
            }
            return on;
        }

        // A plausible mid-flight state for preview renders: 650 km/h at 3,200 m, climbing 25 m/s in a 30 deg right bank, radar lock from
        // the front right.
        public static FlightState Sample()
        {
            var s = new FlightState
            {
                iasKmh = 650, mach = 0.62f, altM = 3200, vsMs = 25, aoaDeg = 6, g = 2.5f, pitchDeg = 10, rollDeg = 30, headingDeg = 60, radAltM = 900,
                rpmL = 92, rpmR = 88, egtL = 720, egtR = 700, fuelKg = 2600, fuelFrac = 0.55f, oxygen = 130, hydraulic = 210, cabinKm = 2.5f,
                volts = 28, throttle = 0.85f, yaw = 0.6f, hours = 10, minutes = 9, seconds = 30, gearDown = false, onGround = false, airborne = true, blink = true,
                lockedOn = true, rwrPower = 0.6f,
            };
            s.rwr.Add((40f, 0));
            return s;
        }
    }
}
