# Adding modded weapons to the MiG-29

This page is for authors of weapon mods who want their weapons to be selectable on the MiG-29 Fulcrum. It assumes you already
have a working weapon mod (a `WeaponMount` with a JSON key) and only covers what is specific to the MiG.

You do not need to change the MiG mod, and the MiG mod does not need to know about yours. Players without the MiG simply get
a warning in the log and your mod carries on.

## The values you need

| | |
|---|---|
| Aircraft JSON key | `mig29_Fulcrum` |
| Unit name | `MiG-29 Fulcrum` |
| Hardpoint sets | 6 (indices 0 to 5, below) |
| Checked against | MiG v0.8.9; the set order has not changed since v0.8.0, when the centreline station was added |

### Hardpoint sets

| Index | Set name | Stations | Use it for |
|---|---|---|---|
| 0 | `GSh-30-1` | internal cannon (left wing root extension) | internal guns only; leave it alone |
| 1 | `Centreline` | 1, under the belly between the engine nacelles | drop tanks, bombs, nukes, gun pods, jammer pods |
| 2 | `Inner Pylons` | 2, one under each wing, 2.36 m from the centreline | the heaviest stores: long missiles, large bombs, racks |
| 3 | `Middle Pylons` | 2, 3.10 m out | single stores only (no racks) |
| 4 | `Outer Pylons` | 2, 3.73 m out, near the wingtips | light stores only: short-range AAMs, small pods |
| 5 | `Tail Hook` | carrier hook | not a weapon station; leave it alone |

Pick sets by what fits: the MiG is a small jet and the game does not check whether stores overlap.

- **Inner and middle** are 0.74 m apart. A store wider than about 0.7 m (a side-by-side double rack, for example) on the inner
  pylon touches whatever is on the middle one. The MiG's own options keep the racks on the inner pylons and give the middle
  pylons single stores; please do the same.
- **Middle and outer** are 0.63 m apart. Keep outer stores slim (about 0.2 m across, the size of an R-73 or AAM1).
- **Centreline**: the nose gear is ahead of it and the intakes are either side. Stores up to the PTB-1500 tank's size
  (4.9 m long, 0.68 m across) fit. Long missiles reach the nose gear, so don't offer them here.

## Option 1: Blueprinter op (no code)

In your Blueprinter project, create **Blueprinter > OpAddWeaponToHardpoint** (Create menu) and add it to your mod's ops:

- **Weapon JSON Key**: your `WeaponMount`'s JSON key.
- **Add Aircraft**, then:
  - **Aircraft JSON Key**: `mig29_Fulcrum`.
  - **Hardpoint Indices**: the set indices from the table, e.g. `2` and `3` for the inner and middle pylons.

The MiG is not in Blueprinter's list of known aircraft, so its hardpoints don't show as tick boxes; type the key and the
indices by hand. One op can target many aircraft, so this is just one more entry next to the base-game jets you already target.

Blueprinter runs these ops after every mod's assets are registered, so load order does not matter: your weapon is added to the
MiG whether your mod loads before or after it. If the MiG is not installed, the log shows
`[Ops] Aircraft mig29_Fulcrum not found` and nothing else happens.

## Option 2: from a BepInEx plugin

If your mod already adds weapons in code, add them to the MiG's `WeaponManager` once the encyclopedia is loaded (after
Blueprinter has run its ops):

```csharp
if (Encyclopedia.Lookup.TryGetValue("mig29_Fulcrum", out var def) && def is AircraftDefinition mig)
{
    var wm = mig.unitPrefab.GetComponentInChildren<WeaponManager>(true);
    foreach (var set in wm.hardpointSets)
        if (set.name == "Inner Pylons" || set.name == "Middle Pylons")
            if (!set.weaponOptions.Contains(myMount)) set.weaponOptions.Add(myMount);
}
```

Finding sets by name, as above, is safer than by index if a later MiG version adds a station.

**Plugins that copy stock options.** Some weapon plugins add their weapon wherever a given base-game mount is already offered
(for example, "everywhere a `Rocket2_4Pod` can go"). Those pick up the MiG automatically wherever it offers that base-game mount.
The base-game mounts each MiG set offers:

