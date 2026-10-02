# The original campaign

OpenDR plays the original Dark Reign campaign by converting its missions from a
full copy of the game. Each converted mission carries its original scenario,
trigger, end-condition, AI and briefing files, and runs them in game.

Game data never enters this repository: it is copyrighted and comes from your
own copy. The converted maps go to your OpenRA map folder
(`%APPDATA%\OpenRA\maps\dr\campaign` on Windows).

## Setting it up

1. Build OpenDR: `make.cmd all` (needs the .NET 8 SDK), then
   `pwsh -File launcher/build.ps1`, which puts the launcher, `DarkReign.exe`,
   in the root and the game host, `DarkReignGame.exe`, in `engine/bin`.
2. Start `DarkReign.exe`, choose **Install from game** and pick a full copy
   of the game (the 1.8.2 community patch, or GOG). That runs
   `import-campaign.ps1 -GameDir "<the folder holding dark>"`, which installs
   the game content OpenDR reads, the snow tileset, the CD soundtrack, the
   menus' videos and sounds and the movies included, and converts every
   mission. Run it by hand with mission names
   after it (`M01F M01I`) to convert only those.
3. **Play** opens the original game's menus: **Single Player → Start New
   Game** leads to the mission ring, where each mission is played from
   either side, ending in mission 13, The Togran. See
   [The original menus](#the-original-menus).

Run the import again after changing the importer: the maps are rebuilt from
the game files each time.

`pwsh -File packaging/windows/package.ps1` builds what a player downloads:
`build/Dark Reign/` and a zip of it, self-contained (no .NET install needed),
the engine, mod, game host and launcher together, with a player's
`Read Me.txt` (`packaging/windows/`), without game data. Its version is the
launcher project's `<Version>` unless `-Version` is given. It
builds from a copy of the sources in `build/src`, so the working `engine/bin`
is left alone.

## The launcher

`DarkReign.exe` (`launcher/`, WPF) is the front door: Play, installing the
game data, and the display settings, over the 1.8.2 patch launcher's art when
the import has copied it. Its settings are the game's own, in OpenRA's
`settings.yaml`, so it and the in-game Display menu always agree:

| Setting | Choices |
|---|---|
| Display mode | Fullscreen (exclusive, any of the monitor's modes), Borderless (the desktop resolution; the default), Windowed |
| Monitor | Each monitor by name, numbered as the engine (SDL) numbers them: the primary first |
| Resolution | The monitor's modes for fullscreen; common window sizes that fit it for windowed |
| Interface size | 100–200%, limited to what leaves the game its minimum 1024×720 layout |
| Battlefield zoom, VSync | As in the game |
| Menus | Faster cube turns, the 1.8.2 patch's (on unless turned off, as in the patch's launcher); kept in `dr-shell.yaml`, as the game rewrites `settings.yaml` with its own fields alone |
| Game data | Installs it, or again, from a chosen Dark Reign folder |

It finds the engine in either layout: `engine/bin` beside it in the
repository, or everything beside it in a package.

- **The game runs as `DarkReignGame.exe`**: OpenRA's own Windows launcher
  (`engine/OpenRA.WindowsLauncher`) built with the mod's name and icon, so
  the taskbar and Task Manager show Dark Reign. Without it the launcher falls
  back to `OpenRA.exe`. The window title is the mod's `mod-windowtitle`
  (`mods/dr/fluent/dr.ftl`).
- **`OPENRA_DISPLAY_SCALE=1`**: sizes are real pixels and Interface size
  alone enlarges the interface; without it the engine also multiplies by the
  Windows display scale.
- **Borderless stores a fullscreen size of 0,0**: the engine sizes its
  drawing surface from that setting, and any other value leaves it smaller
  than the screen. The launcher corrects it before every start.
- **Restarts come back through it**: the engine registers the launcher as
  its launch path, and when a changed setting needs a restart it runs the
  launcher with arguments. The launcher then starts the game straight away
  and stays running, hidden, until the game ends: the engine takes a
  launcher that has already exited for a failed restart.
- **Failures**: if the game exits with an error, the launcher comes back
  with a button to the logs.
- **Installing** starts the folder picker in a copy it finds: GOG's registry
  entries (any game named Dark Reign), the usual GOG folders, or `DrData`
  beside a checkout. The GOG lookup is untested against a real GOG install.
  The window will not close mid-install.
- **One at a time**: opening it again brings forward the launcher, or the
  game if it is running, instead of a second launcher. Engine relaunches are
  exempt.

## The original menus

The game opens on Dark Reign's own menus, the "shell", drawn from the
game's `dark/shell/shell.rld` (the import copies it with `shell.rli`, its
index) at their original 640×480, scaled to fit the screen at 4:3 with
black either side. Each image is first enlarged by a whole factor (up to 3)
pixel for pixel, then filtered to the screen's size: sharp, without the
uneven pixels of a plain stretch. Text is set in the shell's own bitmap
fonts. Without the shell files (an import from before them) the game opens
OpenRA's menu, and the launcher offers to install again.

