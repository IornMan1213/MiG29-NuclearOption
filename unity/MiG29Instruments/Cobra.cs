using System;
using System.Reflection;
using UnityEngine;

namespace MiG29Instruments
{
    // Cobra switch: the Cobra key turns the MiG-29's stability assist (the game's flight assist: pitch-rate, g and AoA limiters)
    // off so the pilot can pull the nose past vertical, then turns it back on, the way MiG-29 display pilots fly Pugachev's Cobra
    // with the AoA limiter overridden. Assist comes back as soon as the move is over: the nose has been high (past 35 deg AoA) and
    // is down again (under 15 deg), or the jet has slowed below 140 km/h, or after "Cobra time" at the latest. Pressing the key
    // again during the window restores it at once. Works on the aircraft the player flies; does nothing if assist is already off.
    public class CobraSwitch : MonoBehaviour
    {
        const string MiG29Key = "mig29_Fulcrum";
        const float MinSeconds = 1f;                  // never hand the limiters back in the first second of the pull
        const float PeakAoA = 35f, RecoveredAoA = 15f, SlowKmh = 140f;

        // FlyByWire blends its limits in and out over ~1 s (limitFactorSmoothed, lerped by fixedDeltaTime). Dropping it to 0 makes
        // the limiter let go as the key is pressed; it eases back in by itself when assist returns.
        static readonly FieldInfo LimitFactor = typeof(ControlsFilter.FlyByWire).GetField("limitFactorSmoothed", BindingFlags.Instance | BindingFlags.NonPublic);

        Aircraft active;
        float started, until, peak;
        AudioSource audioSource;
        AudioClip toneOff, toneOn;

        void Start()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.spatialBlend = 0f; audioSource.volume = 0.35f;
            toneOff = Tone("MiG29_cobra_off", new[] { 880f, 1175f });   // rising pair: limiters off
            toneOn = Tone("MiG29_cobra_on", new[] { 660f });            // one low tone: limiters back
        }

        void Update()
        {
            try
            {
                var hud = SceneSingleton<CombatHUD>.i;
                var ac = hud != null ? hud.aircraft : null;
                bool mig = ac != null && ((Unit)ac).definition != null && ((Unit)ac).definition.jsonKey == MiG29Key;

                if (active != null)
                {
                    bool done = active != ac || Time.time >= until || (mig && Hotkeys.Down(MiG29InstrumentsPlugin.CobraKey));
                    if (!done)
                    {
                        Measure(active, out float aoa, out float iasKmh);
                        peak = Mathf.Max(peak, aoa);
                        done = Time.time - started > MinSeconds && ((peak > PeakAoA && aoa < RecoveredAoA) || iasKmh < SlowKmh);
                    }
                    if (done)
                    {
                        if (!active.flightAssist) active.SetFlightAssist(true);
                        if (active == ac) Beep(toneOn);
                        active = null;
                    }
                    return;
                }
                if (!mig || !Hotkeys.Down(MiG29InstrumentsPlugin.CobraKey)) return;
                if (!ac.flightAssist) return;   // the pilot has assist off already

                ac.SetFlightAssist(false);
                var fbw = ac.GetControlsFilter()?.GetFlyByWire();
                if (fbw != null) LimitFactor?.SetValue(fbw, 0f);
                active = ac;
                started = Time.time; peak = 0f;
                until = Time.time + MiG29InstrumentsPlugin.CobraSeconds.Value;
                Beep(toneOff);
                var report = SceneSingleton<AircraftActionsReport>.i;
                if (report != null) report.ReportText($"Cobra: limiters off (up to {MiG29InstrumentsPlugin.CobraSeconds.Value:0.#} s)", 2f);
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); enabled = false; }
        }

        // angle of attack and indicated airspeed, as the cockpit instruments read them
        static void Measure(Aircraft ac, out float aoaDeg, out float iasKmh)
        {
            var rb = ac.CockpitRB();
            var cock = ac.cockpit != null ? ((Component)ac.cockpit).transform : ac.transform;
            var v = rb != null ? cock.InverseTransformDirection(rb.velocity) : Vector3.zero;
            aoaDeg = ac.speed > 15f ? Mathf.Atan2(-v.y, v.z) * Mathf.Rad2Deg : 0f;
            iasKmh = ac.speed * Mathf.Sqrt(Mathf.Max(ac.GetAirDensity(), 0f) / 1.225f) * 3.6f;
        }

        void Beep(AudioClip clip)
        {
            if (clip != null && audioSource != null && MiG29InstrumentsPlugin.CobraSound.Value) audioSource.PlayOneShot(clip);
        }

        // short sine tones with soft edges, made here so the plugin ships no sound files
        static AudioClip Tone(string name, float[] freqs)
        {
            const int rate = 44100; const float len = 0.09f, gap = 0.05f;
            int per = (int)(rate * len), step = (int)(rate * (len + gap));
            var data = new float[step * (freqs.Length - 1) + per];
            for (int k = 0; k < freqs.Length; k++)
                for (int i = 0; i < per; i++)
                {
                    float env = Mathf.Min(1f, Mathf.Min(i, per - i) / (rate * 0.008f));
                    data[k * step + i] = 0.6f * env * Mathf.Sin(2f * Mathf.PI * freqs[k] * i / rate);
                }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
