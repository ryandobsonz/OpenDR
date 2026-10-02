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
using System.Text;
using System.Text.RegularExpressions;
using OpenRA.FileSystem;

namespace OpenRA.Mods.Dr.FileFormats
{
	/// <summary>
	/// The cube's archive (dark/shell/archive.txt): menus of pages, a tree addressed by paths of two-digit
	/// indices. "[00M]" is the root menu, its lines its items; "[0001M]" is the menu of the root's second
	/// item, "[000100T]" the page of that menu's first, and so on. Pages carry the briefings' markup.
	/// </summary>
	public sealed class DrArchive
	{
		public sealed class Node
		{
			public string Title;

			/// <summary>A menu's items; null for a page.</summary>
			public List<Node> Items;

			/// <summary>A page's text, with its markup.</summary>
			public string Text;

			public Node Parent;
		}

		public readonly Node Root;

		public DrArchive(Stream stream)
		{
			var menus = new Dictionary<string, List<string>>();
			var pages = new Dictionary<string, string>();
			string path = null;
			var isMenu = false;
			var body = new StringBuilder();

			void Close()
			{
				if (path == null)
					return;

				if (isMenu)
				{
					var items = new List<string>();
					foreach (var line in body.ToString().Split('\n'))
						if (line.Trim().Length > 0)
							items.Add(line.Trim());

					menus[path] = items;
				}
				else
					pages[path] = body.ToString().Trim();

				body.Clear();
			}

			using (var reader = new StreamReader(stream, Encoding.Latin1))
			{
				string line;
				while ((line = reader.ReadLine()) != null)
				{
					var header = Regex.Match(line, @"^\[(\d+)([MT])\]");
					if (header.Success)
					{
						Close();
						path = header.Groups[1].Value;
						isMenu = header.Groups[2].Value == "M";
						continue;
					}

					body.Append(line).Append('\n');
				}
			}

			Close();
			Root = Build("00", "", null, menus, pages) ?? new Node { Title = "", Items = new List<Node>() };
		}

		static Node Build(string path, string title, Node parent, Dictionary<string, List<string>> menus, Dictionary<string, string> pages)
		{
			var node = new Node { Title = title, Parent = parent };
			if (menus.TryGetValue(path, out var items))
			{
				node.Items = new List<Node>();

				// The root is "00"; its items are "0000", "0001", and an item of "0001" is "000100".
				for (var i = 0; i < items.Count; i++)
				{
					var child = Build(path + i.ToString("D2", CultureInfo.InvariantCulture), items[i], node, menus, pages);
					if (child != null)
						node.Items.Add(child);
				}

				return node;
			}

			if (pages.TryGetValue(path, out var text))
			{
				node.Text = text;
				return node;
			}

			return null;
		}

		public static DrArchive Load(IReadOnlyFileSystem fileSystem)
		{
			if (!fileSystem.TryOpen("content|shell/ARCHIVE.TXT", out var stream))
				return null;

			using (stream)
			{
				try
				{
					return new DrArchive(stream);
				}
				catch (Exception e)
				{
					Log.Write("debug", $"Could not read the archive: {e.Message}");
					return null;
				}
			}
		}
	}
}
