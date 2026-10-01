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

namespace OpenRA.Mods.Dr.FileFormats
{
	public enum AipBuildType { NumberToHave = 0, NumberToBuild = 1, RatioToBuild = 2, RatioToHave = 3 }

	public class AipAccountElement
	{
		public int Priority;
		public string Item;
		public AipBuildType BuildType;
		public int Amount;
	}

	public class AipAccount
	{
		public string Name;

		/// <summary>Share of income; -1 is UNLIMITED, first call on any money.</summary>
		public int Budget;

		/// <summary>Most the account will hold; -1 is unlimited.</summary>
		public int Cap = -1;
		public readonly List<AipAccountElement> Elements = new();
	}

	/// <summary>
	/// A Dark Reign AI personality (.aip): the strategic AI's priorities (Troop Allocation System) and
	/// its build accounts (Building and Unit Construction System). Spec: the AIP manual shipped with the game.
	/// </summary>
	public class AipFile
	{
		public readonly Dictionary<string, double> Values = new(StringComparer.OrdinalIgnoreCase);
		public readonly List<AipAccount> Accounts = new();
		public readonly Dictionary<string, double> ForceMatching = new(StringComparer.OrdinalIgnoreCase);

		public int Int(string name, int fallback) => Values.TryGetValue(name, out var v) ? (int)v : fallback;
		public double Double(string name, double fallback) => Values.TryGetValue(name, out var v) ? v : fallback;

		static readonly Dictionary<string, double> Constants = new(StringComparer.OrdinalIgnoreCase)
		{
			{ "UNLIMITED", -1 }, { "YES", 1 }, { "NO", 0 }, { "TACTAI_LOW", 0 }, { "TACTAI_HIGH", 2 },
			{ "NUMBER_TO_HAVE", 0 }, { "NUMBER_TO_BUILD", 1 }, { "RATIO_TO_BUILD", 2 }, { "RATIO_TO_HAVE", 3 },
			{ "DONT_CARE", 0 }, { "CENTER_OF_BASE", 1 }, { "PERIMETER", 2 }, { "OUTSIDE_OF_BASE", 3 },
			{ "NEAR_ENEMY_BASE", 4 }, { "NEAR_ENEMY_TROOPS", 5 },
		};

		static readonly Regex Assignment = new(@"^\s*(?:int|float|double)\s+(\w+)\s*=\s*([-\w.]+)", RegexOptions.Compiled);
		static readonly Regex TableHeader = new(@"^\s*(\w+)\s+(\w+)\s*\[", RegexOptions.Compiled);

		public AipFile(Stream s)
		{
			string text;
			using (var reader = new StreamReader(s, Encoding.Latin1))
				text = reader.ReadToEnd();

			text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
			var lines = text.Split('\n').Select(l => { var c = l.IndexOf("//", StringComparison.Ordinal); return c >= 0 ? l[..c] : l; }).ToArray();

			string tableType = null, tableName = null;
			var inData = false;
			var accountsByName = new Dictionary<string, AipAccount>(StringComparer.OrdinalIgnoreCase);

			foreach (var raw in lines)
			{
				var line = raw.Trim();
				if (line.Length == 0)
					continue;

				if (line.StartsWith("#DATA", StringComparison.OrdinalIgnoreCase))
				{
					inData = true;
					continue;
				}

				if (line.StartsWith("#END_DATA", StringComparison.OrdinalIgnoreCase))
				{
					inData = false;
					tableType = tableName = null;
					continue;
				}

				if (inData)
				{
					var fields = line.Split(new[] { ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
						.Select(f => f.Trim('"')).ToArray();
					if (fields.Length == 0)
						continue;

					switch (tableType?.ToUpperInvariant())
					{
						case "UNIT_CONSTRUCTION_PROGRAM":
						{
							var a = new AipAccount { Name = fields[0], Budget = (int)Number(fields, 1, 0), Cap = (int)Number(fields, 2, -1) };
							accountsByName[a.Name] = a;
							Accounts.Add(a);
							break;
						}

						case "ACCOUNT":
						case "ACCOUNT_ELEMENT":
						{
							if (fields.Length < 4)
								break;

							if (!accountsByName.TryGetValue(tableName, out var account))
							{
								account = new AipAccount { Name = tableName, Budget = 0 };
								accountsByName[tableName] = account;
								Accounts.Add(account);
							}

							account.Elements.Add(new AipAccountElement
							{
								Priority = (int)Number(fields, 0, 0),
								Item = fields[1],
								BuildType = (AipBuildType)(int)Number(fields, 2, 0),
								Amount = (int)Number(fields, 3, 1)
							});
							break;
						}

						case "FORCE_MATCHING":
							if (fields.Length >= 2)
								ForceMatching[fields[0]] = Number(fields, 1, 1);
							break;
					}

					continue;
				}

				var m = Assignment.Match(line);
				if (m.Success)
				{
					Values[m.Groups[1].Value] = Number(new[] { m.Groups[2].Value }, 0, 0);
					continue;
				}

				m = TableHeader.Match(line);
				if (m.Success)
				{
					tableType = m.Groups[1].Value;
					tableName = m.Groups[2].Value;
				}
			}
		}

		static double Number(string[] fields, int i, double fallback)
		{
			if (i >= fields.Length)
				return fallback;

			if (Constants.TryGetValue(fields[i], out var c))
				return c;

			return double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
		}
	}
}
