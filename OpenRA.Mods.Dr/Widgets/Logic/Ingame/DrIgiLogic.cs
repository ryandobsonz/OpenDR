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
using System.Linq;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.Orders;
using OpenRA.Mods.Dr.Traits;
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
		Action confirmed;

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
			BindMenuOptions();
			BindOrders();
			BindPaths();
			BindSpecial();
			BindMinimap(player);
			BindHotkeys();

			var confirm = widget.Get("CONFIRM");
			confirm.IsVisible = () => confirmed != null;
			widget.Get<DrIgiButtonWidget>("CONFIRM_YES").OnClick = () =>
			{
				var action = confirmed;
				confirmed = null;
				action?.Invoke();
			};
			widget.Get<DrIgiButtonWidget>("CONFIRM_NO").OnClick = () => confirmed = null;

			// Escape cancels a pending order (a building to place, a target to pick), else opens the menu; the
			// rest are the original's keyboard commands (BindHotkeys).
			widget.Get<LogicKeyListenerWidget>("IGI_KEYS").AddHandler(e =>
			{
				if (e.Event != KeyInputEvent.Down)
					return false;

				if (e.Key == Keycode.ESCAPE && e.Modifiers == Modifiers.None)
				{
					if (confirmed != null)
						confirmed = null;
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
			Press("Guard", "GUARD");
			Press("Sell", "SELL");
			Press("Repair", "REPAIR");
			Press("PowerDown", "POWER");
			Press("IgiSetExitPoint", "SET_EXIT_POINT");

			// As OpenRA's command bar orders them.
			Bind("Scatter", () => IssueOrders(Selected.Where(a => a.Info.HasTraitInfo<IMoveInfo>()).Select(a => new Order("Scatter", a, false))));
			Bind("Deploy", () => IssueOrders(Selected
				.SelectMany(a => a.TraitsImplementing<IIssueDeployOrder>().Where(d => d.CanIssueDeployOrder(a, false)).Select(d => d.IssueDeployOrder(a, false)))
				.Where(o => o != null)));
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
			var options = widget.GetOrNull<MenuButtonWidget>("OPTIONS_BUTTON");
			void OpenMenu() => options?.OnClick();

			Button("LOAD_SAVE").OnClick = OpenMenu;
			Button("OBJECTIVES").OnClick = OpenMenu;

			var singlePlayer = world.LobbyInfo.NonBotClients.Count() == 1;
			var restart = Button("RESTART");
			restart.IsVisible = () => singlePlayer;
			restart.OnClick = () => confirmed = Game.RestartGame;
			Button("RELINQUISH").IsVisible = () => !singlePlayer;
			Button("RELINQUISH").IsDisabled = () => true;

			Button("ABORT").OnClick = () => confirmed = () => Game.RunAfterTick(() =>
			{
				Game.Disconnect();
				Ui.ResetAll();
				Game.LoadShellMap();
			});

			Button("EXIT").OnClick = () => confirmed = Game.Exit;

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
			var window = Game.OpenWindow(id, new WidgetArgs
			{
				{
					"onExit", () =>
					{
						worldRoot.IsVisible = () => true;
						Game.Sound.DisableWorldSounds = sounds;
						if (pause)
							world.SetPauseState(paused);

						windowOpen = false;
					}
				}
			});

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

			static bool CanGuard(Actor a) => a.Info.HasTraitInfo<GuardInfo>() && a.Info.HasTraitInfo<AutoTargetInfo>();
			var guard = Button("GUARD");
			guard.IsDisabled = () => !Selected.Any(CanGuard);
			guard.IsHighlighted = () => world.OrderGenerator is GuardOrderGenerator;
			guard.OnClick = () => world.OrderGenerator = new GuardOrderGenerator(world, Selected.Where(CanGuard).ToArray(), "Guard", "guard");

			// Scout, Harass, Search & Destroy, Pursue, the behaviours and the defaults come with the units' tactics.
			foreach (var name in new[] { "SCOUT", "HARASS", "SEARCH_DESTROY", "PURSUE", "DEFAULT", "SET_DEFAULT" })
				Button(name).IsDisabled = () => true;
		}

		void BindPaths()
		{
			BindAdvancedToggle("PATHS_BASIC", "PATHS_ADVANCED", () => advancedPaths, v => advancedPaths = v);
			widget.Get("PATHS_ADVANCED_PANEL").IsVisible = () => advancedPaths;
			widget.Get<DrIgiImageWidget>("PATHS_TOGGLE").GetFrame = () => advancedPaths ? 1 : 0;

			foreach (var name in new[] { "ADD_WAYPOINTS", "CLEAR_ALL", "DELETE_WAYPOINT", "GO", "DESELECT", "SAVE_PATH" })
				Button(name).IsDisabled = () => true;
		}

		void BindSpecial()
		{
			static bool HasRallyPoint(Actor a) => a.Info.HasTraitInfo<RallyPointInfo>();
			var exit = Button("SET_EXIT_POINT");
			exit.IsDisabled = () => !DrTargetOrderGenerator.Any(world, HasRallyPoint);
			exit.OnClick = () => world.OrderGenerator = new DrTargetOrderGenerator(world, "SetRallyPoint", "ability", false, HasRallyPoint);

			foreach (var name in new[] { "MORPH", "UNMORPH", "PHASE", "UNPHASE", "SELF_DESTRUCT", "FORMATION_MOVE", "SELL_WATER", "PACK" })
				Button(name).IsDisabled = () => true;
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
			bars.GetWater = () => water == null ? 0 : water.WaterPercentage / 100f;
			bars.GetTooltip = () => power == null ? null : $"{power.PowerDrained}/{power.PowerProvided}";

			var players = world.Players.Where(p => !p.NonCombatant && p.Playable || p.IsBot).Take(8).ToArray();
			widget.Get<DrIgiTeamLightsWidget>("TEAM_LIGHTS").GetLight = team =>
				team < players.Length && players[team].WinState == WinState.Undefined ? (players[team] == player ? 12 : 8) : 0;
		}
	}
}
