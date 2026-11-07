using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
// builds the MiG-29's nine shared liveries as Workshop liveries, one bundle each, with the official kit's builder
public static class WorkshopBuild
{
    static readonly (string bundle, string display)[] Liveries =
    {
        ("MiG29FulcrumGrey", "Fulcrum Grey"), ("MiG29DesertTan", "Fulcrum Desert Tan"), ("MiG29DigitalGrey", "Fulcrum Digital Grey"),
        ("MiG29DisplayBlue", "Fulcrum Display Blue"), ("MiG29Special2920", "Fulcrum 29+20 Special"), ("MiG29Tricolour741", "Fulcrum Tricolour Digital 741"),
        ("MiG29OVTSplinter917", "Fulcrum OVT Splinter 917"), ("MiG29White44", "Fulcrum White 44"), ("MiG29LIIGromov84", "Fulcrum LII Gromov 84"),
    };

    public static void Run()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        foreach (var (bundle, display) in Liveries)
        {
            var tex = $"Assets/Workshop/{bundle}.png";
            AssetDatabase.ImportAsset(tex);
            var path = $"Assets/Workshop/{bundle}_Builder.asset";
            AssetDatabase.DeleteAsset(path);
            var b = ScriptableObject.CreateInstance<MiG29LiveryBuilder>();
            AssetDatabase.CreateAsset(b, path);
            b.BundleName = bundle;
            b.CopyToAppData = false;
            b.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            b.Glossiness = 0f;                 // as the mod had them
            b.DisplayName = display;
            b.Faction = "";                    // both sides
            b.Aircraft = "MiG-29 Fulcrum";
            var bb = new BundleBuilder(b.BundleName);
            b.AddToBuilder(bb);
            // the catalog the game reads goes next to the bundle (an empty Remote Catalog path skips it in a fresh project)
            var st = AddressableAssetSettingsDefaultObject.Settings;
            st.RemoteCatalogBuildPath.SetVariableByName(st, "Remote.BuildPath");
            st.RemoteCatalogLoadPath.SetVariableByName(st, "Remote.LoadPath");
            bb.Build(false);
            Debug.Log($"[Workshop] built {bundle} '{display}'");
        }
    }
}
