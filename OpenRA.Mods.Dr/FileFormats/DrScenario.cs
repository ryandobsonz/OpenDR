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
using System.IO;
using System.Linq;

namespace OpenRA.Mods.Dr.FileFormats
{
	public class DrTeam
	{
		public int Index;

		/// <summary>0 Freedom Guard, 1 Imperium, 2 civilian, 3 Togran; 4 and 5 are the expansion's sides.</summary>
		public int Side;
		public int Credits;

		/// <summary>This team's view of each team: 0 enemy, 1 neutral, 2 ally.</summary>
		public int[] Alliance = { 0, 0, 0, 0, 0, 0, 0, 0, 1 };
		public string EndFile;

		/// <summary>Where the camera opens, in pixels; 0,0 if unset.</summary>
		public int StartX, StartY;

		/// <summary>Easy, medium and hard.</summary>
		public string[] FsmFiles = Array.Empty<string>();
	}

	public class DrPlacement
	{
		public int Id;
		public string Type;
		public int X;
		public int Y;
		public int Team;
		public bool IsBuilding;
		public bool IsThing;

		/// <summary>Hitpoints percentage for AddDamagedBuildingAt, else 100.</summary>
		public int Health = 100;
	}

	/// <summary>A unit's standing patrol, set in the editor: walk the points, then loop or walk back.</summary>
	public class DrPatrol
	{
		public readonly List<(int X, int Y)> Points = new();
		public bool Loop = true;
	}

	public class DrRegion
	{
		public int Id;

		/// <summary>In pixels, 24 to a tile; a circle's bounding box for 'c' regions.</summary>
		public int X1, Y1, X2, Y2;
		public bool Circle;
	}

	public class DrSpecialForces
	{
		public int Id;
		public int Team;
		public List<int> UnitIds = new();
	}

	/// <summary>A Dark Reign scenario (.scn). Format: the AIP manual's "Scenario File Definitions".</summary>
	public class DrScenario
	{
		public const int PixelsPerTile = 24;

		public string Terrain = "BARREN";
		public int TechLevel;
		public readonly Dictionary<int, DrTeam> Teams = new();
		public readonly List<DrPlacement> Placements = new();
		public readonly Dictionary<int, DrRegion> Regions = new();
		public readonly Dictionary<int, DrSpecialForces> SpecialForces = new();
		public readonly Dictionary<int, DrPatrol> Patrols = new();

		public DrScenario(Stream s)
		{
			var currentTeam = 0;
			foreach (var node in DrScript.Parse(s))
			{
				switch (node.Name.ToLowerInvariant())
				{
					case "setdefaultterrain":
						Terrain = node.Arg(0)?.ToUpperInvariant() ?? Terrain;
						break;
					case "settechlevel":
						TechLevel = node.IntArg(0);
						break;
					case "setteam":
						Teams[node.IntArg(0)] = ParseTeam(node);
						break;
					case "setdefaultteam":
						currentTeam = node.IntArg(0);
						break;
					case "putunitat":
						Placements.Add(Place(node, currentTeam, false));
						break;
					case "addbuildingat":
						Placements.Add(Place(node, currentTeam, true));
						break;
					case "adddamagedbuildingat":
					{
						var p = Place(node, currentTeam, true);
						p.Health = Math.Clamp(node.IntArg(5, 100), 1, 100);
						Placements.Add(p);
						break;
					}

					case "addthingat":
					{
						var p = Place(node, currentTeam, false);
						p.IsThing = true;
						Placements.Add(p);
						break;
					}

					case "defineregion":
					{
						var r = new DrRegion { Id = node.IntArg(0), Circle = node.Arg(1) == "c" };
						if (r.Circle)
						{
							var radius = node.IntArg(4);
							r.X1 = node.IntArg(2) - radius;
							r.Y1 = node.IntArg(3) - radius;
							r.X2 = node.IntArg(2) + radius;
							r.Y2 = node.IntArg(3) + radius;
						}
						else
						{
							r.X1 = Math.Min(node.IntArg(2), node.IntArg(4));
							r.Y1 = Math.Min(node.IntArg(3), node.IntArg(5));
							r.X2 = Math.Max(node.IntArg(2), node.IntArg(4));
							r.Y2 = Math.Max(node.IntArg(3), node.IntArg(5));
						}

						Regions[r.Id] = r;
						break;
					}

					case "addwaypoint":
					{
						if (!Patrols.TryGetValue(node.IntArg(0), out var patrol))
							Patrols[node.IntArg(0)] = patrol = new DrPatrol();
						patrol.Points.Add((node.IntArg(2), node.IntArg(3)));
						break;
					}

					case "setsordertrail":
						if (Patrols.TryGetValue(node.IntArg(0), out var trail))
							trail.Loop = !node.Args.Any(a => a.Equals("Reverse", StringComparison.OrdinalIgnoreCase));
						break;

					case "definespecialforces":
					{
						// The manual's one-line form lists the units after the team; the files use a block.
						var sf = new DrSpecialForces { Id = node.IntArg(0), Team = node.IntArg(1) };
						sf.UnitIds.AddRange(node.Args.Skip(2).Select(a => int.TryParse(a, out var v) ? v : -1).Where(v => v >= 0));
						sf.UnitIds.AddRange(node.Ids);
						SpecialForces[sf.Id] = sf;
						break;
					}
				}
			}
		}

		static DrPlacement Place(DrScriptNode node, int team, bool building)
		{
			return new DrPlacement
			{
				Id = node.IntArg(0),
				Type = node.Arg(1),
				X = node.IntArg(2),
				Y = node.IntArg(3),
				Team = team,
				IsBuilding = building
			};
		}

		static DrTeam ParseTeam(DrScriptNode node)
		{
			var team = new DrTeam { Index = node.IntArg(0) };

			// Unspecified alliances default to enemies of everyone, allied with itself.
			team.Alliance[team.Index] = 2;
			foreach (var c in node.Children)
			{
				switch (c.Name.ToLowerInvariant())
				{
					case "setteamside":
						team.Side = c.IntArg(0);
						break;
					case "setcredit":
						team.Credits = c.IntArg(0);
						break;
					case "setstartlocation":
						team.StartX = c.IntArg(0);
						team.StartY = c.IntArg(1);
						break;
					case "setend":
						team.EndFile = c.Arg(0);
						break;
					case "setfsm":
						team.FsmFiles = c.Args.ToArray();
						break;
					case "setalliance":
						for (var i = 0; i < c.Args.Length && i < team.Alliance.Length; i++)
							team.Alliance[i] = c.IntArg(i);
						break;
				}
			}

			return team;
		}

		/// <summary>The cell a region's pixel rectangle starts in, with the map's one-cell border.</summary>
		public static CPos PixelToCell(int x, int y) => new(x / PixelsPerTile + 1, y / PixelsPerTile + 1);

		public static CPos TileToCell(int x, int y) => new(x + 1, y + 1);
	}
}
