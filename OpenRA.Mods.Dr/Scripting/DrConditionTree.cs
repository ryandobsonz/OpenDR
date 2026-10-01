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
using OpenRA.Mods.Dr.FileFormats;

namespace OpenRA.Mods.Dr.Scripting
{
	/// <summary>What a condition tree can ask of the running scenario. Times are in Dark Reign game cycles.</summary>
	public interface IDrScenarioContext
	{
		long Cycle { get; }
		bool Evaluate(DrCriterion criterion, int team);
		void Act(DrScriptNode action, int team);
		void Trace(string message);
	}

	/// <summary>
	/// A Dark Reign finite state machine: an AI tree (.fsm) or an end-condition tree (.end).
	/// States number from 1 in file order; in an end tree, a move to state 0 wins the game for the team.
	/// </summary>
	public class DrConditionTree
	{
		public readonly int Team;
		public readonly bool IsEndTree;
		public readonly string Source;

		/// <summary>End trees only: cycles allowed to win, 0 for unlimited. Bonus times add to it.</summary>
		public long TimeLimit;

		readonly List<DrState> states = new();
		int current = -1;
		long enteredAt;

		public bool Won { get; private set; }
		public bool Halted { get; private set; }
		public int CurrentState => current + 1;

		public DrConditionTree(int team, string source, DrScriptNode root)
		{
			Team = team;
			Source = source;
			IsEndTree = root.Is("DefineEndCondTree");
			if (IsEndTree)
				TimeLimit = root.IntArg(0);

			foreach (var s in root.Children.Where(c => c.Is("DefineCondState")))
				states.Add(new DrState(s));
		}

		/// <summary>The default end condition, used by a team with no .end file: destroy every non-allied unit and building.</summary>
		public static DrConditionTree KillAll(int team)
		{
			var root = DrScript.Parse("DefineEndCondTree(0) { DefineCondState() { DefineCondition(0 0 0 0 \"killall\") { CritKillAll() } } }")[0];
			return new DrConditionTree(team, "default", root);
		}

		public void Start(IDrScenarioContext ctx)
		{
			if (states.Count == 0)
			{
				Halted = true;
				return;
			}

			Enter(0, ctx);
		}

		public void Tick(IDrScenarioContext ctx)
		{
			if (Halted || Won || current < 0)
				return;

			if (IsEndTree && TimeLimit > 0 && ctx.Cycle > TimeLimit)
			{
				Halted = true;
				return;
			}

			// One transition per evaluation, so each state's entry actions run before the next check.
			foreach (var condition in states[current].Conditions)
			{
				if (!condition.Criterion.Evaluate(ctx, Team, enteredAt))
					continue;

				TimeLimit += condition.BonusTime;
				ctx.Trace($"team {Team} {Source}: state {CurrentState} -> {condition.NextState}");
				if (condition.NextState == 0)
				{
					if (IsEndTree)
						Won = true;
					else
						Halted = true;

					return;
				}

				if (condition.NextState > states.Count)
				{
					Halted = true;
					return;
				}

				Enter(condition.NextState - 1, ctx);
				return;
			}
		}

		void Enter(int state, IDrScenarioContext ctx)
		{
			current = state;
			enteredAt = ctx.Cycle;
			foreach (var c in states[state].Conditions)
				c.Criterion.Reset();

			foreach (var action in states[state].Actions)
				ctx.Act(action, Team);
		}
	}

	class DrState
	{
		public readonly List<DrConditionLink> Conditions = new();
		public readonly List<DrScriptNode> Actions = new();

		public DrState(DrScriptNode node)
		{
			foreach (var c in node.Children)
			{
				if (c.Is("DefineCondition"))
				{
					var criterion = c.Children.Count > 0 ? DrCriterion.Create(c.Children[0]) : new DrCriterion(c);
					Conditions.Add(new DrConditionLink
					{
						NextState = c.IntArg(0),
						BonusTime = c.IntArg(2),
						Criterion = criterion
					});
				}
				else
					Actions.Add(c);
			}
		}
	}

	class DrConditionLink
	{
		public int NextState;
		public int BonusTime;
		public DrCriterion Criterion;
	}

