# Making a MiG-29 livery the official way

The MiG-29 Fulcrum and MiG-29SMT mods work with Shockfront's own livery kit and the game's Steam Workshop upload, the same
route as for the stock jets. This folder adds the two things the kit is missing for a modded jet: the MiG in its aircraft list,
and a texture template.

Anyone who subscribes to your livery needs the MiG mod installed to see it, like any livery for a modded jet.

## What you need

- Nuclear Option with the MiG-29 Fulcrum or MiG-29SMT mod installed: a version that lists "livery uploads" in its changelog
  (the mod's plugin gives the jet the Workshop tag the upload asks for).
- [Unity Hub](https://unity.com/download) and Unity 2022.3.62f2 (2022.3.6f1 and 2022.3.62f3 work too).
- Shockfront's livery kit: [Shockfront-Studios/Nuclear-Option-Mods](https://github.com/Shockfront-Studios/Nuclear-Option-Mods)
  (Code > Download ZIP).
- An image editor, or Blender / Substance Painter if you paint on the model.

## Steps

1. **Add the MiG builder to the kit.** Copy `MiG29LiveryBuilder.cs` from this folder into the kit's `Assets/Scripts/Editor`
   folder. The same file covers both jets.
2. **Paint the texture.** Start from `templates/` (one set for both jets, they share the texture):
   `MiG-29_basecolor.png` is the stock grey with its panel lines, stencils and insignia, to paint over;
   `MiG-29_uv_layout.png` is the outline of every exterior panel, to keep as a guide layer on top and hide before you export.
   The right-hand part of the texture is the cockpit and the inside of the jet: leave it as it is. Only the base colour goes
   into the game. Keep the texture 2048 x 2048.
3. **Open the kit in Unity Hub** and put your texture anywhere under `Assets` (for example `Assets/Texture`). Leave its import
   settings at the defaults: other compression makes players' games hitch when the livery downloads.
4. **Make a builder.** Nuclear Option > Create MiG-29 Livery Builder, and save it under `Assets`.
5. **Fill it in** (in the Inspector): Bundle Name (one word, it becomes the folder name), Texture, Glossiness, Display Name (what
   players see in the livery list), Faction (leave empty for both), Aircraft (MiG-29 Fulcrum or MiG-29SMT). Keep Copy To App Data
   ticked.
6. **Build.** Press Build. The livery goes to `%USERPROFILE%\AppData\LocalLow\Shockfront\NuclearOption\Skins\<Bundle Name>`.
7. **Check it in game.** Start the game, pick the MiG and choose your livery in the livery list.
8. **Upload.** Main Menu > Workshop > Upload > Aircraft Livery, pick your bundle, add a description and a thumbnail. The game
   tags the item "Aircraft Livery" and with the jet's name, so players find it under that tag.

## If something goes wrong

- **"Aircraft 'MiG-29 Fulcrum' does not have a WorkshopTag set in its definition"** when uploading: the installed MiG mod is
  older than the one with livery uploads, or its `MiG29Instruments.dll` is missing. Update the mod (both files from one release).
- **The livery does not show on the MiG:** check `BepInEx/LogOutput.log` and the game's `Player.log` for "Aircraft did not match
  meta data". The builder writes the jet's key, so this means the livery was built with the kit's own Livery Builder for another
  aircraft: rebuild it with the MiG-29 builder.
- **Unity cannot find `ModBuilder` or `LiveryData`:** `MiG29LiveryBuilder.cs` has to sit inside the kit project, beside the kit's
  own `LiveryBuilder.cs`.

The mod's own liveries are made another way (`blender/livery_edit.py` and the `tools/livery_*.py` scripts): both routes stay.
