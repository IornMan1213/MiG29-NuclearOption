# Changelog

## 0.8.8 (unreleased)
**Working mirrors, carrier take-off.**
- **Working rear-view mirrors** with the [NO Mirrors](https://github.com/IornMan1213/NuclearOption-Mirrors) plugin (0.0.2 or
  later): the three canopy mirrors reflect for real, the centre one straight back over the spine, the side ones back past each side
  of the canopy. They are slightly convex for a useful field of view. Without the plugin they are plain grey glass.
- Mirrors now start unfolded (Ctrl+M still folds them; the option is in Configuration Manager under MiG-29 Instruments > Cockpit).
- **Carrier spawning.** The MiG can be picked from the Hyperion class carrier's hangars, as the KR-67 can.
- The tailplane's static-discharge wicks were left floating behind the stabilators; they now sit on them and move with them.
## 0.8.7 (Update 8.7)
**More weapons, five new liveries, an option to hide the HUD, folding mirrors.**
- **More base-game weapons.**
  - Inner pylons, on top of what they had: IRM-S2 (single and pair), IRM-S1 pair, AGM-48 (pair and triple), AGM-68, ARAD-45,
    AGM-99, PAB-125 (pair and quad), PAB-250, PAB-250LR, PAB-80LR pair, AGR-18 rocket pods (single and triple), the 20 mm gun pod,
    ECM and radar jamming pods, and the GPO-N 1.5 kt nuclear bomb.
  - Middle pylons: the same lighter set, single stores only. Side-by-side racks on both the inner and middle pylons touched.
  - Outer pylons: IRM-S2, MMR-S3, IRM-S1, AGM-48, an AGR-18 pod, ECM pod, smoke and flare pods.
  - The centreline station now takes weapons, not just the drop tank: GPO-500, GBM-500LR, PAB-250 (single and triple),
    PAB-125 quad, CBO-400, GPO-2P Auger, AGM-68, both GPO-N nuclear bombs, the 20 mm gun pod, and jamming, ECM and smoke pods.
- **Five new liveries**, after MiG-29 profile drawings: 29+20 Special (yellow, red and black), Tricolour Digital 741, OVT Splinter
  917, White 44 and LII Gromov 84. Each has its own numbers and fin markings.
- **Hide flight HUD option** (Configuration Manager, MiG-29 Instruments > HUD). While flying the MiG it hides the on-screen
  flight readouts, pitch ladder, compass and velocity vector, so you fly by the cockpit gauges. The map, target markers, mouse-aim
  cursor and other mods' overlays stay. A second option keeps the weapon, countermeasure and damage readouts.
- **Folding mirrors.** The three rear-view mirrors fold up against the canopy, out of the forward view (on by default). Ctrl+M
  folds and unfolds them in flight; both are in Configuration Manager under MiG-29 Instruments > Cockpit.
- **Livery editor for modders**: blender/livery_edit.py opens the MiG in Blender, ready to paint a livery on the model; the build
  ships the result as "Fulcrum Custom".

## 0.8.6 (Update 8.6)
**Round afterburner, gun fixed.**
- **Afterburner.** The afterburner glow came from the donor jet's flat 2D nozzles and showed as a glowing rectangle inside the
  round RD-33 nozzles. It is now round and sits inside the nozzle. The afterburner flame was missing entirely (and was set
  0.73 m above the nozzle); there is now a round flame trailing from each nozzle exit, growing with afterburner.
- **GSh-30-1 cannon.** Rounds left the jet from inside the wing root, about 4.5 m behind the gun. They now leave from the gun
  port on the left side of the forward fuselage, beside the cockpit.

## 0.8.5 (Update 8.5)
**Landing gear rebuilt, new engine nozzles, R-27R fixed.** Everything fixed since 0.8.1:
- **Main landing gear, rebuilt to work like the real MiG-29.**
  - New main legs modelled from scratch: slim MiG-style legs with the wheel on the outboard side. Wheels, suspension and ground
    handling are unchanged.
  - The legs retract as on the real jet. Each leg swings forward and up beside the intake with the wheel edge-on, then the wheel
    turns flat and tucks into the wing root above the intake. With the MiG-29 Instruments plugin installed the gear follows this
    path, on AI MiGs too; without it the legs take a simpler arc that still ends fully hidden.
  - New forward gear doors on the top of each intake side, as on the real jet, so the wheel no longer goes through solid skin.
    They hang open below the intake while the gear moves.
  - Stowed gear stays hidden whether it was raised in flight (wheels hanging at full suspension travel) or the aircraft spawned
    with its gear up. Before, a main wheel raised in flight poked into the intake.
  - No more strut bracket showing through the top of the wing.
