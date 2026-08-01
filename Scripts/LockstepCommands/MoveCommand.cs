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

			int flowFieldID = navGrid.BuildField(startCells, cmd.Position);

			for (int i = 0; i < tmpUnits.Count; i++)
				tmpUnits[i].FollowFlowField(flowFieldID, assignments[i], cmd.Position);
		}
	}
}
