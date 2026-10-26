# Archived analysis scripts

Earlier iterations of the main-gear retraction study, kept for reference. They are superseded by the scripts one folder up,
which the build and the plugin follow:

| Archived | Superseded by |
|---|---|
| `main_gear_search.py` | `main_gear_search2.py` (skewed hinge: yaw, fold, strut twist, shift; `MiG29MainGear.cs` uses its result) |
| `main_gear_path.py` | `main_gear_path3.py` (the retraction path `GearPath.cs` follows; `main_gear_path2.py` stays as `main_gear_apply3.py`'s fallback) |
| `main_gear_apply.py`, `main_gear_apply2.py` | `main_gear_apply3.py` |

They still run from here (they find `main_gear_search_lib.py` and `tools/` one level up), but nothing depends on them.
