
using Godot;

public partial class Player : Node
{
    public int PlayerID { get; set; }
    public int PeerID { get; set; }
    public int Team { get; set; }
    public string PlayerName { get; set; }
    // public Color Color { get; set; }

    public Player(int playerID, int peerID, string name, int team)
    {
        PlayerID = playerID;
        PeerID = peerID;
        PlayerName = name;
        Team = team;
        // Color = color;
    }
}
