using Godot;
using Godot.Collections;

public partial class PlayerManager : Node
{
	public static PlayerManager Instance;
	public Player Player { get; set; }
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
		// if (Player != null)
		// 	GD.Print("Is Player Ready: " + Player.IsReady);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void AddPlayer(int peerID)
	{
		Player newPlayer = new Player(peerID);
		ConnectedPlayers.Add(newPlayer.PeerID, newPlayer);

		var playerPacketList = new Array();
		foreach (var player in ConnectedPlayers.Values)
		{
			var tmpPlayer = new Dictionary
			{
				{nameof(Player.PeerID), player.PeerID},
			};

			playerPacketList.Add(tmpPlayer);
		}

		Rpc(nameof(AddPlayerClients), playerPacketList);

		if (peerID != Multiplayer.GetUniqueId())
		{
			RpcId(peerID, nameof(AssignPlayer), peerID);
		}
		else
		{
			Player = newPlayer;
			GD.Print($"Player {peerID} conencted");
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	private void AssignPlayer(int peerID)
	{
		Player = new Player(peerID);
		GD.Print($"Player {peerID} conencted");
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
			int peerID = (int)playerData[nameof(Player.PeerID)];

			Player player = new Player(peerID);
			ConnectedPlayers.Add(peerID, player);
		}

		Signals.Instance.EmitUpdateLobby();
	}
}
