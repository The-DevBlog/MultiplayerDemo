using Godot;
using System.Collections.Generic;

public partial class PlayerManager : Node
{
    public static PlayerManager Instance;

    public Dictionary<int, Player> ConnectedPlayers { get; set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        ConnectedPlayers = new Dictionary<int, Player>();
    }

    public void AddPlayer(Player player)
    {
        ConnectedPlayers.Add(player.PeerID, player);
    }

    public void RemovePlayer(Player player)
    {
        ConnectedPlayers.Remove(player.PeerID);
    }
}
