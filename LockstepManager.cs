using System.Collections.Generic;
using Godot;
using Godot.Collections;

public partial class LockstepManager : Node
{
	private System.Collections.Generic.Dictionary<int, List<MoveCommand>> _moveCommands = new();
	private SortedDictionary<int, Unit> _units = new();
	private System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, int>> _hashes = new();
	private int _currentTick = 0;
	private int _tickRate = 30;
	private int _desyncInterval;
	private double _tickDuration;
	private double _timeAccumulator = 0;
	private int _commandDelay = 3;
	private NetworkManager _networkManager;
	private PlayerManager _playerManager;
	private bool _simulationRunning;

	public override void _Ready()
	{
		_networkManager = NetworkManager.Instance;
		_playerManager = PlayerManager.Instance;

		_tickDuration = 1.0 / _tickRate;
		_desyncInterval = _tickRate * 3;

		// load units dictionary
		foreach (Node node in GetTree().GetNodesInGroup("units"))
		{
			if (node is Unit unit)
				_units[unit.UnitID] = unit;
		}

		RpcId(1, nameof(NotifyReady), _playerManager.Player.PeerID);
	}

	public override void _Process(double delta)
	{
		if (!_simulationRunning)
			return;

		_timeAccumulator += delta;

		while (_timeAccumulator >= _tickDuration)
		{
			RunTick();
			_timeAccumulator -= _tickDuration;
		}
	}

	public void RequestMove(Array<int> unitIDs, Vector2I position)
	{
		if (Multiplayer.IsServer())
		{
			RequestMoveServer(unitIDs, position);
		}
		else
		{
			RpcId(1, nameof(RequestMoveClient), unitIDs, position);
		}
	}

	private void RequestMoveServer(Array<int> unitIDs, Vector2I position)
	{
		int peerID = Multiplayer.GetUniqueId();
		int targetTick = _currentTick + _commandDelay;
		Rpc(nameof(ReceiveMoveCommand), peerID, unitIDs, position, targetTick);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	private void RequestMoveClient(Array<int> unitIDs, Vector2I position)
	{
		int peerID = Multiplayer.GetRemoteSenderId();
		int targetTick = _currentTick + _commandDelay;
		Rpc(nameof(ReceiveMoveCommand), peerID, unitIDs, position, targetTick);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void NotifyReady(int peerID)
	{
		bool simulationReady = true;
		foreach (var player in _playerManager.ConnectedPlayers.Values)
		{
			if (player.PeerID == peerID)
			{
				player.IsReady = true;
				GD.Print($"Player {peerID} ready");
			}

			if (!player.IsReady)
				simulationReady = false;
		}

		if (simulationReady)
		{
			var err = Rpc(nameof(StartSimulation));
			if (err == Error.Ok)
				GD.Print("Simulation Started");
			else
				GD.Print("Simulation failed to start");
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void StartSimulation()
	{
		_simulationRunning = true;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void ReceiveMoveCommand(int peerID, Array<int> unitIDs, Vector2I position, int targetTick)
	{
		MoveCommand command = new MoveCommand(
			peerID,
			unitIDs,
			position,
			targetTick
		);

		SubmitMoveCommand(command);
	}

	public void SubmitMoveCommand(MoveCommand command)
	{
		if (!_moveCommands.ContainsKey(command.Tick))
		{
			_moveCommands[command.Tick] = new List<MoveCommand>();
		}

		_moveCommands[command.Tick].Add(command);
	}

	private void RunTick()
	{
		DesyncCheck();

		if (_moveCommands.ContainsKey(_currentTick))
		{
			List<MoveCommand> commands = _moveCommands[_currentTick];

			foreach (var cmd in commands)
			{
				foreach (var id in cmd.UnitIDs)
				{
					if (_units.TryGetValue(id, out Unit unit))
					{
						Vector3I newPosition = new Vector3I(cmd.Position.X, 0, cmd.Position.Y);
						unit.MoveTo(newPosition);
					}
				}
			}

			_moveCommands.Remove(_currentTick);
		}

		_currentTick++;
	}

	private void DesyncCheck()
	{
		if (_currentTick % _desyncInterval != 0)
			return;

		int hash = 17;

		unchecked
		{
			hash = hash * 31 + _currentTick;

			foreach (Unit unit in _units.Values)
			{
				int x = Mathf.RoundToInt(unit.Position.X);
				int y = Mathf.RoundToInt(unit.Position.Y);
				int z = Mathf.RoundToInt(unit.Position.Z);

				hash = hash * 31 + unit.UnitID;
				hash = hash * 31 + x;
				hash = hash * 31 + y;
				hash = hash * 31 + z;
			}
		}

		if (Multiplayer.IsServer())
			RecordHash(Multiplayer.GetUniqueId(), _currentTick, hash);
		else
			RpcId(1, nameof(ReportHash), _currentTick, hash);

	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	private void ReportHash(int tick, int hash)
	{
		int peerID = Multiplayer.GetRemoteSenderId();
		RecordHash(peerID, tick, hash);
	}

	private void RecordHash(int peerID, int tick, int hash)
	{
		// GD.Print($"Player {peerID,10} | Tick {tick,6} | Hash {hash,12}");
		if (!_hashes.ContainsKey(tick))
		{
			_hashes[tick] = new System.Collections.Generic.Dictionary<int, int>();
		}

		_hashes[tick][peerID] = hash;

		// all peers have reported tick/hash. Begin hash comparison
		var hashesForThisTick = _hashes[tick];
		if (hashesForThisTick.Count == _playerManager.ConnectedPlayers.Count)
		{
			bool isDesync = false;
			int serverHash = hashesForThisTick[Multiplayer.GetUniqueId()];

			foreach (var kv in hashesForThisTick)
			{
				if (kv.Value != serverHash)
					isDesync = true;
			}

			if (isDesync)
				GD.Print("DESYNC DETECTED");
			// GD.Print($"Desync Detected: {_isDesync}");
		}
	}
}
