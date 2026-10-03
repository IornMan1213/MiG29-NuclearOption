using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MiG29Instruments
{
    // Drop tanks for the MiG-29 Fulcrum mod. In the .nobp a drop tank is a releasable store (select it and fire, like a bomb) whose
    // mass is the full tank. This driver gives it fuel: while a tank hangs on its pylon it keeps the internal tanks topped up, as the
    // real MiG-29 burns external fuel first, and moves the transferred mass from the pylon to the internal tanks so the aircraft's
    // weight stays right. Dropping a tank then sheds only the empty tank plus whatever fuel was left in it.
    // It also adds a jettison key for the player and lets AI-flown MiGs drop their tanks once they run dry.
    // Runs only where the aircraft is simulated (the owner), like the game's own fuel system.
    public class StoresDriver : MonoBehaviour
    {
        class Tank { public Weapon w; public float capacity, fuel, moved; public bool attached; }

        const float TransferRate = 30f;   // kg/s, ample for both engines in full afterburner

        static readonly FieldInfo HardpointField = typeof(Weapon).GetField("hardpoint", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo TankPartField = typeof(FuelTank).GetField("part", BindingFlags.Instance | BindingFlags.NonPublic);

        Aircraft ac;
        readonly List<Tank> tanks = new List<Tank>();
        float nextRefresh;
        bool jettisoning;

        // usable fuel per tank (kg): 1,500 L and 1,150 L of jet fuel at ~0.79 kg/L
        public static float Capacity(WeaponInfo info)
        {
            if (info == null || info.weaponName == null) return 0f;
            if (info.weaponName.StartsWith("PTB-1500")) return 1180f;
            if (info.weaponName.StartsWith("PTB-1150")) return 905f;
            return 0f;
        }

        public float ExternalFuel { get { float f = 0; foreach (var t in tanks) if (t.attached) f += t.fuel; return f; } }
        public bool Carrying { get { foreach (var t in tanks) if (t.attached) return true; return false; } }
        // tanks still hanging but dry: the cockpit's ПТБ caution
        public bool TanksEmpty => Carrying && ExternalFuel < 1f;

        void Start()
        {
            ac = GetComponent<Aircraft>();
            ac.OnRearmUnit += OnRearm;
        }

        void OnDestroy()
        {
            if (ac != null) ac.OnRearmUnit -= OnRearm;
        }

        void Refresh()
        {
            tanks.RemoveAll(t => t.w == null);
            if (ac.weaponManager == null) return;
            foreach (var list in ac.weaponManager.HardpointsIndexes.Values)
                foreach (var w in list)
                {
                    if (w == null) continue;
                    float cap = Capacity(w.info);
                    if (cap <= 0f || tanks.Exists(t => t.w == w)) continue;
                    bool att = w.GetAmmoLoaded() > 0;
                    tanks.Add(new Tank { w = w, capacity = cap, fuel = att ? cap : 0f, attached = att });
                }
        }

        static void ShiftPylonMass(Tank t, float amount)
        {
            if (amount == 0f || HardpointField == null) return;
            (HardpointField.GetValue(t.w) as Hardpoint)?.ModifyMass(amount);
        }

        // rearmed on the ground: tanks still hanging are refilled (their transferred mass goes back on the pylon)
        void OnRearm()
        {
            foreach (var t in tanks)
                if (t.attached && t.w != null)
                {
                    ShiftPylonMass(t, t.moved);
                    t.moved = 0f; t.fuel = t.capacity;
                }
        }

        void FixedUpdate()
        {
            if (ac == null || ac.remoteSim) return;
            if (Time.time >= nextRefresh) { nextRefresh = Time.time + 1f; Refresh(); }
            if (tanks.Count == 0) return;

            foreach (var t in tanks)
            {
                if (t.w == null) continue;
                bool att = t.w.GetAmmoLoaded() > 0;
                if (t.attached && !att)
                {
                    // released: the game took the full tank mass off the pylon; the part already given to the internal tanks comes back
                    ShiftPylonMass(t, t.moved);
                    t.moved = 0f; t.fuel = 0f;
                }
                else if (!t.attached && att)
                {
                    t.moved = 0f; t.fuel = t.capacity;   // a new tank was hung (rearm)
                }
                t.attached = att;
            }
            Transfer(Time.fixedDeltaTime);
        }

        void Transfer(float dt)
        {
            float external = ExternalFuel;
            if (external <= 0f || !ac.Ignition) return;
            var internalTanks = ac.GetFuelTanks();
            float room = 0f;
            foreach (var ft in internalTanks) if (ft != null) room += Mathf.Max(0f, ft.GetCapacity() - ft.fuelMass);
            float amount = Mathf.Min(room, external, TransferRate * dt);
            if (amount <= 0.0001f) return;

            // into the internal tanks, each by its free space
            foreach (var ft in internalTanks)
            {
                if (ft == null) continue;
                float free = Mathf.Max(0f, ft.GetCapacity() - ft.fuelMass);
                if (free <= 0f) continue;
                float x = amount * free / room;
                ft.fuelMass += x;
                var part = TankPartField?.GetValue(ft) as UnitPart;
                if (part != null && part.rb != null) part.ModifyMass(x);
            }
            // out of the drop tanks, evenly by what each holds (pairs drain together)
            foreach (var t in tanks)
            {
                if (!t.attached || t.fuel <= 0f) continue;
                float y = amount * t.fuel / external;
                t.fuel -= y; t.moved += y;
                ShiftPylonMass(t, -y);
            }
        }

        void Update()
        {
            if (ac == null || ac.remoteSim || jettisoning) return;
            if (IsPlayer(ac))
            {
                if (MiG29InstrumentsPlugin.JettisonKey.Value.IsDown() && Carrying) StartCoroutine(Jettison(false));
                else if (MiG29InstrumentsPlugin.AutoDropEmpty.Value && TanksEmpty) StartCoroutine(Jettison(true));
            }
            else if (ac.IsServer && TanksEmpty) StartCoroutine(Jettison(true));   // AI pilots drop their tanks once dry
        }

        // releases every drop tank through the game's own weapon stations (networked like any store release)
        IEnumerator Jettison(bool emptyOnly)
        {
            jettisoning = true;
            int dropped = 0; bool safety = false;
            foreach (var st in ac.weaponStations.ToArray())
            {
                if (st == null || Capacity(st.WeaponInfo) <= 0f) continue;
                if (st.SafetyIsOn(ac)) { safety = true; continue; }
                for (int guard = 0; guard < 4 && st.GetAmmoLoaded() > 0; guard++)
                {
                    st.LaunchMount(ac, null, ac.GlobalPosition() + ac.transform.forward * 1000f);
                    dropped++;
                    yield return new WaitForSeconds(0.25f);
                }
            }
            var report = SceneSingleton<AircraftActionsReport>.i;
            if (report != null && IsPlayer(ac))
            {
                if (dropped > 0) report.ReportText(emptyOnly ? "Empty drop tanks released" : "Drop tanks jettisoned", 3f);
                else if (safety) report.ReportText("Drop tanks: release blocked with the gear down", 3f);
            }
            yield return new WaitForSeconds(1f);
            jettisoning = false;
        }

        static bool IsPlayer(Aircraft a) { var h = SceneSingleton<CombatHUD>.i; return h != null && h.aircraft == a; }
    }
}