| Screen | What it does |
|---|---|
| Main menu | Single Player; Multi Player, Instant Action (a skirmish), Construction Kit (the map editor) and Replays open OpenRA's panels over the shell's art; Settings in the left corner, as the original's Darker sat there; Quit asks with the original's dialog. Replay Intro plays the intro; Credits rolls the original's |
| Credits | The original's credits rolling up their box as it rolled them (`shell/USACREDT.TXT` and `AUSCREDT.TXT` side by side, Activision's centred at 220 and Auran's at 420, then `CREDITS.TXT`; `~T` a title in red, `~N` a name), starting after a pause at a pixel a tick; `<<` and `>>` change the speed a pixel at a time, to 30 either way, and the roll comes round again. The 1.8.2 patch's own credits head Activision's; the expansion's (`AddCredt.txt`) are left out, as the original leaves them out of its campaign. OpenDR and OpenRA close it |
| Single Player | Continue Campaign, Start New Game (which asks before clearing progress), Load Game, Play Custom Mission |
| Load Game | The original's saved game selection (`loadsave`): the campaign's saved games, newest first; for the one chosen, where it was saved ("In Mission 5"), its mission, side and date, and its name below. Load Game, Delete (which asks first), Previous Menu. A loaded mission comes back to its debrief as a launched one does |
| Play Custom Mission | The original's custom mission selection (`custom`): the missions that are not the campaign's (the expansion's, OpenDR's own, the player's), by folder name, and their saved games; for the one chosen, the player's number of enemies, the map's size and the player's side (its scenario's, as the original reads it). Load Game starts or loads it; Delete removes a saved game |
| Results | After a custom mission: the original's results (`cdebrief`), a row for each team a human or the computer played, its side and the mission's statistics; Continue returns to the custom missions |
| The cube: missions | The mission ring around the Encryption Key, read as a clock: mission N at N o'clock, the gate at the top is 12, the key itself 13. A disc lights its side's emblem once that side has won it; locked missions are dark. Click to select, again (or the arrow above) to open. Basic and Advanced Training on either side; the left arrow turns to Options, the right to the archive, the bottom arrow leaves |
| Mission background | The briefing's setting (its `\0`); the Freedom Guard or Imperium emblem at the top picks the side |
| Briefing | The orders (`\1`), Freedom Guard's or Imperium's screen; **Launch** starts the mission in the engine |
| Debrief | After a win: the historical outcome (`\2`) and Togra's word (`\3`), the player's emblem, and the mission's statistics (below); up to the next mission, down to play it again, left to Options, right to the archive |
| Training | The two Basic or Advanced missions (`t1`–`t4`), each to its briefing |
| Options | Mission progression; Load Game (the screen above) and Settings (OpenRA's panel); OpenRA Menu, OpenRA's own main menu (with its mission browser); Quit to Main Menu or to Windows |
| Archive | The cube's right face: `shell/ARCHIVE.TXT`, Togra's final message, the history of the conflict, biographies, unit specifications and a journal, as menus of pages; Up One Level climbs. As in the original, the journal has an entry for each mission won and one more |

As in the original, missions open in order: the first always, each next once
the one before is won from either side, the Togran's once all twelve are.
Progress is kept in `%APPDATA%\OpenRA\dr-campaign.yaml` (the missions won,
by map name); the mission's own script records a win, so missions played
from OpenRA's mission browser count too. Escape goes back a screen.

The shell's art has a screen the original never shows (`loadgame`); its Load
Game and Custom Mission use `loadsave` and `custom`, and their layouts are
`dkreign.exe`'s own (the load screen's entries in `shellCFG.h` are unused).
Its plain text is drawn from the top of its area, and centred across it.

Saving happens in a mission, from OpenRA's in-game menu, and a loaded game
opens paused under that menu, as OpenRA's do. The original's saves also
kept the campaign's progress, which its Load Game showed as the mission
progression; OpenRA's keep a mission alone, so Load Game shows that
mission's title, side and when it was saved instead.

`shell.rli` lists the library's images (`ILR.`, 32-byte entries: name,
type, offset, packed and unpacked size). Each is LZ packed: a flag byte per
eight items, a set bit a literal byte, a clear one a 16-bit reference of 12
bits of distance back and 4 of length less 3. Unpacked, an image is a `TLF.`
container of a `3BGR` palette chunk and a `LXIP` chunk of width, height and
8-bit pixels; colour 0 is transparent. A font is a strip of glyphs, one per
character code, between full-height columns of the colour in its top left
corner. The button positions are the game's own (`dark/shell/shellCFG.h`);
the rest are where each lit overlay matches its screen.

### The menus' videos and sounds

The shell plays the game's own Smacker videos (`dark/shell/*.SMK`, 640×480 at
10 frames a second, and the movies in `dark/movies`, 320×240 at 15) and its
own sounds (`dark/shell/SOUNDS.FTG`) where the original's code does: the
packed `dkreign.exe`, unpacked by `tools/campaign/dkreign-exe.py`, has the
table of which turn joins which faces, and when each video and sound plays.

