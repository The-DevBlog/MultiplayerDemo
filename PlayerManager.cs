using Godot;
using Godot.Collections;

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

    public override void _Process(double delta)
    {
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void AddPlayer(int playerID, int peerID, string name, int team)
    {
        Player newPlayer = new Player(playerID, peerID, name, team);
        ConnectedPlayers.Add(newPlayer.PeerID, newPlayer);

        var playerPacketList = new Array();
        foreach (var player in ConnectedPlayers.Values)
        {
            var tmpPlayer = new Dictionary
            {
                {nameof(Player.PlayerID), player.PlayerID },
                {nameof(Player.PeerID), player.PeerID},
                {nameof(Player.PlayerName), player.PlayerName},
                {nameof(Player.Team), player.Team}
            };

            playerPacketList.Add(tmpPlayer);
        }

        Rpc(nameof(AddPlayerClients), playerPacketList);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void AddPlayerClients(Array playerList)
    {
        RefreshConnectedPlayers(playerList);
    }

    private void RefreshConnectedPlayers(Array playerList)
    {
        ConnectedPlayers.Clear();

        foreach (Dictionary playerData in playerList)
        {
            int playerID = (int)playerData[nameof(Player.PlayerID)];
            int peerID = (int)playerData[nameof(Player.PeerID)];
            string playerName = (string)playerData[nameof(Player.PlayerName)];
            int team = (int)playerData[nameof(Player.Team)];

            Player player = new Player(playerID, peerID, playerName, team);
            ConnectedPlayers.Add(peerID, player);
        }

        Signals.Instance.EmitUpdateLobby();
    }
}
