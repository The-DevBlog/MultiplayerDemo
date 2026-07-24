using Godot;

public partial class NetworkManager : Node
{
    public static NetworkManager Instance;
    private int _port;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        _port = 25000;

        Multiplayer.ConnectedToServer += ConnectClient;
        Multiplayer.ConnectionFailed += ClientConnectedFailed;
    }

    public override void _ExitTree()
    {
        Multiplayer.ConnectedToServer -= ConnectClient;
        Multiplayer.ConnectionFailed -= ClientConnectedFailed;
    }

    public bool Host()
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();

        Error error = peer.CreateServer(_port);
        if (error != Error.Ok)
        {
            GD.PushError("Host failed to create server");
            return false;
        }

        Multiplayer.MultiplayerPeer = peer;
        int peerID = Multiplayer.GetUniqueId();

        var playerManager = GetPlayerManager();
        playerManager.AddPlayer(1, peerID, "Player 1 Host", 1);

        return true;
    }

    public void Join()
    {
        ENetMultiplayerPeer peer = new ENetMultiplayerPeer();

        Error error = peer.CreateClient("127.0.0.1", _port);
        if (error != Error.Ok)
        {
            GD.PushError($"Client failed to connect to server: {error}");
            return;
        }

        Multiplayer.MultiplayerPeer = peer;
    }

    public void Disconnect()
    {

    }

    private void ConnectClient()
    {
        int peerID = Multiplayer.GetUniqueId();

        var playerManager = GetPlayerManager();
        playerManager.RpcId(1, nameof(playerManager.AddPlayer), 2, peerID, "Player 2 Client", 2);

        GD.Print("Client successfully connected");
    }

    private void ClientConnectedFailed()
    {
        GD.PushError("Client failed to connect");
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
