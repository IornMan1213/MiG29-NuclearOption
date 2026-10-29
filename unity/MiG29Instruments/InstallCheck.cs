using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MiG29Instruments
{
    // The install mistakes players report most: two copies of the mod in BepInEx/plugins (an old one left behind, or the same
    // version under GitHub's dotted file name), and a plugin from one release with a .nobp from another. Checked once at start;
    // the problem goes to the log at once and on screen the first time the player flies the MiG (InstrumentDriver).
    static class InstallCheck
    {
        // the names this mod's .nobp has had: "MiG-29 Fulcrum_x.y.z.nobp" (the build), "MiG-29.Fulcrum_x.y.z.nobp" (GitHub's bare
        // download until 0.9.2), "MiG-29_Fulcrum_x.y.z.nobp"
        static readonly Regex Nobp = new Regex(@"^MiG-29[ ._]Fulcrum_(\d+(?:\.\d+){2,3})\.nobp$", RegexOptions.IgnoreCase);

        public static string Problem { get; private set; }

        public static void Run()
        {
            try
            {
                var found = Directory.GetFiles(BepInEx.Paths.PluginPath, "*.nobp", SearchOption.AllDirectories)
                    .Select(f => (file: f, m: Nobp.Match(Path.GetFileName(f)))).Where(x => x.m.Success).ToList();
                if (found.Count > 1)
                    Problem = $"MiG-29: {found.Count} copies of the mod are installed ({string.Join(", ", found.Select(x => x.m.Groups[1].Value))}). " +
                              "Keep only the newest .nobp in BepInEx/plugins.";
                else if (found.Count == 1 && found[0].m.Groups[1].Value != MiG29InstrumentsPlugin.ModVersion)
                    Problem = $"MiG-29: the mod is {found[0].m.Groups[1].Value} but MiG29Instruments.dll is {MiG29InstrumentsPlugin.ModVersion}. " +
                              "Install both from the same release.";
                if (Problem != null)
                {
                    MiG29InstrumentsPlugin.Log.LogWarning(Problem);
                    foreach (var (file, _) in found) MiG29InstrumentsPlugin.Log.LogWarning($"  found: {file}");
                }
                else if (found.Count == 0)
                    MiG29InstrumentsPlugin.Log.LogInfo("install check: no MiG-29 Fulcrum .nobp under BepInEx/plugins (renamed, or not installed)");
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogWarning($"install check: {e.Message}"); }
        }
    }
}