| Video | When |
|---|---|
| `INTRO` | Start New Game, before the cube rises (the original's intro introduced the campaign; nothing plays at start); and Replay Intro |
| `CUBE_IN` | The cube rises from the bridge's table and the view closes on its face: into the cube from Single Player |
| `CUBE_OUT` | The face's panel draws in and the cube sinks into the table: leaving the cube |
| a turn, then `CUBE01` | Between two faces: the face's panel draws into the glass cube, the cube turns, and `CUBE01` brings the next panel out. `CUBE02` to Options (the face on the left) and back from the archive; `CUBE03` to the archive and back from Options. Otherwise the faces are in a column, in the original's order (missions, options, archive, story, training, the bare face, briefing, debrief), and the cube turns up (`CUBE04`) to a face further on and down (`CUBE05`) to one before; except the missions face to the debrief `CUBE05` and back `CUBE04`, and to the Togran's briefing `CUBE_UP2` and back `CUBE_DN2` |
| `BRIEF_F`, `BRIEF_I` | After the turn to a briefing, an iris opens from the face onto the briefing room |
| `M_RING00`–`11` | The Encryption Key, in the hole the missions face leaves for it at (248, 190): concentric rings whose tumblers turn until they line up under the slot, one for each mission won (from either side), a beam reaching further in with each. Each loops |
| `M_RING12`, `M_TOGRAN` | All twelve won: the key lights up, then comes alive, a red swirl, looping |
| `SEGUE` | Opening the Togran's mission from the key: the key is complete, the cube goes into the bridge's table and the Togran's planet appears; then its briefing |
| `OUTRO` | After the Togran's mission is won; then the credits roll, as in the original |

Back from a mission the cube shows a bare face (`return`) and turns to the
debrief after a win (`CUBE04`), the missions after a loss (`CUBE_UP2`), or
training (`CUBE05`).

The faces in the videos are a bare grey stone where the still art has its brown,
veined face with buttons; the last frame fades into the screen in a third of a
second, and the screen before into the first frame. A click or a key skips a
movie to what follows it, and the menus' videos to the screen after. Unused,
as in the original: `CUBE00` (the rise alone; `CUBE_IN` is `CUBE00` then
`CUBE01`) and `M_RING13` (the lit key brightening, which the original's count
of missions never reaches). The 1.8.2 patch's `fix_cubes` mod has a `CUBE02`
whose sound does not cut out after two seconds; the import takes it when it is
there. Its `fast_cubes` mod has the turns, the cube's rise and fall and the
irises at 30 frames a second where the original's run at 10: a turn and the
panel coming out take two and a half seconds, where the original's take seven
and a half. The patch's launcher plays them unless told not to, and so does
this one (Settings → Menus); the import puts them in `shell/fast`.

The sounds: `bridge3.wav` hums under the main menu and single player,
`bridge.wav` on the bridge and the cube's faces (it stops as the cube sinks
and for the movies), and on the cube's faces one of fourteen sounds of the cube
at work (`punct_1`–`14`) plays every 24 to 36 seconds, chosen at random. They
play at the effects volume, at 90/127 as the original set them; the videos'
own sound at the effects volume, the movies' at the video volume, and music
pauses for the movies.

