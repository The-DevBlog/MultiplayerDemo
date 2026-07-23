
using Godot;

public class Player
{
    public int PlayerID { get; set; }
    public int PeerID { get; set; }
    public int Team { get; set; }
    public string Name { get; set; }
    public Color Color { get; set; }

    public Player(int playerID, int peerID, int team, string name, Color color)
    {
        PlayerID = playerID;
        PeerID = peerID;
        Team = team;
        Name = name;
        Color = color;
    }
}
