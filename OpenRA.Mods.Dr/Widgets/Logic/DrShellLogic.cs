#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Common.Widgets.Logic;
using OpenRA.Mods.Dr.FileFormats;
using OpenRA.Mods.Dr.Graphics;
using OpenRA.Network;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets.Logic
{
	/// <summary>
	/// The original game's menus: the main menu, single player, and the cube on the bridge, whose faces hold
	/// the mission ring, the mission background, training and options; then the briefing, whose Launch
	/// button hands over to the engine. The game comes back here when the mission ends. Without the shell
	/// art (an import from before it was copied) it opens OpenRA's own main menu instead.
	///
	/// The game's videos join the screens as the original's shell (dkreign.exe) plays them. A new game plays
	/// the intro, then the cube rises from the bridge's table (CUBE_IN); leaving, it sinks back (CUBE_OUT).
	/// Turning to another face draws the face's panel into the cube, turns the cube (Turn) and brings the
	/// new panel out (CUBE01); the briefing then opens through an iris (BRIEF_F, BRIEF_I). The Encryption
	/// Key turns in the ring (M_RING00-12, M_TOGRAN); the segue plays before the Togran's mission and the
	/// ending after it. The bridge hums under the cube's faces and the main menu has its own hum, with now
	/// and then a sound from the cube.
	/// </summary>
	public class DrShellLogic : ChromeLogic
	{
		[FluentReference]
		const string NewCampaignTitle = "dialog-dr-new-campaign.title";

		[FluentReference]
		const string NewCampaignPrompt = "dialog-dr-new-campaign.prompt";

		[FluentReference]
		const string NewCampaignConfirm = "dialog-dr-new-campaign.confirm";

		[FluentReference]
		const string CombatEngineering = "label-dr-shell-combat-engineering";

		[FluentReference]
		const string ResourceManagement = "label-dr-shell-resource-management";

		[FluentReference]
		const string PathManagement = "label-dr-shell-path-management";

		[FluentReference]
		const string UnitAiControls = "label-dr-shell-unit-ai-controls";

		[FluentReference]
		const string FreedomGuard = "label-dr-shell-freedom-guard";

		[FluentReference]
		const string Imperium = "label-dr-shell-imperium";

		[FluentReference]
		const string Won = "label-dr-shell-won";

		[FluentReference]
		const string MissionSuccessful = "label-dr-shell-mission-successful";

		[FluentReference("number")]
		const string InMission = "label-dr-shell-in-mission";

		[FluentReference("number")]
		const string InTraining = "label-dr-shell-in-training";

		[FluentReference]
		const string SideFreedomGuard = "label-dr-shell-side-freedom-guard";

		[FluentReference]
		const string SideImperium = "label-dr-shell-side-imperium";

		[FluentReference]
		const string SideCivilian = "label-dr-shell-side-civilian";

		[FluentReference]
		const string SideTogran = "label-dr-shell-side-togran";

		[FluentReference]
		const string SideXenite = "label-dr-shell-side-xenite";

		[FluentReference]
		const string SideShadowhand = "label-dr-shell-side-shadowhand";

		[FluentReference]
		const string Human = "label-dr-shell-human";

		[FluentReference]
		const string Computer = "label-dr-shell-computer";

		[FluentReference]
		const string DeleteSaveTitle = "dialog-dr-delete-save.title";

		[FluentReference("save")]
		const string DeleteSavePrompt = "dialog-dr-delete-save.prompt";

		[FluentReference]
		const string DeleteSaveConfirm = "dialog-dr-delete-save.confirm";

		// In the original's order from Cube on (its screens 0x19-0x21), which picks the turn between two faces.
		enum Screen { Main, Quit, Single, Credits, LoadGame, Custom, Results, Cube, Options, Archive, Story, Training, Return, Briefing, Debrief }

		static readonly Dictionary<Screen, string> Backgrounds = new()
		{
			{ Screen.Main, "main" }, { Screen.Quit, "main" }, { Screen.Single, "single" }, { Screen.Credits, "credits" },
			{ Screen.LoadGame, "loadsave" }, { Screen.Custom, "custom" }, { Screen.Results, "cdebrief" }, { Screen.Cube, "missions" },
			{ Screen.Options, "options" }, { Screen.Archive, "archive" }, { Screen.Story, "story" },
			{ Screen.Training, "training" }, { Screen.Return, "return" }, { Screen.Debrief, "debrief" }
		};

		static readonly string[][] TrainingMissions = [["t1", "t2"], ["t3", "t4"]];

		/// <summary>A video of the shell's; the 1.8.2 patch's faster version where it has one and the launcher asks for it.</summary>
		DrShellClip Clip(string name)
		{
			var fast = $"content|shell/fast/{name}.SMK";
			return new(fastTransitions && modData.DefaultFileSystem.Exists(fast) ? fast : $"content|shell/{name}.SMK");
		}

		static DrShellClip Movie(string name) => new($"content|movies/{name}.SMK", DrShellVideoSound.Video, 2);

		DrShellClip CubeIn => Clip("CUBE_IN");
		DrShellClip CubeOut => Clip("CUBE_OUT");

		/// <summary>
		/// The turn of the cube between two of its faces, as the original's table has it: the face's panel
		/// goes in and the cube turns, CUBE02 bringing round the face on the left (options) and CUBE03 the
		/// right (the archive), CUBE04 and CUBE_UP2 one above, CUBE05 and CUBE_DN2 one below; then CUBE01
		/// brings the next panel out. Faces further on in the original's order are above.
		/// </summary>
		DrShellClip[] Turn(Screen from, Screen to)
		{
			string turn;
			if (from == Screen.Options || to == Screen.Archive)
				turn = "CUBE03";
			else if (from == Screen.Archive || to == Screen.Options)
				turn = "CUBE02";
			else if (from == Screen.Return)
				turn = to == Screen.Cube ? "CUBE_UP2" : to == Screen.Training ? "CUBE05" : "CUBE04";
			else if (from == Screen.Cube && to == Screen.Debrief)
				turn = "CUBE05";
			else if (from == Screen.Debrief && to == Screen.Cube)
				turn = "CUBE04";
			else if (from == Screen.Cube && to == Screen.Briefing)
				turn = "CUBE_UP2";
			else if (from == Screen.Briefing && to == Screen.Cube)
				turn = "CUBE_DN2";
			else
				turn = from < to ? "CUBE04" : "CUBE05";

			return [Clip(turn), Clip("CUBE01")];
		}

		const string ShellSounds = "shellsounds|";
		const float ShellVolume = 90 / 127f;

		readonly ModData modData;
		readonly World world;
		readonly bool fastTransitions = DrShellSettings.FastTransitions;
		readonly DrShellWidget shell;
		readonly Dictionary<string, MapPreview> maps = new(StringComparer.OrdinalIgnoreCase);
		readonly Dictionary<string, Dictionary<int, string>> briefings = new(StringComparer.OrdinalIgnoreCase);

		Screen screen = Screen.Main;
		char briefingSide = 'f';
		bool panelOpen;
		bool launching;
		int mission = 1;
		string briefingMission;
		int trainingSet;

		// A test script, run once a process: OPENDR_SHELL="main;cube:3;story:3:f;click:320:101;..." shows each
		// screen (with a mission and side) or clicks a point of the 640x480 screen, and screenshots it.
		static Queue<string> script;
		int scriptTicks;

		[ObjectCreator.UseCtor]
		public DrShellLogic(Widget widget, ModData modData, World world)
		{
			this.modData = modData;
			this.world = world;
			shell = (DrShellWidget)widget;

			if (shell.Art == null)
			{
				Game.RunAfterTick(() =>
				{
					Ui.ResetAll();
					Game.LoadWidget(world, "MAINMENU", Ui.Root, new WidgetArgs());
				});
				return;
			}

			foreach (var preview in modData.MapCache)
				if (preview.Status == MapStatus.Available && preview.Path != null)
					maps.TryAdd(Path.GetFileName(preview.Path.TrimEnd('/', '\\')), preview);

			shell.GetBackground = () => BackgroundOf(screen);
			shell.OnKeyPress = HandleKey;
			archive = DrArchive.Load(modData.DefaultFileSystem);

			SetupMain(widget.Get("MAIN"));
			SetupQuit(widget.Get("QUIT_PROMPT"));
			SetupSingle(widget.Get("SINGLE"));
			SetupCube(widget.Get("CUBE"));
			SetupStory(widget.Get("STORY"));
			SetupBriefing(widget.Get("BRIEFING_F"), 'f');
			SetupBriefing(widget.Get("BRIEFING_I"), 'i');
			SetupTraining(widget.Get("TRAINING"));
			SetupOptions(widget.Get("OPTIONS"));
			SetupArchive(widget.Get("ARCHIVE"));
			SetupCredits(widget.Get("CREDITS_SCREEN"));
			SetupDebrief(widget.Get("DEBRIEF"));
			SetupLoadGame(widget.Get("LOADGAME"));
			SetupCustom(widget.Get("CUSTOM"));
			SetupResults(widget.Get("RESULTS"));

			Ambience(Screen.Main);
			ReturnFromMission();

			script ??= new Queue<string>((Environment.GetEnvironmentVariable("OPENDR_SHELL") ?? "")
				.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		}

		// Sound

		ISound ambience;
		string ambienceName;
		int punctuation = -1;

		/// <summary>The original's hums: bridge.wav on the bridge and the cube's faces, bridge3.wav under the main menus.</summary>
		void Ambience(Screen s) => Ambience(s == Screen.Credits ? "credits.wav" : s < Screen.Cube ? "bridge3.wav" : "bridge.wav");

		void Ambience(string name)
		{
			if (name == ambienceName)
				return;

			if (ambience != null)
				Game.Sound.StopSound(ambience);

			ambience = null;
			ambienceName = name;
			if (name != null && modData.DefaultFileSystem.Exists(ShellSounds + name))
			{
				ambience = Game.Sound.PlayLooped(SoundType.UI, ShellSounds + name);
				if (ambience != null)
					ambience.Volume = Game.Sound.SoundVolume * ShellVolume;
			}
		}

		/// <summary>Now and then, on the cube's faces, one of the original's fourteen sounds of the cube at work.</summary>
		void TickPunctuation()
		{
			if (screen < Screen.Cube || shell.CoversScreen || panelOpen)
				return;

			if (punctuation < 0)
				punctuation = 600 + Game.CosmeticRandom.Next(300);

			if (--punctuation > 0)
				return;

			punctuation = -1;
			var sound = $"{ShellSounds}punct_{Game.CosmeticRandom.Next(14) + 1}.wav";
			if (modData.DefaultFileSystem.Exists(sound))
				Game.Sound.Play(SoundType.UI, sound, ShellVolume);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				Ambience((string)null);

			base.Dispose(disposing);
		}

		readonly Dictionary<Screen, Action> onShow = new();

		void Show(Screen s)
		{
			screen = s;
			Ambience(s);
			shell.Art.ReleaseExcept([BackgroundOf(s)]);
			if (onShow.TryGetValue(s, out var action))
				action();
		}

		string BackgroundOf(Screen s) => s == Screen.Briefing ? (briefingSide == 'i' ? "brief_i" : "brief_f") : Backgrounds[s];

		Func<bool> Visible(Screen s) => () => !panelOpen && screen == s && !shell.CoversScreen;

		/// <summary>Turns the cube from this face to another.</summary>
		void TurnTo(Screen to) => Go(to, Turn(screen, to));

		/// <summary>Plays the videos between this screen and the next, then shows it.</summary>
		void Go(Screen to, params DrShellClip[] clips) => Go(() => Show(to), clips);

		void Go(Action show, params DrShellClip[] clips)
		{
			if (clips.Length == 0)
			{
				show();
				return;
			}

			// The hum stops for the movies and as the cube sinks; the bridge's plays as it rises.
			if (clips.Any(c => c.Sound == DrShellVideoSound.Video || c == CubeOut))
				Ambience((string)null);
			else if (clips.Contains(CubeIn))
				Ambience("bridge.wav");

			// Music gives way to the movies.
			var music = clips.Any(c => c.Sound == DrShellVideoSound.Video) && Game.Sound.MusicPlaying;
			if (music)
				Game.Sound.PauseMusic();

			shell.PlayClips(clips, () =>
			{
				if (music)
					Game.Sound.PlayMusic();

				show();
			});
		}

		/// <summary>Back from a mission the shell launched: the debrief after a win, else the mission again.</summary>
		void ReturnFromMission()
		{
			var launched = DrCampaign.Launched;
			var won = DrCampaign.LastResult == true;
			statistics = DrCampaign.LastStatistics;
			DrCampaign.Launched = null;
			DrCampaign.LastResult = null;
			DrCampaign.LastStatistics = null;
			if (launched == null)
				return;

			// A custom mission comes back to its results, if it kept them, then the custom missions.
			if (!DrCampaign.IsCampaignMission(launched))
			{
				Show(statistics != null ? Screen.Results : Screen.Custom);
				return;
			}

			// The game comes back to a bare face of the cube, which turns to the next; the Togran's defeat
			// plays the ending, then the credits roll, as in the original.
			Show(Screen.Return);
			if (launched.StartsWith('t'))
			{
				trainingSet = launched is "t3" or "t4" ? 1 : 0;
				TurnTo(Screen.Training);
				return;
			}

			mission = int.TryParse(launched.AsSpan(1, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
			if (!won)
				TurnTo(Screen.Cube);
			else
			{
				briefingMission = launched;
				if (mission == DrCampaign.Togran)
					Go(Screen.Credits, Movie("OUTRO"));
				else
					TurnTo(Screen.Debrief);
			}
		}

		bool HandleKey(KeyInput e)
		{
			if (e.Event != KeyInputEvent.Down || e.Key != Keycode.ESCAPE || panelOpen || launching)
				return false;

			switch (screen)
			{
				case Screen.Main: Show(Screen.Quit); break;
				case Screen.Quit: case Screen.Single: case Screen.Credits: Show(Screen.Main); break;
				case Screen.LoadGame: Show(loadGameReturn); break;
				case Screen.Custom: Show(Screen.Single); break;
				case Screen.Results: Show(Screen.Custom); break;
				case Screen.Cube: Go(Screen.Single, CubeOut); break;
				case Screen.Options: case Screen.Story: case Screen.Training: case Screen.Debrief: TurnTo(Screen.Cube); break;
				case Screen.Briefing: BackFromBriefing(); break;
				case Screen.Archive:
					if (archiveNode?.Parent != null)
						OpenArchive(archiveNode.Parent);
					else
						TurnTo(Screen.Cube);

					break;
			}

			return true;
		}

		public override void Tick()
		{
			TickPunctuation();
			if (script == null || (script.Count == 0 && scriptTicks == 0))
				return;

			// Steps wait for the videos between screens, but for shot (a screenshot now), skip and wait.
			if (shell.PlayingClips && (script.Count == 0 || script.Peek() is not ("shot" or "skip" or "wait")))
				return;

			// Each step shows a screen and screenshots it 30 ticks later.
			if (++scriptTicks % 40 == 30)
				Game.TakeScreenshot();

			if (scriptTicks % 40 != 0)
				return;

			if (script.Count == 0)
			{
				scriptTicks = 0;
				return;
			}

			var step = script.Dequeue().Split(':');
			switch (step[0])
			{
				case "shot": Game.TakeScreenshot(); return;
				case "skip": shell.Skip(); return;
				case "wait": return;
				case "intro": Go(Screen.Main, Movie("INTRO")); return;
			}

			if (step[0] == "click" && step.Length == 3)
			{
				var at = shell.ToScreen(new float2(int.Parse(step[1], CultureInfo.InvariantCulture), int.Parse(step[2], CultureInfo.InvariantCulture)));
				var location = new int2((int)at.X, (int)at.Y);
				foreach (var e in new[] { MouseInputEvent.Move, MouseInputEvent.Down, MouseInputEvent.Up })
					Ui.HandleInput(new MouseInput(e, e == MouseInputEvent.Move ? MouseButton.None : MouseButton.Left, location, int2.Zero, Modifiers.None, 1));

				return;
			}

			if (step.Length > 1 && int.TryParse(step[1], out var m))
			{
				mission = m;
				briefingMission = DrCampaign.MissionName(m, step.Length > 2 ? step[2][0] : 'f');
			}

			// briefingf and briefingi name the briefing's side.
			var name = step[0] is "briefingf" or "briefingi" ? "briefing" : step[0];
			if (step[0] == "briefingi" && step.Length < 3)
				briefingMission = DrCampaign.MissionName(mission, 'i');

			if (Enum.TryParse<Screen>(name, true, out var s))
			{
				shell.SkipClips();
				if (s == Screen.Story)
					OpenStory(force: true);
				else if (s == Screen.Briefing)
					OpenBriefing(briefingMission, animate: false);
				else
					Show(s);
			}

		}

		// Main menu

		void SetupMain(Widget main)
		{
			main.IsVisible = Visible(Screen.Main);
			main.Get<DrShellButtonWidget>("SINGLE_PLAYER").OnClick = () => Show(Screen.Single);
			main.Get<DrShellButtonWidget>("MULTI_PLAYER").OnClick = OpenMultiplayer;
			main.Get<DrShellButtonWidget>("INSTANT_ACTION").OnClick = StartSkirmish;
			main.Get<DrShellButtonWidget>("CONSTRUCTION_KIT").OnClick = OpenMapEditor;
			var intro = main.Get<DrShellButtonWidget>("REPLAY_INTRO");
			intro.IsDisabled = () => !HasMovie("INTRO");
			intro.OnClick = () => Go(Screen.Main, Movie("INTRO"));
			main.Get<DrShellButtonWidget>("CREDITS").OnClick = () => Show(Screen.Credits);
			main.Get<DrShellButtonWidget>("QUIT").OnClick = () => Show(Screen.Quit);
			main.Get<DrShellButtonWidget>("SETTINGS").OnClick = () => OpenPanel("SETTINGS_PANEL", new WidgetArgs());
			main.Get<DrShellButtonWidget>("REPLAYS").OnClick = () => OpenPanel("REPLAYBROWSER_PANEL", new WidgetArgs { { "onStart", () => { } } });
		}

		void SetupQuit(Widget quit)
		{
			quit.IsVisible = Visible(Screen.Quit);
			quit.Get<DrShellButtonWidget>("YES").OnClick = Game.Exit;
			quit.Get<DrShellButtonWidget>("NO").OnClick = () => Show(Screen.Main);
		}

		// Single player

		void SetupSingle(Widget single)
		{
			single.IsVisible = Visible(Screen.Single);
			var resume = single.Get<DrShellButtonWidget>("CONTINUE");
			resume.IsVisible = () => DrCampaign.HasProgress;
			resume.OnClick = () => OpenCube(false);

			single.Get<DrShellButtonWidget>("START_NEW_GAME").OnClick = () =>
			{
				if (!DrCampaign.HasProgress)
				{
					OpenCube(true);
					return;
				}

				panelOpen = true;
				ConfirmationDialogs.ButtonPrompt(modData,
					title: NewCampaignTitle,
					text: NewCampaignPrompt,
					onConfirm: () => { panelOpen = false; DrCampaign.Reset(); OpenCube(true); },
					confirmText: NewCampaignConfirm,
					onCancel: () => panelOpen = false);
			};

			single.Get<DrShellButtonWidget>("LOAD_GAME").OnClick = () => OpenLoadGame(Screen.Single);
			single.Get<DrShellButtonWidget>("CUSTOM_MISSION").OnClick = () => Show(Screen.Custom);

			single.Get<DrShellButtonWidget>("PREVIOUS_MENU").OnClick = () => Show(Screen.Main);
		}

		/// <summary>Up to the cube from the bridge; a new game first plays the intro, as the original's did.</summary>
		void OpenCube(bool newGame)
		{
			mission = Enumerable.Range(1, DrCampaign.Togran).LastOrDefault(DrCampaign.IsUnlocked, 1);
			Go(Screen.Cube, newGame ? [Movie("INTRO"), CubeIn] : [CubeIn]);
		}

		// The cube's main face: the mission ring

		void SetupCube(Widget cube)
		{
			cube.IsVisible = Visible(Screen.Cube);
			cube.Get<DrShellLabelWidget>("MISSION_TITLE").GetText = () => MissionTitle(mission);

			var ring = cube.Get<DrShellRingWidget>("RING");
			ring.IsUnlocked = DrCampaign.IsUnlocked;
			ring.GetSelected = () => mission;
			ring.GetState = m => (DrCampaign.HasWon(DrCampaign.MissionName(m, 'f')) ? 1 : 0) + (DrCampaign.HasWon(DrCampaign.MissionName(m, 'i')) ? 2 : 0);
			ring.OnSelect = m =>
			{
				// A second click on the selected mission opens it, as the arrow above does.
				if (m == mission)
					OpenStory();
				else
					mission = m;
			};

			cube.Get<DrShellImageWidget>("FG_LOGO").GetFrame = () => DrCampaign.HasWon(DrCampaign.MissionName(mission, 'f')) ? 1 : 0;
			cube.Get<DrShellImageWidget>("IMP_LOGO").GetFrame = () => DrCampaign.HasWon(DrCampaign.MissionName(mission, 'i')) ? 1 : 0;

			// The key turns until its tumblers line up, one a mission won; with all twelve it lights up and
			// comes alive.
			ring.GetKeyVideos = () =>
			{
				var completed = DrCampaign.Completed;
				return completed < DrCampaign.Missions
					? [$"content|shell/M_RING{completed:D2}.SMK"]
					: ["content|shell/M_RING12.SMK", "content|shell/M_TOGRAN.SMK"];
			};

			onShow[Screen.Cube] = ring.RestartKey;

			cube.Get<DrShellButtonWidget>("BASIC_TRAINING").OnClick = () => { trainingSet = 0; TurnTo(Screen.Training); };
			cube.Get<DrShellButtonWidget>("ADVANCED_TRAINING").OnClick = () => { trainingSet = 1; TurnTo(Screen.Training); };

			var up = cube.Get<DrShellButtonWidget>("UP");
			up.IsDisabled = () => !DrCampaign.IsUnlocked(mission);
			up.OnClick = OpenStory;
			cube.Get<DrShellButtonWidget>("LEFT").OnClick = () => TurnTo(Screen.Options);
			var right = cube.Get<DrShellButtonWidget>("RIGHT");
			right.IsDisabled = () => archive == null;
			right.OnClick = () => TurnTo(Screen.Archive);
			cube.Get<DrShellButtonWidget>("DOWN").OnClick = () => Go(Screen.Single, CubeOut);
		}

		string MissionTitle(int number)
		{
			var map = Map(DrCampaign.MissionName(number, 'f')) ?? Map(DrCampaign.MissionName(number, 'i'));
			return map?.Title.ToUpperInvariant() ?? $"{number}";
		}

		// The mission background, where the player picks a side

		DrShellTextWidget storyText;

		void SetupStory(Widget story)
		{
			story.IsVisible = Visible(Screen.Story);
			story.Get<DrShellLabelWidget>("TITLE").GetText = () => MissionTitle(mission);
			storyText = story.Get<DrShellTextWidget>("TEXT");
			SetupScroll(story, storyText);

			var fg = story.Get<DrShellButtonWidget>("FREEDOM_GUARD");
			fg.IsDisabled = () => mission == DrCampaign.Togran || Map(DrCampaign.MissionName(mission, 'f')) == null;
			fg.OnClick = () => OpenBriefing(DrCampaign.MissionName(mission, 'f'));

			var imp = story.Get<DrShellButtonWidget>("IMPERIUM");
			imp.IsDisabled = () => mission == DrCampaign.Togran || Map(DrCampaign.MissionName(mission, 'i')) == null;
			imp.OnClick = () => OpenBriefing(DrCampaign.MissionName(mission, 'i'));

			story.Get<DrShellButtonWidget>("DOWN").OnClick = () => TurnTo(Screen.Cube);
		}

		void OpenStory() => OpenStory(false);

		void OpenStory(bool force)
		{
			if (!force && !DrCampaign.IsUnlocked(mission))
				return;

			// The Togran's mission has no sides to choose between: the cube is spent, as the segue shows.
			if (mission == DrCampaign.Togran)
			{
				OpenBriefing(DrCampaign.MissionName(mission, 't'), [Movie("SEGUE")], animate: !force);
				return;
			}

			var name = Map(DrCampaign.MissionName(mission, 'f')) != null ? DrCampaign.MissionName(mission, 'f') : DrCampaign.MissionName(mission, 'i');
			storyText.SetText(Briefing(name, 0));
			if (force)
				Show(Screen.Story);
			else
				TurnTo(Screen.Story);
		}

		// The briefing, with Launch

		readonly Dictionary<char, DrShellTextWidget> briefingTexts = new();

		void SetupBriefing(Widget briefing, char side)
		{
			var visible = Visible(Screen.Briefing);
			briefing.IsVisible = () => visible() && briefingSide == side;
			var text = briefingTexts[side] = briefing.Get<DrShellTextWidget>("TEXT");
			SetupScroll(briefing, text);

			var launch = briefing.Get<DrShellButtonWidget>("LAUNCH");
			launch.IsDisabled = () => launching;
			launch.OnClick = () => Launch(briefingMission);

			briefing.Get<DrShellButtonWidget>("BACK").OnClick = BackFromBriefing;
		}

		/// <summary>Back down the cube to the face the briefing came from.</summary>
		void BackFromBriefing()
		{
			if (briefingMission.StartsWith('t'))
				TurnTo(Screen.Training);
			else if (mission == DrCampaign.Togran)
				TurnTo(Screen.Cube);
			else
				OpenStory();
		}

		/// <summary>Turns the cube to the briefing, which opens through its iris; or plays the given videos instead.</summary>
		void OpenBriefing(string name, DrShellClip[] instead = null, bool animate = true)
		{
			briefingMission = name;
			briefingSide = name.EndsWith('i') ? 'i' : 'f';
			var text = Briefing(name, 1);
			var title = Map(name)?.Title.ToUpperInvariant();
			briefingTexts[briefingSide].SetText(title != null ? $"\\c{title}\\n\\n{text}" : text);
			if (!animate)
				Show(Screen.Briefing);
			else
				Go(Screen.Briefing, instead ?? [.. Turn(screen, Screen.Briefing), Clip(briefingSide == 'i' ? "BRIEF_I" : "BRIEF_F")]);
		}

		// Training

		void SetupTraining(Widget training)
		{
			training.IsVisible = Visible(Screen.Training);
			var text = training.Get<DrShellTextWidget>("TEXT");
			SetupScroll(training, text);

			string[] labels =
			[
				FluentProvider.GetMessage(CombatEngineering), FluentProvider.GetMessage(ResourceManagement),
				FluentProvider.GetMessage(PathManagement), FluentProvider.GetMessage(UnitAiControls)
			];

			for (var i = 0; i < 2; i++)
			{
				var index = i;
				var button = training.Get<DrShellButtonWidget>(i == 0 ? "ONE" : "TWO");
				button.GetText = () => labels[trainingSet * 2 + index];
				button.IsDisabled = () => Map(TrainingMissions[trainingSet][index]) == null;
				button.OnClick = () => OpenBriefing(TrainingMissions[trainingSet][index]);
			}

			// The text lists both missions' descriptions, which begin with their titles.
			onShow[Screen.Training] = () => text.SetText(string.Join("\\n\\n\\n", TrainingMissions[trainingSet].Select(t => Briefing(t, 0))));

			// Either arrow turns back down to the missions.
			training.Get<DrShellButtonWidget>("UP").OnClick = () => TurnTo(Screen.Cube);
			training.Get<DrShellButtonWidget>("DOWN").OnClick = () => TurnTo(Screen.Cube);
		}

		// Options: OpenRA's load and settings panels, and the way out

		void SetupOptions(Widget options)
		{
			options.IsVisible = Visible(Screen.Options);
			var progress = options.Get<DrShellTextWidget>("PROGRESS");
			onShow[Screen.Options] = () => progress.SetText(ProgressText());
			SetupScroll(options, progress);
			options.Get<DrShellButtonWidget>("LOAD_GAME").OnClick = () => OpenLoadGame(Screen.Options);
			options.Get<DrShellButtonWidget>("SETTINGS").OnClick = () => OpenPanel("SETTINGS_PANEL", new WidgetArgs());
			options.Get<DrShellButtonWidget>("ENGINE_MENU").OnClick = () => Game.RunAfterTick(() =>
			{
				Ui.ResetAll();
				Game.LoadWidget(world, "MAINMENU", Ui.Root, new WidgetArgs());
			});

			options.Get<DrShellButtonWidget>("QUIT_TO_MAIN_MENU").OnClick = () => Go(Screen.Main, CubeOut);
			options.Get<DrShellButtonWidget>("QUIT_TO_WINDOWS").OnClick = () => Show(Screen.Quit);
			options.Get<DrShellButtonWidget>("RIGHT").OnClick = () => TurnTo(Screen.Cube);
		}

		string ProgressText()
		{
			var sb = new StringBuilder();
			for (var m = 1; m <= DrCampaign.Togran; m++)
			{
				var sides = m == DrCampaign.Togran
					? DrCampaign.HasWon(m) ? FluentProvider.GetMessage(Won) : ""
					: string.Join(", ", new[] { ('f', FreedomGuard), ('i', Imperium) }
						.Where(s => DrCampaign.HasWon(DrCampaign.MissionName(m, s.Item1)))
						.Select(s => FluentProvider.GetMessage(s.Item2)));

				sb.Append(MissionTitle(m));
				if (sides.Length > 0)
					sb.Append(" - ").Append(sides);

				sb.Append("\\n");
			}

			return sb.ToString();
		}

		// The credits, as the original rolls them

		void SetupCredits(Widget credits)
		{
			credits.IsVisible = Visible(Screen.Credits);
			var roll = credits.Get<DrShellCreditsWidget>("ROLL");
			onShow[Screen.Credits] = roll.Restart;
			credits.Get<DrShellButtonWidget>("SLOWER").OnClick = roll.Slower;
			credits.Get<DrShellButtonWidget>("FASTER").OnClick = roll.Faster;
			credits.Get<DrShellButtonWidget>("PREVIOUS_MENU").OnClick = () => Show(Screen.Main);
		}

		// The archive, the cube's right face: the history of the war, its people and units, and a journal

		DrArchive archive;
		DrArchive.Node archiveNode;
		DrShellTextWidget archiveText;

		void SetupArchive(Widget page)
		{
			page.IsVisible = Visible(Screen.Archive);
			page.Get<DrShellLabelWidget>("HEADING").GetText = () =>
				(archiveNode?.Items != null ? archiveNode : archiveNode?.Parent)?.Title.ToUpperInvariant();

			var items = page.Get<DrShellMenuWidget>("ITEMS");
			items.IsVisible = () => archiveNode?.Items != null;
			items.GetItems = () => ArchiveItems(archiveNode).Select(n => n.Title).ToList();
			items.OnSelect = i => OpenArchive(ArchiveItems(archiveNode)[i]);

			archiveText = page.Get<DrShellTextWidget>("TEXT");
			archiveText.IsVisible = () => archiveNode?.Items == null;
			SetupScroll(page, archiveText);
			foreach (var id in new[] { "SCROLL_UP", "SCROLL_DOWN" })
				page.Get<DrShellButtonWidget>(id).IsVisible = archiveText.IsVisible;

			var up = page.Get<DrShellButtonWidget>("UP_ONE");
			up.IsDisabled = () => archiveNode?.Parent == null;
			up.OnClick = () => OpenArchive(archiveNode.Parent);
			page.Get<DrShellButtonWidget>("LEFT").OnClick = () => TurnTo(Screen.Cube);

			onShow[Screen.Archive] = () => OpenArchive(archive?.Root);
		}

		/// <summary>A menu's items; the journal, the root's last, has as many entries as missions won, and one more.</summary>
		List<DrArchive.Node> ArchiveItems(DrArchive.Node menu)
		{
			if (menu?.Items == null)
				return [];

			var root = archive.Root;
			if (menu.Parent == root && root.Items.Count > 0 && menu == root.Items[^1])
				return menu.Items.Take(DrCampaign.Completed + 1).ToList();

			return menu.Items;
		}

		void OpenArchive(DrArchive.Node node)
		{
			archiveNode = node;
			if (node?.Items == null)
				archiveText.SetText(node?.Text ?? "");
		}

		// The debrief after a won mission: the historical outcome, and the mission's statistics

		(int Kind, int Side, int[] Figures)[] statistics;

		void SetupDebrief(Widget debrief)
		{
			debrief.IsVisible = Visible(Screen.Debrief);
			debrief.Get<DrShellLabelWidget>("TITLE").GetText = () => briefingMission != null ? Map(briefingMission)?.Title.ToUpperInvariant() : null;
			debrief.Get<DrShellStatisticsWidget>("STATISTICS").GetRows = () =>
				statistics?.Take(2).Select(r => ((string)null, r.Side, r.Figures)).ToArray();

			// The player's side's emblem: the Freedom Guard's left of the title, any other's right of it.
			debrief.Get<DrShellImageWidget>("FG_LOGO").GetFrame = () => statistics != null && statistics[0].Side == 0 ? 0 : -1;
			debrief.Get<DrShellImageWidget>("IMP_LOGO").GetFrame = () => statistics != null && statistics[0].Side != 0 ? 0 : -1;
			var text = debrief.Get<DrShellTextWidget>("TEXT");
			SetupScroll(debrief, text);

			onShow[Screen.Debrief] = () =>
			{
				var heading = FluentProvider.GetMessage(MissionSuccessful);
				var outcome = Briefing(briefingMission, 2);
				var closing = Briefing(briefingMission, 3);
				text.SetText($"\\c{heading}\\n\\n{outcome}" + (closing.Length > 0 ? $"\\n\\n{closing}" : ""));
			};

			debrief.Get<DrShellButtonWidget>("CONTINUE").OnClick = () =>
			{
				// On to the next mission, if the win opened it.
				if (mission < DrCampaign.Togran && DrCampaign.IsUnlocked(mission + 1))
					mission++;

				TurnTo(Screen.Cube);
			};

			// As in the original: the mission again below, options on the left and the archive on the right.
			var replay = debrief.Get<DrShellButtonWidget>("REPLAY");
			replay.IsDisabled = () => briefingMission == null || Map(briefingMission) == null;
			replay.OnClick = () => OpenBriefing(briefingMission);
			debrief.Get<DrShellButtonWidget>("LEFT").OnClick = () => TurnTo(Screen.Options);
			var right = debrief.Get<DrShellButtonWidget>("RIGHT");
			right.IsDisabled = () => archive == null;
			right.OnClick = () => TurnTo(Screen.Archive);
		}

		// Saved games, as the Load Game and Custom Mission screens list them

		sealed record SavedGame(string Path, string Name, string Mission, MapPreview Map, DateTime Saved);

		/// <summary>The saved games of the campaign's missions, or of the others; newest first.</summary>
		List<SavedGame> SavedGames(bool campaign)
		{
			var folder = Path.Combine(Platform.SupportDir, "Saves", modData.Manifest.Id, modData.Manifest.Metadata.Version);
			var saves = new List<SavedGame>();
			if (!Directory.Exists(folder))
				return saves;

			foreach (var path in Directory.GetFiles(folder, "*.orasav", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime))
			{
				try
				{
					var map = modData.MapCache[new GameSave(path).GlobalSettings.Map];
					var mission = map.Status == MapStatus.Available && map.Path != null ? Path.GetFileName(map.Path.TrimEnd('/', '\\')) : null;
					if (DrCampaign.IsCampaignMission(mission) == campaign)
						saves.Add(new SavedGame(path, Path.GetFileNameWithoutExtension(path), mission, map, File.GetLastWriteTime(path)));
				}
				catch (Exception e)
				{
					Log.Write("debug", $"Could not read the saved game {path}: {e.Message}");
				}
			}

			return saves;
		}

		void LoadSavedGame(SavedGame save)
		{
			if (launching || save.Map.Status != MapStatus.Available)
				return;

			launching = true;
			DrCampaign.Launched = save.Mission;
			DrCampaign.LastResult = null;
			Game.CreateAndStartLocalServer(save.Map.Uid,
			[
				Order.FromTargetString("LoadGameSave", Path.GetFileName(save.Path), true),
				Order.Command($"state {Session.ClientState.Ready}")
			]);
		}

		void DeleteSavedGame(SavedGame save, Action deleted)
		{
			panelOpen = true;
			ConfirmationDialogs.ButtonPrompt(modData,
				title: DeleteSaveTitle,
				text: DeleteSavePrompt,
				textArguments: ["save", save.Name],
				onConfirm: () =>
				{
					panelOpen = false;
					try
					{
						File.Delete(save.Path);
					}
					catch (Exception e)
					{
						Log.Write("debug", $"Could not delete the saved game {save.Path}: {e.Message}");
					}

					deleted();
				},
				confirmText: DeleteSaveConfirm,
				onCancel: () => panelOpen = false);
		}

		readonly Dictionary<string, int?> scenarioSides = [];

		/// <summary>
		/// The side the player takes in a mission: team 0's in its scenario (SetTeamSide), as the original reads
		/// it, else the faction of the map's first playable player.
		/// </summary>
		string SideOf(MapPreview map)
		{
			string[] sides = [SideFreedomGuard, SideImperium, SideCivilian, SideTogran, SideXenite, SideShadowhand];
			if (ScenarioSide(map) is int side && side >= 0 && side < sides.Length)
				return FluentProvider.GetMessage(sides[side]);

			var faction = map?.Players.Players.Values.FirstOrDefault(p => p.Playable)?.Faction;
			return faction switch
			{
				null => "--",
				"fguard" => FluentProvider.GetMessage(SideFreedomGuard),
				"imperium" => FluentProvider.GetMessage(SideImperium),
				"togran" => FluentProvider.GetMessage(SideTogran),
				_ => faction.ToUpperInvariant()
			};
		}

		/// <summary>Team 0's side in a converted mission's scenario; null for a map without one.</summary>
		int? ScenarioSide(MapPreview map)
		{
			if (map?.Path == null)
				return null;

			if (!scenarioSides.TryGetValue(map.Path, out var side))
			{
				try
				{
					var scn = Directory.Exists(map.Path) ? Directory.GetFiles(map.Path, "*.scn").FirstOrDefault() : null;
					if (scn != null)
					{
						using var stream = File.OpenRead(scn);
						side = new DrScenario(stream).Teams.TryGetValue(0, out var team) ? team.Side : null;
					}
				}
				catch (Exception e)
				{
					Log.Write("debug", $"Could not read the scenario of {map.Path}: {e.Message}");
				}

				scenarioSides[map.Path] = side;
			}

			return side;
		}

		// Load Game, the original's saved game selection: the campaign's saved games

		Screen loadGameReturn = Screen.Single;
		List<SavedGame> campaignSaves = [];
		int campaignSave = -1;

		void OpenLoadGame(Screen from)
		{
			loadGameReturn = from;
			Show(Screen.LoadGame);
		}

		void SetupLoadGame(Widget load)
		{
			load.IsVisible = Visible(Screen.LoadGame);
			SavedGame Selected() => campaignSave >= 0 && campaignSave < campaignSaves.Count ? campaignSaves[campaignSave] : null;
			string Field(Func<SavedGame, string> f) => Selected() is { } s ? f(s) : "--";

			var list = load.Get<DrShellMenuWidget>("SAVES");
			list.GetItems = () => campaignSaves.Select(s => s.Name).ToList();
			list.GetSelected = () => campaignSave;
			list.OnSelect = i => campaignSave = i;

			// Where it was saved, and its mission, side and date: an OpenRA save is always in a mission.
			load.Get<DrShellLabelWidget>("LOCATION").GetText = () => Field(s => s.Mission.StartsWith('t')
				? FluentProvider.GetMessage(InTraining, "number", s.Mission[1..])
				: FluentProvider.GetMessage(InMission, "number", int.Parse(s.Mission.AsSpan(1, 2), CultureInfo.InvariantCulture)));
			load.Get<DrShellLabelWidget>("PROGRESSION_1").GetText = () => Field(s => s.Map.Title);
			load.Get<DrShellLabelWidget>("PROGRESSION_2").GetText = () => Field(s => SideOf(s.Map));
			load.Get<DrShellLabelWidget>("PROGRESSION_3").GetText = () => Field(s => s.Saved.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
			load.Get<DrShellLabelWidget>("NAME").GetText = () => Field(s => s.Name);

			var launch = load.Get<DrShellButtonWidget>("LOAD");
			launch.IsDisabled = () => Selected() == null || launching;
			launch.OnClick = () => LoadSavedGame(Selected());

			var delete = load.Get<DrShellButtonWidget>("DELETE");
			delete.IsDisabled = () => Selected() == null;
			delete.OnClick = () => DeleteSavedGame(Selected(), Refresh);

			load.Get<DrShellButtonWidget>("PREVIOUS_MENU").OnClick = () => Show(loadGameReturn);

			void Refresh()
			{
				campaignSaves = SavedGames(true);
				campaignSave = -1;
			}

			onShow[Screen.LoadGame] = () =>
			{
				Refresh();
				list.ScrollToTop();
			};
		}

		// Custom missions: the missions that are not the campaign's, and their saved games

		List<string> customMissions = [];
		List<SavedGame> customSaves = [];
		int customMission = -1;
		int customSave = -1;

		void SetupCustom(Widget custom)
		{
			custom.IsVisible = Visible(Screen.Custom);
			string Mission() => customMission >= 0 && customMission < customMissions.Count ? customMissions[customMission] : null;
			SavedGame Save() => customSave >= 0 && customSave < customSaves.Count ? customSaves[customSave] : null;
			// A saved game whose mission has gone shows nothing and cannot be loaded, only deleted.
			MapPreview Selected() => Mission() is { } m ? Map(m) : Save()?.Map is { Status: MapStatus.Available } map ? map : null;
			string Field(Func<MapPreview, string> f) => Selected() is { } m ? f(m) : "--";

			// The missions by their folders' names, as the original lists its scenario folders; choosing in one
			// list clears the other, as in the original.
			var missions = custom.Get<DrShellMenuWidget>("MISSIONS");
			missions.GetItems = () => customMissions.Select(m => Map(m).Title).ToList();
			missions.GetSelected = () => customMission;
			missions.OnSelect = i => { customMission = i; customSave = -1; };

			var saves = custom.Get<DrShellMenuWidget>("SAVES");
			saves.GetItems = () => customSaves.Select(s => s.Name).ToList();
			saves.GetSelected = () => customSave;
			saves.OnSelect = i => { customSave = i; customMission = -1; };

			foreach (var (list, id) in new[] { (missions, "MISSIONS"), (saves, "SAVES") })
			{
				var up = custom.Get<DrShellButtonWidget>(id + "_UP");
				up.IsDisabled = () => !list.CanScrollUp;
				up.OnClick = () => list.Scroll(-1);
				var down = custom.Get<DrShellButtonWidget>(id + "_DOWN");
				down.IsDisabled = () => !list.CanScrollDown;
				down.OnClick = () => list.Scroll(1);
			}

			// The mission's enemies, its size in cells, as the original's map was, and the player's side.
			custom.Get<DrShellLabelWidget>("ENEMIES").GetText = () =>
				Field(m => (m.Players.Players.Values.FirstOrDefault(p => p.Playable)?.Enemies.Length ?? 0).ToString(CultureInfo.InvariantCulture));
			custom.Get<DrShellLabelWidget>("SIZE").GetText = () => Field(m => $"{m.Bounds.Width}, {m.Bounds.Height}");
			custom.Get<DrShellLabelWidget>("SIDE").GetText = () => Field(SideOf);

			var launch = custom.Get<DrShellButtonWidget>("LOAD");
			launch.IsDisabled = () => Selected() == null || launching;
			launch.OnClick = () =>
			{
				if (Mission() is { } m)
					Launch(m);
				else if (Save() is { } s)
					LoadSavedGame(s);
			};

			var delete = custom.Get<DrShellButtonWidget>("DELETE");
			delete.IsDisabled = () => Save() == null;
			delete.OnClick = () => DeleteSavedGame(Save(), Refresh);

			custom.Get<DrShellButtonWidget>("PREVIOUS_MENU").OnClick = () => Show(Screen.Single);

			void Refresh()
			{
				customMissions = maps
					.Where(kv => !DrCampaign.IsCampaignMission(kv.Key) && kv.Value.Visibility.HasFlag(MapVisibility.MissionSelector))
					.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
					.Select(kv => kv.Key).ToList();
				customSaves = SavedGames(false);
				customMission = customSave = -1;
			}

			onShow[Screen.Custom] = () =>
			{
				Refresh();
				missions.ScrollToTop();
				saves.ScrollToTop();
			};
		}

		// The results after a custom mission: every team a human or the computer played

		void SetupResults(Widget results)
		{
			results.IsVisible = Visible(Screen.Results);
			results.Get<DrShellStatisticsWidget>("STATISTICS").GetRows = () => statistics?
				.Where(r => r.Kind != 0)
				.Select(r => (FluentProvider.GetMessage(r.Kind == 1 ? Human : Computer), r.Side, r.Figures))
				.ToArray();

			results.Get<DrShellButtonWidget>("CONTINUE").OnClick = () => Show(Screen.Custom);
		}

		void SetupScroll(Widget parent, DrShellTextWidget text)
		{
			var up = parent.GetOrNull<DrShellButtonWidget>("SCROLL_UP");
			if (up != null)
			{
				up.IsDisabled = () => !text.CanScrollUp;
				up.OnClick = () => text.Scroll(-3);
			}

			var down = parent.GetOrNull<DrShellButtonWidget>("SCROLL_DOWN");
			if (down != null)
			{
				down.IsDisabled = () => !text.CanScrollDown;
				down.OnClick = () => text.Scroll(3);
			}
		}

		// Missions and their briefings

		MapPreview Map(string name) => maps.GetValueOrDefault(name);

		bool HasMovie(string name) => modData.DefaultFileSystem.Exists($"content|movies/{name}.SMK");

		/// <summary>A section of the mission's briefing file, with its markup: \0 the background, \1 the orders, \2 the outcome, \3 Togra's word.</summary>
		string Briefing(string name, int section)
		{
			if (!briefings.TryGetValue(name, out var sections))
			{
				sections = new Dictionary<int, string>();
				var preview = Map(name);
				if (preview != null)
				{
					try
					{
						var map = preview.ToMap();
						var brf = map.Package.Contents.FirstOrDefault(f => f.EndsWith(".brf", StringComparison.OrdinalIgnoreCase));
						if (brf != null)
						{
							using (var s = map.Open(brf))
							using (var reader = new StreamReader(s, Encoding.Latin1))
							{
								// Training briefings begin their description without a \0.
								var all = reader.ReadToEnd();
								var lead = Regex.Match(all, @"^(.*?)(?=\\\d|$)", RegexOptions.Singleline).Groups[1].Value.Trim();
								if (lead.Length > 0)
									sections[0] = lead;

								foreach (Match m in Regex.Matches(all, @"\\(\d)\s*(.*?)(?=\\\d|$)", RegexOptions.Singleline))
									sections[m.Groups[1].Value[0] - '0'] = m.Groups[2].Value.Trim();
							}
						}
					}
					catch (Exception e)
					{
						Log.Write("debug", $"Could not read the briefing of {name}: {e.Message}");
					}
				}

				briefings[name] = sections;
			}

			return sections.GetValueOrDefault(section, "");
		}

		void Launch(string name)
		{
			var preview = Map(name);
			if (preview == null || launching)
				return;

			launching = true;
			DrCampaign.Launched = name;
			DrCampaign.LastResult = null;
			Game.CreateAndStartLocalServer(preview.Uid, [Order.Command($"state {Session.ClientState.Ready}")]);
		}

		// OpenRA's own panels, over the shell's art

		void OpenPanel(string id, WidgetArgs args, Action onExit = null)
		{
			panelOpen = true;
			args["onExit"] = () =>
			{
				onExit?.Invoke();
				panelOpen = false;
			};

			Game.OpenWindow(id, args);
		}

		void OpenMultiplayer()
		{
			OpenPanel("MULTIPLAYER_PANEL", new WidgetArgs
			{
				{ "onStart", () => { } },
				{ "directConnectEndPoint", null },
			});
		}

		void StartSkirmish()
		{
			panelOpen = true;
			var map = modData.MapCache.ChooseInitialMap(modData.MapCache.PickLastModifiedMap(MapVisibility.Lobby) ?? Game.Settings.Server.Map, Game.CosmeticRandom);
			Game.Settings.Server.Map = map;
			Game.Settings.Save();

			ConnectionLogic.Connect(Game.CreateLocalServer(map, isSkirmish: true), "",
				() => Game.OpenWindow("SERVER_LOBBY", new WidgetArgs
				{
					{ "onExit", () => { Game.Disconnect(); panelOpen = false; } },
					{ "onStart", () => { } },
					{ "skirmishMode", true }
				}),
				() => { Game.CloseServer(); panelOpen = false; });
		}

		void OpenMapEditor()
		{
			OpenPanel("MAPCHOOSER_PANEL", new WidgetArgs
			{
				{ "initialMap", null },
				{ "initialGeneratedMap", (MapGenerationArgs)null },
				{ "remoteMapPool", null },
				{ "initialTab", MapClassification.User },
				{ "onSelect", (Action<string>)(uid => Game.LoadEditor(uid)) },
				{ "onSelectGenerated", null },
				{ "filter", MapVisibility.Lobby | MapVisibility.Shellmap | MapVisibility.MissionSelector },
			});
		}
	}
}
