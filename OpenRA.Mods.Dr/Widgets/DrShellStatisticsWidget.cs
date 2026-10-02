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
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;

namespace OpenRA.Mods.Dr.Widgets
{
	/// <summary>
	/// A mission's statistics, laid out as the original's shell (dkreign.exe) lays them out: COLLECTED, CREATED,
	/// LOST and DESTROYED over their columns, the columns' names below, then a row a team, each its side's name
	/// (and, in the results, who played it) and eight figures. In each column the highest figure is framed in
	/// red; a column of noughts has none. The debrief (0x581d3d) has two rows under its outcome; the results
	/// after a custom mission (cdebrief, 0x57e000) a row for each team a human or the computer played.
	/// </summary>
	public class DrShellStatisticsWidget : DrShellAreaWidget
	{
		[FluentReference]
		const string Collected = "label-dr-shell-collected";

		[FluentReference]
		const string Created = "label-dr-shell-created";

		[FluentReference]
		const string Lost = "label-dr-shell-lost";

		[FluentReference]
		const string Destroyed = "label-dr-shell-destroyed";

		[FluentReference]
		const string Player = "label-dr-shell-player";

		[FluentReference]
		const string Side = "label-dr-shell-side";

		[FluentReference]
		const string Water = "label-dr-shell-water";

		[FluentReference]
		const string Taelon = "label-dr-shell-taelon";

		[FluentReference]
		const string Units = "label-dr-shell-units";

		[FluentReference]
		const string Buildings = "label-dr-shell-buildings";

		[FluentReference]
		const string FreedomGuard = "label-dr-shell-side-freedom-guard";

		[FluentReference]
		const string Imperium = "label-dr-shell-side-imperium";

		[FluentReference]
		const string Civilian = "label-dr-shell-side-civilian";

		[FluentReference]
		const string Togran = "label-dr-shell-side-togran";

		[FluentReference]
		const string Xenite = "label-dr-shell-side-xenite";

		[FluentReference]
		const string Shadowhand = "label-dr-shell-side-shadowhand";

		// By the original's side numbers, as the scenarios give them.
		static readonly string[] Sides = [FreedomGuard, Imperium, Civilian, Togran, Xenite, Shadowhand];

		// The debrief art's colour 0xaa, which frames the highest figures.
		static readonly Color Frame = Color.FromArgb(215, 0, 8);

		public readonly string Font = "font12";

		[Desc("The tops of the groups' headings and of the columns' names.")]
		public readonly int GroupY = 315;
		public readonly int HeadingY = 335;

		[Desc("The first row's top, from one row to the next, and a row's height.")]
		public readonly int RowY = 356;
		public readonly int RowStep = 26;
		public readonly int RowHeight = 24;

		[Desc("The players' names, from the left at this x; none if negative.")]
		public readonly int PlayerX = -1;

		[Desc("The sides' names: centred in this width from SideX, or from the left at SideX if it is 0.")]
		public readonly int SideX = 52;
		public readonly int SideWidth = 104;

		[Desc("The first figure's left, from one column to the next, a column's width and a group's.")]
		public readonly int CellX = 157;
		public readonly int CellStep = 53;
		public readonly int CellWidth = 52;
		public readonly int GroupWidth = 105;

		public Func<(string Player, int Side, int[] Figures)[]> GetRows = () => null;

		public override void Draw()
		{
			var rows = GetRows();
			if (rows == null)
				return;

			// The headings are plain text, drawn from the top; the rest is centred in its row.
			var font = Shell.Art.GetFont(Font);
			void Text(string text, int x, int y, int width, int height, TextAlign align = TextAlign.Center, bool top = false) =>
				DrShellLabelWidget.DrawAligned(Shell, font, text, new Rectangle(x, y, width, height), align, top: top);

			string[] groups = [Collected, Created, Lost, Destroyed];
			for (var i = 0; i < groups.Length; i++)
				Text(FluentProvider.GetMessage(groups[i]), CellX + 2 * i * CellStep, GroupY, GroupWidth, 0, top: true);

			var sideAlign = SideWidth > 0 ? TextAlign.Center : TextAlign.Left;
			if (PlayerX >= 0)
				Text(FluentProvider.GetMessage(Player), PlayerX, HeadingY, 0, 0, TextAlign.Left, true);

			Text(FluentProvider.GetMessage(Side), SideX, HeadingY, SideWidth, 0, sideAlign, true);
			string[] columns = [Water, Taelon, Units, Buildings, Units, Buildings, Units, Buildings];
			for (var i = 0; i < columns.Length; i++)
				Text(FluentProvider.GetMessage(columns[i]), CellX + i * CellStep, HeadingY, CellWidth, 0, top: true);

			var highest = Enumerable.Range(0, columns.Length).Select(c => rows.Length > 0 ? rows.Max(r => r.Figures[c]) : 0).ToArray();
			for (var r = 0; r < rows.Length; r++)
			{
				var y = RowY + r * RowStep;
				var (player, side, figures) = rows[r];
				if (PlayerX >= 0)
					Text(player, PlayerX, y, 0, RowHeight, TextAlign.Left);

				Text(side >= 0 && side < Sides.Length ? FluentProvider.GetMessage(Sides[side]) : "--", SideX, y, SideWidth, RowHeight, sideAlign);
				for (var c = 0; c < columns.Length; c++)
				{
					var x = CellX + c * CellStep;
					Text(figures[c].ToString(CultureInfo.InvariantCulture), x, y, CellWidth, RowHeight);
					if (figures[c] > 0 && figures[c] == highest[c])
						DrawFrame(new Rectangle(x, y, CellWidth, RowHeight));
				}
			}
		}

		/// <summary>A one-pixel frame just inside an area of the shell, as the original draws it.</summary>
		void DrawFrame(Rectangle area)
		{
			var tl = Shell.ToScreen(new float2(area.Left, area.Top));
			var br = Shell.ToScreen(new float2(area.Right, area.Bottom));
			var w = Shell.Scale.X;
			var h = Shell.Scale.Y;
			var r = Game.Renderer.RgbaColorRenderer;
			r.FillRect(new float3(tl.X, tl.Y, 0), new float3(br.X, tl.Y + h, 0), Frame);
			r.FillRect(new float3(tl.X, br.Y - h, 0), new float3(br.X, br.Y, 0), Frame);
			r.FillRect(new float3(tl.X, tl.Y, 0), new float3(tl.X + w, br.Y, 0), Frame);
			r.FillRect(new float3(br.X - w, tl.Y, 0), new float3(br.X, br.Y, 0), Frame);
		}
	}
}