- **Wheel wells.** The gear bays were open holes you could see straight through. They are now closed wells shaped around the
  gear, with a detailed texture (frames, rivets, hydraulic lines, wiring, stencils) and surface relief. The gear doors have a
  painted inner side.
- **Gear doors open the right way.** The left nose and main doors swung up through the fuselage; all doors now swing down.
- **Nose gear stows cleanly.** The stowed nose strut hung 15 cm below the closed doors. Every part now ends up at least 10 cm
  inside the bay.
- **Landing light.** It was a bright light shining in every direction, later a glowing panel floating beside the strut. The lamp
  now sits on the front of the nose strut, aimed ahead and slightly down to light the runway, and stows with the gear.
- **Navigation lights** sit on the wingtip edges with lamp-sized glows instead of hanging in the air off the tips.
- **New engine nozzles, modelled from scratch.** RD-33 nozzles replace the model's low-poly cans: feathered shrouds, inner
  petals, heat-tinted metal, and afterburner flame holders inside. The petals move with the engine: open with the engine off,
  partly closed at idle, closed at full power, wide open in afterburner (also on AI MiGs).
- **R-27R fixed.** It exploded at the pylon when fired without a target, and the blast damaged the launching jet. It now leaves
  the rail unarmed and arms itself after one second. Fired without a target it flies straight ahead and self-destructs after
  its motor burns out; with a radar target it guides as before.
## 0.8.1
**Interior polish.**
- **Equipment bay behind the seat.** The space under the rear canopy, between the seat and the canopy's rear edge, was empty:
  looking over your shoulder you saw straight out of the aircraft. It is now a closed bay modelled for the mod:
  - A deck, side walls and canopy sills.
  - A riveted rear bulkhead shaped to the canopy, with a ФОНАРЬ stencil and an access hatch.
  - Two avionics blocks with data plates, connectors and carry handles.
  - A radio box, two blue oxygen bottles in straps with valves and lines, and cable looms.
  - The bay also shows through the rear canopy from outside.
- The K-36 seat has its canopy breakers on the headbox, and the seat's guide rails are on the bulkhead behind it.
- **Windscreen bow and mirrors, from scratch.**
  - Inside the cockpit, the model's low-poly bow, black blades, mirror plank and stray mirror lump are gone.
  - In their place is a clean dark-grey bow fitted to the gap in the glass, covering the windscreen and canopy seam.
  - It carries three rear-view mirrors: one at the top and one in each upper corner.
  - The model's bow still shows from outside, with the livery.
- **Real switches.** Every toggle on the consoles, pedestal and wall boxes is now a 3D bat-handle lever, thrown to its drawn
  state, and every rotary selector is a 3D knob with a white pointer. They used to be printed flat on the plates.
- **Sharper cockpit textures.** The cockpit atlas is now 4096 px instead of 2048, so switch legends, placards, rivets and
  gauge faces stay crisp up close.
- **Rebuilt K-36DM seat.** It was made of flat slabs; now it has:
  - three separate padded back cushions;
  - split thigh pads with a front roll on the survival-kit box;
  - a rounded headbox with a raised head pad and side wings;
  - small canopy breakers at the back of the headbox top (they had looked like horns);
  - trim on the bucket edges;
  - flat webbing shoulder and lap straps meeting at a metal quick-release buckle, plus a negative-g strap.
- **Circuit-breaker panels (АЗС)** on both cockpit walls above the consoles: three rows of 3D breaker buttons with legends.
- Seat fabric is plain now; the printed quilting showed as stray diagonal slashes.
- Fixed: the bulkhead behind the seat and the footwell's front wall faced away from the pilot, so the game didn't draw them
  (you could see out of the aircraft past the seat). Both face into the cockpit now.

## 0.8.0
**Drop tanks and chaff.**
- **PTB-1500 centreline tank** (1,500 L, about 1,180 kg of fuel) on a new belly station between the engine nacelles, and
  **PTB-1150 wing tanks** (1,150 L each) on the inner pylons. Both are modelled for the mod.
