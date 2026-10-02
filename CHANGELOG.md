# Changelog

## 0.5.0 (unreleased)
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
  main-gear struts folded further in, dark intake ducts hide the stowed main wheels.

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
