# MODLOG: MiG-29 Fulcrum for Nuclear Option

Goal: MiG-29 as a flyable aircraft (spawnable from hangars), single-player/offline first.

## Environment (2026-10-01)
- Game: `C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option` (Steam 2168680), Unity 2022.3.62f2 Mono, no anti-cheat.
- BepInEx 5.4.23.x installed; Blueprinter 2.0.1 (`BepInEx/plugins/Blueprinter_2.0.1.dll`) + many aircraft mods.
- Log: `BepInEx/LogOutput.log`. Saves: `%USERPROFILE%\AppData\LocalLow\Shockfront\NuclearOption`.
- Unity 2022.3.62f2 at `C:\Program Files\Unity\Hub\Editor\2022.3.62f2`. Blender 5.2 at `C:\Program Files\Blender Foundation\Blender 5.2`.
- Blueprinter Editor project (already set up, `_donotship` built): `<Blueprinter-Editor project>`.
- User's notes: `Desktop/Blueprinter_Modding_Guide_v2.md`.

## Route
Blueprinter `.nobp` (data-only AssetBundle) built from the Editor project. No C# needed.
- Base: KR-67 Ifrit (`Multirole1`, twin engine, twin nozzles, stabilators, wing pylons) → closest MiG-29 analogue.
- Clone prefab + AircraftDefinition + AircraftParameters into `Assets/Blueprinter/Mods/mig29`.
- New jsonKey `mig29_Fulcrum` (unique), unitName "MiG-29 Fulcrum", code "MiG-29".
- Hide KR-67 exterior renderers, parent Blender-made MiG-29 parts under the KR-67 transforms (moving surfaces under `*_visible` transforms so they animate).
- Encyclopedia registration is automatic (OpBuilder emits OpAddToEncyclopedia for definitions in the mod folder).
- Hangars: `OpAddAircraftToHangars` using the same hangars as Multirole1.
- Build headless: Unity `-batchmode -executeMethod` → `Blueprinter.ModBuilder.Build(modName, displayName, version, outFolder)`.

## Base-game names
| In-game | jsonKey / prefab |
|---|---|
| FS-12 Revoker | Fighter1 (single engine, canards) |
| KR-67 Ifrit | Multirole1 (twin engine) ← base |
| FS-20 Vortex | SmallFighter1 |

## Model
User-supplied Sketchfab MiG-29 (see Log). Not using DCS assets (not redistributable).

## Log
### 2026-10-01: model + builder ready
- Model: user-supplied Sketchfab "mig-29-fighter-jet-free.zip" (H:\). `.blend` + 4K PBR. ~10k verts airframe. Units ≈ dm; nose +X, up +Z.
  Scale 17.32/176.7176. **License/attribution to confirm before publishing** (Sketchfab free download; usually CC-BY).
- `blender/export_mig29.py` → `out/mig29_mesh.json` (Unity coords, metres; tris reversed for handedness). Splits 8 control-surface islands
  (aileron/flap/stab/rudder L/R) with hinge pivots + axes. Textures packed to 2K: metallic RGB=metal, A=1-roughness (game convention).
- Staged in `<BlueprinterProject>/MiG29Source/`. Builder: `Assets/Editor/MiG29Tools/MiG29Builder.cs` (menu `MiG29/1…`, `MiG29/2…`, batch `MiG29Tools.MiG29Builder.BuildAll`).
- Game facts (decompiled to a local folder, not shipped):
  - `ControlSurface` rotates `visibleMesh.localRotation = resting * AngleAxis(angle, Vector3.right)`; +angle = trailing edge up.
    Conventions: pitch>0 → TE down on stabs (KR-67 elevator pitchRange −35), roll>0 = right, yaw>0 = right. Lift uses KR-67 hinge transforms → leave them; MiG surfaces get *extra* visual-only ControlSurface (maxSplit 0).
  - `Aircraft.SetCockpitRenderers` toggles renderer.enabled → hide KR-67 exterior by nulling MeshFilter.sharedMesh instead.
  - Livery only touches `UnitPart.damageMaterial.renderers` → MiG renderers unaffected.
  - `JetNozzle` thrust = `AddForceAtPosition(thrustTransform)` → moving nozzles 0.9 m lower adds pitch moment (watch in flight test).