- **External fuel is used first**, as on the real MiG-29: while the tanks hang, they keep the internal tanks full. The aircraft's
  weight follows the fuel: a full tank weighs what it should, and dropping one sheds only the empty tank and the fuel left in it.
- **Drop on command:** select the tanks and fire like any store (one per press), or press **Ctrl+J** to jettison them all.
  Both are blocked with the gear down. When tanks you carry run dry, the new **ПТБ** caution light comes on (it replaces the
  rarely seen АККУМ light). AI MiGs drop their tanks once they are empty. Optional: `Drop empty tanks automatically`.
- **Chaff**: the MiG now carries a chaff dispenser, using the game's own radar chaff (no stock aircraft carries it).
  It decoys active and semi-active radar missiles, works best when the missile sees you side-on and is close, and fills
  a glittering cloud behind you. Cycle countermeasures to pick it: IR Flares, Radar Chaff, Radar ECM.
- **MiG-29 dispensers**: flares and chaff now fire upward from the BVP-30-26M dispensers on the tail booms, as on the real
  aircraft, instead of from the KR-67's engine tops. 60 cartridges in all, as on the real jet: 30 flares and 30 chaff
  (the KR-67 carried 72 flares). The Radar ECM stays (the 9.13's internal Gardeniya jammer).
- New preset loadouts: Long-Range CAP (PTB-1500 / R-27R / R-73) and Ferry (three tanks / R-73).
- The instruments plugin (now also the stores plugin) is required for the tanks to give fuel.

## 0.7.1
- Packaged for mod managers ([NOMNOM](https://github.com/KopterBuzz/NOMNOM) / NOMM): each release now starts with
  `MiG-29-Fulcrum_<version>.zip`, holding the .nobp, `MiG29Instruments.dll` and `CREDITS.txt`.
- The instruments plugin's version now follows the mod version (0.7.1), as mod managers expect.
- No gameplay changes from 0.7.0.

## 0.7.0
**Every cockpit instrument works.** Install `MiG29Instruments.dll` (a small BepInEx plugin) next to the .nobp.
- 20 needles: airspeed and Mach, altimeter (two hands), vertical speed, angle of attack, g meter, radar altimeter, both engine
  tachometers and exhaust temperatures, fuel, hydraulics, oxygen, cabin altitude, and the clock (mission time of day).
- A real attitude ball (pitch and bank) and a turning HSI compass card.
- 24-light caution panel: every caption reacts to something (engine fire and damage, generators, hydraulics, fuel low and
  reserve, low altitude, angle of attack, over-g, overspeed, missile inbound, radar lock, canopy, gear, flaps, brakes, intake
  guards, afterburner, master caution).
- SPO-15 radar warning receiver: sector lamps toward each radar painting you, signal-strength bar, threat type; flashing for
  missiles. Gear lights: green down and locked, red in transit.
- Rudder pedals slide with your rudder input; the landing-gear lever (left of the radar altimeter) swings down and up with the
  gear.
- Night lighting for the panel and consoles (BepInEx config `Cockpit / Night panel lighting`, 0-4, default 1).
- Fixes found testing in game: the cockpit is on the game's Cockpit layer (parts of it were clipped away in the cockpit view),
  with an exterior copy for the outside view; it no longer takes the airframe's damage shading (holes and livery tint); the
  side-panel instruments sat tilted into the panel; the radar screen sat behind the side panel; a gap above the side panels
  showed outside.

## 0.6.0
**New cockpit, modelled from scratch for the MiG-29** (no parts from other aircraft):
- Turquoise MiG-29 (9.12-style) instrument panel with a T layout: attitude indicator, HSI, airspeed/Mach, altimeter, AoA/G and
  vertical speed, plus RWR, engine RPM and temperature, fuel, hydraulics, oxygen, cabin altitude, radar altimeter and clock. All
  dial faces are drawn for the mod, with Russian markings.
- Black glareshield, ILS-31-style HUD on the coaming, master warning lamps either side of it, caution-light panel.
- Side consoles with switch panels, knobs and toggles; a throttle quadrant with twin throttle levers on the left; equipment boxes,
  cable looms and the canopy handle on the walls.
- Centre stick between the pilot's legs, rudder pedals, tread-plate floor, rear bulkhead and avionics deck behind the seat.
- K-36-style ejection seat with headbox, harness and yellow/black ejection handle.
- The game's tactical display now sits in the panel as the radar/IRST screen; the stick, throttle and warning lamps still move
  and light as before (they keep the game's own animated parts, with new models).
- The pilot sits 17 cm further back, against the new headrest (the view had been crammed up against the windscreen bow).
- The cockpit fits the airframe exactly: walls, rails and glareshield are fitted to the fuselage and kept clear of the canopy
  glass, and the interior is visible through the canopy from outside.
- The 0.5.3 missile/rail clearance fix is confirmed: missiles no longer blow up the aircraft at launch.

## 0.5.3 (testing)
- Missiles hang 1.5 cm below their rails and the rail colliders stop 3 cm short of them, so a launched missile no longer spawns
  touching the rail on the wing. Attempted fix for "the aircraft explodes when firing missiles"; needs confirming in game.

## 0.5.2
- Fixed the jet flipping over backwards above ~70 % throttle and pitching up uncontrollably at low speed: the engine thrust acted
  0.73 m below the centre of mass (at the MiG's real nozzle height), so full afterburner out-muscled the weight on the main
  wheels and the tail. Thrust now acts at centre-of-mass height; the nozzles and exhaust effects are unchanged.
- Cockpit: a dark floor and bulkhead close the gaps under the panel (the MiG model has no real cockpit floor).

## 0.5.1
- Fixed R-27R and R-27T: their copied seekers lost the reference to their own missile, so every launch threw a
  NullReferenceException at launch and on every physics tick (tens of thousands of errors, frame-rate drops) and the missiles
  did not guide. Seeker mode also taken from the seeker donor.
- Cockpit reworked: the KR-67 glass-cockpit panel (tactical screen, MFDs, side-stick and throttle in their own mounts) framed by
  the MiG's windscreen arch, mirrors, sills and seat. The full MiG cockpit still shows from outside.

## 0.5.0
**Weapons**
- GSh-30-1 cannon replaces the KR-67's 27 mm: 150 rounds, ~1,650 rpm, 870 m/s, muzzle in the left wing root.
- R-27T (infrared R-27, fire-and-forget) on the AKU-470 rail, and R-60M (light infrared dogfight missile) on an APU-60 rail.
- Pylon options: inner R-27R / R-27T / R-73 / R-60M, middle R-73 / R-60M, outer R-73 / R-60M.
- Default loadout: gun + 2x R-27R + 4x R-73. Three preset loadouts (air superiority, dogfight, strike), which AI-flown MiGs also
  use (they previously took off with only the gun).

**Cockpit**
- The model's own MiG-29 cockpit is shown from the pilot's seat; the game's tactical display and warning lights sit on a
  retrofit console in the lower panel.

**Looks**
- New liveries: Fulcrum Digital Grey (pixel camouflage) and Fulcrum Display Blue (display-team scheme).
- Two new loading screens (Digital Grey, and a Display Blue pair).
- Exterior clean-up: no KR-67 canopy frames or seat headbox poking through the canopy, nose wheel stowed inside the fuselage,
  main-gear struts folded further in, dark intake ducts hide the stowed main wheels, landing light moved onto the nose-gear
  strut.

## 0.4.2
- Cockpit damage display: smooth part borders (per-pixel height raster and a box-vote smoothing pass) instead of jagged wing-root
  edges; per-part images match the outline exactly.

## 0.4.1
- Fixed the aircraft exploding on spawn: hitboxes are the KR-67 part colliders scaled to the MiG again, with lift points re-placed
  so handling is unchanged.
- Fixed missiles hanging below the pylons (stock mount scales reset; missiles are true size on their rails) and inside-out faces
  on the R-73 nose and rail fronts.
- Fixed a cockpit pylon-indicator error at spawn.

## 0.4.0
- Opening canopy (rear hinge) and ejection, gear doors, the MiG's own damage display and map icon, Desert Tan livery, loading
  screens.

## 0.3.0
- Flight model tuned to MiG-29 data with an offline re-implementation of the game's aerodynamics; RD-33 engines; all-moving
  stabilators.
- R-27R and R-73 modelled from scratch, three pylons per wing.

## 0.1.0 - 0.2.x
- First flyable build: the Sketchfab MiG-29 on the KR-67 Ifrit's systems, split into the game's damage parts, with moving
  control surfaces and MiG-placed landing gear.
