# The MiG-29's Workshop liveries

The nine liveries for either side that used to ship with the mod, published on the Steam Workshop:

| Livery | Workshop id |
|---|---|
| Fulcrum Grey | [3835186135](https://steamcommunity.com/sharedfiles/filedetails/?id=3835186135) |
| Fulcrum Desert Tan | [3835186967](https://steamcommunity.com/sharedfiles/filedetails/?id=3835186967) |
| Fulcrum Digital Grey | [3835187545](https://steamcommunity.com/sharedfiles/filedetails/?id=3835187545) |
| Fulcrum Display Blue | [3835188075](https://steamcommunity.com/sharedfiles/filedetails/?id=3835188075) |
| Fulcrum 29+20 Special | [3835188704](https://steamcommunity.com/sharedfiles/filedetails/?id=3835188704) |
| Fulcrum Tricolour Digital 741 | [3835189313](https://steamcommunity.com/sharedfiles/filedetails/?id=3835189313) |
| Fulcrum OVT Splinter 917 | [3835189838](https://steamcommunity.com/sharedfiles/filedetails/?id=3835189838) |
| Fulcrum White 44 | [3835190309](https://steamcommunity.com/sharedfiles/filedetails/?id=3835190309) |
| Fulcrum LII Gromov 84 | [3835190841](https://steamcommunity.com/sharedfiles/filedetails/?id=3835190841) |

How they were made: `tools/livery_profiles.py`, `livery_desert.py`, `livery_digital.py` and `livery_display.py` paint the
textures (the build writes them to `MiG29Source`); `WorkshopBuild.cs` here, placed in the kit's `Assets/Scripts/Editor` with
`MiG29LiveryBuilder.cs`, builds one bundle per livery (`Unity -batchmode -executeMethod WorkshopBuild.Run`, textures in
`Assets/Workshop/<bundle>.png`). Each was uploaded from the game (Workshop > Upload > Aircraft Livery) with Public and Mod
Required ticked. To update one, rebuild its bundle into the game's `Skins/<bundle>` folder, keeping its `workshop.json`, and
upload it again: the game offers "Update Existing".
