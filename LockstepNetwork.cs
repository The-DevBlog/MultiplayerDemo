using Godot;
using System;
using System.Collections.Generic;

public partial class LockstepNetwork : Node
{
    [Export] public bool AutoStartLocalDebugNetwork { get; set; } = true;
    [Export] public string ServerAddress { get; set; } = "127.0.0.1";
    [Export] public int Port { get; set; } = 24500;
    [Export] public int MaxClients { get; set; } = 8;

    private ENetMultiplayerPeer _peer;
    private bool _isHost;
    private bool _started;

    public bool IsStarted => _started;
    public bool IsHost => _isHost;

    public override void _Ready()
    {
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;

        if (AutoStartLocalDebugNetwork)
        {
            StartHostOrClient();
        }
    }

    public override void _ExitTree()
    {
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
    }

    public bool SubmitMoveCommand(int[] unitIds, Vector3 targetPosition)
    {
        if (!_started || unitIds.Length == 0)
        {
            return false;
        }

        string unitIdsText = EncodeUnitIds(unitIds);
        int targetX = MoveCommand.Quantize(targetPosition.X);
        int targetZ = MoveCommand.Quantize(targetPosition.Z);

        if (_isHost)
        {
            ScheduleAndBroadcastMoveCommand(Multiplayer.GetUniqueId(), unitIdsText, targetX, targetZ);
            return true;
        }

        if (Multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
        {
            GD.PushWarning("Client is not connected yet; move command was not sent.");
            return false;
        }

        Error error = RpcId(1, nameof(RequestMoveCommand), unitIdsText, targetX, targetZ);
        if (error != Error.Ok)
        {
            GD.PushWarning($"Failed to send move command to host: {error}.");
            return false;
        }

        return true;
    }

    private void StartHostOrClient()
    {
        _peer = new ENetMultiplayerPeer();
        Error hostError = _peer.CreateServer(Port, MaxClients);
        if (hostError == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = _peer;
            _isHost = true;
            _started = true;
            SetSimulationPlayerId(Multiplayer.GetUniqueId());
            GD.Print($"Lockstep host started on port {Port}.");
            return;
        }

        _peer = new ENetMultiplayerPeer();
        Error clientError = _peer.CreateClient(ServerAddress, Port);
        if (clientError != Error.Ok)
        {
            _started = false;
            GD.PushWarning($"Failed to host ({hostError}) or connect ({clientError}); running without network sync.");
            return;
        }

        Multiplayer.MultiplayerPeer = _peer;
        _isHost = false;
        _started = true;
        GD.Print($"Lockstep client connecting to {ServerAddress}:{Port}.");
    }

    private void OnConnectedToServer()
    {
        SetSimulationPlayerId(Multiplayer.GetUniqueId());
        GD.Print($"Lockstep client connected as peer {Multiplayer.GetUniqueId()}.");
    }

    private void OnConnectionFailed()
    {
        _started = false;
        GD.PushWarning("Lockstep client failed to connect; move commands will stay local if submitted.");
    }

    private void OnServerDisconnected()
    {
        _started = false;
        GD.PushWarning("Lockstep server disconnected.");
    }

    private void OnPeerConnected(long peerId)
    {
        GD.Print($"Lockstep peer connected: {peerId}.");

        if (!_isHost)
        {
            return;
        }

        SimulationManager simulationManager = GetSimulationManager();
        if (simulationManager != null)
        {
            RpcId(peerId, nameof(ReceiveTickSync), simulationManager.CurrentTick);
        }
    }

    private void OnPeerDisconnected(long peerId)
    {
        GD.Print($"Lockstep peer disconnected: {peerId}.");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestMoveCommand(string unitIdsText, int targetX, int targetZ)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        ScheduleAndBroadcastMoveCommand(Multiplayer.GetRemoteSenderId(), unitIdsText, targetX, targetZ);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveScheduledMoveCommand(long applyTick, int playerId, string unitIdsText, int targetX, int targetZ)
    {
        SimulationManager simulationManager = GetSimulationManager();
        if (simulationManager == null)
        {
            GD.PushWarning("SimulationManager was not found; received move command was ignored.");
            return;
        }

        int[] unitIds = DecodeUnitIds(unitIdsText);
        if (unitIds.Length == 0)
        {
            return;
        }

        simulationManager.ScheduleMoveCommand(MoveCommand.CreateQuantized(applyTick, playerId, unitIds, targetX, targetZ));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveTickSync(long tick)
    {
        if (Multiplayer.IsServer())
        {
            return;
        }

        SimulationManager simulationManager = GetSimulationManager();
        if (simulationManager == null)
        {
            return;
        }

        simulationManager.SyncToTick(tick);
        SetSimulationPlayerId(Multiplayer.GetUniqueId());
        GD.Print($"Lockstep tick synchronized to {tick}.");
    }

    private void ScheduleAndBroadcastMoveCommand(int playerId, string unitIdsText, int targetX, int targetZ)
    {
        SimulationManager simulationManager = GetSimulationManager();
        if (simulationManager == null)
        {
            GD.PushWarning("SimulationManager was not found; move command was not scheduled.");
            return;
        }

        long applyTick = simulationManager.NextCommandApplyTick;
        ReceiveScheduledMoveCommand(applyTick, playerId, unitIdsText, targetX, targetZ);
        Rpc(nameof(ReceiveScheduledMoveCommand), applyTick, playerId, unitIdsText, targetX, targetZ);
    }

    private void SetSimulationPlayerId(int playerId)
    {
        SimulationManager simulationManager = GetSimulationManager();
        if (simulationManager != null)
        {
            simulationManager.LocalPlayerId = playerId;
        }
    }

    private SimulationManager GetSimulationManager()
    {
        Node currentScene = GetTree().CurrentScene;
        return currentScene?.GetNodeOrNull<SimulationManager>("SimulationManager");
    }

    private static string EncodeUnitIds(int[] unitIds)
    {
        return string.Join(",", unitIds);
    }

    private static int[] DecodeUnitIds(string unitIdsText)
    {
        if (string.IsNullOrWhiteSpace(unitIdsText))
        {
            return Array.Empty<int>();
        }

        string[] parts = unitIdsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<int> unitIds = new();
        foreach (string part in parts)
        {
            if (int.TryParse(part, out int unitId))
            {
                unitIds.Add(unitId);
            }
        }

        unitIds.Sort();
        return unitIds.ToArray();
    }
}
