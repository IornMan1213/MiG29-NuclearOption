using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MiG29Instruments
{
    // Optional: hides the game's screen-space flight HUD while the player flies the MiG-29, whose analog gauges show the same data.
    // Hidden: the HUD/HMD flight-data apps (speed, altitude, climb, AoA, g, Mach, fuel, throttle, gear...) and FlightHud's pitch
    // ladder, compass, waterline and velocity vector. Untouched: the map, target markers (CombatHUD), the mouse-aim cursor, cockpit
    // screens (MFD apps) and anything other mods draw. Optionally the weapon, countermeasure and damage readouts stay.
    // Elements fade out through a CanvasGroup (alpha 0), so the game can keep toggling and updating them underneath.
    public class GlassHudHider : MonoBehaviour
    {
        const string MiG29Key = "mig29_Fulcrum";
        static readonly HashSet<string> CombatReadouts = new HashSet<string>
            { "WeaponIndicator", "CountermeasureIndicator", "PylonIndicator", "TurretAutoIndicator", "SystemStatusDisplay" };
        static readonly FieldInfo AppTypeField = typeof(HUDApp).GetField("type", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly string[] FlightHudFields = { "pitchCompassCenter", "compass", "waterline", "velocityVector" };

        class Hidden { public CanvasGroup group; public bool added; public float alpha; }
        readonly Dictionary<GameObject, Hidden> hidden = new Dictionary<GameObject, Hidden>();
        float nextScan;
        bool wasActive;

        void LateUpdate()
        {
            bool active;
            try { active = MiG29InstrumentsPlugin.HideGlassHud.Value && PlayerFliesMiG(); }
            catch (Exception) { active = false; }

            if (!active)
            {
                if (wasActive) RestoreAll();
                wasActive = false;
                return;
            }
            if (!wasActive || Time.unscaledTime >= nextScan)   // HUD apps come and go with the aircraft and weapon selection
            {
                nextScan = Time.unscaledTime + 1f;
                try { Scan(); } catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); nextScan = Time.unscaledTime + 10f; }
            }
            wasActive = true;
            var gone = new List<GameObject>();
            foreach (var kv in hidden)
            {
                if (kv.Key == null || kv.Value.group == null) { gone.Add(kv.Key); continue; }
                kv.Value.group.alpha = 0f;   // the game may fade its own groups: keep ours at 0
            }
            foreach (var g in gone) hidden.Remove(g);
        }

        static bool PlayerFliesMiG()
        {
            var hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.aircraft == null) return false;
            var def = ((Unit)hud.aircraft).definition;
            return def != null && def.jsonKey == MiG29Key;
        }

        void Scan()
        {
            var wanted = new HashSet<GameObject>();
            bool keepCombat = MiG29InstrumentsPlugin.KeepCombatReadouts.Value;
            foreach (var app in FindObjectsOfType<HUDApp>())
            {
                string kind = AppTypeField != null ? AppTypeField.GetValue(app)?.ToString() : "HUD";
                if (kind == "MFD") continue;                                     // cockpit screens stay
                if (keepCombat && CombatReadouts.Contains(app.GetType().Name)) continue;
                wanted.Add(app.gameObject);
            }
            var fh = SceneSingleton<FlightHud>.i;
            if (fh != null)
                foreach (var name in FlightHudFields)
                {
                    var v = typeof(FlightHud).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(fh);
                    var go = v is GameObject g ? g : v is Component c ? c.gameObject : null;
                    if (go != null) wanted.Add(go);
                }

            // restore anything no longer wanted (e.g. "keep combat readouts" switched on)
            var drop = new List<GameObject>();
            foreach (var kv in hidden) if (!wanted.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var g in drop) Restore(g);
            foreach (var go in wanted)
            {
                if (hidden.ContainsKey(go)) continue;
                var cg = go.GetComponent<CanvasGroup>();
                var h = new Hidden { group = cg, alpha = cg != null ? cg.alpha : 1f };
                if (cg == null) { h.group = go.AddComponent<CanvasGroup>(); h.added = true; h.group.interactable = false; h.group.blocksRaycasts = false; }
                hidden[go] = h;
            }
        }

        void Restore(GameObject go)
        {
            if (go != null && hidden.TryGetValue(go, out var h) && h.group != null)
            {
                if (h.added) Destroy(h.group);
                else h.group.alpha = h.alpha;
            }
            hidden.Remove(go);
        }

        void RestoreAll()
        {
            foreach (var go in new List<GameObject>(hidden.Keys)) Restore(go);
            hidden.Clear();
        }
    }
}
