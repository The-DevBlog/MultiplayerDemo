using System;
using Godot;

public partial class NetworkManager : Node
{
    public NetworkManager Instance;
    private int _port;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        _port = 25000;
    }

    public void Host()
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();

        Error error = peer.CreateServer(_port);
        if (error != Error.Ok)
        {
            GD.PushError($"Failed to host server");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
        int peerID = Multiplayer.GetUniqueId();

        Player player = new Player(1, peerID, 1, "Player 1 Username", new Color("#0000FF"));

        var playerManager = GetPlayerManager();
        playerManager.AddPlayer(player);
    }

    public void Join()
    {

    }

    public void Disconnect()
    {

    }

    private PlayerManager GetPlayerManager()
    {
        var playerManager = PlayerManager.Instance;

        if (playerManager != null)
        {
            return playerManager;
        }
        else
        {
            GD.PushError("GetPlayerManager() returned null");
        }

        return null;
    }
}
