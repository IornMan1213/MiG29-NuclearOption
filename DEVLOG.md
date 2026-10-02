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
