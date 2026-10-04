# MiG-29 Fulcrum for Nuclear Option (v0.8.1)

A flyable MiG-29 (9.12) with its own flight model, R-27R / R-27T / R-73 / R-60M missiles, the GSh-30-1 cannon, droppable fuel
tanks, flares and chaff, working canopy and gear doors, its own damage display, four liveries and loading screens.

## Install
1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx) into the Nuclear Option folder.
2. Install [Blueprinter](https://github.com/nikkorap/NOBlueprinter-Releases) (2.0.1 or newer) into `BepInEx/plugins`.
3. Put `MiG-29 Fulcrum_0.8.1.nobp` and `MiG29Instruments.dll` (both in `MiG-29-Fulcrum_0.8.1.zip`) in
   `BepInEx/plugins/MiG-29_Fulcrum/`, or install the mod with a NOMNOM mod manager such as NOMM.
   Remove older `MiG-29 Fulcrum_*.nobp` files. The DLL makes the cockpit instruments work and gives the drop tanks their
   fuel; without it the MiG still flies, with the gauges parked and the drop tanks empty.

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
  - Three pylons per wing: inner (R-27R / R-27T / R-73 / R-60M / PTB-1150 tank / stock stores), middle (R-73 / R-60M /
    stock stores), outer (R-73 / R-60M); and a centreline station under the belly for the PTB-1500 tank.
  - Default loadout: gun, 2x R-27R, 4x R-73. Preset loadouts (also used by AI MiGs): air superiority, long-range CAP
    (PTB-1500 / R-27R / R-73), dogfight (R-27T / R-73 / R-60M), strike (FAB-500 / rockets / R-73), ferry (three tanks / R-73).
- **Drop tanks**: PTB-1500 centreline tank (1,500 L, ~1,180 kg of fuel) and PTB-1150 wing tanks (1,150 L, ~905 kg each).
  Their fuel is used first: the internal tanks stay full until the drop tanks run dry, and the aircraft's weight follows
  the fuel. Drop them by selecting them and firing (one per press), or all at once with **Ctrl+J** (blocked with the gear
  down). The **ПТБ** caution light comes on when the tanks you carry are empty. AI MiGs drop theirs when empty.
- **Countermeasures** from the MiG-29's own BVP-30-26M dispensers on top of the tail booms, firing upward: 30 IR flares and
  30 radar chaff cartridges (60 in all, as on the real jet), plus the Radar ECM jammer. Cycle countermeasures to choose.
  Chaff decoys radar-guided missiles (active and semi-active). It works best when the missile sees you side-on (beam it) and
  is close, so pop it as you turn across the missile's path.
- **Airframe**: canopy opens from its rear hinge on the ground and ejects with the seat; nose and main gear doors open with
  the gear; parts break off and show scorch damage; hitboxes are the KR-67 part colliders scaled to the MiG.
- **Cockpit** modelled from scratch: turquoise MiG-29 instrument panel with Russian-marked gauges, HUD, side consoles with real
  3D toggles and knobs, circuit-breaker panels, twin throttles, centre stick, a K-36DM seat with harness, the windscreen bow with
  three rear-view mirrors, and the equipment bay behind the seat. The tactical display is built into the panel as the radar screen.
- **Every instrument works**: 20 needles, a rolling attitude ball, a turning compass card, a 24-light caution panel, the SPO-15
  radar warning receiver and gear lights, all driven by the aircraft's real state (see below).
- **Cockpit damage display** and map icon drawn from the MiG itself.
- **Liveries**: Two-Tone Grey, Desert Tan, Digital Grey and Display Blue (both factions can pick any).
- Four loading screens.

## Cockpit instruments
Every dial and light works (with `MiG29Instruments.dll` installed) and reads the aircraft's real state:

| Instrument | Where | What it shows |
|---|---|---|
| Airspeed / Mach (КМ/Ч, M) | centre, top left | indicated airspeed, 0-1,600 km/h (white); Mach number on the inner yellow scale |
| Attitude indicator (ball) | centre, top | pitch and bank on a real rolling ball; the orange symbol is your aircraft |
| AoA / G (α, n) | centre, top right | angle of attack (yellow, red above 25 deg) and load factor in g (white) |
| Altimeter (ВД-30) | centre, middle left | barometric altitude: long hand hundreds of metres, short hand thousands |
| HSI compass | centre, bottom | heading on a turning compass card |
| Vertical speed (М/С) | centre, middle right | climb / descent rate, up to 300 m/s |
| Radar altimeter (РВ) | centre, bottom left | height above the ground, 0-1,500 m |
| Clock (АЧС-1) | centre, bottom right | mission time of day |
| RWR (SPO-15) | left panel, top | radar warning: sector lamps show where a radar is painting you from, the bar shows its strength, the letter its type (П fighter, З long-range SAM, Н short-range SAM, О ship); sectors flash for an incoming missile |
| Gear lights (ШАССИ) | left panel | green = down and locked, red = moving |
| Cabin altitude, oxygen | left panel, bottom | cabin pressure altitude; oxygen pressure, falling slowly in flight |
| Tachometers (ОБ/МИН %) | right panel | left (white) and right (yellow) engine RPM, ~70 % at idle, 100 % at full power |
| Exhaust temperature (t°C) | right panel | left / right engine EGT; rises with power, +60 deg C in afterburner, pegs in a fire |
| Fuel (ТОПЛИВО) | right panel, bottom | fuel remaining, kg |
| Hydraulics (ГИДРО) | right panel, bottom | hydraulic pressure from the engine pumps; bleeds down with both engines out |
| Radar screen | right panel, top | the game's tactical display |

Caution panel (right, bottom): ПОЖАР Л/П engine fire, ОПАСН ВЫС low altitude with gear up and descending, ВЫХОД α angle of attack
over 24 deg, ПЕРЕГРУЗ over-g, РАКЕТА missile inbound, ОСТАТОК fuel below 25 %, РЕЗЕРВ fuel below 10 %, ГЕН Л/П generator off (engine
below 40 %), ГИДРО low hydraulics, ЗАХВАТ radar lock on you (a SAM, ship or enemy fighter targeting you), НАСОС fuel pump (an engine off), МАСЛО / ВИБРАЦ engine damage,
ФОНАРЬ canopy open, ШАССИ gear moving or still up when low and slow, СКОРОСТЬ overspeed, ПТБ drop tanks carried but empty,
ЩИТКИ flaps down, ТОРМОЗ brakes on, ПЗУ intake guards closed (on the ground), ФОРСАЖ afterburner, ПРОВЕРЬ master caution.
The rudder pedals move with your rudder input and the gear lever with the gear. At night the panel and consoles are lit
(brightness: `BepInEx/config/iornman.mig29.instruments.cfg`, `Night panel lighting`, 0 = off; the same file has the
drop-tank `Jettison key` and `Drop empty tanks automatically`).

## Troubleshooting
- **The MiG-29 isn't in the aircraft list.** It spawns from medium hangars, shelters and revetments on land bases (not carriers).
  Check that `BepInEx/LogOutput.log` has the line `Loaded mig29 x.y.z`, and that only one `MiG-29 Fulcrum_*.nobp` is installed.
- **Can't join a multiplayer server.** Every player needs the same mod set and the same MiG-29 version; Blueprinter compares
  them when you join.
- **R-27R misses.** It's semi-active: keep the target locked on your radar until impact. For fire-and-forget at medium range,
  use the R-27T (infrared; lock its seeker before launch).
- **The gun runs dry fast.** The GSh-30-1 has 150 rounds, about five and a half seconds of fire. Short bursts.
- **Drop tanks don't feed fuel / Ctrl+J does nothing.** Both come from `MiG29Instruments.dll`; check it is installed (below).
  Tanks can't be released with the gear down.
- **Gauges don't move.** `MiG29Instruments.dll` must be in `BepInEx/plugins` (next to the .nobp is fine);
  `BepInEx/LogOutput.log` then shows `MiG-29 instruments on MiG29`.
- **Something else.** [Open an issue](https://github.com/IornMan1213/MiG29-NuclearOption/issues/new/choose) with your mod version
  and the `Exception` lines from `%USERPROFILE%\AppData\LocalLow\Shockfront\NuclearOption\Player.log`.

## Known limitations
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
- Missile, launcher and drop-tank models, flight model, tooling: made for this mod.
- Built on the KR-67 Ifrit's systems from Nuclear Option (Shockfront), loaded with Blueprinter by nikkorap.
- Built with AI assistance (Claude).
