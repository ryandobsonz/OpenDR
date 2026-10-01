# Dark Reign: The Future of War, on OpenRA

The original Dark Reign campaigns, Freedom Guard and Imperium, played on the
[OpenRA](https://github.com/OpenRA/OpenRA) engine at modern resolutions:
fullscreen, borderless or windowed, ultrawide and 4K included. Each mission is
converted from your own copy of the game with its original scenario,
triggers, AI and briefing. It is a fork of [OpenDR](https://github.com/drogoganor/OpenDR).

## Playing

You need your own copy of Dark Reign: the 1.8.2 community patch or the GOG
release. No game data comes with this.

1. Unzip the package anywhere and run **DarkReign.exe**.
2. Choose **Install from game** and pick your Dark Reign folder (the one
   holding the game's `dark` folder). It copies the graphics, sounds and
   music and converts the campaign, in about a minute.
3. **Play**. The campaigns are under **Missions**.

**Settings** in the launcher choose fullscreen, borderless or windowed, the
monitor, the resolution, the interface size and the battlefield zoom.

## Building

With the .NET 8 SDK:

```
make.cmd all                                  # the engine and the mod
pwsh -File launcher/build.ps1                 # DarkReign.exe, and the game host
pwsh -File packaging/windows/package.ps1      # the self-contained package in build/
```

How the campaign port works, what differs from the original and how it is
tested: [CAMPAIGN.md](CAMPAIGN.md). Working on it: [CLAUDE.md](CLAUDE.md).

## About OpenDR

[![Discord](https://img.shields.io/discord/102860784329052160.svg)](https://discord.gg/3MKcGSW)

OpenDR is a mod for the [OpenRA](https://github.com/OpenRA/OpenRA) strategy game engine that aims to recreate the original Auran RTS classic. Created using the [OpenRAModSDK](https://github.com/OpenRA/OpenRAModSDK).

Its releases are on its [Releases page](https://github.com/drogoganor/OpenDR/releases), with help on its [Installation page](https://github.com/drogoganor/OpenDR/wiki/Installation). OpenDR itself installs content from the freely available demo; this fork's campaign needs the full game.

Join us on the [OpenDR discord](https://discord.gg/3MKcGSW) if you want to talk about the mod.

### Running the mod in Visual Studio

You will need to switch to OpenRA.Launcher and set the command line arguments to: `Engine.EngineDir=".." Engine.ModSearchPaths="..\\..\\mods" Game.Mod=dr`

When running OpenRA.Utility, set the environment variables to the following:

| Name             | Value         |
| ---------------- | ------------- |
| ENGINE_DIR       | ..            |
| MOD_SEARCH_PATHS | ..\\..\\mods  |

### Thanks to

* [OpenRA](http://www.openra.net/) and associates
* IceReaper and friends for [KKnD](https://www.kknd-game.com/) and [PixelMagic](https://eiveo.net/pixelmagic.html)
* Nolt and friends for [Shattered Paradise](https://www.moddb.com/mods/shattered-paradise/downloads)
* dr_zaphod for readspr.c
* btigi for [drExplorer](https://github.com/btigi/drExplorer)
* My family and friends