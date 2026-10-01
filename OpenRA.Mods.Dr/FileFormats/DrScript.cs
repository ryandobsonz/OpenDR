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

namespace OpenRA.Mods.Dr.FileFormats
{
	/// <summary>
	/// A command in Dark Reign's scenario script syntax, shared by .scn, .fsm and .end files:
	/// <c>Name(arg arg) { Child() 101 102 }</c>, with ';' comments.
	/// A block holds child commands and bare numbers (unit, building or region ids).
	/// </summary>
	public class DrScriptNode
	{
		public readonly string Name;
		public readonly string[] Args;
		public readonly List<DrScriptNode> Children = new();
		public readonly List<int> Ids = new();

		public DrScriptNode(string name, string[] args)
		{
			Name = name;
			Args = args;
		}

		public bool Is(string name) => string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);

		public string Arg(int i) => i < Args.Length ? Args[i] : null;

		public int IntArg(int i, int fallback = 0)
		{
			return i < Args.Length && int.TryParse(Args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
		}

		public override string ToString() => $"{Name}({string.Join(" ", Args)})";
	}

	public static class DrScript
	{
		public static List<DrScriptNode> Parse(Stream s)
		{
			using (var reader = new StreamReader(s, Encoding.Latin1))
				return Parse(reader.ReadToEnd());
		}

		public static List<DrScriptNode> Parse(string text)
		{
			var tokens = Tokenize(text);
			var pos = 0;
			var root = new DrScriptNode("root", Array.Empty<string>());
			ParseBlock(tokens, ref pos, root, false);
			return root.Children;
		}

		static void ParseBlock(List<string> tokens, ref int pos, DrScriptNode parent, bool inBraces)
		{
			DrScriptNode last = null;
			while (pos < tokens.Count)
			{
				var t = tokens[pos++];
				if (t == "}")
				{
					if (inBraces)
						return;
					continue;
				}

				if (t == "{")
				{
					// A block belongs to the command before it; a stray block is flattened into the parent.
					ParseBlock(tokens, ref pos, last ?? parent, true);
					last = null;
					continue;
				}

				if (t.StartsWith('('))
					continue;

				if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
				{
					parent.Ids.Add(id);
					last = null;
					continue;
				}

				var args = Array.Empty<string>();
				if (pos < tokens.Count && tokens[pos].StartsWith('('))
				{
					var inner = tokens[pos++];
					args = SplitArgs(inner[1..^1]);
				}

				last = new DrScriptNode(t, args);
				parent.Children.Add(last);
			}
		}

		static string[] SplitArgs(string s)
		{
			var args = new List<string>();
			var sb = new StringBuilder();
			var quoted = false;
			foreach (var c in s)
			{
				if (c == '"')
				{
					quoted = !quoted;
					continue;
				}

				if (!quoted && (char.IsWhiteSpace(c) || c == ','))
				{
					if (sb.Length > 0)
						args.Add(sb.ToString());
					sb.Clear();
					continue;
				}

				sb.Append(c);
			}

			if (sb.Length > 0)
				args.Add(sb.ToString());

			return args.ToArray();
		}

		static List<string> Tokenize(string text)
		{
			var tokens = new List<string>();
			var i = 0;
			while (i < text.Length)
			{
				var c = text[i];
				if (c == ';')
				{
					while (i < text.Length && text[i] != '\n')
						i++;
					continue;
				}

				if (char.IsWhiteSpace(c))
				{
					i++;
					continue;
				}

				if (c == '{' || c == '}')
				{
					tokens.Add(c.ToString());
					i++;
					continue;
				}

				if (c == '(')
				{
					var start = i;
					var quoted = false;
					while (i < text.Length && (quoted || text[i] != ')'))
					{
						if (text[i] == '"')
							quoted = !quoted;

						// Comments inside an argument list end it at the line, as the original parser did.
						if (!quoted && text[i] == '\n')
							break;
						i++;
					}

					tokens.Add(text[start..i] + ")");
					if (i < text.Length && text[i] == ')')
						i++;
					continue;
				}

				var wordStart = i;
				while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != '{' && text[i] != '}' && text[i] != ';')
					i++;
				tokens.Add(text[wordStart..i]);
			}

			return tokens;
		}
	}
}
