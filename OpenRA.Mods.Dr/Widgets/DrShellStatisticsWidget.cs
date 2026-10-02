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
	/// The debrief's statistics, laid out as the original's shell (dkreign.exe, 0x581d3d) lays them out over the
	/// debrief art's two rows of boxes: COLLECTED, CREATED, LOST and DESTROYED over their columns, SIDE and the
	/// columns' names below, then a row for the player's team and one for team 1, each its side's name and eight
	/// figures. In each column the larger figure is framed in red; a column of noughts has none.
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

		const int GroupY = 315;
		const int HeadingY = 335;
		const int HeadingHeight = 20;
		const int RowY = 356;
		const int RowStep = 26;
		const int RowHeight = 24;
		const int SideX = 52;
		const int SideWidth = 104;
		const int CellX = 157;
		const int CellStep = 53;
		const int CellWidth = 52;

		// The debrief art's colour 0xaa, which frames the larger figures.
		static readonly Color Frame = Color.FromArgb(215, 0, 8);

		public readonly string Font = "font12";

		public Func<(int Side, int[] Figures)[]> GetRows = () => null;

		public override void Draw()
		{
			var rows = GetRows();
			if (rows == null)
				return;

			var font = Shell.Art.GetFont(Font);
			void Text(string text, int x, int y, int width, int height) =>
				DrShellLabelWidget.DrawAligned(Shell, font, text, new Rectangle(x, y, width, height), TextAlign.Center);

			string[] groups = [Collected, Created, Lost, Destroyed];
			for (var i = 0; i < groups.Length; i++)
				Text(FluentProvider.GetMessage(groups[i]), CellX + 2 * i * CellStep, GroupY, 2 * CellWidth + 1, HeadingHeight);

			Text(FluentProvider.GetMessage(Side), SideX, HeadingY, SideWidth, HeadingHeight);
			string[] columns = [Water, Taelon, Units, Buildings, Units, Buildings, Units, Buildings];
			for (var i = 0; i < columns.Length; i++)
				Text(FluentProvider.GetMessage(columns[i]), CellX + i * CellStep, HeadingY, CellWidth, HeadingHeight);

			var highest = Enumerable.Range(0, columns.Length).Select(c => rows.Max(r => r.Figures[c])).ToArray();
			for (var r = 0; r < rows.Length; r++)
			{
				var y = RowY + r * RowStep;
				var side = rows[r].Side;
				Text(side >= 0 && side < Sides.Length ? FluentProvider.GetMessage(Sides[side]) : "--", SideX, y, SideWidth, RowHeight);

				for (var c = 0; c < columns.Length; c++)
				{
					var x = CellX + c * CellStep;
					var figure = rows[r].Figures[c];
					Text(figure.ToString(CultureInfo.InvariantCulture), x, y, CellWidth, RowHeight);
					if (figure > 0 && figure == highest[c])
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
