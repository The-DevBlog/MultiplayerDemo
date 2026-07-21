using Godot;
using System.Collections.Generic;

public partial class SimulationManager : Node
{
    [Export] public int TickRate { get; set; } = 30;
    [Export] public int InputDelayTicks { get; set; } = 6;
    [Export] public int LocalPlayerId { get; set; } = 0;

    private readonly Dictionary<int, Unit> _unitsById = new();
    private readonly SortedDictionary<long, List<MoveCommand>> _commandsByTick = new();
    private double _tickAccumulator;

    public long CurrentTick { get; private set; }
    private float TickDelta => 1.0f / TickRate;

    public long NextCommandApplyTick => CurrentTick + InputDelayTicks;

    public override void _Ready()
    {
        RefreshUnitCache();
    }

    public override void _PhysicsProcess(double delta)
    {
        _tickAccumulator += delta;

        while (_tickAccumulator >= TickDelta)
        {
            AdvanceTick();
            _tickAccumulator -= TickDelta;
        }
    }

    public bool SubmitMoveCommand(IReadOnlyList<Unit> units, Vector3 targetPosition)
    {
        int[] unitIds = GetUnitIds(units);
        if (unitIds.Length == 0)
        {
            return false;
        }

        LockstepNetwork lockstepNetwork = GetLockstepNetwork();
        if (lockstepNetwork != null && lockstepNetwork.IsStarted)
        {
            return lockstepNetwork.SubmitMoveCommand(unitIds, targetPosition);
        }

        ScheduleMoveCommand(MoveCommand.Create(NextCommandApplyTick, LocalPlayerId, unitIds, targetPosition));
        return true;
    }

    public void ScheduleMoveCommand(MoveCommand command)
    {
        ScheduleCommand(command);
    }

    public void SyncToTick(long tick)
    {
        CurrentTick = tick;
        _tickAccumulator = 0.0;
    }

    public int GetStateHash()
    {
        RefreshUnitCache();

        unchecked
        {
            int hash = 17;
            hash = hash * 31 + CurrentTick.GetHashCode();

            foreach (int unitId in GetSortedUnitIds())
            {
                Unit unit = _unitsById[unitId];
                Vector3 position = unit.GlobalPosition;
                hash = hash * 31 + unitId;
                hash = hash * 31 + MoveCommand.Quantize(position.X);
                hash = hash * 31 + MoveCommand.Quantize(position.Z);
            }

            return hash;
        }
    }

    private void ScheduleCommand(MoveCommand command)
    {
        if (!_commandsByTick.TryGetValue(command.ApplyTick, out List<MoveCommand> commands))
        {
            commands = new List<MoveCommand>();
            _commandsByTick[command.ApplyTick] = commands;
        }

        commands.Add(command);
    }

    private void AdvanceTick()
    {
        RefreshUnitCache();
        ApplyCommandsForTick(CurrentTick);
        SimulateUnits();
        CurrentTick++;
    }

    private void ApplyCommandsForTick(long tick)
    {
        if (!_commandsByTick.TryGetValue(tick, out List<MoveCommand> commands))
        {
            return;
        }

        foreach (MoveCommand command in commands)
        {
            ApplyMoveCommand(command);
        }

        _commandsByTick.Remove(tick);
    }

    private void ApplyMoveCommand(MoveCommand command)
    {
        Vector3 center = Vector3.Zero;
        int unitCount = 0;

        foreach (int unitId in command.UnitIds)
        {
            if (_unitsById.TryGetValue(unitId, out Unit unit))
            {
                center += unit.GlobalPosition;
                unitCount++;
            }
        }

        if (unitCount == 0)
        {
            return;
        }

        center /= unitCount;
        Vector3 targetPosition = command.TargetPosition;

        foreach (int unitId in command.UnitIds)
        {
            if (!_unitsById.TryGetValue(unitId, out Unit unit))
            {
                continue;
            }

            Vector3 offset = unit.GlobalPosition - center;
            offset.Y = 0.0f;
            unit.MoveTo(targetPosition + offset);
        }
    }

    private void SimulateUnits()
    {
        foreach (int unitId in GetSortedUnitIds())
        {
            _unitsById[unitId].SimulateTick(TickDelta);
        }
    }

    private void RefreshUnitCache()
    {
        _unitsById.Clear();

        foreach (Node node in GetTree().GetNodesInGroup(Unit.UnitsGroup))
        {
            if (node is not Unit unit || !IsInstanceValid(unit))
            {
                continue;
            }

            if (_unitsById.ContainsKey(unit.UnitId))
            {
                GD.PushWarning($"Duplicate UnitId {unit.UnitId}; only the first unit will be simulated.");
                continue;
            }

            _unitsById[unit.UnitId] = unit;
        }
    }

    private LockstepNetwork GetLockstepNetwork()
    {
        return GetParent()?.GetNodeOrNull<LockstepNetwork>("LockstepNetwork");
    }

    private static int[] GetUnitIds(IReadOnlyList<Unit> units)
    {
        List<int> unitIds = new();
        foreach (Unit unit in units)
        {
            if (IsInstanceValid(unit))
            {
                unitIds.Add(unit.UnitId);
            }
        }

        unitIds.Sort();
        return unitIds.ToArray();
    }

    private List<int> GetSortedUnitIds()
    {
        List<int> unitIds = new(_unitsById.Keys);
        unitIds.Sort();
        return unitIds;
    }
}
