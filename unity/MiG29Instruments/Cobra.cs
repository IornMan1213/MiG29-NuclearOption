using System;
using System.Reflection;
using UnityEngine;

namespace MiG29Instruments
{
    // Cobra switch: the Cobra key turns the MiG-29's stability assist (the game's flight assist: pitch-rate, g and AoA limiters)
    // off for a few seconds so the pilot can pull the nose past vertical, then turns it back on, the way MiG-29 display pilots
    // fly Pugachev's Cobra with the AoA limiter overridden. Works on the aircraft the player flies; does nothing if assist is
    // already off. Pressing the key again during the window restores assist at once.
    public class CobraSwitch : MonoBehaviour
    {
        const string MiG29Key = "mig29_Fulcrum";

        // FlyByWire blends its limits in and out over ~1 s (limitFactorSmoothed, lerped by fixedDeltaTime). Dropping it to 0 makes
        // the limiter let go as the key is pressed; it eases back in by itself when assist returns.
        static readonly FieldInfo LimitFactor = typeof(ControlsFilter.FlyByWire).GetField("limitFactorSmoothed", BindingFlags.Instance | BindingFlags.NonPublic);

        Aircraft active;
        float until;

        void Update()
        {
            try
            {
                var hud = SceneSingleton<CombatHUD>.i;
                var ac = hud != null ? hud.aircraft : null;
                bool mig = ac != null && ((Unit)ac).definition != null && ((Unit)ac).definition.jsonKey == MiG29Key;

                if (active != null && (active != ac || Time.time >= until || (mig && MiG29InstrumentsPlugin.CobraKey.Value.IsDown())))
                {
                    if (active != null && !active.flightAssist) active.SetFlightAssist(true);
                    active = null;
                    return;
                }
                if (!mig || active != null || !MiG29InstrumentsPlugin.CobraKey.Value.IsDown()) return;
                if (!ac.flightAssist) return;   // the pilot has assist off already

                ac.SetFlightAssist(false);
                var fbw = ac.GetControlsFilter()?.GetFlyByWire();
                if (fbw != null) LimitFactor?.SetValue(fbw, 0f);
                active = ac;
                until = Time.time + MiG29InstrumentsPlugin.CobraSeconds.Value;
                var report = SceneSingleton<AircraftActionsReport>.i;
                if (report != null) report.ReportText($"Cobra: limiters off for {MiG29InstrumentsPlugin.CobraSeconds.Value:0.#} s", 2f);
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); enabled = false; }
        }
    }
}
