# Working on the Dark Reign campaign port

This fork of OpenDR plays the original Dark Reign campaign at modern
resolutions. **What it is, how it works and what differs from the original:
[CAMPAIGN.md](CAMPAIGN.md).** This file is how to keep working on it.

## Where things are

| | |
|---|---|
| Repo | `C:\Users\ryand\Git\OpenDR`, `master`, pushed to `github.com/ryandobsonz/OpenDR` (upstream `drogoganor/OpenDR`) |
| Game data | `DrData/` (gitignored, never commit it): the Dark Reign 1.8.2 install, copied from Ghost's `/mnt/GhostMedia/WindowsGames/dkreign`. `DrData/manuals/` holds the original manuals; the AIP manual (`Artificial Intelligence Personalities/aipmanual.pdf`) is the spec for the triggers and AI |
| Game content | `%APPDATA%\OpenRA\Content\dr`, installed by `import-campaign.ps1` |
| Converted missions | `%APPDATA%\OpenRA\maps\dr\campaign\<m01f…>` — rebuilt by every import |
| Logs, screenshots | `%APPDATA%\OpenRA\Logs` (`drscenario.log` traces every trigger), `%APPDATA%\OpenRA\Screenshots` |
| Campaign progress | `%APPDATA%\OpenRA\dr-campaign.yaml`: the missions won, which the menus' mission ring reads |
| Engine | `engine/`, OpenRA `playtest-20260222`, fetched by `make.cmd all`; .NET 8 SDK in `C:\Program Files\dotnet` |
| Launcher | `launcher/` (WPF); `pwsh -File launcher/build.ps1` builds `DarkReign.exe` in the root and the game host `engine/bin/DarkReignGame.exe` (both gitignored). How it works: [CAMPAIGN.md](CAMPAIGN.md#the-launcher) |
| Package | `pwsh -File packaging/windows/package.ps1 [-Version x]`: `build/Dark Reign/` and a zip, self-contained, built from a copy of the sources in `build/src` (gitignored). About 5 minutes |

It is a standalone game on the user's PC, not part of Ghost or WinGE.

## The loop

```
pwsh -File tools/campaign/build-import.ps1 M01F            # build, convert M01F (no names: all 25)
pwsh -File tools/campaign/run-game.ps1 -Mission m01f -Seconds 40 -Test "..."   # play it, print the trace
pwsh -File tools/campaign/smoke.ps1                        # every mission 40 s: crashes, triggers, a screenshot
pwsh -File tools/campaign/wintest.ps1                      # every mission: destroy the enemy, report the winner
python tools/campaign/check-missions.py                    # static: unsupported commands, missing ids
pwsh -File launcher/build.ps1                              # DarkReign.exe and engine/bin/DarkReignGame.exe
```

The user plays through `DarkReign.exe` (Play starts `DarkReignGame.exe`);
the scripts start `engine/bin/OpenRA.exe` directly. After `make.cmd all`
refetches or rebuilds the engine, run `launcher/build.ps1` again for the game
host.

`smoke.ps1` and `wintest.ps1` take about 20 minutes; run them in the
background. Their output goes to `tools/campaign/out/`.

- **Close the game before building**: a running game locks the mod DLL and
  the build fails to copy it. `build-import.ps1` closes test games
  (`OpenRA.exe`) itself, but stops if the user's game (`DarkReignGame.exe`)
  is running: ask them to close it.
- **The laptop is usually locked during long runs**, so desktop captures are
  black. The game screenshots itself instead: `-Ticks` or a `shot` step.
- **Don't leave a shell sitting in a map folder** while importing: Windows
  then refuses to delete it and that mission silently fails.
- Run the scripts with `pwsh`; the PowerShell tool may be Windows
  PowerShell 5.1.
- **The player's game shares everything with the tests**: settings, logs,
  screenshots. `run-game.ps1` restores `settings.yaml` after each run (the
  engine saves the test's windowed 1600×900 into it), and its log and
  screenshot clean-up empties folders the user's game also writes to.
- **The user often has another session working on this repo**: check
  `git status` and running processes before building, stage only your own
  files, and say before anything puts a window on their screen.

## Scripted tests

`-Test` sets `OPENDR_TEST`: semicolon-separated `tick:command` steps, run by
`Traits/World/DebugScreenshots.cs`. Commands: `shot`, `cash TEAM N`,
`killunits TEAM`, `killall TEAM`, `kill NAME`, `killtype TEAM ACTOR`,
`spawn TEAM ACTOR X,Y [NAME]`, `teleport NAME X,Y`, `steal INFILTRATOR TARGET`,
`select NAME`, `explore TEAM`, `camera X,Y`. Names are map actor names (`u<id>`,
the original unit id) or those given to `spawn`. `order` exists, but its
orders never reached units; use `teleport` and `steal` instead. `leave`
returns to the menus, as the in-game Leave does. The game pauses when it
ends, so steps still to come then run a second apart in real time.

The menus have their own script. Without `-Mission`, `run-game.ps1 -Shell`
sets `OPENDR_SHELL`: steps 40 ticks apart, each screenshotted 30 ticks in.
A step is a screen (`main`, `quit`, `single`, `credits`, `cube`, `story`,
`briefingf`, `briefingi`, `training`, `options`, `archive`, `debrief`), optionally with a mission
and side (`story:3`, `briefingi:7:i`; locks are ignored), or
`click:X:Y`, a click at a point of the 640×480 screen through the real input
path. Clicks play the videos between screens (a cube turn is 7.5 seconds);
steps wait for them to end, except `shot` (a screenshot now), `skip` (as a
click: a movie to what follows it, else to the screen after) and `wait`
(nothing, 40 ticks). Screen steps skip them all, and `intro` plays the intro.
Start New Game plays the intro first, so a script skips it. The script outlives a mission, so it can play
through one:

```
-Shell "click:320:101;click:320:203;skip;click:364:193;click:137:41;click:405:391;click:321:18"
-Test "100:cash 0 6000;110:killunits 1;120:spawn 0 trainingfacility.fguard 18,48;130:spawn 0 assemblyplant.human 22,43;500:leave"
```

Single Player, Start New Game (skipping the intro), mission 1, Freedom
Guard, Launch; M01F is won and left; the debrief's Continue returns to the
ring with mission 2 open. Run it for 130 seconds: the turns take their time.
`run-game.ps1` sets `OPENDR_SCRIPTED`, which keeps test wins out of the
player's `dr-campaign.yaml`.

Every mission's win condition has been met this way. Where destroying the
enemy is not enough, these are the recipes (cells are map cells, the
original's tile + 1):

| Mission | Win condition | Test |
|---|---|---|
| M01F | Destroy listed units, 8000 credits, training facility and assembly plant | `100:cash 0 6000;110:killunits 1;120:spawn 0 trainingfacility.fguard 18,48;130:spawn 0 assemblyplant.human 22,43` |
| M01I | The same for the Imperium | `100:killall 1;110:spawn 0 trainingfacility.cyborg 12,12;120:spawn 0 assemblyplant.cyborg 16,6` |
| M02I | Five plasma turrets and no enemy buildings | five `spawn 0 plasmaturret` from `10,28` to `22,28`, then `100:killall 1` |
| M03F | Free every prison (a unit in each region), raze team 1 | `50:killunits 1`, `teleport u69644` through 18,71 13,13 70,7 56,76 76,77 76,88 56,89, `spawn 0 raider 65,82`, `680:killall 1` |
| M05F | 30000 credits | `100:cash 0 30000` |
| M06F | Steal the hover transporter plans, then destroy the assembly plant | `10:killtype 1 plasmaturret;20:spawn 0 hq.human 58,58 home;25:spawn 0 infiltrator 56,62 spy;40:steal spy u2316;300:kill u2316` |
| M09F | Karoch (u47470) to the transport | `100:teleport u47470 109,112` |
| M09I | Karoch (u24570) to the transport | `100:teleport u24570 109,112` |
| M13T | Destroy teams 1 and 2, then a unit at Togra's workshop | `100:killall 1;101:killall 2;200:spawn 0 raider 112,99` |

The rest (M02F, M03I, M04, M05I, M06I, M07, M08, M10–M12) win when
`wintest.ps1` destroys their enemies. Its "losses" in M03F and M09 come from
killing teams the player must protect, as alliances change at cycle 0.

## Lessons that cost time

- **`Map.Contains` tests projected cells**: terrain height lifts cells near
  the top edge out of it. The importer checks the plain map size, then nudges
  actors down until they are visible; an actor with no visible footprint
  crashes the engine.
- **Map rules merge by exact actor name**: an override must use the mod's
  own casing (`Power.Constructing`, not `power.constructing`), or the game
  fails on duplicate actors. The importer reads the casing from the mod's
  rules files.
- **Tech levels come from `deftxt` only**: the expansion's `deftxtEx` tables
  raise every original unit to level 40.
- **OpenDR doesn't load `structures-retail.yaml`**, so anything defined only
  there (`FGPlanetaryDefense`) is unusable; the finale uses
  `FGPlanetaryDefense2`.
- **A missing unit or building counts as destroyed** in `CritDestroy*`. If
  the importer skips an actor that an end tree names, a mission can be lost
  or won at cycle 0. `check-missions.py` lists such ids; those it lists now
  are absent from the original scenarios too.
- **The mission browser finds maps by folder name**, and only in a `System`
  map folder: `~^SupportDir|maps/dr/campaign: System` in `mod.yaml`.
- **The 1.8.2 copy is not what OpenDR's content installer expects**; it looks
  for GOG or the CD. `import-campaign.ps1` copies the files itself, the
  "Auran extra content" OpenDR would otherwise download included.
- **The package shares one folder and one runtime** between the launcher
  (WPF) and the engine, and where both bring a file the higher version must
  stay: the engine's NuGet `System.Threading.Channels` 9 over the runtime's
  8, WPF's `WindowsBase` 8 over the runtime's 4.0 stub. Publishing both into
  one folder gets this wrong, because publish copies by date. `package.ps1`
  publishes the launcher apart and merges by version, then checks the
  packaged game host before zipping. The launcher needs a window to check:
  start `build/Dark Reign/DarkReign.exe` once.
- **Building the game host with extra properties rebuilds `OpenRA.Game`**:
  global properties flow to project references. `launcher/build.ps1` passes
  `BuildProjectReferences=false`.
- **Backslashes in shell heredocs get mangled** by the Bash tool (`\b` in
  `engine\bin` became a backspace twice, the briefing codes `\0`–`\3` NULs).
  Edit Windows paths with the Edit or Write tools, and scan for control
  characters after scripted edits.
- **Textures must be powers of two** in size, or the engine throws on
  upload. The shell's art takes the top left of a larger sheet, and its
  enlargement stops at 3× so a screen fits 2048².
- **A game's end pauses the world** (`World.EndGame`): world traits stop
  ticking, which is why `DebugScreenshots` runs late steps in real time.
- **Training briefings have no `\0`**: their description is the text before
  `\1`.
- **Change screens in `Tick`, never in `Draw`**: showing a screen can read a
  mission's map, which can put up OpenRA's loading screen, and that ends the
  frame being drawn (`EndFrame called with renderType = None`).
- **Read the original's code before guessing**: `dkreign.exe` is packed, but
  `python tools/campaign/dkreign-exe.py unpack` (pip: unicorn, capstone,
  pefile) emulates its unpacking stub and saves the image; then `strings`,
  `xrefs` and `dis` find what uses a file or value. The shell's screens are a
  switch on the screen number at 0x587274, the turns at 0x586000, the key at
  0x57f421, the sounds at 0x586740 and 0x579600. Guessed from the videos, half
  the turns were wrong and the intro played at the wrong time.
- **Look at a Smacker video before guessing its use**:
  `dotnet bin\OpenRA.Utility.dll dr --dump-smacker FILE OUT [COLUMNS] [SCALE] [EVERY]`
  (from `engine`, with `MOD_SEARCH_PATHS` and `ENGINE_DIR` set as
  `build-import.ps1` does) writes a sheet of its frames and its sound as a
  WAV.

## Open work, roughly by value

- **A human playthrough.** Nothing has been played by hand; the user's play
  is the real test. Expect AI tuning (`DrAipBotModule`) to follow.
- **Stolen designs buildable.** Plans are recorded (`DrPlanStealing.cs`) but
  grant nothing: OpenRA prerequisites have no "or", so it needs a generated
  copy of each stealable actor with its own prerequisite.
- **The expansion campaigns** (`sh*`, `fgx*`) convert but lack most units.
  They would need tech tables from `deftxtEx` and the Shadowhand and Xenite
  units added to OpenDR.
- **The rest of the menus.** The original menus, their videos and sounds,
  the archive and the credits are in
  ([CAMPAIGN.md](CAMPAIGN.md#the-original-menus)). Still to come: the
  debrief's statistics grid (`SS_COLLECTED`, `SS_CREATED`, `SS_LOST`,
  `SS_DESTROYED` by `SS_WATER`, `SS_TAELON`, `SS_UNITS`, `SS_BLDGS`, in
  `dark/local/MLSTRING.CFG`), which needs the mission to report them, and
  the original's own Load Game and Custom Mission screens (`loadgame`,
  `custom`). The expansion's movies (`rsintro`, `rSOUTROS`, `rSOUTROX`) and
  credits (`AddCredt.txt`) wait for its campaigns. `dkreign-exe.py` answers
  how the original did each. The goal is a remaster: the original menus with
  the game on the new engine.
- **Distribution polish**: the package is a zip. An installer (Start menu,
  uninstall) and a code-signing certificate (unsigned, Windows SmartScreen
  warns on first run). The GOG install lookup has never met a real GOG
  install.

## Committing

Commit to `master` and push. Never commit `DrData/` or anything converted from
the game. End messages with:

```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