A Smacker file is a 104-byte header (size, frames, frame rate, seven audio
tracks' rates and flags), each frame's size and type (a palette, which audio
tracks), four Huffman trees and the frames. A frame is a palette change
(entries kept, copied from the last palette, or new in six bits a colour),
each audio track's data, then the picture in 4×4 blocks, each run of blocks
one type: two colours and a 16-bit map, sixteen pixels coded two at a time,
unchanged, or one colour. The trees code 16-bit values as pairs of 8-bit
codes, and three escape values hold the last three decoded. Audio is DPCM:
each sample's change from the one before, Huffman coded per byte. The shell's
videos are drawn as its still art is (enlarged by a whole factor, then
filtered); the movies only filtered, or their dithering shows as blocks.

### The debrief's statistics

Under the outcome the debrief has the original's grid: water and taelon
collected, then units and buildings created, lost and destroyed, a row for
the player's team and one for team 1, each named by its side. The results
after a custom mission have a row for every team a human or the computer
played. In each column the highest figure is framed in red. The layout and
the counting are the original's, read from `dkreign.exe`:

- **Created**: every unit and building a team comes to have, those the
  mission starts with included (the original resets its counts before it
  places them). A building counts once, as its construction begins; a
  construction rig that becomes it is not lost.
- **Lost**: every one that dies, whoever killed it. **Destroyed**: those a
  team kills of another team's (its own don't count).
- **Collected**: what refineries take in, water and taelon apart, in
  credits.

`Traits/World/DrMissionStatistics.cs` keeps the figures, which every unit
and building reports through `Traits/DrCountsInStatistics.cs`; the mission's
end hands teams 0 to 7 to the shell (`DrCampaign.LastStatistics`), and
`Widgets/DrShellStatisticsWidget.cs` draws them in either layout.

Code: `FileFormats/DrShellLibrary.cs` (the files), `Graphics/DrShellArt.cs`
(textures and fonts), `FileFormats/SmackerVideo.cs` (the videos),
`FileFormats/DrArchive.cs` (the archive), `Graphics/DrShellVideo.cs`
(playing a video), `Widgets/DrShell*Widget.cs` (the 640×480 screen, its
videos between screens, and its buttons, text, menus, ring and credits),
`Widgets/Logic/DrShellLogic.cs` (the screens), `DrCampaign.cs` (progress),
`mods/dr/chrome/shell.yaml` (the layout).

## The in-game interface

