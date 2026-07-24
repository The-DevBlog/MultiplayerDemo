
using Godot;

public partial class Player : Node
{
    public int PeerID { get; set; }

    public Player(int peerID)
    {
        PeerID = peerID;
    }
}
