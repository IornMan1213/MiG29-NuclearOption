// MiG-29 livery builder for Shockfront's official livery kit (github.com/Shockfront-Studios/Nuclear-Option-Mods).
// Put this file in the kit's Assets/Scripts/Editor folder, then use Nuclear Option > Create MiG-29 Livery Builder.
// It works like the kit's own Livery Builder (same fields, same Build button, same bundle) with the MiG-29 mods' jets to pick
// from. It writes the jet's key into the livery's meta.json, so the game puts the livery on that jet only.
// The same file ships with the MiG-29 Fulcrum and the MiG-29SMT mods: either copy covers both jets.
using System.Linq;
using UnityEditor;
using UnityEngine;

public class MiG29LiveryBuilder : ModBuilder<LiveryData, LiveryMetaData>
{
    // unit name (what the game shows) -> jsonKey
    static readonly (string name, string key)[] Jets =
    {
        ("MiG-29 Fulcrum", "mig29_Fulcrum"),
        ("MiG-29SMT", "mig29smt_Fulcrum"),
    };

    [Header("Livery Settings")]
    [Tooltip("The base colour texture, painted over the MiG template (liveries/templates in the mod's repository)")]
    public Texture2D Texture;
    [Range(0, 1)] public float Glossiness = 0.4f;
    public string DisplayName;

    [StringOptions("", "Boscali", "Primeva")]
    public string Faction;

    [StringOptions("MiG-29 Fulcrum", "MiG-29SMT")]
    public string Aircraft = "MiG-29 Fulcrum";

    public bool UseCustomColors;
    public LiveryData.TextureColor[] Colors = new LiveryData.TextureColor[5];

    protected override string Label => "Skin";

    [MenuItem("Nuclear Option/Create MiG-29 Livery Builder")]
    static void Create()
    {
        Selection.activeObject = ScriptableObjectUtility.CreateAsset<MiG29LiveryBuilder>("MiG-29 Livery Builder", "Assets", true, true);
    }

    public override LiveryData CreateObject()
    {
        var asset = CreateInstance<LiveryData>();
        asset.Texture = Texture;
        asset.Glossiness = Glossiness;
        asset.Colors = UseCustomColors ? Colors : GetColorsFromTexture();
        return asset;
    }

    // the five most common colours, which the game uses for the jet's icon and the parts the texture does not cover
    public LiveryData.TextureColor[] GetColorsFromTexture() =>
        TextureColorCalculator.GetPallet(Texture, 5).Select(x => new LiveryData.TextureColor { Color = x.color, Count = x.count }).ToArray();

    public override LiveryMetaData CreateMeta()
    {
        var jet = Jets.FirstOrDefault(j => j.name == Aircraft);
        return new LiveryMetaData { DisplayName = DisplayName, Faction = Faction, Aircraft = jet.name, AircraftKey = jet.key };
    }

    void OnValidate()
    {
        if (Colors == null || Colors.Length != 5) System.Array.Resize(ref Colors, 5);
        if (!Jets.Any(j => j.name == Aircraft)) Aircraft = Jets[0].name;
    }
}

[CustomEditor(typeof(MiG29LiveryBuilder))]
public class MiG29LiveryBuilderEditor : ModBuilderEditor
{
    protected override void ExtraGui()
    {
        var builder = (MiG29LiveryBuilder)target;
        if (GUILayout.Button("Get Colors from Texture"))
        {
            builder.Colors = builder.GetColorsFromTexture();
            EditorUtility.SetDirty(builder);
        }
        if (builder.Texture == null)
            EditorGUILayout.HelpBox("Pick the texture you painted over the MiG template.", MessageType.Info);
    }
}