A mission is played under the original game's own interface ("IGI" in
`dkreign.exe`: `Igidisp.c`, `Igizone.c`, `Igievnt.c`), drawn from its art,
`dark/graphics/INTFACE/IGI` (the import copies it to `content/igi`, with
`dark/local/MLSTRING.CFG`, the game's own words for every label and tip).
The original is 640×480 only: a bar across the top of the map view (0–447,
32 high) and a panel down the right (448–639).

| Part | Original place | What it does |
|---|---|---|
| Top bar | 0,0 | Sell/Cancel, Power, Repair (49×32 each, from 6); the credits (153–293, `FONT16`); Attack, Attack Without Moving, Stop (from 294). Ends from `TOPBITS.BMP` |
| Tabs | 448,0 | BUILD, COMMS, MENU over ORDERS, PATHS, SPECIAL, 64×32 cells of `MFDBTNS.BMP` |
| Panel | 448,64 | `MFDBAC1.BMP`, 192×278, under the tab's controls |
| Build menu | 448,64 | 3×5 slots of 64×50 (`BUISOBOX.BMP`); its bar at 448,314 with the scroll arrows, Upgrade and Decoy |
| Minimap | 448,342 | `MINIMAP.BMP`, the map inside at 455,351 (126×122); `Static00`–`07` when there is no picture |
| Team lights | 588,342 | `TEAMPIC.BMP`, a light for each of eight teams |
| Resource bars | 588,376 | `RESOBARS.BMP`: power left, water right; the second frame, the lightning red, when power is short |

**On a wider screen** the whole is scaled by the screen's height over 480
(at 1080p, 2.25); the panel keeps to the right edge at full height and the
map view takes the rest. The top bar keeps its pieces in the original order:
the left buttons stay left, the right ones by the panel, the credits in the
middle of the map view, and between them the bar's plate (the column beside
each join) stretches. Its art and text are enlarged as the menus' are. The
original drew all of it over black, which shows through the art's gaps.

**Where the positions come from:** `dkreign.exe`'s zone table. Every
clickable area is set up by one function, `zone(id, type, left, top, right,
bottom, flags, handler)` at 0x466610, called with constants from
0x4bde70–0x4c0200, directly or through the standard button (0x4bda60:
label, size, x, y, the tabs it shows in), whose sizes are set at 0x432110:
small 71, medium 103, large 153, all 22 high, four states each in
`SBTNS.BMP`. The bitmaps are a table at 0x5d076c (name, width, height,
index), loaded into objects at 0x73f4b0 + 16 × (index − 1); the draws are
calls to 0x49cba0 (x, y, bitmap, source x, y, width, height). The flags are
the tabs a control belongs to: 2 build, 4 and 8 orders (basic, advanced), 16
and 32 paths, 64 comms, 128 menu, 256 special.

