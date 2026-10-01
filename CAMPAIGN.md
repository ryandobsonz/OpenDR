# The original campaign

OpenDR plays the original Dark Reign campaign by converting its missions from a
full copy of the game. Each converted mission carries its original scenario,
trigger, end-condition, AI and briefing files, and runs them in game.

Game data never enters this repository: it is copyrighted and comes from your
own copy. The converted maps go to your OpenRA map folder
(`%APPDATA%\OpenRA\maps\dr\campaign` on Windows).

## Setting it up

1. Build OpenDR: `make.cmd all` (needs the .NET 8 SDK).
2. With a full copy of the game (the 1.8.2 community patch, or GOG), run
   `pwsh -File import-campaign.ps1 -GameDir "<the folder holding dark>"`.
   It installs the game content OpenDR reads, the snow tileset and the CD
   soundtrack included, and converts every mission. Name missions after it (`M01F M01I`) to convert
   only those.
3. Launch OpenDR; the campaigns are under **Missions**: Freedom Guard and
   Imperium, each ending in mission 13, The Togran.

Run the import again after changing the importer: the maps are rebuilt from
the game files each time.

## Playing on a large screen

OpenRA draws at the screen's own resolution and aspect ratio, ultrawide
included, and scales the battlefield so its visible height stays in a band
set by **Settings → Display → Battlefield Zoom**: Close shows 480 to 600
game pixels top to bottom, about the original's 640×480 view; Medium 600 to
900; Far 900 to 1300. The mouse wheel zooms within the band. At 4K, also set
**UI Scale** to 2 or more, or the sidebar is small. The art is the 1997 art
scaled up: sharper and larger, not more detailed.

## How it works

| Part | Code | What it does |
|---|---|---|
| Import | `UtilityCommands/ImportDrCampaignCommand.cs` | Terrain, units, buildings and scenery from the `.map` and `.scn`; one player per original team, with its alliances; the mission files copied into the map; tech level limits; the briefing |
| Formats | `FileFormats/DrScript.cs`, `DrScenario.cs`, `AipFile.cs` | The scenario script syntax shared by `.scn`, `.fsm` and `.end`; the AI personality format |
| Triggers | `Traits/World/DrScenarioScript.cs`, `Scripting/DrConditionTree.cs` | The FSM and end-condition trees, special forces, patrols, messages and their voices, the win or loss with the briefing's historical outcome |
| Enemy AI | `Traits/BotModules/DrAipBotModule.cs` | Each AI team builds through its AIP's accounts and sends troops by its priorities |

The behaviour follows the game's own AIP manual (the *AIP and Scenario End
Conditions Guide*), shipped with the game. Its one undocumented criterion,
`CritONCE`, fires the first time its inner criterion is met and never again:
the missions loop through states that a latch would re-trigger forever.

**Team 0 is the player** in every original mission. A team's end tree reaching
state 0 wins for it and its mutual allies; any other team winning is a loss.
A team without an `.end` file wins by destroying every non-allied unit and
building, civilians and the map's own walls aside.

**One game cycle is one tick.** The manual gives 15 to 40 cycles a second;
OpenRA runs 25 ticks a second at normal speed. `DrScenarioScript.CyclesPerTick`
changes the rate.

## What differs from the original

- **The enemy AI is a reimplementation.** The build accounts follow the
  manual closely; troop allocation follows its formulas, but unit strength is
  cost divided by 10, where the original weighed firepower and hitpoints.
  Expect the enemy to play differently in the details.
- **Stealing plans** (`Traits/DrPlanStealing.cs`): an Infiltrator that
  enters an enemy headquarters, training facility or assembly plant leaves
  with the plans of what that facility makes in the original tables, and
  they are stolen once it is back beside its own headquarters, which is what
  mission 6's goals check. The stolen designs do not yet become buildable.
- **Not yet in OpenDR:** phasing, the water contaminator, and water and
  taelon as separate resources (collected resources count as credits;
  `CritCollectWater` and `CritCollectMineral` use credits earned). No
  original campaign mission needs them to be won.
- **Harassing a region** (`CritHarassRegion`, one use) counts the team's units
  fighting in the region, not damage done.
- **Units' tactical settings** (`SetTactAI`: pursuit, damage tolerance,
  independence) are not read: every campaign use sets pursuit medium or
  high, which OpenRA's default stance already is.
- **The expansion's campaigns** (Rise of the Shadowhand, the Xenite missions)
  convert but lack most of their units and buildings.
- **No campaign menu**: missions are chosen from the mission browser.

## Testing aids

- Every trigger transition and action is logged to `drscenario.log` in the
  OpenRA log folder, with the game cycle.
- `OPENDR_TEST` (an environment variable) scripts a test run of a campaign
  map: `100:cash 0 6000;110:killunits 1;120:spawn 0 trainingfacility.fguard 18,48;145:shot`
  meets mission 1's goals and screenshots the victory. Commands are listed
  on `DebugScreenshots`; screenshots go to the OpenRA screenshot folder and
  work with the screen locked.
- `Launch.Map=<uid>` on the game's command line starts a mission directly;
  `OpenRA.Utility dr --map-hash <map folder>` gives the uid.
