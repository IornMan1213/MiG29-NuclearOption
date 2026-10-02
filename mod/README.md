# MiG-29 Fulcrum for Nuclear Option (v0.5.1)

A flyable MiG-29 (9.12) with its own flight model, R-27R / R-27T / R-73 / R-60M missiles, the GSh-30-1 cannon, working canopy
and gear doors, its own damage display, four liveries and loading screens.

## Install
1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx) into the Nuclear Option folder.
2. Install [Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases) (2.0.1 or newer) into `BepInEx/plugins`.
3. Put `MiG-29 Fulcrum_0.5.1.nobp` anywhere under `BepInEx/plugins` (e.g. `BepInEx/plugins/MiG-29_Fulcrum/`).
   Remove older `MiG-29 Fulcrum_*.nobp` files.

The MiG-29 appears in medium hangars, shelters and revetments on land bases, and in the Encyclopedia.
Multiplayer: everyone needs the same mod set (Blueprinter checks this).

## Features
- **Flight model** tuned offline against published MiG-29 data with a re-implementation of the game's aerodynamics:
  ~1,530 km/h at sea level, Mach 2.24 at 13 km, ~210 km/h stall, ~28 deg/s instantaneous and ~19-20 deg/s sustained turn,
  9 g limit. RD-33 engines (49 kN dry / ~85 kN wet, no thrust vectoring), 3.7 t internal fuel, 11.5 t empty.
  Flat all-moving stabilators (pitch + differential roll), rudders, ailerons and flaps all move physically.
- **Weapons**
  - R-27R (AA-10 Alamo-A): semi-active radar, two-stage motor, ~50 km. Keep your radar locked on the target until impact.
  - R-27T (AA-10 Alamo-B): infrared, fire-and-forget, ~30 km. The seeker must lock before launch.
  - R-73 (AA-11 Archer): all-aspect infrared, thrust vectoring, 45 deg off-boresight.
  - R-60M (AA-8 Aphid): light, agile infrared missile, ~8 km.
  - GSh-30-1: 30 mm cannon in the left wing root, 150 rounds at ~1,650 rpm. Fire short bursts.
  - Three pylons per wing: inner (R-27R / R-27T / R-73 / R-60M / stock stores), middle (R-73 / R-60M / stock stores),
    outer (R-73 / R-60M).
  - Default loadout: gun, 2x R-27R, 4x R-73. Preset loadouts (also used by AI MiGs): air superiority, dogfight
    (R-27T / R-73 / R-60M), strike (FAB-500 / rockets / R-73).
- **Airframe**: canopy opens from its rear hinge on the ground and ejects with the seat; nose and main gear doors open with
  the gear; parts break off and show scorch damage; hitboxes are the KR-67 part colliders scaled to the MiG.
- **Cockpit**: glass-cockpit panel framed by the MiG-29's own canopy arch, mirrors and sills.
- **Cockpit damage display** and map icon drawn from the MiG itself.
- **Liveries**: Two-Tone Grey, Desert Tan, Digital Grey and Display Blue (both factions can pick any).
- Four loading screens.

## Known limitations
- Cockpit: a modern glass panel (the KR-67's displays and controls) inside the MiG-29's canopy arch, sills and seat.
- Flight model was tuned and verified offline; report handling issues with your controls and speed/altitude.
- Nuclear Option has no supersonic drag rise, so drag is tuned to hit the real top speeds; as a result subsonic climb
  (~210 m/s at sea level) and acceleration are somewhat below the real MiG's.

## Links
- Project page: https://iornman1213.github.io/MiG29-NuclearOption/
- Source, releases and issues: https://github.com/IornMan1213/MiG29-NuclearOption

## Credits
- MiG-29 3D model: "MiG-29 - Fighter Jet - Free" by **bohmerang** (https://sketchfab.com/3d-models/mig-29-fighter-jet-free-0a21787096244220b246ec8747e7b09c),
  licensed CC BY-NC-SA 4.0 (https://creativecommons.org/licenses/by-nc-sa/4.0/). Converted and modified for this mod; this mod file is
  distributed under the same licence: free, non-commercial, credit required.
- Missile and launcher models, flight model, tooling: made for this mod.
- Built on the KR-67 Ifrit's systems from Nuclear Option (Shockfront), loaded with Blueprinter by nikkorap.
- Built with AI assistance (Claude).
