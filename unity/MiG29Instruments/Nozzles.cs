using System.Collections.Generic;
using UnityEngine;

namespace MiG29Instruments
{
    // Opens and closes the MiG-29's RD-33 nozzles (MiG29Tools/MiG29Nozzles.cs builds them: hinged petals named
    // MiG29_noz_<L|R>_<flap|seal|inner>_<i>) like the real convergent-divergent nozzle: open with the engine off, partly closed at
    // idle, closed down at full military power, wide open in afterburner. Visual only; runs for every MiG, player or AI.
    public class NozzleDriver : MonoBehaviour
    {
        // nozzle geometry (tools/nozzle_gen.py layout)
        const int Count = 16;
        const float RRoot = 0.490f, LFlap = 0.935f, RThroat = 0.352f, LInner = 0.42f;
        const float ExitOff = 0.452f, ExitIdle = 0.442f, ExitMil = 0.405f, ExitAB = 0.472f;

        class Petal { public Transform t; public string kind; public Quaternion spin; }
        class Nozzle { public List<Petal> petals = new List<Petal>(); public float exit = 0.435f; }

        Aircraft ac;
        readonly Nozzle[] nozzles = { new Nozzle(), new Nozzle() };   // 0 = left, 1 = right (engine order in Cockpit.GetEngineStates)
        float nextFind;
        int finds;
        Cockpit cockpit;   // carries the engine list (on the cockpit part, its own physics root in flight)

        void Start() => ac = GetComponent<Aircraft>();

        void Find()
        {
            foreach (var t in MiGParts.Named(ac, "MiG29_noz_"))
            {
                var bits = t.name.Split('_');              // MiG29, noz, L|R, kind, index
                if (bits.Length < 5 || !int.TryParse(bits[4], out int i)) continue;
                var n = nozzles[bits[2] == "L" ? 0 : 1];
                float phase = bits[3] == "flap" ? 0f : bits[3] == "seal" ? 0.5f : 0.25f;
                n.petals.Add(new Petal { t = t, kind = bits[3], spin = Quaternion.AngleAxis(360f * (i + phase) / Count, Vector3.forward) });
            }
        }

        // The petals are only drawn out to the outside level of detail (~2.3 km, MiG29Lod): nothing to pose beyond it, and every
        // fourth frame past 600 m, where a frame's movement is under a pixel.
        const float SkipBeyond = 2400f, SlowBeyond = 600f;
        float lastPose = -1f;

        bool Due()
        {
            var cam = Camera.main;
            var p = nozzles[0].petals.Count > 0 ? nozzles[0].petals[0].t : nozzles[1].petals[0].t;
            if (cam == null || p == null) return true;
            float d2 = (p.position - cam.transform.position).sqrMagnitude;
            if (d2 > SkipBeyond * SkipBeyond) return false;
            return d2 < SlowBeyond * SlowBeyond || ((Time.frameCount + GetInstanceID()) & 3) == 0;
        }

        static float Target(float rpmPercent, bool afterburner, bool running)
        {
            if (!running || rpmPercent < 20f) return ExitOff;
            if (afterburner) return ExitAB;
            if (rpmPercent <= 70f) return ExitIdle;
            return Mathf.Lerp(ExitIdle, ExitMil, Mathf.InverseLerp(70f, 100f, rpmPercent));
        }

        void LateUpdate()
        {
            if (ac == null) return;
            if (nozzles[0].petals.Count == 0 && nozzles[1].petals.Count == 0)
            {
                if (Time.time < nextFind || finds > 20) return;
                nextFind = Time.time + 1f; finds++;
                Find();
                return;
            }
            if (!Due()) return;
            float dt = lastPose < 0f ? Time.deltaTime : Mathf.Min(Time.time - lastPose, 1f);   // time since the last pose, frames skipped included
            lastPose = Time.time;
            if (cockpit == null && ac.cockpit != null) cockpit = ((Component)ac.cockpit).GetComponentInChildren<Cockpit>(true);
            var engines = cockpit != null ? cockpit.GetEngineStates() : null;
            var inputs = ac.GetInputs();
            float throttle = inputs != null ? inputs.throttle : 0f;
            float k = 1f - Mathf.Exp(-dt / 0.6f);          // the nozzle actuators take about a second
            for (int e = 0; e < 2; e++)
            {
                var n = nozzles[e];
                if (n.petals.Count == 0) continue;
                float rpm = 0f; bool running = false;
                if (engines != null && engines.Count > 0)
                {
                    var eng = engines[Mathf.Min(e, engines.Count - 1)];
                    float ratio = eng.GetRPMRatio();
                    rpm = ratio <= 0.33f ? ratio / 0.33f * 70f : 70f + (ratio - 0.33f) / 0.67f * 30f;   // as the tachometers read
                    running = ratio > 0.05f;
                }
                n.exit += (Target(rpm, throttle > 0.9f && rpm > 90f, running) - n.exit) * k;
                float alpha = Mathf.Asin(Mathf.Clamp((RRoot - n.exit) / LFlap, -0.5f, 0.5f)) * Mathf.Rad2Deg;
                float beta = Mathf.Asin(Mathf.Clamp((n.exit - 0.015f - RThroat) / LInner, -0.5f, 0.5f)) * Mathf.Rad2Deg;
                foreach (var p in n.petals)
                {
                    if (p.t == null) continue;
                    p.t.localRotation = p.spin * Quaternion.Euler(p.kind == "inner" ? beta : -alpha, 0f, 0f);
                }
            }
        }
    }
}