	/// <summary>
	/// A criterion. The simple ones ask the scenario; the logical and timed ones, and those that remember
	/// what they have seen, keep their own state here.
	/// </summary>
	public class DrCriterion
	{
		public readonly DrScriptNode Node;
		public readonly string Name;
		protected readonly List<DrCriterion> Children = new();

		public DrCriterion(DrScriptNode node)
		{
			Node = node;
			Name = node.Name.ToLowerInvariant();
			foreach (var c in node.Children)
				Children.Add(Create(c));
		}

		public static DrCriterion Create(DrScriptNode node)
		{
			return node.Name.ToLowerInvariant() switch
			{
				"critonce" => new OnceCriterion(node),
				"crittimer" => new TimerCriterion(node),
				"critholdregion" => new PeriodCriterion(node),
				"critharassregion" => new PeriodCriterion(node),
				"critmoveunitstoregion" => new VisitCriterion(node),
				_ => new DrCriterion(node),
			};
		}

		/// <summary>Called when the state holding this criterion is entered.</summary>
		public virtual void Reset()
		{
			foreach (var c in Children)
				c.Reset();
		}

		public virtual bool Evaluate(IDrScenarioContext ctx, int team, long stateEnteredAt)
		{
			switch (Name)
			{
				case "critand":
					foreach (var c in Children)
						if (!c.Evaluate(ctx, team, stateEnteredAt))
							return false;
					return Children.Count > 0;

				case "critor":
					foreach (var c in Children)
						if (c.Evaluate(ctx, team, stateEnteredAt))
							return true;
					return false;

				case "critnot":
					return Children.Count == 0 || !Children[0].Evaluate(ctx, team, stateEnteredAt);

				default:
					return ctx.Evaluate(this, team);
			}
		}
	}

	/// <summary>Undocumented: true the first time its inner criterion is, then never again, so a trigger fires once.</summary>
	class OnceCriterion : DrCriterion
	{
		bool fired;

		public OnceCriterion(DrScriptNode node)
			: base(node) { }

		public override bool Evaluate(IDrScenarioContext ctx, int team, long stateEnteredAt)
		{
			if (fired || Children.Count == 0 || !Children[0].Evaluate(ctx, team, stateEnteredAt))
				return false;

			fired = true;
			return true;
		}
	}

	/// <summary>Cycles since this state was entered.</summary>
	class TimerCriterion : DrCriterion
	{
		public TimerCriterion(DrScriptNode node)
			: base(node) { }

		public override bool Evaluate(IDrScenarioContext ctx, int team, long stateEnteredAt)
		{
			return ctx.Cycle - stateEnteredAt >= Node.IntArg(0);
		}
	}

	/// <summary>
	/// Hold or harass a region for a number of 32-cycle periods. Holding need not be continuous; harassing
	/// must be, unless flag 1 (accumulative) is set.
	/// </summary>
	class PeriodCriterion : DrCriterion
	{
		public const int Period = 32;
		long lastPeriod = -1;
		int periods;

		public PeriodCriterion(DrScriptNode node)
			: base(node) { }

		public int Periods => periods;

		public override bool Evaluate(IDrScenarioContext ctx, int team, long stateEnteredAt)
		{
			var period = ctx.Cycle / Period;
			if (period != lastPeriod)
			{
				lastPeriod = period;
				if (ctx.Evaluate(this, team))
					periods++;
				else if (Name == "critharassregion" && (Node.IntArg(2) & 1) == 0)
					periods = 0;
			}

			return periods >= Node.IntArg(1);
		}
	}

	/// <summary>At least N of the listed units have, at some point, been in the region.</summary>
	public class VisitCriterion : DrCriterion
	{
		public readonly HashSet<int> Visited = new();

		public VisitCriterion(DrScriptNode node)
			: base(node) { }

		public override bool Evaluate(IDrScenarioContext ctx, int team, long stateEnteredAt)
		{
			ctx.Evaluate(this, team);
			return Visited.Count >= Math.Max(1, Node.IntArg(1));
		}
	}
}
