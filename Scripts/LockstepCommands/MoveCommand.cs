using System.Collections.Generic;
using Godot;
using Godot.Collections;

public class MoveCommand
{
	public int PeerID { get; set; }
	public Array<int> UnitIDs { get; set; }
	public Vector2I Position { get; set; }
	public int Tick { get; set; }

	public MoveCommand(int peerID, Array<int> unitIDs, Vector2I position, int tick)
	{
		PeerID = peerID;
		UnitIDs = unitIDs;
		Position = position;
		Tick = tick;
	}

	public static void ProcessCommand(
		SortedDictionary<int, Unit> units,
		List<MoveCommand> moveCmds,
		NavGrid navGrid)
	{
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
				startCells.Add(navGrid.WorldToCell(unit.GetSimWorldPosition()));

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

	private static int GetMoveGroupID(MoveCommand command, List<Unit> units)
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
