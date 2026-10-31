using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace MiG29Instruments
{
    // Blueprinter 2.1.1's state API (Blueprinter.Api: CurrentState and StateChanged, through NotStarted, LoadingBundles,
    // ApplyingPatches, ApplyingOps to Ready or Failed), found by reflection so the plugin still loads beside an older Blueprinter
    // (Known stays false). Once Blueprinter is Ready the mod's jets must be in the encyclopedia: the log lists them, or says plainly
    // that the .nobp is missing or did not load (before, nothing said so).
    static class BlueprinterState
    {
        public static bool Known { get; private set; }
        public static string Current { get; private set; } = "";
        static string mod; static (string key, string name)[] jets; static bool needAll;

        public static void Hook(string modName, (string key, string name)[] modJets, bool allNeeded)
        {
            mod = modName; jets = modJets; needAll = allNeeded;
            try
            {
                var api = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Blueprinter.Api", false)).FirstOrDefault(t => t != null);
                var prop = api?.GetProperty("CurrentState", BindingFlags.Public | BindingFlags.Static);
                var ev = api?.GetEvent("StateChanged", BindingFlags.Public | BindingFlags.Static);
                if (prop == null || ev == null) { MiG29InstrumentsPlugin.Log.LogInfo("Blueprinter has no state API (older than 2.1.1)"); return; }
                var s = Expression.Parameter(ev.EventHandlerType.GetGenericArguments()[0], "s");
                var body = Expression.Call(typeof(BlueprinterState).GetMethod(nameof(Set), BindingFlags.NonPublic | BindingFlags.Static),
                    Expression.Call(Expression.Convert(s, typeof(object)), typeof(object).GetMethod("ToString", Type.EmptyTypes)));
                ev.AddEventHandler(null, Expression.Lambda(ev.EventHandlerType, body, s).Compile());
                Known = true;
                Set(prop.GetValue(null)?.ToString() ?? "");
            }
            catch (Exception e) { Known = false; MiG29InstrumentsPlugin.Log.LogWarning($"Blueprinter state API not used: {e.Message}"); }
        }

        static void Set(string state)
        {
            if (state == Current) return;
            Current = state;
            try
            {
                if (state == "Failed")
                    MiG29InstrumentsPlugin.Log.LogError($"Blueprinter failed to load the mods (its own error popup and Player.log say why): the {mod} will be missing.");
                if (state != "Ready") return;
                var lookup = Encyclopedia.Lookup;
                var found = jets.Where(j => lookup != null && lookup.ContainsKey(j.key)).ToArray();
                if (found.Length == 0 || (needAll && found.Length < jets.Length))
                    MiG29InstrumentsPlugin.Log.LogError($"Blueprinter is ready but the {mod} is not in the game ({found.Length} of {jets.Length} jets): " +
                        "its .nobp is missing from BepInEx/plugins or did not load (an older or a second copy, see the lines above).");
                else MiG29InstrumentsPlugin.Log.LogInfo($"{mod} jets: {string.Join(", ", found.Select(j => j.name))}");
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError($"Blueprinter state: {e}"); }
        }
    }
}