| Tab | Controls | Working |
|---|---|---|
| BUILD | The build menu: a rig's buildings, else every production building's units; red without the prerequisites, blue when the selected building cannot make it; left click orders one more or resumes, right click pauses then cancels, shift and right click cancels all; a number for those queued, PAUSED, a veil for the time left. Mouse wheel and arrows scroll | All but Decoy (no decoys in OpenDR yet). Upgrade queues the selected building's own upgrade at the headquarters (`upgrade.hq*`, `barracks*`, `assemblyplant*`, `phasing*`), its tip "Upgrade 2050c" |
| MENU | Sliders for effects, music, game speed and scroll speed (`MEICON.BMP`, `MESLIDE.BMP`); Load/Save Game, Restate Objective, Start Again (Relinquish Control in multiplayer), Abort, Exit To System, the last three behind the original "Are You Sure?" | Load/Save Game and Restate Objective open OpenRA's in-game menu for now; game speed is fixed in OpenRA once a game starts |
| ORDERS | Basic: Scout, Harass, Search & Destroy; Guard, Pursue, Default. Advanced adds Pursuit Range, Damage Tolerance and Independence (LOW/MED/HIGH, `ORLMH.BMP`) and Set Default | Guard |
| PATHS | Basic: Add Waypoints, Clear All, Delete, Go. Advanced adds the path direction (one way, patrol, loop: `TRAILMDE.BMP`), the current and saved paths, De-Select, Save Path | None yet |
| SPECIAL | Morph, Unmorph, Phase, Unphase, Self Destruct, Formation Move, Sell Water, Packup/UnPack, Set Exit Point | Set Exit Point (the building's rally point) |
| COMMS | The players with their alliances, giving units or credits, messages to all, none, allies, neutral or enemies | None yet |

Controls not working yet are drawn as the original drew a disabled button:
red. Escape cancels a pending order, else opens the MENU tab. Tips appear in
the original's strip (`PT.BMP`) after 800 ms, as `TACTICS.CFG`'s
`InfoDelay`. The game's end still opens OpenRA's in-game menu, whose Leave
returns to the menus.

Code: `FileFormats/DrIgiLibrary.cs` (the bitmaps, PCX fonts and strings),
`Widgets/DrIgiWidget.cs` (the frame's scaling, and its buttons, bars,
sliders, boxes and labels), `Widgets/DrIgiBuildMenuWidget.cs`,
`Widgets/Logic/Ingame/DrIgiLogic.cs`, `Traits/DrAttackInPlace.cs` (Attack
Without Moving), `Orders/DrTargetOrderGenerator.cs`,
`mods/dr/chrome/ingame-player.yaml` (the layout, in the original's
coordinates).

## Playing on a large screen

OpenRA draws at the screen's own resolution and aspect ratio, ultrawide
included, and scales the battlefield so its visible height stays in a band
set by **Battlefield zoom** (in the launcher, or the game's Settings →
Display): Close shows 480 to 600 game pixels top to bottom, about the
original's 640×480 view; Medium 600 to 900; Far 900 to 1300. The mouse wheel
zooms within the band. At 4K, also set **Interface size** (the game's UI
Scale) to 200%, or the sidebar is small. The art is the 1997 art scaled up:
sharper and larger, not more detailed.

## How it works

| Part | Code | What it does |
|---|---|---|
| Import | `UtilityCommands/ImportDrCampaignCommand.cs` | Terrain, units, buildings and scenery from the `.map` and `.scn`; one player per original team, with its alliances; the mission files copied into the map; tech level limits; the briefing |
| Formats | `FileFormats/DrScript.cs`, `DrScenario.cs`, `AipFile.cs` | The scenario script syntax shared by `.scn`, `.fsm` and `.end`; the AI personality format |
| Triggers | `Traits/World/DrScenarioScript.cs`, `Scripting/DrConditionTree.cs` | The FSM and end-condition trees, special forces, patrols, messages and their voices, the win or loss with the briefing's historical outcome |
| Enemy AI | `Traits/BotModules/DrAipBotModule.cs` | Each AI team builds through its AIP's accounts and sends troops by its priorities |
| Menus | `Widgets/Logic/DrShellLogic.cs` | The original menus and mission ring, from the game's shell art; see [The original menus](#the-original-menus) |
| Interface | `Widgets/Logic/Ingame/DrIgiLogic.cs` | The original in-game interface over a mission; see [The in-game interface](#the-in-game-interface) |

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
- **Not yet in OpenDR:** phasing, the water contaminator, water and taelon
  as separate resources (collected resources count as credits;
  `CritCollectWater` and `CritCollectMineral` use credits earned), decoys,
  morphing, self destruct, formation moves, forced water sales and packing
  up Freedom Guard buildings. No original campaign mission needs them to be
  won, but the interface has buttons for them (see above) and they are owed.
- **Units' tactical settings** (`SetTactAI`: pursuit, damage tolerance,
  independence) are not read, and neither are the player's orders (Scout,
  Harass, Search & Destroy) or the original's paths: every campaign use sets
  pursuit medium or high, which OpenRA's default stance already is.
- **The expansion's campaigns** (Rise of the Shadowhand, the Xenite missions)
  convert but lack most of their units and buildings.
- **The debrief's water and taelon** are the credits each brought in: OpenDR
  pays for resources in credits.

## Working on it

Building, the test scripts and the scripted win tests for every mission:
[CLAUDE.md](CLAUDE.md).
