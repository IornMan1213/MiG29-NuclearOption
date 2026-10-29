using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MiG29Instruments
{
    // Optional keys that pop flares or chaff directly, whichever countermeasure is selected: two BepInEx shortcuts (a keyboard key
    // or a HOTAS button), unbound by default, so the game's own Countermeasures and Next Countermeasure buttons work as always.
    // Held, a key sets the game's countermeasure trigger on that station through Aircraft.Countermeasures, the same networked call
    // as the game's button (the server and other players see the cartridges, and they decoy the server's missiles); released, it
    // lets go and leaves the station that was selected before. The game's player controls switch the trigger off on every frame
    // its own button is up, so while one of these keys is held that call is held back for the player's MiG (CmKeysBlock).
    public class CmKeys : MonoBehaviour
    {
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo ManagerField = typeof(Aircraft).GetField("countermeasureManager", BF);
        static readonly FieldInfo TriggerField = typeof(Aircraft).GetField("countermeasureTrigger", BF);
        static readonly FieldInfo StationsField = typeof(CountermeasureManager).GetField("countermeasureStations", BF);

        internal static Aircraft Driving;    // the player's MiG while one of the keys holds the trigger
        internal static bool Releasing;      // our own "off" call goes through
        byte selected;                       // the station the player had chosen

        static int Station(CountermeasureManager cm, string name)
        {
            if (!(StationsField?.GetValue(cm) is IList list)) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i]?.GetType().GetField("displayName")?.GetValue(list[i]) as string == name) return i;
            return -1;
        }

        void Update()
        {
            try
            {
                var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                string want = null;
                if (ac != null && Holes.IsMiG(ac) && ac.radarAlt > 0.2f)   // the game's own rule: none on the ground
                {
                    if (Hotkeys.Held(MiG29InstrumentsPlugin.FlaresKey)) want = "IR Flares";
                    else if (Hotkeys.Held(MiG29InstrumentsPlugin.ChaffKey)) want = "Radar Chaff";
                }
                var cm = want != null ? ManagerField?.GetValue(ac) as CountermeasureManager : null;
                int idx = cm != null ? Station(cm, want) : -1;
                if (idx >= 0)
                {
                    if (Driving != ac) { Release(); selected = cm.activeIndex; Driving = ac; }
                    bool on = TriggerField != null && (bool)TriggerField.GetValue(ac);
                    if (!on || cm.activeIndex != idx) ac.Countermeasures(true, (byte)idx);   // a network call only when it changes
                }
                else Release();
            }
            catch (System.Exception e) { MiG29InstrumentsPlugin.Log.LogWarning($"flare / chaff keys: {e.Message}"); Driving = null; }
        }

        void Release()
        {
            var ac = Driving;
            Driving = null;
            if (ac == null) return;
            Releasing = true;
            try { ac.Countermeasures(false, selected); }   // trigger off, the player's own station selected again
            finally { Releasing = false; }
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Countermeasures))]
    static class CmKeysBlock
    {
        static bool Prefix(Aircraft __instance, bool __0) => !(__instance == CmKeys.Driving && CmKeys.Driving != null && !__0 && !CmKeys.Releasing);
    }
}
