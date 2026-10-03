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
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.Orders;
using OpenRA.Mods.Dr.Traits;
using OpenRA.Mods.Dr.UtilityCommands;
using OpenRA.Orders;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets.Logic
{
	/// <summary>
	/// The original in-game interface's behaviour (chrome/ingame-player.yaml): the top bar's buttons and
	/// credits, the six tabs and their panels, the minimap and the resource bars.
	/// </summary>
	public class DrIgiLogic : ChromeLogic
	{
		static readonly string[] Tabs = ["BUILD", "COMMS", "MENU", "ORDERS", "PATHS", "SPECIAL"];

		readonly World world;
		readonly ModData modData;
		readonly Widget widget;
		readonly List<(HotkeyReference Key, Action Run)> hotkeys = [];
		readonly DrIgiWidget igi;
		string tab = "BUILD";
		bool advancedOrders;
		bool advancedPaths;
		bool advancedMenu;
		bool windowOpen;
		Action closeWindow;
		Action confirmed;
		string confirmText;
		string missionEnd;
		bool loadSaveOpen;
		bool objectiveOpen;
		Action closeLoadSave;

		[ObjectCreator.UseCtor]
		public DrIgiLogic(Widget widget, World world, ModData modData)
		{
			this.world = world;
			this.modData = modData;
			this.widget = widget;
			igi = (DrIgiWidget)widget;
			var player = world.LocalPlayer;

			foreach (var name in Tabs)
			{
				var button = widget.Get<DrIgiButtonWidget>("TAB_" + name);
				button.IsHighlighted = () => tab == name;
				button.OnClick = () => tab = name;
				widget.Get("PANEL_" + name).IsVisible = () => tab == name;
			}

			BindTopBar(player);
			BindBuild(player);
			BindMenu();
			BindLoadSave(player);
			BindObjective(player);
			BindMenuOptions();
			BindOrders();
			BindPaths();
			BindSpecial();
			BindMinimap(player);
			BindHotkeys();

			var confirm = widget.Get("CONFIRM");
			confirm.IsVisible = () => confirmed != null;
			var areYouSure = igi.Library?.GetString("IGI_EVNT_AREUSURE");
			widget.Get<DrIgiBoxWidget>("CONFIRM_BOX").GetText = () => confirmText ?? areYouSure;
			widget.Get<DrIgiButtonWidget>("CONFIRM_YES").OnClick = () =>
			{
				var action = confirmed;
				confirmed = null;
				action?.Invoke();
			};
			widget.Get<DrIgiButtonWidget>("CONFIRM_NO").OnClick = () => confirmed = null;

			BindMissionEnd(player);

			// Escape cancels a pending order (a building to place, a target to pick), else opens the menu; the
			// rest are the original's keyboard commands (BindHotkeys).
			var addWaypointsKey = modData.Hotkeys["IgiAddWaypoints"];
			widget.Get<LogicKeyListenerWidget>("IGI_KEYS").AddHandler(e =>
			{
				// The waypoint key let go: the selection follows what was laid while it was held.
				if (e.Event == KeyInputEvent.Up && e.Key == addWaypointsKey.GetValue().Key && world.OrderGenerator is DrWaypointOrderGenerator && missionEnd == null)
				{
					FollowPath();
					world.CancelInputMode();
					return true;
				}

				if (e.Event != KeyInputEvent.Down)
					return false;

				if (missionEnd != null)
				{
					if (e.Key == Keycode.RETURN || e.Key == Keycode.KP_ENTER)
						Leave();

					return true;
				}

				if (e.Key == Keycode.ESCAPE && e.Modifiers == Modifiers.None)
				{
					if (confirmed != null)
						confirmed = null;
					else if (loadSaveOpen || objectiveOpen)
						ClosePopups();
					else if (world.OrderGenerator.GetType() != typeof(UnitOrderGenerator))
						world.CancelInputMode();
					else
						tab = tab == "MENU" ? "BUILD" : "MENU";

					return true;
				}

				if (e.IsRepeat)
					return false;

				foreach (var (key, run) in hotkeys)
				{
					if (key.IsActivatedBy(e))
					{
						run();
						return true;
					}
				}

				return false;
			});
		}

		DrIgiButtonWidget Button(string name) => widget.Get<DrIgiButtonWidget>(name);

		/// <summary>
		/// The original's end of a mission: "Mission Successful" or "Mission Failed" over the map, and Continue,
		/// which leaves for the menus (the debrief after a win, else the mission ring). DrLoadIngameUILogic
		/// leaves OpenRA's in-game menu shut when this is here.
		/// </summary>
		void BindMissionEnd(Player player)
		{
			widget.Get("MISSION_END").IsVisible = () => missionEnd != null;
			widget.Get<DrIgiBoxWidget>("MISSION_END_BOX").GetText = () => missionEnd;
			Button("MISSION_END_CONTINUE").OnClick = Leave;

			// The game's end comes from synced code; it closes any window open over the battlefield.
			world.GameOver += () => Sync.RunUnsynced(world, () =>
			{
				closeWindow?.Invoke();
				ClosePopups();
				confirmed = null;
				world.CancelInputMode();
				var won = player != null && player.WinState == WinState.Won;
				missionEnd = igi.Library?.GetString(won ? "MLS_EVNT_MSUCCESS" : "MLS_EVNT_MFAILURE")
					?? (won ? "Mission Successful" : "Mission Failed");
			});
		}

		/// <summary>Back to the menus, as OpenRA's Leave does; the shell then shows what follows the mission.</summary>
		static void Leave() => Game.RunAfterTick(() =>
		{
			Game.Disconnect();
			Ui.ResetAll();
			Game.LoadShellMap();
		});

		/// <summary>
		/// Dark Reign's keyboard commands (its F1 list, dark/local/HELP.TXT), as rebindable hotkeys
		/// (mods/dr/hotkeys.yaml): the tabs, and the buttons they press wherever their tab is.
		/// </summary>
		void BindHotkeys()
		{
			void Bind(string hotkey, Action run) => hotkeys.Add((modData.Hotkeys[hotkey], run));
			void Press(string hotkey, string name)
			{
				var button = Button(name);
				Bind(hotkey, () =>
				{
					if (!button.IsVisible() || button.IsDisabled())
						return;

					Game.Sound.PlayNotification(modData.DefaultRules, null, "Sounds", ChromeMetrics.Get<string>("ClickSound"), null);
					button.OnClick();
				});
			}

			Bind("IgiBuildTab", () => tab = "BUILD");
			Bind("IgiCommsTab", () => tab = "COMMS");
			Bind("IgiOrdersTab", () => tab = "ORDERS");
			Bind("IgiPathsTab", () => tab = "PATHS");
			Bind("IgiSpecialTab", () => tab = "SPECIAL");
			Bind("IgiHotkeyList", () => OpenSettings("HOTKEYS_PANEL"));
			Press("IgiExitToMainMenu", "ABORT");

			Press("IgiAttack", "ATTACK");
			Press("IgiAttackInPlace", "ATTACK_IN_PLACE");
			Press("Stop", "STOP");
			Press("Sell", "SELL");
			Press("Repair", "REPAIR");
			Press("PowerDown", "POWER");
			Press("IgiSetExitPoint", "SET_EXIT_POINT");
			Press("IgiSellWater", "SELL_WATER");
			Bind("IgiAddWaypoints", () =>
			{
				if (Selected.Any(CanFollowPath) && world.OrderGenerator is not DrWaypointOrderGenerator)
					AddWaypoints();
			});

			// As OpenRA's command bar orders them.
			Bind("Scatter", () => IssueOrders(Selected.Where(a => a.Info.HasTraitInfo<IMoveInfo>()).Select(a => new Order("Scatter", a, false))));
			Bind("Deploy", () => IssueOrders(Selected
				.SelectMany(a => a.TraitsImplementing<IIssueDeployOrder>().Where(d => d.CanIssueDeployOrder(a, false)).Select(d => d.IssueDeployOrder(a, false)))
				.Where(o => o != null)));

			// OpenRA's guard (follow and protect a unit): the original's Guard button is a behaviour preset.
			static bool CanGuard(Actor a) => a.Info.HasTraitInfo<GuardInfo>() && a.Info.HasTraitInfo<AutoTargetInfo>();
			Bind("Guard", () =>
			{
				var guards = Selected.Where(CanGuard).ToArray();
				if (guards.Length > 0)
					world.OrderGenerator = new GuardOrderGenerator(world, guards, "Guard", "guard");
			});
		}

		void IssueOrders(IEnumerable<Order> orders)
		{
			var all = orders.ToArray();
			foreach (var o in all)
				world.IssueOrder(o);

			all.PlayVoiceForOrders();
		}

		IEnumerable<Actor> Selected => world.Selection.Actors.Where(a => a.Owner == world.LocalPlayer && a.IsInWorld && !a.IsDead);

		void BindToggle<T>(string name) where T : IOrderGenerator
		{
			var button = Button(name);
			button.IsHighlighted = () => world.OrderGenerator is T;
			button.OnClick = () =>
			{
				if (world.OrderGenerator is T)
					world.CancelInputMode();
				else
					world.OrderGenerator = (IOrderGenerator)typeof(T).GetConstructor([typeof(World)]).Invoke([world]);
			};
		}

		void BindTopBar(Player player)
		{
			BindToggle<SellOrderGenerator>("SELL");
			BindToggle<PowerDownOrderGenerator>("POWER");
			BindToggle<RepairOrderGenerator>("REPAIR");

			var attack = Button("ATTACK");
			attack.IsDisabled = () => !Selected.Any(a => a.Info.HasTraitInfo<AttackBaseInfo>());
			attack.IsHighlighted = () => world.OrderGenerator is ForceModifiersOrderGenerator f && f.Modifiers.HasFlag(Modifiers.Ctrl);
			attack.OnClick = () =>
			{
				if (attack.IsHighlighted())
					world.CancelInputMode();
				else
					world.OrderGenerator = new ForceModifiersOrderGenerator(world, Modifiers.Ctrl, true);
			};

			static bool CanAttackInPlace(Actor a) => a.Info.HasTraitInfo<DrAttackInPlaceInfo>() && a.Info.HasTraitInfo<AttackBaseInfo>();
			var inPlace = Button("ATTACK_IN_PLACE");
			inPlace.IsDisabled = () => !DrTargetOrderGenerator.Any(world, CanAttackInPlace);
			inPlace.IsHighlighted = () => world.OrderGenerator is DrTargetOrderGenerator;
			inPlace.OnClick = () => world.OrderGenerator = new DrTargetOrderGenerator(world, DrAttackInPlace.OrderName, "attack", true, CanAttackInPlace);

			var stop = Button("STOP");
			stop.IsDisabled = () => !Selected.Any();
			stop.OnClick = () =>
			{
				var actors = Selected.ToArray();
				if (actors.Length > 0)
					world.IssueOrder(new Order("Stop", null, false, null, actors));
			};

			var resources = player.PlayerActor.Trait<PlayerResources>();
			var topBar = widget.Get<DrIgiTopBarWidget>("TOP_BAR");
			topBar.GetCredits = () => (resources.Cash + resources.Resources).ToString(CultureInfo.InvariantCulture);

			// A double click on the credits forces a water sale, whose cost the credits' tooltip gives meanwhile.
			var water = player.PlayerActor.TraitOrDefault<DrPlayerResources>();
			var creditTooltip = topBar.GetTooltip;
			var saleTooltip = igi.Library?.GetString("MLS_DISP_WATERSALE");
			topBar.OnCreditsDoubleClick = () => SellWater(player);
			topBar.GetTooltip = () =>
			{
				var cost = water?.ForcedSaleCost ?? 0;
				return cost > 0 && saleTooltip != null ? saleTooltip.Replace("%d", cost.ToString(CultureInfo.InvariantCulture)) : creditTooltip?.Invoke();
			};
		}

		void BindBuild(Player player)
		{
			var menu = widget.Get<DrIgiBuildMenuWidget>("BUILD_MENU");
			var up = Button("SCROLL_UP");
			up.IsDisabled = () => !menu.CanScrollUp;
			up.OnClick = menu.ScrollUp;
			var down = Button("SCROLL_DOWN");
			down.IsDisabled = () => !menu.CanScrollDown;
			down.OnClick = menu.ScrollDown;

			// The selected building's upgrade, made by the headquarters' upgrade queue.
			(ProductionQueue Queue, ActorInfo Item) Upgrade()
			{
				var building = Selected.FirstOrDefault(a => a.Info.HasTraitInfo<BuildingInfo>());
				if (building == null)
					return default;

				var family = UpgradeFamily(building);
				if (family == null)
					return default;

				foreach (var queue in world.ActorsWithTrait<ProductionQueue>()
					.Where(p => p.Actor.Owner == player && p.Trait.Enabled && p.Trait.Info.Type == "Upgrade").Select(p => p.Trait))
				{
					var item = queue.AllItems().FirstOrDefault(i => i.Name.StartsWith(family, StringComparison.Ordinal) && !UpgradeDone(i));
					if (item != null)
						return (queue, item);
				}

				return default;
			}

			var upgrade = Button("UPGRADE");
			upgrade.IsDisabled = () =>
			{
				var (queue, item) = Upgrade();
				return queue == null || !queue.BuildableItems().Contains(item) || queue.AllQueued().Any(q => q.Item == item.Name);
			};

			upgrade.IsHighlighted = () =>
			{
				var (queue, item) = Upgrade();
				return queue != null && queue.AllQueued().Any(q => q.Item == item.Name);
			};

			upgrade.OnClick = () =>
			{
				var (queue, item) = Upgrade();
				if (queue != null)
					world.IssueOrder(Order.StartProduction(queue.Actor, item.Name, 1));
			};

			upgrade.GetTooltip = () =>
			{
				var lib = igi.Library;
				if (!Selected.Any(a => a.Info.HasTraitInfo<BuildingInfo>()))
					return lib.GetString("MLS_DISP_SELUP");

				var (queue, item) = Upgrade();
				if (queue == null)
					return lib.GetString("MLS_DISP_NOUP");

				if (queue.AllQueued().Any(q => q.Item == item.Name))
					return lib.GetString("MLS_DISP_UPGRADE");

				var cost = item.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
				return lib.GetString("MLS_DISP_UPGRADE_N").Replace("%d", cost.ToString(CultureInfo.InvariantCulture));
			};

			// Decoys are not in OpenDR yet.
			Button("DECOY").IsDisabled = () => true;
		}

		bool UpgradeDone(ActorInfo item)
		{
			var prerequisite = item.TraitInfos<ProvidesPrerequisiteInfo>().Select(p => p.Prerequisite).FirstOrDefault(p => p != null) ?? item.Name;
			return world.LocalPlayer.PlayerActor.Trait<TechTree>().HasPrerequisites([prerequisite]);
		}

		/// <summary>The upgrades a building has: those named after what it provides (hq, barracks, vehicles, phasing).</summary>
		static string UpgradeFamily(Actor building)
		{
			var provides = building.Info.TraitInfos<ProvidesPrerequisiteInfo>().Select(p => p.Prerequisite).ToHashSet();
			if (provides.Contains("hq"))
				return "upgrade.hq";
			if (provides.Contains("barracks"))
				return "upgrade.barracks";
			if (provides.Contains("vehicles"))
				return "upgrade.assemblyplant";
			if (provides.Contains("phasing"))
				return "upgrade.phasing";

			return null;
		}

		void BindMenu()
		{
			var singlePlayer = world.LobbyInfo.NonBotClients.Count() == 1;
			var restart = Button("RESTART");
			restart.IsVisible = () => singlePlayer;
			restart.OnClick = () => Confirm(Game.RestartGame);
			Button("RELINQUISH").IsVisible = () => !singlePlayer;
			Button("RELINQUISH").IsDisabled = () => true;

			Button("ABORT").OnClick = () => Confirm(Leave);

			Button("EXIT").OnClick = () => Confirm(Game.Exit);

			var effects = widget.Get<DrIgiSliderWidget>("EFFECTS_VOLUME");
			effects.GetValue = () => Game.Settings.Sound.SoundVolume;
			effects.OnChange = v =>
			{
				Game.Settings.Sound.SoundVolume = v;
				Game.Sound.SoundVolume = v;
				Game.Settings.Save();
			};

			var music = widget.Get<DrIgiSliderWidget>("MUSIC_VOLUME");
			music.GetValue = () => Game.Settings.Sound.MusicVolume;
			music.OnChange = v =>
			{
				Game.Settings.Sound.MusicVolume = v;
				Game.Sound.MusicVolume = v;
				Game.Settings.Save();
			};

			// The game's speed is fixed once a game starts in OpenRA.
			var speed = widget.Get<DrIgiSliderWidget>("GAME_SPEED");
			speed.GetValue = () => 0.5f;
			speed.IsDisabled = () => true;

			var scroll = widget.Get<DrIgiSliderWidget>("SCROLL_SPEED");
			scroll.GetValue = () => (Game.Settings.Game.ViewportEdgeScrollStep - 10) / 40f;
			scroll.OnChange = v =>
			{
				Game.Settings.Game.ViewportEdgeScrollStep = 10 + 40 * v;
				Game.Settings.Save();
			};
		}

		/// <summary>The original's "Are You Sure?", or another of its questions in that box, before an action.</summary>
		void Confirm(Action action, string question = null)
		{
			confirmText = question != null ? igi.Library?.GetString(question) : null;
			confirmed = action;
		}

		void ClosePopups()
		{
			closeLoadSave?.Invoke();
			objectiveOpen = false;
		}

		/// <summary>
		/// The original's Load/Save popup (Load/Save Game on the MENU tab): the saved games, a name to save under,
		/// and Load, Save and Delete. Choosing a game puts its name in the field, so Save overwrites it after the
		/// original's "Overwrite existing file?"; a double click loads it. Alone against the computer the game
		/// waits while the popup is open.
		/// </summary>
		void BindLoadSave(Player player)
		{
			var saves = new List<DrSavedGame>();
			var names = new List<string>();
			var selected = -1;
			DrSavedGame Selected() => selected >= 0 && selected < saves.Count ? saves[selected] : null;

			var name = widget.Get<DrIgiTextFieldWidget>("SAVE_NAME");
			var list = widget.Get<DrIgiListWidget>("SAVE_LIST");
			widget.Get("LOAD_SAVE_POPUP").IsVisible = () => loadSaveOpen;
			list.GetItems = () => names;
			list.GetSelected = () => selected;
			list.OnSelect = i =>
			{
				selected = i;
				name.Text = saves[i].Name;
			};

			var up = Button("SAVE_LIST_UP");
			up.IsDisabled = () => !list.CanScrollUp;
			up.OnClick = () => list.Scroll(-1);
			var down = Button("SAVE_LIST_DOWN");
			down.IsDisabled = () => !list.CanScrollDown;
			down.OnClick = () => list.Scroll(1);

			void Refresh()
			{
				saves = DrSavedGames.List(modData);
				names = saves.Select(s => s.Name).ToList();
				selected = -1;
				list.ScrollToTop();
			}

			var pause = world.LobbyInfo.NonBotClients.Count() == 1;
			var wasPaused = false;
			closeLoadSave = () =>
			{
				if (!loadSaveOpen)
					return;

				loadSaveOpen = false;
				name.YieldKeyboardFocus();
				if (pause && !world.IsGameOver)
					world.SetPauseState(wasPaused);
			};

			bool CanSave() => world.Type == WorldType.Regular && !world.IsReplay && world.LobbyInfo.GlobalSettings.EnableGameSaves
				&& player != null && player.WinState == WinState.Undefined && !world.IsGameOver;

			void Save()
			{
				var file = name.Text.Trim();
				if (!CanSave() || file.Length == 0 || confirmed != null)
					return;

				void Write()
				{
					world.RequestGameSave(file + ".orasav", false);
					closeLoadSave();
				}

				if (File.Exists(Path.Combine(DrSavedGames.Folder(modData), file + ".orasav")))
					Confirm(Write, "MLS_IGI_SAVE_OVERWRITE");
				else
					Write();
			}

			void Load(DrSavedGame save)
			{
				if (save == null || confirmed != null)
					return;

				closeLoadSave();
				DrSavedGames.Load(save);
			}

			list.OnActivate = i => Load(saves[i]);

			var load = Button("LOAD_GAME");
			load.IsDisabled = () => Selected() == null || confirmed != null;
			load.OnClick = () => Load(Selected());

			var save = Button("SAVE_GAME");
			save.IsDisabled = () => !CanSave() || name.Text.Trim().Length == 0 || confirmed != null;
			save.OnClick = Save;
			name.OnEnter = Save;
			name.OnEscape = () => closeLoadSave();

			var delete = Button("DELETE_GAME");
			delete.IsDisabled = () => Selected() == null || confirmed != null;
			delete.OnClick = () =>
			{
				var doomed = Selected();
				Confirm(() =>
				{
					DrSavedGames.Delete(doomed);
					Refresh();
				});
			};

			Button("LOAD_SAVE").OnClick = () =>
			{
				if (loadSaveOpen)
				{
					closeLoadSave();
					return;
				}

				objectiveOpen = false;
				Refresh();
				name.Text = "";
				name.TakeKeyboardFocus();
				if (pause)
				{
					wasPaused = world.PredictedPaused;
					world.SetPauseState(true);
				}

				loadSaveOpen = true;
			};
		}

		/// <summary>
		/// Restate Objective: the original's text window over the map with the briefing's orders (0x42a690 reads
		/// the mission's .brf into zone 3); the button again, or Escape, closes it.
		/// </summary>
		void BindObjective(Player player)
		{
			var text = widget.Get<DrIgiTextWidget>("OBJECTIVE_TEXT");
			widget.Get("OBJECTIVE_WINDOW").IsVisible = () => objectiveOpen;
			Button("OBJECTIVES").OnClick = () =>
			{
				if (objectiveOpen)
				{
					objectiveOpen = false;
					return;
				}

				closeLoadSave?.Invoke();
				var script = world.WorldActor.TraitOrDefault<DrScenarioScript>();
				var orders = script?.Briefing(1);
				if (string.IsNullOrEmpty(orders))
					orders = script?.Briefing(0);

				if (string.IsNullOrEmpty(orders))
				{
					// A map without a briefing: its objectives, as OpenRA keeps them.
					var objectives = player?.PlayerActor.TraitOrDefault<MissionObjectives>()?.Objectives;
					orders = objectives == null ? "" : string.Join("\\n", objectives.Select(o => o.Description));
				}

				text.SetText(orders);
				objectiveOpen = true;
			};
		}

		/// <summary>
		/// The MENU tab's Advanced page: what the remaster adds to the original's menu, its settings, which open
		/// over the battlefield as OpenRA's in-game menu opens them.
		/// </summary>
		void BindMenuOptions()
		{
			BindAdvancedToggle("MENU_BASIC", "MENU_ADVANCED", () => advancedMenu, v => advancedMenu = v);
			widget.Get("MENU_BASIC_PANEL").IsVisible = () => !advancedMenu;
			widget.Get("MENU_ADVANCED_PANEL").IsVisible = () => advancedMenu;
			widget.Get<DrIgiImageWidget>("MENU_TOGGLE").GetFrame = () => advancedMenu ? 1 : 0;

			Button("SETTINGS").OnClick = () => OpenSettings();
		}

		/// <summary>OpenRA's settings, on one of its tabs if given; they open on the first, and their tab buttons carry the panels' names.</summary>
		void OpenSettings(string panel = null)
		{
			OpenWindow("SETTINGS_PANEL", window =>
			{
				if (panel != null)
					window.Get("SETTINGS_TAB_CONTAINER").GetOrNull<ButtonWidget>(panel)?.OnClick();
			});
		}

		/// <summary>
		/// One of OpenRA's windows over the battlefield, as its in-game menu opens them: the interface hidden,
		/// world sounds off and, alone against the computer, the game paused, until it closes.
		/// </summary>
		void OpenWindow(string id, Action<Widget> opened = null)
		{
			if (windowOpen)
				return;

			var worldRoot = Ui.Root.Get("WORLD_ROOT");
			var pause = world.LobbyInfo.NonBotClients.Count() == 1;
			var paused = world.PredictedPaused;
			var sounds = Game.Sound.DisableWorldSounds;

			world.CancelInputMode();
			worldRoot.IsVisible = () => false;
			Game.Sound.DisableWorldSounds = true;
			if (pause)
				world.SetPauseState(true);

			windowOpen = true;
			closeWindow = () =>
			{
				worldRoot.IsVisible = () => true;
				Game.Sound.DisableWorldSounds = sounds;
				if (pause)
					world.SetPauseState(paused);

				windowOpen = false;
				closeWindow = null;
			};

			var window = Game.OpenWindow(id, new WidgetArgs { { "onExit", closeWindow } });

			opened?.Invoke(window);
			Game.RunAfterTick(Ui.ResetTooltips);
		}

		void BindAdvancedToggle(string basic, string advanced, Func<bool> get, Action<bool> set)
		{
			var b = Button(basic);
			b.IsHighlighted = () => !get();
			b.OnClick = () => set(false);
			var a = Button(advanced);
			a.IsHighlighted = get;
			a.OnClick = () => set(true);
		}

		void BindOrders()
		{
			BindAdvancedToggle("ORDERS_BASIC", "ORDERS_ADVANCED", () => advancedOrders, v => advancedOrders = v);
			widget.Get("ORDERS_ADVANCED_PANEL").IsVisible = () => advancedOrders;
			widget.Get<DrIgiImageWidget>("ORDERS_TOGGLE").GetFrame = () => advancedOrders ? 1 : 0;

			// The selected units' tactics (DrTactics): their orders, the behaviour presets and the three settings.
			DrTactics[] Tactics() => Selected.Select(a => a.TraitOrDefault<DrTactics>()).Where(t => t != null).ToArray();
			void Send(DrTactics.Field field, int value = 0)
			{
				var actors = Selected.Where(a => a.Info.HasTraitInfo<DrTacticsInfo>()).ToArray();
				if (actors.Length == 0)
					return;

				world.IssueOrder(new Order(DrTactics.OrderName, null, false, groupedActors: actors) { ExtraData = DrTactics.Pack(field, value) });
				if (field == DrTactics.Field.Order && value != (int)DrUnitOrder.None)
					actors.Select(a => new Order("Move", a, false)).ToArray().PlayVoiceForOrders();
			}

			// The setting the selection shares, else none lit (the original's 3).
			static int Common(DrTactics[] all, Func<DrTactics, int> get) => all.Length > 0 && all.All(t => get(t) == get(all[0])) ? get(all[0]) : -1;

			foreach (var (name, order) in new[] { ("SCOUT", DrUnitOrder.Scout), ("HARASS", DrUnitOrder.Harass), ("SEARCH_DESTROY", DrUnitOrder.SearchAndDestroy) })
			{
				var button = Button(name);
				button.IsDisabled = () => Tactics().Length == 0;
				button.IsHighlighted = () => Common(Tactics(), t => (int)t.Order) == (int)order;

				// Pressed again it is cancelled, as the original's toggles were.
				button.OnClick = () => Send(DrTactics.Field.Order, (int)(button.IsHighlighted() ? DrUnitOrder.None : order));
			}

			foreach (var (name, preset) in new[] { ("GUARD", DrTactics.Field.Guard), ("PURSUE", DrTactics.Field.Pursue), ("DEFAULT", DrTactics.Field.Default) })
			{
				var button = Button(name);
				button.IsDisabled = () => Tactics().Length == 0;
				button.OnClick = () => Send(preset);
			}

			foreach (var (name, field, get) in new (string, DrTactics.Field, Func<DrTactics, int>)[]
			{
				("PURSUIT", DrTactics.Field.Pursuit, t => t.Pursuit),
				("TOLERANCE", DrTactics.Field.Tolerance, t => t.Tolerance),
				("INDEPENDENCE", DrTactics.Field.Independence, t => t.Independence),
			})
			{
				var level = widget.Get<DrIgiLevelWidget>(name);
				level.IsDisabled = () => Tactics().Length == 0;
				level.GetValue = () => Common(Tactics(), get);
				level.OnSelect = v => Send(field, v);
			}

			// Use As Default: what the rows show, for the units built from now on.
			var setDefault = Button("SET_DEFAULT");
			setDefault.IsDisabled = () => Tactics().Length == 0 || world.LocalPlayer == null;
			setDefault.OnClick = () =>
			{
				var all = Tactics();
				int Value(Func<DrTactics, int> get) => Common(all, get) is var v && v >= 0 ? v : get(all[0]);
				world.IssueOrder(new Order(DrTacticsDefaults.OrderName, world.LocalPlayer.PlayerActor, false)
				{
					ExtraData = DrTacticsDefaults.Pack(Value(t => t.Pursuit), Value(t => t.Tolerance), Value(t => t.Independence))
				});
			};
		}

		void BindPaths()
		{
			BindAdvancedToggle("PATHS_BASIC", "PATHS_ADVANCED", () => advancedPaths, v => advancedPaths = v);
			widget.Get("PATHS_ADVANCED_PANEL").IsVisible = () => advancedPaths;
			widget.Get<DrIgiImageWidget>("PATHS_TOGGLE").GetFrame = () => advancedPaths ? 1 : 0;

			var noTrail = igi.Library?.GetString("MLS_EVNT_NOTRAIL") ?? "";
			var newTrail = igi.Library?.GetString("MLS_EVNT_NEWTRAIL") ?? "Trail %d";
			var name = widget.Get<DrIgiTextFieldWidget>("CURRENT_PATH");
			name.Text = noTrail;
			void Choose(IgiPath chosen)
			{
				path = chosen;
				name.Text = chosen.Name ?? noTrail;
				name.YieldKeyboardFocus();
			}

			var add = Button("ADD_WAYPOINTS");
			add.IsHighlighted = () => world.OrderGenerator is DrWaypointOrderGenerator;
			add.OnClick = AddWaypoints;

			// The path being laid, else the chosen saved path, else the selected units' paths (0x4b7410).
			var clear = Button("CLEAR_ALL");
			clear.IsDisabled = () => path.Points.Count == 0 && path.Name == null && !Selected.Any(CanFollowPath);
			clear.OnClick = () =>
			{
				if (path.Points.Count > 0)
					path.Points.Clear();
				else if (path.Name != null)
				{
					savedPaths.Remove(path);
					Choose(new IgiPath());
				}
				else
					IssueOrders(Selected.Where(CanFollowPath).Select(a => new Order("Stop", a, false)));
			};

			var delete = Button("DELETE_WAYPOINT");
			delete.IsDisabled = () => path.Points.Count == 0;
			delete.OnClick = () => path.Points.RemoveAt(path.Points.Count - 1);

			var go = Button("GO");
			go.IsDisabled = () => path.Points.Count == 0 || !Selected.Any(CanFollowPath);
			go.OnClick = FollowPath;

			// Basic paths are one way (the manual, p. 50); the direction is the advanced page's.
			var direction = widget.Get<DrIgiLevelWidget>("PATH_DIRECTION");
			direction.GetValue = () => (int)path.Mode;
			direction.OnSelect = v => path.Mode = (DrPathMode)v;

			name.IsDisabled = () => path.Name == null;
			name.OnEnter = () =>
			{
				if (path.Name != null && name.Text.Trim().Length > 0)
					path.Name = name.Text.Trim();

				name.Text = path.Name ?? noTrail;
				name.YieldKeyboardFocus();
			};
			name.OnEscape = () => name.Text = path.Name ?? noTrail;

			var list = widget.Get<DrIgiListWidget>("SAVED_PATHS");
			list.GetItems = () => savedPaths.Select(p => p.Name).ToList();
			list.GetSelected = () => savedPaths.IndexOf(path);
			list.OnSelect = i => Choose(savedPaths[i]);
			list.OnActivate = i =>
			{
				Choose(savedPaths[i]);
				FollowPath();
			};

			var deselect = Button("DESELECT");
			deselect.IsDisabled = () => path.Name == null;
			deselect.OnClick = () => Choose(new IgiPath());

			// Save Path keeps the path laid so far under the next name, or starts a new one to lay (the manual, p. 52).
			Button("SAVE_PATH").OnClick = () =>
			{
				var saved = path.Name == null && path.Points.Count > 0 ? path : new IgiPath { Mode = path.Mode };
				saved.Name = newTrail.Replace("%d", (++trails).ToString(CultureInfo.InvariantCulture));
				savedPaths.Add(saved);
				Choose(saved);
				if (saved.Points.Count == 0)
					AddWaypoints();
			};

			var overlay = widget.Get<DrIgiPathOverlayWidget>("PATH_OVERLAY");
			overlay.GetPoints = () => tab == "PATHS" || world.OrderGenerator is DrWaypointOrderGenerator ? path.Points : [];
			overlay.IsLooped = () => advancedPaths && path.Mode == DrPathMode.Loop;
		}

		/// <summary>A path on the PATHS tab: the one being laid, or one saved under a name ("Trail 1").</summary>
		sealed class IgiPath
		{
			public string Name;
			public readonly List<CPos> Points = [];
			public DrPathMode Mode;
		}

		IgiPath path = new();
		readonly List<IgiPath> savedPaths = [];
		int trails;

		static bool CanFollowPath(Actor a) => a.Info.HasTraitInfo<DrPathFollowerInfo>();

		void AddWaypoints()
		{
			tab = "PATHS";
			world.OrderGenerator = new DrWaypointOrderGenerator(path.Points.Add, "move");
		}

		/// <summary>Go: the selection follows the path. One laid but not saved is done with once followed.</summary>
		void FollowPath()
		{
			var actors = Selected.Where(CanFollowPath).ToArray();
			if (actors.Length == 0 || path.Points.Count == 0)
				return;

			var mode = advancedPaths ? path.Mode : DrPathMode.OneWay;
			world.IssueOrder(new Order(DrPathFollower.OrderName, null, false, groupedActors: actors)
			{
				TargetString = DrPathFollower.Encode(mode, path.Points.ToArray())
			});
			actors.Select(a => new Order("Move", a, false)).ToArray().PlayVoiceForOrders();

			if (world.OrderGenerator is DrWaypointOrderGenerator)
				world.CancelInputMode();

			if (path.Name == null)
				path.Points.Clear();
		}

		void BindSpecial()
		{
			static bool HasRallyPoint(Actor a) => a.Info.HasTraitInfo<RallyPointInfo>();
			var exit = Button("SET_EXIT_POINT");
			exit.IsDisabled = () => !DrTargetOrderGenerator.Any(world, HasRallyPoint);
			exit.OnClick = () => world.OrderGenerator = new DrTargetOrderGenerator(world, "SetRallyPoint", "ability", false, HasRallyPoint);

			var water = world.LocalPlayer?.PlayerActor.TraitOrDefault<DrPlayerResources>();
			var sellWater = Button("SELL_WATER");
			sellWater.IsDisabled = () => water == null || water.ForcedSaleCost == 0;
			sellWater.OnClick = () => SellWater(world.LocalPlayer);

			foreach (var name in new[] { "MORPH", "UNMORPH", "PHASE", "UNPHASE", "SELF_DESTRUCT", "FORMATION_MOVE", "PACK" })
				Button(name).IsDisabled = () => true;
		}

		/// <summary>Launches whatever water the pads hold, less a fee for each: the original's forced sale.</summary>
		void SellWater(Player player)
		{
			var water = player?.PlayerActor.TraitOrDefault<DrPlayerResources>();
			if (water != null && water.ForcedSaleCost > 0)
				world.IssueOrder(new Order(DrPlayerResources.SellWaterOrder, player.PlayerActor, false));
		}

		void BindMinimap(Player player)
		{
			var radar = widget.Get<RadarWidget>("RADAR_MINIMAP");
			widget.Get("RADAR_STATIC").IsVisible = () => !radar.IsEnabled();

			var power = player.PlayerActor.TraitOrDefault<PowerManager>();
			var water = player.PlayerActor.TraitOrDefault<DrPlayerResources>();
			var bars = widget.Get<DrIgiResourceBarsWidget>("RESOURCE_BARS");

			// The power well holds what the base makes, against the most it needs or makes.
			float Scale() => Math.Max(1, Math.Max(power?.PowerProvided ?? 0, power?.PowerDrained ?? 0));
			bars.GetPower = () => power == null ? 0 : power.PowerProvided / Scale();
			bars.GetPowerUsed = () => power == null ? 0 : power.PowerDrained / Scale();
			bars.IsLowPower = () => power != null && power.PowerState != PowerState.Normal;
			bars.GetWater = () => water == null ? 0 : water.WaterFraction;
			bars.GetTooltip = () => power == null ? null : $"{power.PowerDrained}/{power.PowerProvided}";

			// A light for each of the original's eight teams, in its colour: the player's own brightest, mutual
			// allies lit, the rest dark (0x42d300). A converted mission's players are its teams; elsewhere the
			// players in order.
			var teams = Enumerable.Range(0, 8).Select(t => world.Players.FirstOrDefault(p => p.InternalName == ImportDrCampaignCommand.TeamName(t))).ToArray();
			if (teams.All(p => p == null))
				teams = world.Players.Where(p => !p.NonCombatant && p.Playable || p.IsBot).Take(8).Concat(new Player[8]).Take(8).ToArray();

			var lights = widget.Get<DrIgiTeamLightsWidget>("TEAM_LIGHTS");
			lights.GetColor = team => teams[team]?.Color;
			lights.GetLight = team =>
			{
				var p = teams[team];
				if (p == null || p.WinState == WinState.Lost)
					return 0;

				return p == player ? 3 : p.IsAlliedWith(player) && player.IsAlliedWith(p) ? 1 : 0;
			};
		}
	}
}
