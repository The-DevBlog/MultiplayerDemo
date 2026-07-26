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
}
