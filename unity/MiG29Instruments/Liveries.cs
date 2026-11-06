using HarmonyLib;
using NuclearOption.ModScripts.Impl;

namespace MiG29Instruments
{
    // Liveries made with Shockfront's livery kit (github.com/Shockfront-Studios/Nuclear-Option-Mods, liveries/README.md) and
    // uploaded in game (Workshop > Upload > Aircraft Livery). The game loads a livery for an aircraft whose jsonKey matches the
    // livery's AircraftKey, or whose unit name matches its Aircraft, so that side needs nothing. The upload does: it tags the
    // Workshop item with the aircraft's UnitDefinition.WorkshopTag and refuses an aircraft without one ("does not have a
    // WorkshopTag set in its definition"). Blueprinter's editor predates the field, so the .nobp cannot carry it: the tag is the
    // jet's name, set here just before the game looks for it.
    [HarmonyPatch(typeof(AircraftLiveryMod), "TryGetAircraftTag")]
    static class LiveryWorkshopTag
    {
        static readonly string[] Keys = { "mig29_Fulcrum" };

        static void Prefix()
        {
            var aircraft = Encyclopedia.i?.aircraft;
            if (aircraft == null) return;
            foreach (var def in aircraft)
                if (def != null && string.IsNullOrEmpty(def.WorkshopTag) && System.Array.IndexOf(Keys, def.jsonKey) >= 0)
                {
                    def.WorkshopTag = def.unitName;
                    MiG29InstrumentsPlugin.Log.LogInfo($"livery upload: Workshop tag '{def.WorkshopTag}' for {def.jsonKey}");
                }
        }
    }
}
