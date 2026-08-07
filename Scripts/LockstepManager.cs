using System.Collections.Generic;
using Godot;
using Godot.Collections;

public partial class LockstepManager : Node
{
	private System.Collections.Generic.Dictionary<int, List<MoveCmd>> _moveCommands = new();
	private System.Collections.Generic.Dictionary<int, List<MoveCmd>> _pendingMoveCommands = new();
	private HashSet<int> _sealedTicks = new();
	private HashSet<int> _publishedTicks = new();
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
	private NavGrid _navGrid;
	private bool _simulationRunning;
	private LocalResources _localResources;

	public override void _Ready()
	{
		_networkManager = NetworkManager.Instance;
		_playerManager = PlayerManager.Instance;
		_localResources = GetTree().CurrentScene as LocalResources;
		_navGrid = GetTree().CurrentScene.GetNode<NavGrid>("%NavGrid");

		if (_localResources == null)
			GD.PrintErr("[LockstepManager.Ready()] Could not find LocalResources");

		_tickDuration = 1.0 / _tickRate;
		_desyncInterval = _tickRate * 3;

		// load units dictionary
		RefreshUnits();

		if (_navGrid.IsNavReady)
			CallDeferred(nameof(StartReadyHandshake));
		else
			_navGrid.NavigationReady += OnNavigationReady;
	}

	private void OnNavigationReady()
	{
		GD.Print("On Nav Ready Called");
		_navGrid.NavigationReady -= OnNavigationReady;
		CallDeferred(nameof(StartReadyHandshake));
	}

	private void StartReadyHandshake()
	{
		RpcId(1, nameof(NotifyReady), _playerManager.Player.PeerID);
	}

	public override void _Process(double delta)
	{
		if (!_simulationRunning)
			return;

		_timeAccumulator += delta;

		while (_timeAccumulator >= _tickDuration)
		{
			if (Multiplayer.IsServer() && !_publishedTicks.Contains(_currentTick))
				PublishTickFrame(_currentTick);

			if (!_sealedTicks.Remove(_currentTick))
				break;

			_publishedTicks.Remove(_currentTick);
			RunTick();
			_timeAccumulator -= _tickDuration;
		}

		float interpolationFraction = (float)(_timeAccumulator / _tickDuration);
		float visualTickFraction = (float)(delta / _tickDuration);
		foreach (Unit unit in _units.Values)
			unit.UpdateVisualPosition(interpolationFraction, visualTickFraction);
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
		QueueMoveRequest(peerID, unitIDs, position);
	}

