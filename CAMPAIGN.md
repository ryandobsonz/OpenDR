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
   menus' videos and the movies included, and converts every mission. Run it by hand with mission names
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
| Main menu | Single Player; Multi Player, Instant Action (a skirmish), Construction Kit (the map editor), Credits and Replays open OpenRA's panels over the shell's art; Settings in the left corner, as the original's Darker sat there; Quit asks with the original's dialog. Replay Intro plays the intro |
| Single Player | Continue Campaign, Start New Game (which asks before clearing progress), Load Game, Play Custom Mission (the mission browser) |
| The cube: missions | The mission ring around the Encryption Key, read as a clock: mission N at N o'clock, the gate at the top is 12, the key itself 13. A disc lights its side's emblem once that side has won it; locked missions are dark. Click to select, again (or the arrow above) to open. Basic and Advanced Training on either side; the left arrow turns to Options, the bottom arrow leaves |
| Mission background | The briefing's setting (its `\0`); the Freedom Guard or Imperium emblem at the top picks the side |
| Briefing | The orders (`\1`), Freedom Guard's or Imperium's screen; **Launch** starts the mission in the engine |
| Debrief | After a win: the historical outcome (`\2`) and Togra's word (`\3`); on to the next mission |
| Training | The two Basic or Advanced missions (`t1`–`t4`), each to its briefing |
| Options | Mission progression; Load Game and Settings (OpenRA's panels); OpenRA Menu, OpenRA's own main menu; Quit to Main Menu or to Windows |

As in the original, missions open in order: the first always, each next once
the one before is won from either side, the Togran's once all twelve are.
Progress is kept in `%APPDATA%\OpenRA\dr-campaign.yaml` (the missions won,
by map name); the mission's own script records a win, so missions played
from the mission browser count too. Escape goes back a screen.

`shell.rli` lists the library's images (`ILR.`, 32-byte entries: name,
type, offset, packed and unpacked size). Each is LZ packed: a flag byte per
eight items, a set bit a literal byte, a clear one a 16-bit reference of 12
bits of distance back and 4 of length less 3. Unpacked, an image is a `TLF.`
container of a `3BGR` palette chunk and a `LXIP` chunk of width, height and
8-bit pixels; colour 0 is transparent. A font is a strip of glyphs, one per
character code, between full-height columns of the colour in its top left
corner. The button positions are the game's own (`dark/shell/shellCFG.h`);
the rest are where each lit overlay matches its screen.

### The menus' videos

The shell plays the game's own Smacker videos (`dark/shell/*.SMK`, 640×480 at
10 frames a second, and the movies in `dark/movies`, 320×240 at 15), as the
original did between its screens:

| Video | When |
|---|---|
| `CUBE_IN` | The cube rises from the bridge's table and the view closes on its face: Single Player into the cube, and back from a mission |
| `CUBE_OUT` | The face's panel draws in and the cube sinks into the table: leaving the cube |
| `CUBE02`, `CUBE03` | The panel draws into the glass cube and the cube turns, bringing round the face on the left (Options) or the right; Options back to the missions turns the other way |
| `CUBE_UP2`, `CUBE_DN2` | The same, rolling the cube to bring down the face above (the mission background) and back |
| `CUBE04`, `CUBE05` | Rolling the other way: to training, the face below, and back from training and the debrief |
| `CUBE01` | The last part of every turn: the new face's panel comes out of the cube and fills the screen (`CUBE_IN` is `CUBE00`, the rise, and `CUBE01`) |
| `BRIEF_F`, `BRIEF_I` | An iris opens from the face onto the briefing room |
| `M_RING00`–`11` | The Encryption Key, in the hole the missions face leaves for it at (248, 190): concentric rings whose tumblers turn until they line up under the slot, one for each mission won (from either side), a beam reaching further in with each. Each loops |
| `M_RING12` | All twelve won: the key lights up, a spiral of lights and then the bright key, held |
| `M_TOGRAN` | The key selected, as the Togran's mission: the key alive, a red swirl |
| `INTRO` | The first time the game starts, and Replay Intro |
| `SEGUE` | Opening the Togran's mission: the key is complete, the cube goes into the bridge's table and the Togran's planet appears; then its briefing |
| `OUTRO` | After the Togran's mission is won, before the debrief |

Which turn goes with which arrow is read from the videos (a turn's edges
sweep right in `CUBE02`, left in `CUBE03`, down in `CUBE04`, up in `CUBE05`);
the executable is packed, so the original's own pairing is unknown. Every turn
ends on the frame where `CUBE00` ends, which is where `CUBE01` begins. The
faces in the videos are a bare grey stone where the still art has its brown,
veined face with buttons; the last frame fades into the screen in a third of a
second, and the screen before into the first frame. A click or a key skips to
the screen after. `M_RING13`, eight frames of the lit key brightening, with
black corners and a white edge, is not used.

The 1.8.2 patch's `fix_cubes` mod has a `CUBE02` whose sound does not cut out
after two seconds; the import takes it when it is there. Its `fast_cubes`
mod (30 frames a second, two seconds a turn) is not used: the original's turns
take seven and a half seconds.

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
Their sound plays at the effects volume, the movies' at the video volume, and
music pauses for the movies.

Code: `FileFormats/DrShellLibrary.cs` (the files), `Graphics/DrShellArt.cs`
(textures and fonts), `FileFormats/SmackerVideo.cs` (the videos),
`Graphics/DrShellVideo.cs` (playing one), `Widgets/DrShell*Widget.cs` (the
640×480 screen, its videos between screens, and its buttons, text and ring),
`Widgets/Logic/DrShellLogic.cs` (the screens),
`DrCampaign.cs` (progress), `mods/dr/chrome/shell.yaml` (the layout).

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
- **Units' tactical settings** (`SetTactAI`: pursuit, damage tolerance,
  independence) are not read: every campaign use sets pursuit medium or
  high, which OpenRA's default stance already is.
- **The expansion's campaigns** (Rise of the Shadowhand, the Xenite missions)
  convert but lack most of their units and buildings.
- **The menus are not all there**: the cube turns, the key fills in and the
  movies play ([The menus' videos](#the-menus-videos)), but the menus' own
  sounds are missing, the credits are OpenRA's, and the cube's archive face
  and the debrief's statistics are not there yet.

## Working on it

Building, the test scripts and the scripted win tests for every mission:
[CLAUDE.md](CLAUDE.md).