- Alignment: ModelOffset (0,−0.44,−2.655) → MiG spans z −7.98..9.3 ≈ KR-67 −7.93..9.4; MiG main wheels on KR-67 main gear (CoM stays ahead).
  Nose gear moved −3.5 m, main gear track 4.23→3.22 m, cockpit interior shifted (0,−0.44,−2.23), pylons → MiG rails (±2.4, ±3.1).
- Internal bays (Forward/Rear/Side) emptied; hangars: hangar_med, shelter1, revetment1.
- Unity licensing: launching Unity.exe from the agent shell fails `Code 10 ... Licensing Client signature` → "No valid license" (even GUI). Needs user-launched editor.
### 2026-10-01: automated build + first in-game sighting
- **Headless build works**: `tools/build_mig29.ps1` (PowerShell). Keys:
  1. Launch Unity via PowerShell `Start-Process` (from Git Bash the licensing client signature check fails, Code 10). Hub must be running.
  2. Blueprinter keeps the *game's* Assembly-CSharp.dll read-only in Library/ScriptAssemblies → batchmode recompile fails ("target path ... read-only").
     Clear RO, run a compile-only pass, then copy `Packages/nuclearoption/Assembly-CSharp*.dll` back + RO (Blueprinter's GameAssemblySync uses delayCall, never runs in batchmode).
  3. Build pass `-executeMethod MiG29Tools.MiG29Builder.BuildAll`. ModBuilder rebuilds the 1 GB `_donotship` bundle each time (~10 min).
- Addressables in a mod must resolve inside the mod → cloned KR-67 liveries failed the build. Fixed: own `MiG29_livery` LiveryData, one livery per faction.
- Installed `BepInEx/plugins/MiG-29_Fulcrum/`. Log: `[BundleRegistry] Loaded mig29 0.1.0`, no MiG warnings, Blueprinter done in 79.6 s.
- **Main menu background spawned an AI MiG-29** → model, game shader, textures, pylons, nozzles, deflecting stabs confirmed (shot2.png).
- Bug: stowed KR-67 main wheel pokes out under the intake (only 11% inside skin). `LandingGear` folds gearHinge by `foldDegrees` about local X
  and lerps localPosition by `hingeFoldMotion` → builder now sets hingeFoldMotion so wheels stow at MiG-frame (±0.5,0,2.9) (89% hidden) / nose (0,0.45,3.2) (100%).
  Positions from `blender/gearbay.py` (BVH nearest-normal inside test). v0.1.1.
- Screenshots: `um win shot` failed (ffmpeg 8.0.1 essentials lacks gfxcapture; `um win setup` false-positive check). `tools/shot.ps1` (CopyFromScreen) works when the game is visible.

## Phase 2 (2026-10-01): refined mod: MiG-29 flight model + R-27R / R-73
User asked for: custom flight model closer to the real MiG-29, and R-27 + "R70" (taken as R-73 Archer; R-27R Alamo-A radar version).
Game facts:
- Aero (NuclearOption.Jobs/AeroJob_Math): per AeroPart, lift = CL(α)·q·S·eff along -(v × liftNormal.right); drag = CD(α)·q·S·eff +
  0.5·q·0.5·(dragArea + (dragArea+0.1S)(1-eff)); transonic bump ×(1+0.15·k³) within M0.8–1.2. Airfoil curves live in AircraftParameters.airfoils.
- Missile: motors[] (thrust, burnTime, fuelMass, thrustVectoring), mass, finArea, gLimit, maxTurnRate, torque, liftCurve, dragCurve, supersonicDrag.
- SARHSeeker uses missile.owner.radar (launching aircraft) → air-launched SARH works. Only stock SARH users: SAM_Radar1/2.
- Stock AAMs: AAM1 MMR-S3 (IR, TVC 20, 80 g) → R-73 base. AAM3 IRM-S2 (IR). AAM2 Scythe / AAM4 Scimitar (ARH).
Plan: offline Python sim of the game's aero model → tune parts/airfoils/engine to MiG-29 data → builder; Blender-from-scratch missile models;
missile prefabs cloned in the builder (R-73 from AAM1, R-27R from AAM2 with SARHSeeker copied from SAM_Radar1); mounts on MiG pylons.

### 2026-10-01: v0.3.0 / v0.4.0 (flight model, weapons, polish). No in-game testing (user request)
- v0.3.0: flight model (`MiG29FlightModel.cs`, `tools/fm_config.json` → `fm_export.py`), R-27R/R-73 (`MiG29Weapons.cs`,
  models from `tools/missile_gen.py`), 3 pylons/wing (outer pair new: HardpointIndex 8/9, loadout slot inserted).
  Gotcha: copying SARHSeeker from SAM_Radar1 keeps references into the SAM prefab → Blueprinter "Unsupported game subasset";
  null any reference whose asset path is the donor prefab.
- v0.4.0 polish (`MiG29Polish.cs`):
  - Canopy: MiG glass split at windscreen arch (MiG z 7.15); rear hinge pivot (0,0.80,5.66) under canopyHinge (no longer animated),
    canopyFrame_R (ejection transform) re-parented under it, Canopy.canopyHinges[0] → pivot, hingeAngle −42; glass added to glassRenderers.
  - Gear doors: closed-door mesh split (nose L/R, main L/R), pivots on hinges, appended to LandingGear.gearDoors (local Euler lerp).
  - StatusDisplay: PartStatusDisplay matches part by GameObject NAME → rasterised per-part MiG silhouettes (256) + outline (1024),
    every part image full-frame; "Multirole1" image renamed to root "MiG29". Map icon 128 px silhouette.
  - Liveries: Two-Tone Grey + Desert Tan (`tools/livery_desert.py`, UV-masked recolour), both per faction; PALA defaults to desert.
  - Loading screens: Blender beauty renders (`blender/beauty.py`) composited over blurred game terrain; OpAddLoadingScreens.
- Verified offline: re-dumped built prefab (MiG29AeroDump) → 0 mismatches vs fm_config; sim on built values identical.
  Preview poses (MiG29Preview: flight/ground/parts) confirm gear stow, doors, canopy.
- Build: `tools/build_mig29.ps1` (runs generators, compile pass, DLL restore, build). Output `MiG29Build/MiG-29 Fulcrum_0.4.0.nobp` (19 MB) + README.md.

### 2026-10-01: v0.4.1: user report "explodes on spawn; missiles sit just under the pylon"
- Explosion cause (most likely): v0.2-0.4 replaced part colliders with MiG-shaped convex hulls (+0.3 m cubes for parts without MiG
  skin). In complex physics (local sim) every AeroPart gets its own Rigidbody + FixedJoints; only jointed pairs ignore collisions
  (FixedJoint.enableCollision=false), there is no IgnoreCollision for the rest and layer 14 collides with itself. Overlapping hulls of
  non-jointed parts + tiny-inertia cubes on 500 kg parts → depenetration impulses → joints break. (Convex hull >255 polys warnings too.)
  Fix: KR-67 colliders (designed disjoint) scaled x*0.8 in aircraft space (linear → convex & disjoint preserved; 7.1 m → 5.7 m span).
  Lift points re-placed via centerOfLift (target = old MiG-hull lift point) + 0.31 m forward to restore static margin 0.56 m (CG moved
  z −1.10 → −0.76 with KR-67 colliders). Built prefab re-dumped: 0 mismatches.
- PylonIndicator.UpdateImages IndexOutOfRange: pylonElementSets indexed by Hardpoint.HardpointIndex (KR-67 max 8). Outer pylons now 8/0
  (freed side-bay indices) instead of 8/9.
- Missiles: stock mount children carry localScale for stock meshes (aam1 1.49/1.11/1.25, pylon 0.67/0.9/0.8) → reset to 1.
  missile_gen.py: taper normal hint sign flipped (noses/boattails culled); rail wedge-nose hint on wrong side of the face.
  Armed preview (MiG29Preview "armed") confirms shoe → rail → missile contact on all pylons.

## v0.4.2 (2026-10-01)
- Project moved to a git repo (GitHub: IornMan1213/MiG29-NuclearOption) with a GitHub Pages site in `docs/`. The repo is now the
  source of truth: `tools/build_mig29.ps1` copies `unity/MiG29Tools/*.cs` into the Blueprinter project before building. Paths come
  from `-Project` / `BLUEPRINTER_PROJECT`. `tools/prepare_textures.py` scripts the texture packing that was done by hand.
- Model licence confirmed: "MiG-29 - Fighter Jet - Free" by bohmerang, CC BY-NC-SA 4.0 (credits filled in everywhere).
- v0.4.1 in-game check (user session): the MiG spawned and sat for 48 s with no errors or explosion. The "Invalid hardpoint index 5
  on cockpit" warning in the log comes from the DelamereAerospace weapons pack, not this mod.
- Damage display: the top-down raster now interpolates height per pixel (was the per-triangle mean), and part borders are smoothed by
  a box vote (integral image per part, radius 9 then 5) instead of a 5x5 majority filter. The wing-root zigzags were the airframe
  split following the model's fan triangulation, not depth noise. Per-part images are downsampled from the same id map with
  coverage as alpha, so they line up with the outline exactly.
- `blender/showcase.py`: armed renders (missiles from missiles.json on the real pylon positions) in both liveries for the site.

## v0.5.0 (2026-10-01)
- GSh-30-1: WeaponInfo cloned from Gun27mm_Autocannon (870 m/s, 600 pierce, 0.06 blast, 0.83 kg/round, 1.8 km), gun prefab from
  gun_27mm_internal (1650 rpm, 150 rds, recoil 420), mount `mig29_gsh301_internal`. The "Internal Cannon" set (renamed GSh-30-1)
  keeps its hardpoint transform `gun` (fuselage_F); moved so the prefab muzzle (+3 m, +0.038 m) sits at MiG (-1.10, 0.11, 5.0),
  inside the left LERX root (ray cast: LERX skin y -0.02..0.24 there).
- R-27T: AAM2 clone + IRSeeker copied from AAM1 (IRSeeker has no object references, safe to copy), info cloned from AAM1_info,
  model = R-27 airframe with a short IR section and glass dome. R-60M: AAM1 clone, no TVC, 44 kg, APU-60 rail. Pylon options:
  inner R-27R/R-27T/R-73/R-60M, middle R-73/R-60M, outer R-73/R-60M. armedB preview confirms rail contact.
- Loadouts: `loadouts[1]` is the loadout screen default / fallback (Aircraft, LoadoutSelector, WeaponManager) -> gun + 2x R-27R
  + 4x R-73. `StandardLoadouts` (empty before, so AI MiGs spawned with only the gun) are picked at random for AI spawns
  (FactionHQ) and used as the mission-editor default: air superiority, dogfight, strike.
- Livery 3 "Fulcrum Digital Grey" (`tools/livery_digital.py`): 3-tone 16-texel block camo on exterior paint texels; shading is
  brightness relative to a blurred copy so panel lines stay but the old two-tone shapes don't ghost through. The build now
  regenerates the desert and digital textures.
- MiG cockpit interior (`MiG29Polish.SetupMiGCockpit`): the Sketchfab `MiG-29-cockpit` object is a full interior (gauges, HUD
  frame, windscreen arch, mirrors, seat). It no longer goes in `exteriorRenderers`, so it shows in the cockpit view too; KR-67
  canopy_*_int, canopyFrame_*_int, cockpit_int(+_simple), joystick and throttle meshes are nulled. The KR-67 `tacScreen` (the
  Cockpit component's tacScreenRender; main display + 3 sub-displays in one game mesh, can't be split) is scaled to 0.33 m and
  placed on the lower-centre panel at (0.02, 0.255, 4.25), in front of the gauge panel (face at z 4.30-4.45; the 4.50-4.55
  faces are the HUD housing), with a dark atlas-grey console box behind it and the warning-light strip along its top.
  Checked with `MiG29Preview.RenderCockpit` (pilot's-eye renders from cockpitViewPoint with exterior + pilot renderers off).
  Needs an in-game look: lighting, tac-screen readability, head movement clipping.
- Not done: in-game spawn check. Windows input idle stayed under 2 minutes (someone at the PC), so the game was not driven.

### Flight-model notes (offline, 2026-10-01)
- Climb: sim gives Ps 207 m/s at sea level (published 260-330). The game has no Mach drag rise (only a 15 % transonic bump), so
  matching the SL and 13 km top speeds needs drag area ~1.5 m^2 at all speeds (real subsonic ~0.7). Kept: top speed and turn
  matter more in play; documented as a known limitation.
- FBW candidates, not applied (need flight testing): flyByWire.alphaLimiter 27 -> 26 (MiG-29 AoA limiter), cornerSpeed 160 ->
  ~175 m/s, maxRollAngularVel 10 -> ~7 (effective cap 0.5x = ~200 deg/s at low q).

### Exterior clean-up (2026-10-01)
- `canopyFrame_F_int_simple` / `canopyFrame_R_int_simple` (KR-67 simplified interior frames) draw from outside and poked through
  the MiG canopy: now hidden with the other KR-67 interior meshes. The KR-67 ejection-seat headbox (top y 0.95) poked through the
  lower MiG canopy (0.91): seat dropped 0.09 m (visual only; the pilot and the cockpitViewPoint = helmetCamPoint stay).
- Nose wheel (KR-67, 0.66 m) stowed at MiG (0, 0.45, 3.2) stuck out of the thin centre fuselage. Ray scan of the skin -> stowed at
  (0, 0.30, 3.5): wheel top 0.63 vs skin 0.66 at x 0.32; strut now ~5-15 cm below the belly between the nose-gear doors.
- Main gear: foldDegrees -100 -> -88 (MainGearExtraFold 12, env MIG29_MAINFOLD to try others) lifts the KR-67 strut tips from
  y -1.21 to -0.90 (prefab); only thin slivers show in the centre tunnel from directly below. The main wheels (0.85 m) already
  touch the nacelle top skin, so they can't go higher.
- The main wheels live inside the intake ducts (the only volume big enough) and showed from dead ahead: black atlas panels
  0.5 m behind each intake lip (`AddIntakeBlockers`; mouth x 0.45..0.95, y -0.85..-0.32, lip z 4.1 by ray scan) read as the
  dark duct. Not damage renderers (liveries retexture those).
- Dev: `MIG29_IDCOLORS=1` with MiG29Preview.RenderAll colours each leftover KR-67 renderer uniquely (legend in the log);
  `MiG29Inspect.RunVisible` lists every KR-67 renderer that still draws.

### Livery 4: Fulcrum Display Blue (2026-10-01)
- `tools/livery_display.py` designs on the airframe instead of in texture space: each exterior triangle of mig29_mesh.json is
  rasterised into the 2K texture with interpolated MiG-frame position + normal. Upper/lower split by normal (crisp blue/white line
  along the sides), red/white chevron a fixed distance behind the planform leading edge (two straight lines, LERX and wing,
  fitted to the forward-most vertex per 5 cm span bin), white/red bands on the fins by height, white radome tip. Only paint
  texels change; shading relative to a blurred copy. Islands grown 4 texels for mip seams. ~10 s.
- Landing light: `gearLight_F` (+ its spotlight/particle child) hung on the KR-67 front gear door, 3.6 m ahead of the moved nose
  gear. Re-parented to `gear_F_sprung`, 0.72 m above / 0.42 m ahead of the nose wheel; hidden when stowed (checked with
  MIG29_IDCOLORS renders). Nav lights, wingtip vortices, wing vapour and heat haze were already at MiG positions.

## v0.5.1 (2026-10-02)
- User session log: 40,000 x NullReferenceException in SARHSeeker.Seek / Initialize after two R-27R launches. Cause: when the
  seeker is copied from a donor prefab, references into the donor are nulled, including MissileSeeker.missile (the seeker's own
  missile). Now set to this missile's Missile component; Missile.seekerMode copied from the seeker donor. Same fix for R-27T.
- Cockpit rework after user feedback ("closer to the Aryx FS-41/F-22 interior"): MiG29_cockpit (full) is an exterior renderer
  again; a trimmed copy `MiG29_cockpit_shell` (TrimmedCockpit: panel/glareshield z>6.8 |x|<0.29 y<1.05, HUD |x|<0.20 y<1.19,
  consoles/floor y<0.82 except the seat, plus small leftover islands near the panel) is appended to Aircraft.cockpitRenderers.
  The KR-67 cockpit_int, tacScreen, warning lights, joystick and throttle stay in place; only KR-67 canopy frames and
  cockpit_int_simple are hidden. The retrofit console is gone.

## v0.5.2 (2026-10-02): thrust line
- User: "the plane just flips backwards, it won't fly at all; same over 70 % throttle". Aero dump identical to v0.5.0 (the only
  diff was my dump's renderer-based side/top area). Cause: JetNozzle applies thrust at thrustTransform; at the MiG nozzle height
  (y -0.73 prefab) vs dry CG y 0.00 that is 170 kN x 0.73 m = 124 kNm nose-up at full AB, against 16 t x 0.67 m (CG to main
  wheels) = 105 kNm on the ground. Dry thrust (99 kN, 72 kNm) holds, AB (>~70 % throttle) flips it. The v0.5.0 flight at 705 kt
  worked because the stabilators had authority at that q.
- Fix: thrustTransform_L/R moved to y = ThrustLineY (0.0, CG height), their heat-haze children re-parented to the nozzle first.
  fm_sim.py has no thrust moment, so its trim results are unchanged. Lesson: re-check the thrust line whenever nozzles move.

## 2026-10-02: cockpit from scratch (v0.6.0)
The KR-67 glass panel inside the MiG's arch never looked right, and the player asked for a cockpit made for the MiG instead of
parts from other aircraft. Now it is modelled procedurally in Blender (`blender/cockpit_build.py`) with its own texture atlas
(`tools/cockpit_atlas.py`); both run in the headless build.
- **Fitting.** Walls are ray-cast from the fuselage (`body`) part: half-width at each station and height, inset 1.2 cm. The wall top
  is the highest point where a horizontal ray still hits the fuselage (the rim). It is raised to just under the canopy glass, so
  the jagged rim never shows. Every piece near the canopy is clamped below the glass (`under_glass`). The build prints how far
  anything pokes through the glass, and which vertices end up outside the fuselage. The windscreen comes down much lower than it
  looks (glass at 1.11 m at z 7.4 on the centreline, 0.97 m at x 0.3), so the glareshield stops at z 7.33 and the coaming deck
  follows the glass down.
- **Pilot.** In the stock layout the eye was 0.4 m behind the windscreen bow. The pilot (and with it `cockpitViewPoint`) moves
  back 17 cm (`MiG29Cockpit.PilotShift`), against the new headrest. The panel then sits about 0.7 m from the eye.
- **Game-driven parts keep their objects.** `Cockpit` animates `joystick` (range 9 degrees) and `throttle` (rotation, 17 degrees).
  `TargetCam` and `Cockpit` render the tactical screen into `tacScreen`'s material; its main area is UV u 0-0.75, v 0.25-1 (2.1:1).
  `CockpitWarningLights` drives `warningLights`, whose stock UVs use the integer part of u per lamp side (0 left, 1 right).
  The pivots move and new meshes are baked into each transform's local frame; materials stay where the game drives them.
  `cockpit_int` keeps its components with its mesh removed.
- **Kept from the MiG model:** only the windscreen bow and its mirrors (triangles within 4.5 cm of the glass at the bow station).
- **Winding.** Blender to Unity is a reflection. The exporter takes the triangle winding convention from the airframe dump (stored
  normal against triangle normal) and matches it.

## 2026-10-02: working instruments (v0.7.0)
Blueprinter bundles can only use components that already exist in the game, and the game has no needle gauges: its HUD
"gauges" are text, and the only cockpit animation is `Cockpit` turning the stick and throttle. So a small BepInEx plugin
(`unity/MiG29Instruments`) drives the instruments. Players already have BepInEx for Blueprinter.
- **Parts.** `cockpit_build.py` exports each moving part (`ins_<id>`) with its pivot, dial normal and up vector. The builder
  makes a fixed mount `MiG29_ins_<id>` (z into the panel, y up) with a child `needle`, and the plugin sets
  `needle.localRotation = Euler(0, 0, -angle)`. The attitude ball is a 0.10 m sphere behind a black mask (equirectangular
  texture, u 0.5 facing the pilot); its rotation is `Euler(0, 0, roll) * Euler(-pitch, 0, 0)`. Lamps (`MiG29_lamp_<id>`) are quads
  over their unlit pictures. The plugin enables them and gives them an emissive copy of the tac-screen material (URP Lit).
- **Shared math.** `MiG29Tools/MiG29InstrumentMath.cs` (value to angle, lamp logic) is compiled into both the plugin and the
  editor tools. `MIG29_POSE=sample` renders the preview with a known flight state, so the render checks the same code that
  flies.
- **Game facts found in testing.**
  - In flight, the aircraft's parts are their own physics roots, so the cockpit part is not a child of the `Aircraft`. Search
    from `aircraft.cockpit`.
  - The cockpit view draws layer 3 ("Cockpit") with its own camera (`cockpitRenderer`, near 0.01, mask `00004008`). The main
    camera (mask `0002FE77`) never draws layer 3, and default-layer objects close to the eye vanished in the cockpit view.
    Interior objects therefore go on layer 3, and the tub and frames get exterior copies (layer 0, in `exteriorRenderers`).
  - Renderers in a part's `damageMaterial` list get the airframe's livery and damage maps in their own UV layout, which
    punched holes in the atlas-mapped interior and tinted it. Only the frames (MiG skin UVs) stay in the list.
  - `Aircraft.gForce` is the unsigned acceleration without gravity. The plugin uses `Pilot.gForce` (signed, along the pilot's up
    axis) plus 1 g along that axis.
  - The engine spool ratio idles at 0.33; the plugin maps it to 70 % idle and 100 % military.
  - `IsLanded()` means stopped on the ground; on-ground uses radar altitude instead.
- **Testing in game.** `BepInEx/mig29_dump.flag` turns on developer mode: a hierarchy and camera dump (`BepInEx/mig29_dump.txt`),
  telemetry in the log every 2 s, F10 / F11 for orbit and cockpit camera, and F9 to lift the jet 1,500 m at 220 m/s. F9 moves
  every rigidbody of the aircraft (the part list misses some, and a stretched joint wrecks the jet). It also resets
  `Pilot.velocityPrev` and `Aircraft.velocityPrev`, or the pilot sees a ~1,000 g jump and dies. First in-flight check: IAS
  655 km/h at M0.59 and 1,600 m, AoA 9 deg, 3.3 g in a pull-up, then climbing at 21 m/s.
- **RWR tested live** (Free Flight with 16 AI aircraft): fighters lit the sector toward them with type П, a ship radar
  type О. `Aircraft.onRadarWarning` arrives once per sweep, so warnings are held 4 s. The strength ladder uses distance
  (1 bar at 50 km, 10 bars close), because the game's `power` (max range / distance, capped) saturates. `isTarget` is only set
  for non-aircraft emitters, so ЗАХВАТ means a SAM or ship tracking you.