	[Rpc(
		MultiplayerApi.RpcMode.AnyPeer,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestMoveClient(Array<int> unitIDs, Vector2I position)
	{
		int peerID = Multiplayer.GetRemoteSenderId();
		QueueMoveRequest(peerID, unitIDs, position);
	}

	private void QueueMoveRequest(int peerID, Array<int> unitIDs, Vector2I position)
	{
		int targetTick = _currentTick + _commandDelay;
		AddCommand(
			_pendingMoveCommands,
			new MoveCmd(peerID, unitIDs, position, targetTick)
		);
	}

	[Rpc(
		MultiplayerApi.RpcMode.AnyPeer,
		CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
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

	[Rpc(
		MultiplayerApi.RpcMode.Authority,
		CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void StartSimulation()
	{
		if (_simulationRunning)
			return;

		if (!_navGrid.IsNavReady)
		{
			GD.PushError("Cannot start lockstep before navigation data is ready.");
			return;
		}

		_localResources.CreateUnits();
		RefreshUnits();
		_simulationRunning = true;
	}

	private void RefreshUnits()
	{
		_units.Clear();

		foreach (Node node in GetTree().GetNodesInGroup("units"))
		{
			if (node is Unit unit)
			{
				_units[unit.UnitID] = unit;
			}
		}
	}

	[Rpc(
		MultiplayerApi.RpcMode.Authority,
		CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveMoveCommand(int peerID, Array<int> unitIDs, Vector2I position, int targetTick)
	{
		if (targetTick < _currentTick || _sealedTicks.Contains(targetTick))
		{
			StopForProtocolError(
				$"Move command for closed tick {targetTick}; current tick is {_currentTick}."
			);
			return;
		}

		AddCommand(
			_moveCommands,
			new MoveCmd(peerID, unitIDs, position, targetTick)
		);
	}

	public void SubmitMoveCommand(MoveCmd command)
	{
		if (!Multiplayer.IsServer())
		{
			StopForProtocolError("Only the server may submit lockstep commands.");
			return;
		}

		if (command.Tick < _currentTick)
		{
			StopForProtocolError(
				$"Cannot queue command for tick {command.Tick}; current tick is {_currentTick}."
			);
			return;
		}

		AddCommand(_pendingMoveCommands, command);
	}

	private static void AddCommand(
		System.Collections.Generic.Dictionary<int, List<MoveCmd>> commandsByTick,
		MoveCmd command)
	{
		if (!commandsByTick.TryGetValue(command.Tick, out List<MoveCmd> commands))
		{
			commands = new List<MoveCmd>();
			commandsByTick.Add(command.Tick, commands);
		}

		commands.Add(command);
	}

	private void PublishTickFrame(int tick)
	{
		_publishedTicks.Add(tick);

		if (_pendingMoveCommands.TryGetValue(tick, out List<MoveCmd> commands))
		{
			foreach (MoveCmd command in commands)
			{
				Error commandError = Rpc(
					nameof(ReceiveMoveCommand),
					command.PeerID,
					command.UnitIDs,
					command.Position,
					command.Tick
				);
				if (commandError != Error.Ok)
				{
					StopForProtocolError(
						$"Failed to publish a command for lockstep frame {tick}: {commandError}."
					);
					return;
				}
			}

			_pendingMoveCommands.Remove(tick);
		}

		Error error = Rpc(nameof(SealTickFrame), tick);
		if (error != Error.Ok)
			StopForProtocolError($"Failed to publish lockstep frame {tick}: {error}.");
	}

	[Rpc(
		MultiplayerApi.RpcMode.Authority,
		CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SealTickFrame(int tick)
	{
		if (tick < _currentTick || !_sealedTicks.Add(tick))
		{
			StopForProtocolError(
				$"Invalid or duplicate lockstep frame {tick}; current tick is {_currentTick}."
			);
		}
	}

	private void StopForProtocolError(string message)
	{
		_simulationRunning = false;
		GD.PushError(message);
	}

	private void RunTick()
	{
		// MOVE COMMANDS
		if (_moveCommands.ContainsKey(_currentTick))
		{
			List<MoveCmd> moveCmds = _moveCommands[_currentTick];
			MoveCmd.ProcessCommand(_units, moveCmds, _navGrid);

			_moveCommands.Remove(_currentTick);
		}

		RunUnitSimulation();

		DesyncCheck();

		_currentTick++;
	}

	private void RunUnitSimulation()
	{
		var units = new List<Unit>(_units.Values);
		int bucketSize = 1;

		foreach (Unit unit in units)
		{
			unit.PrepareSimTick(_tickRate);
			bucketSize = System.Math.Max(bucketSize, unit.AvoidanceRadiusSim);
		}

		var buckets = new System.Collections.Generic.Dictionary<Vector2I, List<Unit>>();
		foreach (Unit unit in units)
		{
			Vector2I bucketPosition = GetBucketPosition(unit.SimPosition, bucketSize);
			if (!buckets.TryGetValue(bucketPosition, out List<Unit> bucket))
			{
				bucket = new List<Unit>();
				buckets.Add(bucketPosition, bucket);
			}

			bucket.Add(unit);
		}

		foreach (Unit unit in units)
		{
			var neighbors = new List<Unit>();
			Vector2I centerBucket = GetBucketPosition(unit.SimPosition, bucketSize);

			for (int yOffset = -1; yOffset <= 1; yOffset++)
			{
				for (int xOffset = -1; xOffset <= 1; xOffset++)
				{
					Vector2I bucketPosition = centerBucket + new Vector2I(xOffset, yOffset);
					if (buckets.TryGetValue(bucketPosition, out List<Unit> bucket))
						neighbors.AddRange(bucket);
				}
			}

			neighbors.Sort((left, right) =>
			{
				int distanceComparison = unit.GetDistanceSquared(left).CompareTo(unit.GetDistanceSquared(right));
				return distanceComparison != 0
					? distanceComparison
					: left.UnitID.CompareTo(right.UnitID);
			});

			unit.PrepareAvoidanceVelocity(neighbors);
		}

		foreach (Unit unit in units)
			unit.ApplySimTick();
	}

	private static Vector2I GetBucketPosition(Vector2I position, int bucketSize)
	{
		return new Vector2I(
			DeterministicMath.FloorDivide(position.X, bucketSize),
			DeterministicMath.FloorDivide(position.Y, bucketSize)
		);
	}

	private void DesyncCheck()
	{
		if (_currentTick % _desyncInterval != 0)
			return;

		int hash = 17;

		unchecked
		{
			hash = hash * 31 + _currentTick;
			hash = hash * 31 + _tickRate;
			hash = hash * 31 + _navGrid.GetDeterministicStateHash();

			foreach (Unit unit in _units.Values)
				hash = hash * 31 + unit.GetDeterministicStateHash();
		}

		if (Multiplayer.IsServer())
			RecordHash(Multiplayer.GetUniqueId(), _currentTick, hash);
		else
			RpcId(1, nameof(ReportHash), _currentTick, hash);

	}

	[Rpc(
		MultiplayerApi.RpcMode.AnyPeer,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
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
		}
	}
}
