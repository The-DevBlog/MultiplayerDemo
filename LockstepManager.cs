using System.Collections.Generic;
using Godot;
using Godot.Collections;

public partial class LockstepManager : Node
{
	private System.Collections.Generic.Dictionary<int, List<MoveCommand>> _moveCommands = new();
	private System.Collections.Generic.Dictionary<int, Unit> _units = new();
	private int _currentTick = 0;
	private int _tickRate = 30;
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
			int peerID = Multiplayer.GetUniqueId();
			RequestMoveServer(peerID, unitIDs, position);
		}
		else
		{
			int peerID = Multiplayer.GetRemoteSenderId();
			RpcId(1, nameof(RequestMoveServer), peerID, unitIDs, position);
		}
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

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	private void RequestMoveServer(int peerID, Array<int> unitIDs, Vector2I position)
	{
		int targetTick = _currentTick + _commandDelay;
		Rpc(nameof(ReceiveMoveCommand), peerID, unitIDs, position, targetTick);
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
		if (_moveCommands.ContainsKey(_currentTick))
		{
			List<MoveCommand> commands = _moveCommands[_currentTick];

			foreach (var cmd in commands)
			{
				foreach (var ID in cmd.UnitIDs)
				{
					if (_units.ContainsKey(ID))
					{
						Unit unit = _units[ID];
						Vector3 newPosition = new Vector3(cmd.Position.X, 0, cmd.Position.Y);

						unit.MoveTo(newPosition);
					}
				}
			}

			_moveCommands.Remove(_currentTick);
		}

		_currentTick++;
	}
}
