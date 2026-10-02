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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.Dr.Orders;
using OpenRA.Mods.Dr.Traits;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>
	/// The build menu (zone 44 of the original, 448,64 to 639,313): three columns of five 64x50 slots in
	/// BUISOBOX.BMP's frames, each with the item's own menu picture. With a construction rig selected it
	/// holds the buildings the rig can put up; otherwise the units every production building can make. As the
	/// manual has it, an item the player lacks the prerequisites for is red, and one the selected building
	/// cannot make is blue. A left click orders one more (or resumes it), a right click pauses it and a second
	/// cancels it, shift and a right click cancels them all. A number shows how many more are queued.
	/// </summary>
	public class DrIgiBuildMenuWidget : DrIgiAreaWidget
	{
		public const int Columns = 3;
		public const int Rows = 5;
		public const int SlotWidth = 64;
		public const int SlotHeight = 50;

		static readonly string[] QueueOrder = ["Economy", "Infantry", "Vehicle"];

		public readonly Color UnavailableTint = Color.FromArgb(120, 200, 16, 16);
		public readonly Color WrongFacilityTint = Color.FromArgb(120, 24, 48, 220);
		public readonly Color ProgressVeil = Color.FromArgb(150, 0, 0, 0);
		public readonly string ClickSound = ChromeMetrics.Get<string>("ClickSound");
		public readonly string ClickDisabledSound = ChromeMetrics.Get<string>("ClickDisabledSound");

		sealed class Item
		{
			public ActorInfo Actor;
			public ProductionQueue Queue;
			public BuilderUnit Builder;
			public bool Buildable;
			public bool WrongFacility;
			public string Faction;
		}

		readonly World world;
		readonly WorldRenderer worldRenderer;
		readonly Dictionary<(string, string), Animation> icons = [];
		List<Item> items = [];
		int hovered = -1;

		public int RowOffset { get; private set; }
		public int ItemCount => items.Count;
		public bool CanScrollUp => RowOffset > 0;
		public bool CanScrollDown => (RowOffset + Rows) * Columns < items.Count;

		/// <summary>The construction rig the menu shows the buildings of, if any.</summary>
		public BuilderUnit Builder { get; private set; }

		[ObjectCreator.UseCtor]
		public DrIgiBuildMenuWidget(World world, WorldRenderer worldRenderer)
		{
			this.world = world;
			this.worldRenderer = worldRenderer;
			GetTooltip = () => hovered >= 0 && hovered < items.Count ? TooltipFor(items[hovered]) : null;
		}

		public void ScrollUp()
		{
			if (CanScrollUp)
				RowOffset--;
		}

		public void ScrollDown()
		{
			if (CanScrollDown)
				RowOffset++;
		}

		public override void Tick()
		{
			base.Tick();
			var player = world.LocalPlayer;
			if (player == null || player.WinState != WinState.Undefined)
			{
				items = [];
				return;
			}

			var selected = world.Selection.Actors.Where(a => a.Owner == player && a.IsInWorld && !a.IsDead).ToArray();
			var builder = selected.SelectMany(a => a.TraitsImplementing<BuilderUnit>()).FirstOrDefault(b => b.Enabled);
			if (builder != Builder)
				RowOffset = 0;

			Builder = builder;
			items = builder != null ? BuildingItems(builder) : UnitItems(player, selected);
			RowOffset = Math.Max(0, Math.Min(RowOffset, (items.Count + Columns - 1) / Columns - Rows));
		}

		static List<Item> BuildingItems(BuilderUnit builder)
		{
			var buildable = builder.BuildableItems().ToHashSet();
			return builder.AllItems()
				.OrderBy(a => a.TraitInfo<BuildableInfo>().BuildPaletteOrder)
				.Select(a => new Item { Actor = a, Builder = builder, Buildable = buildable.Contains(a), Faction = builder.Faction })
				.ToList();
		}

		static List<Item> UnitItems(Player player, Actor[] selected)
		{
			// One queue of each type: the selected building's, else the primary building's, else any.
			var queues = player.World.ActorsWithTrait<ProductionQueue>()
				.Where(p => p.Actor.Owner == player && p.Actor.IsInWorld && !p.Actor.IsDead && p.Trait.Enabled
					&& p.Trait is not BuilderQueue && p.Trait.Info.Type != "Dummy" && p.Trait.Info.Type != "Upgrade")
				.GroupBy(p => p.Trait.Info.Type)
				.Select(g => g.OrderByDescending(p => selected.Contains(p.Actor))
					.ThenByDescending(p => p.Actor.TraitOrDefault<PrimaryBuilding>()?.IsPrimary ?? false)
					.First().Trait)
				.OrderBy(q => Array.IndexOf(QueueOrder, q.Info.Type) is var i && i < 0 ? QueueOrder.Length : i)
				.ToList();

			var selectedTypes = selected.SelectMany(a => a.TraitsImplementing<ProductionQueue>()).Select(q => q.Info.Type).ToHashSet();
			var list = new List<Item>();
			foreach (var queue in queues)
			{
				var buildable = queue.BuildableItems().ToHashSet();
				var faction = queue.Actor.TraitsImplementing<Production>().FirstOrDefault()?.Faction ?? player.Faction.InternalName;
				foreach (var actor in queue.AllItems().OrderBy(a => a.TraitInfo<BuildableInfo>().BuildPaletteOrder))
				{
					list.Add(new Item
					{
						Actor = actor,
						Queue = queue,
						Buildable = buildable.Contains(actor),
						WrongFacility = selectedTypes.Count > 0 && !selectedTypes.Contains(queue.Info.Type),
						Faction = faction,
					});
				}
			}

			return list;
		}

		int SlotAt(int2 location)
		{
			var b = RenderBounds;
			if (!b.Contains(location))
				return -1;

			var column = (int)((location.X - b.X) / (SlotWidth * Scale));
			var row = (int)((location.Y - b.Y) / (SlotHeight * Scale));
			if (column >= Columns || row >= Rows)
				return -1;

			var index = (RowOffset + row) * Columns + column;
			return index < items.Count ? index : -1;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			if (mi.Event == MouseInputEvent.Move)
			{
				hovered = SlotAt(mi.Location);
				return false;
			}

			if (mi.Event == MouseInputEvent.Scroll)
			{
				if (mi.Delta.Y > 0)
					ScrollUp();
				else if (mi.Delta.Y < 0)
					ScrollDown();

				return true;
			}

			if (mi.Event != MouseInputEvent.Down)
				return EventBounds.Contains(mi.Location);

			var slot = SlotAt(mi.Location);
			if (slot < 0)
				return EventBounds.Contains(mi.Location);

			var item = items[slot];
			var handled = mi.Button == MouseButton.Left ? Produce(item) : mi.Button == MouseButton.Right && Pause(item, mi.Modifiers.HasModifier(Modifiers.Shift));
			Game.Sound.PlayNotification(world.Map.Rules, world.LocalPlayer, "Sounds", handled ? ClickSound : ClickDisabledSound, null);
			return true;
		}

		bool Produce(Item item)
		{
			if (!item.Buildable || item.WrongFacility)
				return false;

			if (item.Builder != null)
			{
				world.OrderGenerator = new BuilderUnitBuildingOrderGenerator(item.Builder, item.Actor.Name, worldRenderer);
				return true;
			}

			var queued = item.Queue.AllQueued().FirstOrDefault(i => i.Item == item.Actor.Name);
			if (queued != null && queued.Paused)
				world.IssueOrder(Order.PauseProduction(item.Queue.Actor, item.Actor.Name, false));
			else
				world.IssueOrder(Order.StartProduction(item.Queue.Actor, item.Actor.Name, 1));

			return true;
		}

		bool Pause(Item item, bool all)
		{
			if (item.Queue == null)
				return false;

			var queued = item.Queue.AllQueued().Where(i => i.Item == item.Actor.Name).ToList();
			if (queued.Count == 0)
				return false;

			var current = item.Queue.CurrentItem();
			if (!all && current != null && current.Item == item.Actor.Name && !current.Paused)
				world.IssueOrder(Order.PauseProduction(item.Queue.Actor, item.Actor.Name, true));
			else
				world.IssueOrder(Order.CancelProduction(item.Queue.Actor, item.Actor.Name, all ? queued.Count : 1));

			return true;
		}

		string TooltipFor(Item item)
		{
			var tooltip = item.Actor.TraitInfos<TooltipInfo>().FirstOrDefault();
			var name = tooltip != null ? FluentProvider.GetMessage(tooltip.Name) : item.Actor.Name;
			var cost = item.Actor.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;
			return $"{name} {cost}c";
		}

		Animation Icon(Item item)
		{
			if (!icons.TryGetValue((item.Actor.Name, item.Faction), out var icon))
			{
				var rsi = item.Actor.TraitInfo<RenderSpritesInfo>();
				icon = new Animation(world, rsi.GetImage(item.Actor, item.Faction));
				icon.Play(item.Actor.TraitInfo<BuildableInfo>().Icon);
				icons[(item.Actor.Name, item.Faction)] = icon;
			}

			return icon;
		}

		public override void Draw()
		{
			if (Art == null)
				return;

			var b = RenderBounds;
			var s = Scale;
			var font = Igi.Font("FONT12W.PCX");
			for (var slot = 0; slot < Columns * Rows; slot++)
			{
				var index = RowOffset * Columns + slot;
				var position = new float2(b.X + slot % Columns * SlotWidth * s, b.Y + slot / Columns * SlotHeight * s);
				var r = new Rectangle((int)position.X, (int)position.Y, (int)Math.Ceiling(SlotWidth * s), (int)Math.Ceiling(SlotHeight * s));
				var item = index < items.Count ? items[index] : null;
				var queued = item?.Queue?.AllQueued().Where(i => i.Item == item.Actor.Name).ToList();
				var current = queued != null && queued.Count > 0 && item.Queue.CurrentItem() == queued[0] ? queued[0] : null;

				if (item != null)
				{
					var bi = item.Actor.TraitInfo<BuildableInfo>();
					var palette = bi.IconPaletteIsPlayerPalette ? bi.IconPalette + world.LocalPlayer.InternalName : bi.IconPalette;
					var sprite = Icon(item).Image;
					var center = position + new float2(SlotWidth, SlotHeight) * s / 2;
					Game.Renderer.EnableAntialiasingFilter();
					Game.Renderer.SpriteRenderer.DrawSprite(sprite, worldRenderer.Palette(palette), center - sprite.Size.XY * s / 2, s);
					Game.Renderer.DisableAntialiasingFilter();

					if (!item.Buildable)
						WidgetUtils.FillRectWithColor(r, UnavailableTint);
					else if (item.WrongFacility)
						WidgetUtils.FillRectWithColor(r, WrongFacilityTint);

					if (current != null && current.TotalTime > 0)
					{
						var veil = (int)(r.Height * (float)current.RemainingTime / current.TotalTime);
						if (veil > 0)
							WidgetUtils.FillRectWithColor(new Rectangle(r.X, r.Y, r.Width, veil), ProgressVeil);
					}
				}

				// The slot's frame: normal, under the mouse, or busy.
				var frame = current != null ? 2 : index == hovered && item != null ? 1 : 0;
				Igi.DrawSprite(Art.GetRegion("BUISOBOX.BMP", new Rectangle(frame * SlotWidth, 0, SlotWidth, SlotHeight)), position);

				if (current != null && current.Paused)
					Igi.DrawTextCentered(font, Igi.Library.GetString("MLS_DISP_PAUSED", "PAUSED"), r);

				if (queued != null && queued.Count > 1)
					Igi.DrawText(font, (queued.Count - 1).ToString(NumberFormatInfo), position + new float2(9, 7) * s);
			}
		}

		static readonly System.Globalization.NumberFormatInfo NumberFormatInfo = System.Globalization.NumberFormatInfo.InvariantInfo;
	}
}
