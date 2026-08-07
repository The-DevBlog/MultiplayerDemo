using System.Collections.Generic;
using Godot;
using Godot.Collections;

public class MoveCmd
{
	public int PeerID { get; set; }
	public Array<int> UnitIDs { get; set; }
	public Vector2I Position { get; set; }
	public int Tick { get; set; }

	public MoveCmd(int peerID, Array<int> unitIDs, Vector2I position, int tick)
	{
		PeerID = peerID;

		var sortedUnitIDs = new List<int>(unitIDs.Count);
		foreach (int unitID in unitIDs)
			sortedUnitIDs.Add(unitID);
		sortedUnitIDs.Sort();

		UnitIDs = new Array<int>();
		foreach (int unitID in sortedUnitIDs)
			UnitIDs.Add(unitID);

		Position = position;
		Tick = tick;
	}

	public static void ProcessCommand(
		SortedDictionary<int, Unit> units,
		List<MoveCmd> moveCmds,
		NavGrid navGrid)
	{
		moveCmds.Sort(CompareDeterministically);

		foreach (var cmd in moveCmds)
		{
			var tmpUnits = new List<Unit>();
			foreach (var id in cmd.UnitIDs)
			{
				if (units.TryGetValue(id, out Unit unit))
					tmpUnits.Add(unit);
			}

			if (tmpUnits.Count == 0)
				continue;

			tmpUnits.Sort((l, r) => l.UnitID.CompareTo(r.UnitID));

			var startCells = new List<Vector2I>();

			foreach (Unit unit in tmpUnits)
				startCells.Add(navGrid.SimToCell(unit.SimPosition));

			List<Vector2I> destCells = navGrid.FindDestinationCells(cmd.Position, tmpUnits.Count);

			if (destCells.Count < tmpUnits.Count)
			{
				GD.PrintErr($"Only found {destCells.Count} destinations for {tmpUnits.Count} units");
				continue;
			}

			List<Vector2I> assignments = NavGrid.AssignDestinationCells(tmpUnits, destCells, navGrid);
			Rect2I destinationSectorRegion = navGrid.GetSectorRegion(destCells);

			int flowFieldID = navGrid.BuildField(
				startCells,
				cmd.Position,
				destinationSectorRegion
			);
			int moveGroupID = GetMoveGroupID(cmd, tmpUnits);

			for (int i = 0; i < tmpUnits.Count; i++)
				tmpUnits[i].FollowFlowField(
					flowFieldID,
					assignments[i],
					destinationSectorRegion,
					moveGroupID
				);
		}
	}

	private static int CompareDeterministically(MoveCmd left, MoveCmd right)
	{
		int comparison = left.PeerID.CompareTo(right.PeerID);
		if (comparison != 0)
			return comparison;

		comparison = left.Position.Y.CompareTo(right.Position.Y);
		if (comparison != 0)
			return comparison;

		comparison = left.Position.X.CompareTo(right.Position.X);
		if (comparison != 0)
			return comparison;

		comparison = left.UnitIDs.Count.CompareTo(right.UnitIDs.Count);
		if (comparison != 0)
			return comparison;

		for (int index = 0; index < left.UnitIDs.Count; index++)
		{
			comparison = left.UnitIDs[index].CompareTo(right.UnitIDs[index]);
			if (comparison != 0)
				return comparison;
		}

		return 0;
	}

	private static int GetMoveGroupID(MoveCmd command, List<Unit> units)
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + command.PeerID;
			hash = hash * 31 + command.Tick;
			hash = hash * 31 + command.Position.X;
			hash = hash * 31 + command.Position.Y;

			foreach (Unit unit in units)
				hash = hash * 31 + unit.UnitID;

			return hash & int.MaxValue;
		}
	}
}
