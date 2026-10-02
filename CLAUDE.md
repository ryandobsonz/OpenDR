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
`select NAME`, `explore TEAM`, `camera X,Y`, `press WIDGET` (an in-game
interface button by its id in `ingame-player.yaml`, e.g. `TAB_MENU`, or a
button of an OpenRA window, e.g. the settings' `BACK_BUTTON`), `key KEY
[MODIFIERS]` (a key through the real input path, by OpenRA's key names:
`key A Shift`, `key F1`, `key BACKQUOTE`),
`clickui X,Y` and `rclickui X,Y` (a click through the real input path, in
the UI's pixels: the window's size over the interface scale, so 1280×720
for the tests' 1600×900 at 125%; the debug log says what was under it).
Names are map actor names (`u<id>`, the original unit id) or those given to
`spawn`. `order` exists, but its
orders never reached units; use `teleport` and `steal` instead. `leave`
returns to the menus, as the in-game Leave does. `save NAME` saves the game
(NAME without spaces); a loaded game goes straight on, and its replay up to
the save runs no steps, so give later steps ticks past the save's. `type
TEXT` types into whatever has the keyboard (the Load/Save popup's name). A
mission's end shows its "Mission Successful" or "Failed" popup; `press
MISSION_END_CONTINUE` leaves as its Continue does.
The game pauses when it ends, or when a step opens a window that pauses it
(the settings, the in-game menu), so steps still to come then run a second
apart in real time. Saves go to the player's own `Saves` folder: delete test saves.

The menus have their own script. Without `-Mission`, `run-game.ps1 -Shell`
sets `OPENDR_SHELL`: steps 40 ticks apart, each screenshotted 30 ticks in.
A step is a screen (`main`, `quit`, `single`, `credits`, `cube`, `story`,
`briefingf`, `briefingi`, `training`, `options`, `archive`, `debrief`,
`loadgame`, `custom`, `results`), optionally with a mission
and side (`story:3`, `briefingi:7:i`; locks are ignored), or
`click:X:Y`, a click at a point of the 640×480 screen through the real input
path. Clicks play the videos between screens (a cube turn is 2.5 seconds
with the 1.8.2 patch's fast turns, the default, and 7.5 with the original's);
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
- **The in-game interface's layout is in `dkreign.exe`'s zone calls**, not
  guessed: every control is a call to 0x466610 (or the button helper
  0x4bda60) with constants, and every draw a call to 0x49cba0. A short
  capstone script that tracks pushes and `mov reg, imm` before each call
  lists them all; [CAMPAIGN.md](CAMPAIGN.md#the-in-game-interface) has the
  addresses. The manual's screenshots (`DrData/manuals/Dark Reign/Dark Reign
  Manual.pdf`, pip `pymupdf` to extract) confirm the arrangement.
- **Python on Windows writes CRLF** in text mode: open files with
  `newline=''` and `encoding='utf-8'` when editing sources from a script
  (without it, cp1252 fails on `−` or `→` halfway through a write and leaves
  the file truncated). Working copies are CRLF or LF as git's autocrlf
  checked them out; keep each as it is. The Edit tool can join lines when it
  removes a whole line from a CRLF file: check `git diff` after.
- **Test clicks must run unsynced**: `Game.RunAfterTick` runs inside the
  world's synced tick, where changing the order generator throws; wrap UI
  actions in `Sync.RunUnsynced`.
- **Look at a Smacker video before guessing its use**:
  `dotnet bin\OpenRA.Utility.dll dr --dump-smacker FILE OUT [COLUMNS] [SCALE] [EVERY]`
  (from `engine`, with `MOD_SEARCH_PATHS` and `ENGINE_DIR` set as
  `build-import.ps1` does) writes a sheet of its frames and its sound as a
  WAV.

## Open work, in the user's order

The goal is a professional **remaster, not a remake**: the entire original
interface rebuilt from the game's own art, with the real graphical gains in
the game itself (OpenDR on the new engine). What the campaign player touches
comes first; a human playthrough then decides what matters next.

1. **The in-game interface on the campaign's path**
   ([CAMPAIGN.md](CAMPAIGN.md#the-in-game-interface) says what works):
   - Left over, small: the tooltip strip's place (`PT.BMP` is drawn
     right-aligned at x 468 by 0x428a40, its y from a caller reached through a
     pointer, not yet found; ours sits under the control), and the minimap's
     scroll arrows (`MM*.BMP`), which have nothing to do while OpenRA's
     minimap shows the whole map.
   - Then `wintest.ps1` again (not run since the interface landed) and a new
     package, so the user plays a current build.
2. **A human playthrough** of M01–M04 on both sides. Nothing has been played
   by hand; the user's play is the real test. Expect AI tuning
   (`DrAipBotModule`) and a bug list to follow, and let them reorder 3.
3. **The gameplay behind the interface's buttons**, owed even though no
   campaign mission needs them, roughly by how much a player feels it. Each
   lands with its button: enable it in `DrIgiLogic`.
   - Water and taelon as separate resources, and forced water sales
     (`MLS_DISP_WATERSALE`, a double click on the credits); the debrief and
     `CritCollectWater`/`CritCollectMineral` then count them properly.
   - The units' tactics (pursuit, damage tolerance, independence; `SetTactAI`
     in the scenarios) and the orders (Scout, Harass, Search & Destroy,
     Pursue, Default, Set Default).
   - Paths and waypoints (one way, patrol, loop, saved paths): the PATHS tab.
   - Self destruct, formation moves, packing up buildings.
   - Phasing, decoys, morphing, the water contaminator.
   - Stolen designs buildable. Plans are recorded (`DrPlanStealing.cs`) but
     grant nothing: OpenRA prerequisites have no "or", so it needs a
     generated copy of each stealable actor with its own prerequisite.
4. **The interface off the campaign's path:**
   - COMMS: the player rows (`COPYRNM1/2.BMP` at 465,95, 95×18 each; the side
     at 560; alliance icons `COALIANC.BMP` at 571), giving credits (the field
     at 481,296), messages (the chat line at 15,400, 418×33).
   - Multi Player and Instant Action from `graphics/INTFACE/MULTMENU` (the
     manual's screenshots show them), replacing OpenRA's panels.
   - The settings window (the MENU tab's Advanced → Settings, and F1) and the
     music panel still look like OpenRA's. The user chose one Settings button
     over a page of in-place choices and a button per tab; restyling that
     window with the original's art (`TEXTBRDR.BMP` box, `SBTNS.BMP` buttons,
     the PCX fonts) would finish the look.
   - The game speed slider: OpenRA fixes the speed once a game starts.
5. **Distribution polish**: an installer (Start menu, uninstall) and a
   code-signing certificate (unsigned, Windows SmartScreen warns on first
   run). The GOG install lookup has never met a real GOG install.

**Parked, a separate piece of work:** the expansion campaigns (`sh*`,
`fgx*`). They convert but lack most units, and would need tech tables from
`deftxtEx`, the Shadowhand and Xenite units, and the expansion's movies
(`rsintro`, `rSOUTROS`, `rSOUTROX`) and credits (`AddCredt.txt`).

## Committing

Commit to `master` and push. Never commit `DrData/` or anything converted from
the game. End messages with:

```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