| Set | Base-game mounts |
|---|---|
| Centreline | `bomb_500_single`, `bomb_500_glide_single`, `bomb_250_single`, `bomb_250_triple`, `bomb_125_quad`, `bomb_cluster1_single`, `bomb_penetrator1`, `AGM_heavy_single`, `nuclearBomb1_external`, `nuclearBomb1_strategic_external`, `gun_20mm_pod`, `JammingPod1`, `ECMPod1`, `SpecialSmokePod` |
| Inner Pylons | the KR-67's inner wing pylon options, plus `AAM3_single`, `AAM3_double`, `IRMS1_double`, `AGM1_double`, `AGM1_triple`, `AGM_heavy_single`, `ARM2_single`, `AShM2_single`, `bomb_125_double`, `bomb_125_quad`, `bomb_250_single`, `bomb_250_glide_single`, `bomb_glide1_double`, `RocketPod1_single`, `RocketPod1_triple`, `gun_20mm_pod`, `ECMPod1`, `JammingPod1`, `nuclearBomb1_external` |
| Middle Pylons | the KR-67's outer wing pylon options without its racks, plus `AAM3_single`, `AGM1_single`, `AGM_heavy_single`, `ARM2_single`, `bomb_125_single`, `bomb_250_single`, `bomb_250_glide_single`, `bomb_glide1_single`, `RocketPod1_single`, `gun_20mm_pod`, `ECMPod1`, `nuclearBomb1_external` (any mount whose name contains `_double`, `_triple` or `_quad` is removed from this set) |
| Outer Pylons | `AAM3_single`, `AAM1_single`, `IRMS1_single`, `AGM1_single`, `RocketPod1_single`, `ECMPod1`, `SpecialSmokePod`, `SpecialFlarePod` |

## How your weapon will sit

Your mount is placed at the station's hardpoint, exactly as on any other aircraft. Two things are different from most jets:

- **The wing stations are pitched nose-down**, following the MiG's sloped pylon fairings: 2.5 degrees on the inner and middle
  pylons, 5 degrees on the outer ones. A mount whose top is flat at its origin sits flush against the pylon, and the store points
  slightly nose-down, as stock stores do on the real jet. The MiG's own missiles are levelled inside their launchers; you don't
  need to do this for yours.
- **The pylons are short.** Mounts that bring their own long pylon or adapter (like many base-game racks) hang a little lower
  than on the KR-67. That is fine; just check it looks right.

Station positions, if you want to build a MiG-specific variant (aircraft frame, metres: +X right, +Y up, +Z forward, from the
aircraft's origin):

| Station | Position (right side; the left mirrors X) | Pitch |
|---|---|---|
| Centreline | (0, -0.44, 1.0) | 0 |
| Inner | (2.36, -0.23, 1.31) | 2.5 nose-down |
| Middle | (3.10, -0.25, 0.66) | 2.5 nose-down |
| Outer | (3.73, -0.37, 0.28) | 5 nose-down |

## Testing

1. Install your mod and the MiG, start the game and open the MiG's loadout screen. Your weapon should be listed on the sets you
   chose.
2. Check `BepInEx/LogOutput.log` for `[Ops]` warnings: `Weapon ... not found` means the weapon's JSON key is wrong;
   `Invalid hardpoint index` means an index above 5.
3. Look at it from the side and the front: it should touch the pylon and not overlap the store next to it.

**Multiplayer:** everyone in a session needs the same mods, including your weapon mod, for the loadout to match.

## The MiG's own weapons

If you want the other direction, putting the MiG's weapons on your aircraft, these are the `WeaponMount` keys (available
whenever the MiG is installed):

| Key | Store |
|---|---|
| `mig29_R73_single` | R-73 on an APU-73 launcher |
| `mig29_R73_outer` | R-73, launcher shaped for the MiG's thin outer pylon |
| `mig29_R60M_single` | R-60M on an APU-60 launcher |
| `mig29_R60M_outer` | R-60M, outer-pylon launcher |
| `mig29_R27R_single` | R-27R on an AKU-470 launcher |
| `mig29_R27T_single` | R-27T on an AKU-470 launcher |
| `mig29_PTB1500` | PTB-1500 centreline drop tank |
| `mig29_PTB1150` | PTB-1150 wing drop tank |

The launchers have a 2.5 degree slope built in (5 degrees for the outer ones) to match the MiG's pylons, so the missiles point
slightly nose-up on a level hardpoint. The drop tanks' fuel is fed by the MiG's plugin, which only does this on MiGs: on other
aircraft they are dead weight.

Questions or a weapon that won't fit: [open an issue](https://github.com/IornMan1213/MiG29-NuclearOption/issues/new/choose).
