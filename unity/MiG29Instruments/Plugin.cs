using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using MiG29Tools;
using UnityEngine;

namespace MiG29Instruments
{
    // Makes every instrument in the MiG-29 Fulcrum mod's cockpit work. The .nobp carries the moving parts (MiG29_ins_<id> mounts with
    // a "needle" child, MiG29_lamp_<id> lamps); this plugin finds them on the aircraft you fly and drives them from its live state.
    // Visual only: nothing is sent over the network, other aircraft are untouched.
    [BepInPlugin("iornman.mig29.instruments", "MiG-29 Instruments", "1.0.0")]
    public class MiG29InstrumentsPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            // some games destroy BepInEx's manager object on scene loads: scan from an object of our own
            var go = new GameObject("MiG29InstrumentsScanner");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Scanner>();
            Log.LogInfo("MiG-29 instruments ready");
        }
    }

    // Finds the aircraft the player flies (CombatHUD) and, if it is the MiG-29, attaches the instrument driver.
    public class Scanner : MonoBehaviour
    {
        float nextScan;
        int lastChecked;

        bool dev;

        void Start() => dev = System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "mig29_dump.flag"));

        void Update()
        {
            if (dev)   // developer hotkeys (only with BepInEx/mig29_dump.flag): F10 outside orbit camera, F11 back to the cockpit
            {
                var cams = SceneSingleton<CameraStateManager>.i;
                if (cams != null && Input.GetKeyDown(KeyCode.F10)) cams.SwitchState(cams.orbitState);
                if (cams != null && Input.GetKeyDown(KeyCode.F11)) cams.SwitchState(cams.cockpitState);
            }
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            try
            {
                var hud = SceneSingleton<CombatHUD>.i;
                if (hud == null || hud.aircraft == null) return;
                var ac = hud.aircraft;
                int id = ac.GetInstanceID();
                if (id == lastChecked) return;
                lastChecked = id;
                MiG29InstrumentsPlugin.Log.LogInfo($"player aircraft: {ac.name}");
                if (System.IO.File.Exists(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "mig29_dump.flag")))
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var t in InstrumentDriver.Root(ac).GetComponentsInChildren<Transform>(true))
                    {
                        var mf = t.GetComponent<MeshFilter>(); var r = t.GetComponent<Renderer>();
                        sb.AppendLine($"{t.name} parent={t.parent?.name} active={t.gameObject.activeInHierarchy} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name + " v" + mf.sharedMesh.vertexCount + " sub" + mf.sharedMesh.subMeshCount : "-")} r={(r ? r.enabled + " " + (r.sharedMaterial ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "nomat") + " b=" + r.bounds.size : "-")}");
                    }
                    foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
                        sb.AppendLine($"CAMERA {cam.name} enabled={cam.enabled} near={cam.nearClipPlane} mask={cam.cullingMask:X8} cockpitLayer={(cam.cullingMask & (1 << 3)) != 0} depth={cam.depth} clear={cam.clearFlags}");
                    System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "mig29_dump.txt"), sb.ToString());
                }
                if (ac.GetComponent<InstrumentDriver>() != null) return;
                foreach (var t in InstrumentDriver.Root(ac).GetComponentsInChildren<Transform>(true))
                    if (t.name == "MiG29_ins_asi_kmh")
                    {
                        ac.gameObject.AddComponent<InstrumentDriver>();
                        return;
                    }
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); nextScan = Time.unscaledTime + 5f; }
        }
    }

    public class InstrumentDriver : MonoBehaviour
    {
        class Needle { public string id; public Transform t; public Func<FlightState, float> f; public float cur; public bool wraps; public float k; }

        Aircraft ac;
        readonly List<Needle> needles = new List<Needle>();
        Transform ball;
        readonly Dictionary<string, Renderer> lamps = new Dictionary<string, Renderer>();
        readonly HashSet<string> litNow = new HashSet<string>();
        readonly FlightState st = new FlightState();
        List<IEngine> engines = new List<IEngine>();
        readonly bool[] damaged = new bool[2];
        readonly List<(Unit emitter, float time, float power, bool target)> radar = new List<(Unit, float, float, bool)>();
        Canopy[] canopies;
        Light panelLight, consoleLight;
        static readonly FieldInfo CanopiesField = typeof(Aircraft).GetField("canopies", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo OpenAmountField = typeof(Canopy).GetField("openAmount", BindingFlags.Instance | BindingFlags.NonPublic);
        float oxygen = 150f, hydraulic, egtL = 15f, egtR = 15f;
        bool first = true;

        void Start()
        {
            try { Bind(); }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); enabled = false; }
        }

        // the cockpit part carries the instruments; in flight it is its own physics root, not a child of the aircraft
        public static Transform Root(Aircraft a) => a.cockpit != null ? ((Component)a.cockpit).transform : a.transform;

        void Bind()
        {
            ac = GetComponent<Aircraft>();
            var root = Root(ac);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("MiG29_ins_"))
                {
                    var id = t.name.Substring("MiG29_ins_".Length);
                    var n = t.Find("needle");
                    if (n == null) continue;
                    if (id == "adi_ball") { ball = n; continue; }
                    if (!MiG29InstrumentMath.Needles.TryGetValue(id, out var f)) continue;
                    needles.Add(new Needle { id = id, t = n, f = f, wraps = id.StartsWith("alt_") || id == "hsi_card" || id.StartsWith("clock"), k = MiG29InstrumentMath.Damping(id) });
                }
                else if (t.name.StartsWith("MiG29_lamp_") && t.TryGetComponent<Renderer>(out var r))
                    lamps[t.name.Substring("MiG29_lamp_".Length)] = r;
            }
            SetupLampMaterial();
            var cockpit = root.GetComponentInChildren<Cockpit>(true);
            if (cockpit != null) engines = cockpit.GetEngineStates();
            for (int i = 0; i < engines.Count && i < 2; i++)
            {
                int k = i;
                engines[i].OnEngineDamage += () => damaged[k] = true;
            }
            ac.onRadarWarning += OnRadarWarning;
            canopies = CanopiesField?.GetValue(ac) as Canopy[];
            // night lighting: a soft flood under the glareshield and one over the consoles, faded in after dusk
            var adi = root.Find("MiG29_ins_adi_ball");
            if (adi != null)
            {
                panelLight = MakeLight("MiG29_panel_light", adi.position - adi.forward * 0.20f + adi.up * 0.12f, adi, 0.9f, new Color(1f, 0.78f, 0.62f));
                consoleLight = MakeLight("MiG29_console_light", adi.position - adi.forward * 0.62f + adi.up * 0.02f, adi, 1.1f, new Color(1f, 0.7f, 0.55f));
            }
            MiG29InstrumentsPlugin.Log.LogInfo($"MiG-29 instruments on {ac.name}: {needles.Count} needles, ball {(ball != null)}, {lamps.Count} lamps, {engines.Count} engines");
        }

        Light MakeLight(string name, Vector3 pos, Transform parent, float range, Color col)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.parent, true);
            go.transform.position = pos;
            go.layer = parent.gameObject.layer;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = range; l.color = col; l.intensity = 0f; l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            l.enabled = false;
            return l;
        }

        // 0 by day, 1 at night (game time of day, hours)
        static float Night(float tod)
        {
            float d = Mathf.Min(Mathf.Abs(tod - 6.5f), Mathf.Abs(tod - 18.5f));
            bool dark = tod < 6.5f || tod > 18.5f;
            return dark ? Mathf.Clamp01(0.5f + d) : Mathf.Clamp01(0.5f - d);
        }

        void OnDestroy()
        {
            if (ac != null) ac.onRadarWarning -= OnRadarWarning;
        }

        void OnRadarWarning(Aircraft.OnRadarWarning w)
        {
            if (w.emitter == null) return;
            radar.Add((w.emitter, Time.time, w.power, w.isTarget));
        }

        // Lit lamps glow: an emissive copy of the game's tactical-screen material (URP Lit with emission) showing the cockpit atlas.
        void SetupLampMaterial()
        {
            Renderer any = null;
            foreach (var r in lamps.Values) { any = r; break; }
            if (any == null) return;
            var atlas = any.sharedMaterial != null ? any.sharedMaterial.GetTexture("_Basecolor") : null;
            Material baseMat = null;
            foreach (var r in Root(ac).GetComponentsInChildren<Renderer>(true))
                if (r.name == "tacScreen") { baseMat = r.sharedMaterial; break; }
            Material lit;
            if (baseMat != null)
            {
                lit = new Material(baseMat) { name = "MiG29_lamp_lit" };
                lit.SetTexture("_BaseMap", atlas); lit.SetTexture("_EmissionMap", atlas);
                lit.SetColor("_BaseColor", Color.white); lit.SetColor("_EmissionColor", Color.white * 1.6f);
                lit.EnableKeyword("_EMISSION");
                lit.mainTextureScale = Vector2.one; lit.mainTextureOffset = Vector2.zero;
            }
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) return;
                lit = new Material(sh) { name = "MiG29_lamp_lit" };
                lit.SetTexture("_BaseMap", atlas);
            }
            foreach (var r in lamps.Values) { r.sharedMaterial = lit; r.enabled = false; }
        }

        static float Wrap(float a) => a > 180f ? a - 360f : a;

        void Sample(float dt)
        {
            float speed = ac.speed;
            float density = ac.GetAirDensity();
            st.iasKmh = speed * Mathf.Sqrt(Mathf.Max(density, 0f) / 1.225f) * 3.6f;
            st.altM = ac.GlobalPosition().y;
            st.mach = speed / Mathf.Max(LevelInfo.GetSpeedOfSound(st.altM), 1f);
            var rb = ac.CockpitRB();
            var vel = rb != null ? rb.velocity : Vector3.zero;
            st.vsMs = vel.y;
            var cock = ac.cockpit != null ? ((Component)ac.cockpit).transform : transform;
            var vl = cock.InverseTransformDirection(vel);
            st.aoaDeg = speed > 15f ? Mathf.Atan2(-vl.y, vl.z) * Mathf.Rad2Deg : 0f;
            st.g = ac.gForce;
            var e = cock.eulerAngles;
            st.pitchDeg = -Wrap(e.x); st.rollDeg = -Wrap(e.z); st.headingDeg = e.y;
            st.radAltM = ac.radarAlt;
            var inputs = ac.GetInputs();
            st.throttle = inputs != null ? inputs.throttle : 0f; st.brake = inputs != null ? inputs.brake : 0f;

            // engines: RPM from the game; exhaust temperature follows RPM with thermal lag (fire pegs it)
            float rl = engines.Count > 0 ? engines[0].GetRPMRatio() * 100f : 0f;
            float rr = engines.Count > 1 ? engines[1].GetRPMRatio() * 100f : rl;
            st.rpmL = rl; st.rpmR = rr;
            bool fireL = engines.Count > 0 && engines[0] is Turbojet j0 && j0.engineFire;
            bool fireR = engines.Count > 1 ? engines[1] is Turbojet j1 && j1.engineFire : fireL;
            float Egt(float rpm, bool fire) => fire ? 1050f : rpm < 5f ? 15f : 300f + 520f * Mathf.Pow(rpm / 100f, 2.2f) + (st.throttle > 0.9f ? 60f : 0f);
            float lag = 1f - Mathf.Exp(-0.8f * dt);
            egtL += (Egt(rl, fireL) - egtL) * lag; egtR += (Egt(rr, fireR) - egtR) * lag;
            st.egtL = egtL; st.egtR = egtR;
            st.fireL = fireL; st.fireR = fireR; st.damagedL = damaged[0]; st.damagedR = engines.Count > 1 ? damaged[1] : damaged[0];

            st.fuelKg = ac.GetFuelQuantity(); st.fuelFrac = ac.GetFuelLevel();
            // hydraulics: engine-driven pumps (210 kgf/cm2), bleeding down slowly with both engines off or both damaged
            float hydTarget = (Mathf.Max(rl, rr) > 30f && !(st.damagedL && st.damagedR)) ? 210f : 0f;
            hydraulic += (hydTarget - hydraulic) * (1f - Mathf.Exp(-(hydTarget > hydraulic ? 1.5f : 0.15f) * dt));
            st.hydraulic = hydraulic;
            st.onGround = ac.IsLanded(); st.airborne = !st.onGround;
            if (st.airborne) oxygen = Mathf.Max(0f, oxygen - dt * 0.6f / 60f);
            st.oxygen = oxygen;
            st.cabinKm = Mathf.Clamp(st.altM < 2000f ? st.altM / 1000f : 2f + (st.altM - 2000f) * 0.00045f, 0f, 20f);
            st.volts = Mathf.Max(rl, rr) > 45f ? 28.5f : 24f;

            var lvl = NetworkSceneSingleton<LevelInfo>.i;
            float tod = lvl != null ? lvl.timeOfDay : (float)DateTime.Now.TimeOfDay.TotalHours;
            st.hours = Mathf.Floor(tod); st.minutes = Mathf.Floor((tod - st.hours) * 60f); st.seconds = ((tod - st.hours) * 60f - st.minutes) * 60f;

            var gs = ac.gearState;
            st.gearDown = gs == LandingGear.GearState.LockedExtended;
            st.gearMoving = gs == LandingGear.GearState.Extending || gs == LandingGear.GearState.Retracting;
            bool open = false;
            if (canopies != null && OpenAmountField != null)
                foreach (var c in canopies) if (c != null && (float)OpenAmountField.GetValue(c) > 0.05f) open = true;
            st.canopyOpen = open;
            st.blink = Mathf.Sin(Time.time * 12f) > 0f;

            // threats: radar emitters painting us in the last 2.5 s, plus incoming missiles (flashing sector)
            st.rwr.Clear(); st.rwrPower = 0f; st.lockedOn = false;
            radar.RemoveAll(r => r.emitter == null || Time.time - r.time > 2.5f);
            var fwd = Vector3.ProjectOnPlane(cock.forward, Vector3.up);
            foreach (var r in radar)
            {
                var to = Vector3.ProjectOnPlane(r.emitter.transform.position - cock.position, Vector3.up);
                int type = r.emitter is Aircraft ? 0 : r.emitter is Ship ? 5 : r.power > 0.5f ? 1 : 2;
                st.rwr.Add((Vector3.SignedAngle(fwd, to, Vector3.up), type));
                st.rwrPower = Mathf.Max(st.rwrPower, 0.3f + 0.7f * Mathf.Clamp01(r.power));
                st.lockedOn |= r.target;
            }
            var mw = ac.GetMissileWarningSystem();
            st.missileIncoming = mw != null && mw.knownMissiles.Count > 0;
            if (st.missileIncoming)
                foreach (var m in mw.knownMissiles)
                    if (m != null)
                    {
                        var to = Vector3.ProjectOnPlane(((Component)m).transform.position - cock.position, Vector3.up);
                        st.rwr.Add((Vector3.SignedAngle(fwd, to, Vector3.up), -1));
                        st.rwrPower = 1f;
                    }
        }

        int errors;

        void LateUpdate()
        {
            if (ac == null) return;
            try { Drive(); }
            catch (Exception e) { if (errors++ < 3) MiG29InstrumentsPlugin.Log.LogError(e); }
        }

        void Drive()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            Sample(dt);
            foreach (var n in needles)
            {
                float target = n.f(st);
                if (first) n.cur = target;
                else
                {
                    float a = 1f - Mathf.Exp(-n.k * dt);
                    n.cur = n.wraps ? n.cur + Mathf.DeltaAngle(n.cur, target) * a : n.cur + (target - n.cur) * a;
                }
                n.t.localRotation = Quaternion.Euler(0f, 0f, -n.cur);
            }
            if (ball != null) ball.localRotation = first ? MiG29InstrumentMath.Ball(st) : Quaternion.Slerp(ball.localRotation, MiG29InstrumentMath.Ball(st), 1f - Mathf.Exp(-12f * dt));
            first = false;

            float night = Night(st.hours + st.minutes / 60f);
            foreach (var l in new[] { panelLight, consoleLight })
                if (l != null) { l.enabled = night > 0.02f; l.intensity = night * (l == panelLight ? 0.55f : 0.35f); }

            var on = MiG29InstrumentMath.Lamps(st);
            foreach (var kv in lamps)
            {
                bool want = on.Contains(kv.Key);
                if (want != litNow.Contains(kv.Key))
                {
                    kv.Value.enabled = want;
                    if (want) litNow.Add(kv.Key); else litNow.Remove(kv.Key);
                }
            }
        }
    }
}
