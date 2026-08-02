using Godot;
using System;
using System.Collections.Generic;

public partial class Unit : Node3D
{
	private const string UnitsGroup = "units";

	[Export] public int UnitID { get; set; }
	[Export] public int PlayerID { get; set; }
	[Export] public int Speed { get; set; } = 10;
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";
	[ExportGroup("Navigation")]
	[Export(PropertyHint.Range, "0,40,1")] public int SlotSteeringRangeCells { get; set; } = 20;
	[ExportGroup("Avoidance")]
	[Export(PropertyHint.Range, "0.1,2.0,0.05")] public float AgentRadius { get; set; } = 0.65f;
	[Export(PropertyHint.Range, "0.5,10.0,0.1")] public float AvoidanceRadius { get; set; } = 2.5f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")] public float AvoidancePriority { get; set; } = 1.0f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")] public float IdleAvoidancePriority { get; set; } = 0.1f;
	public int FlowFieldID { get; set; } = -1;
	public Vector2I DestinationCell { get; private set; }

	// Lockstep Simulation
	private const int SimScale = 1000; // converts floats to ints
	private const int MinSpeedPerTick = 50;
	private const int MaxSpeedPerTick = 800;
	private const int MaxAvoidanceNeighbors = 24;
	private const float AvoidanceTimeHorizon = 8.0f;
	private const float GroupAvoidanceTimeHorizon = 5.0f;
	private int _speedPerTick
	{
		get
		{
			return MinSpeedPerTick + (Speed - 1) * (MaxSpeedPerTick - MinSpeedPerTick) / 19;
		}
	}
	public Vector2I SimPosition { get; private set; }
	internal int AvoidanceRadiusSim => Mathf.RoundToInt(AvoidanceRadius * SimScale);

	private NavGrid _navGrid;
	private MeshInstance3D _mesh;
	private MeshInstance3D _avoidanceMesh;
	private Material _defaultMaterialOverride;
	private StandardMaterial3D _selectedMaterial;
	private bool _showAvoidanceVisualization;
	private Vector2I _simVelocity;
	private Vector2I _preferredSimVelocity;
	private Vector2I _nextSimVelocity;

	// navigation
	private Vector2I _flowTargetCell;
	private bool _isMoving;
	private int _moveGroupID = -1;
	private bool _isSettlingAtDestination;
	private Vector2I _currentWaypointSim;
	private bool _hasWaypoint;
	private bool _stopAfterWaypoint;

	public bool IsSelected => _isSelected;
	private bool _isSelected;

	public override void _Ready()
	{
		AddToGroup(UnitsGroup);

		_navGrid = GetTree().CurrentScene.GetNode<NavGrid>("%NavGrid");
		if (_navGrid == null)
			GD.PrintErr("[Unit.cs.Ready()] Could not find NavGrid");

		_mesh = GetNodeOrNull<MeshInstance3D>(MeshPath);
		_avoidanceMesh = GetNodeOrNull<MeshInstance3D>("AvoidanceVisualization");

		_defaultMaterialOverride = _mesh?.MaterialOverride;
		_selectedMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(1.0f, 0.85f, 0.2f),
			EmissionEnabled = true,
			Emission = new Color(1.0f, 0.65f, 0.05f),
		};

		SimPosition = WorldToSimPosition(GlobalPosition);
		ConfigureAvoidanceVisualization();
	}

	public void SimTick()
	{
		PrepareSimTick();
		PrepareAvoidanceVelocity(Array.Empty<Unit>());
		ApplySimTick();
	}

	public void PrepareSimTick()
	{
		_preferredSimVelocity = GetPreferredVelocity();
		_nextSimVelocity = _preferredSimVelocity;
	}

	public void PrepareAvoidanceVelocity(IReadOnlyList<Unit> neighbors)
	{
		if (neighbors.Count == 0)
			return;

		var neighborSnapshots = new List<OrcaAgentSnapshot>(Math.Min(neighbors.Count, MaxAvoidanceNeighbors));
		long neighborDistanceSquared = (long)AvoidanceRadiusSim * AvoidanceRadiusSim;

		foreach (Unit neighbor in neighbors)
		{
			if (neighbor == this ||
				ShouldIgnoreGroupAvoidance(neighbor) ||
				GetDistanceSquared(neighbor) > neighborDistanceSquared)
			{
				continue;
			}

			neighborSnapshots.Add(neighbor.GetAvoidanceSnapshot());
			if (neighborSnapshots.Count == MaxAvoidanceNeighbors)
				break;
		}

		if (neighborSnapshots.Count == 0)
			return;

		Vector2 avoidanceVelocity = OrcaAvoidanceSolver.Solve(
			GetAvoidanceSnapshot(),
			neighborSnapshots,
			SimToFlatWorldPosition(_preferredSimVelocity),
			_speedPerTick / (float)SimScale,
			AvoidanceTimeHorizon,
			GroupAvoidanceTimeHorizon
		);

		_nextSimVelocity = WorldVelocityToSim(avoidanceVelocity);
	}

	public void ApplySimTick()
	{
		Vector2I previousSimPosition = SimPosition;
		SimPosition += _nextSimVelocity;

		if (_isMoving && _hasWaypoint && HasReachedWaypoint())
		{
			SimPosition = _currentWaypointSim;
			_hasWaypoint = false;

			if (_stopAfterWaypoint)
			{
				_isMoving = false;
			}
		}

		_simVelocity = SimPosition - previousSimPosition;
		if (SimPosition != previousSimPosition)
			ApplySimPositionToWorld();
	}

	public void SetAvoidanceVisualization(bool visible)
	{
		_showAvoidanceVisualization = visible;

		if (_avoidanceMesh != null)
			_avoidanceMesh.Visible = visible;
	}

	public void SetSelected(bool selected)
	{
		_isSelected = selected;

		if (_mesh == null)
			return;

		_mesh.MaterialOverride = selected ? _selectedMaterial : _defaultMaterialOverride;
	}

	public void FollowFlowField(
		int flowFieldID,
		Vector2I destCell,
		Vector2I flowTargetCell,
		int moveGroupID = -1)
	{
		if (flowFieldID < 0)
			return;

		FlowFieldID = flowFieldID;
		DestinationCell = destCell;
		_flowTargetCell = flowTargetCell;
		_isMoving = true;
		_moveGroupID = moveGroupID;
		_isSettlingAtDestination = false;
		_hasWaypoint = false;
		_stopAfterWaypoint = false;
	}

	public Vector3 GetSimWorldPosition()
	{
		Vector2 flatWorldPos = SimToFlatWorldPosition(SimPosition);
		return new Vector3(flatWorldPos.X, 0.0f, flatWorldPos.Y);
	}

	internal long GetDistanceSquared(Unit other)
	{
		return LengthSquared(other.SimPosition - SimPosition);
	}

	private Vector2I GetPreferredVelocity()
	{
		if (!_isMoving || _navGrid == null || FlowFieldID < 0)
			return Vector2I.Zero;

		Vector2I currentCellPos = _navGrid.WorldToCell(GetSimWorldPosition());

		if (ShouldSteerToDestination(currentCellPos))
		{
			_isSettlingAtDestination = true;
			_currentWaypointSim = WorldToSimPosition(_navGrid.CellToWorld(DestinationCell));
			_stopAfterWaypoint = true;
			_hasWaypoint = true;
			return MoveTowards(Vector2I.Zero, _currentWaypointSim - SimPosition, _speedPerTick);
		}

		Vector2I direction = _navGrid.GetDirection(FlowFieldID, currentCellPos);
		if (direction == Vector2I.Zero)
		{
			_hasWaypoint = false;
			_stopAfterWaypoint = false;
			return Vector2I.Zero;
		}

		_hasWaypoint = false;
		_stopAfterWaypoint = false;
		return GetBlendedFlowVelocity(currentCellPos, direction);
	}

	private Vector2I GetBlendedFlowVelocity(Vector2I currentCell, Vector2I flowDirection)
	{
		Vector2I flowHeading = MoveTowards(Vector2I.Zero, flowDirection * SimScale, SimScale);
		if (SlotSteeringRangeCells <= 0)
			return MoveTowards(Vector2I.Zero, flowHeading, _speedPerTick);

		int directSteeringRadius = GetDirectSteeringRadius();
		int distanceToFlowTarget = GetCellDistance(currentCell, _flowTargetCell);
		int slotWeight = directSteeringRadius + SlotSteeringRangeCells - distanceToFlowTarget;

		if (slotWeight <= 0)
			return MoveTowards(Vector2I.Zero, flowHeading, _speedPerTick);

		_isSettlingAtDestination = true;
		slotWeight = Math.Min(slotWeight, SlotSteeringRangeCells);
		int flowWeight = SlotSteeringRangeCells - slotWeight;
		Vector2I destinationSim = WorldToSimPosition(_navGrid.CellToWorld(DestinationCell));
		Vector2I slotHeading = MoveTowards(Vector2I.Zero, destinationSim - SimPosition, SimScale);
		Vector2I blendedHeading = flowHeading * flowWeight + slotHeading * slotWeight;

		return MoveTowards(Vector2I.Zero, blendedHeading, _speedPerTick);
	}

	private bool ShouldIgnoreGroupAvoidance(Unit neighbor)
	{
		return
			_moveGroupID != -1 &&
			_moveGroupID == neighbor._moveGroupID &&
			(_isSettlingAtDestination || neighbor._isSettlingAtDestination);
	}

	private OrcaAgentSnapshot GetAvoidanceSnapshot()
	{
		return new OrcaAgentSnapshot(
			UnitID,
			_moveGroupID,
			SimToFlatWorldPosition(SimPosition),
			SimToFlatWorldPosition(_simVelocity),
			AgentRadius,
			_isMoving ? AvoidancePriority : IdleAvoidancePriority
		);
	}

	private bool HasReachedWaypoint()
	{
		int arrivalDistance = Math.Max(MinSpeedPerTick, Mathf.RoundToInt(AgentRadius * SimScale * 0.2f));
		return LengthSquared(_currentWaypointSim - SimPosition) <= (long)arrivalDistance * arrivalDistance;
	}

	private void ConfigureAvoidanceVisualization()
	{
		if (_avoidanceMesh == null)
			return;

		_avoidanceMesh.Visible = _showAvoidanceVisualization;

		if (_avoidanceMesh.Mesh is not QuadMesh quadMesh)
			return;

		quadMesh.Size = Vector2.One * AvoidanceRadius * 2.0f;

		if (quadMesh.Material is ShaderMaterial shaderMaterial)
		{
			shaderMaterial.SetShaderParameter("AgentRadius", AgentRadius);
			shaderMaterial.SetShaderParameter("AvoidanceRadius", AvoidanceRadius);
		}
	}

	private bool ShouldSteerToDestination(Vector2I currentCell)
	{
		return GetCellDistance(currentCell, _flowTargetCell) <= GetDirectSteeringRadius();
	}

	private int GetDirectSteeringRadius()
	{
		return GetCellDistance(_flowTargetCell, DestinationCell) + 1;
	}

	private static int GetCellDistance(Vector2I first, Vector2I second)
	{
		int xDist = Math.Abs(first.X - second.X);
		int yDist = Math.Abs(first.Y - second.Y);

		return Math.Max(xDist, yDist);
	}

	private static Vector2I MoveTowards(Vector2I current, Vector2I target, int maxDistance)
	{
		Vector2I toTarget = target - current;
		long distanceSquared = LengthSquared(toTarget);

		if (distanceSquared <= (long)maxDistance * maxDistance)
			return target;

		int distance = IntegerSqrt(distanceSquared);
		if (distance == 0)
			return target;

		return current + new Vector2I(
			toTarget.X * maxDistance / distance,
			toTarget.Y * maxDistance / distance
		);
	}

	private static Vector2I WorldToSimPosition(Vector3 worldPos)
	{
		return new Vector2I(
			Mathf.RoundToInt(worldPos.X * SimScale),
			Mathf.RoundToInt(worldPos.Z * SimScale)
		);
	}

	private static Vector2 SimToFlatWorldPosition(Vector2I simPos)
	{
		return new Vector2(
			simPos.X / (float)SimScale,
			simPos.Y / (float)SimScale
		);
	}

	private static Vector2I WorldVelocityToSim(Vector2 velocity)
	{
		return new Vector2I(
			Mathf.RoundToInt(velocity.X * SimScale),
			Mathf.RoundToInt(velocity.Y * SimScale)
		);
	}

	private void ApplySimPositionToWorld()
	{
		Vector2 flatWorldPos = SimToFlatWorldPosition(SimPosition);
		float y = _navGrid.GetTerrainHeight(flatWorldPos);

		GlobalPosition = new Vector3(
			flatWorldPos.X,
			y,
			flatWorldPos.Y
		);
	}

	private static long LengthSquared(Vector2I value)
	{
		return (long)value.X * value.X + (long)value.Y * value.Y;
	}

	private static int IntegerSqrt(long value)
	{
		if (value <= 0)
			return 0;

		long left = 1;
		long right = value;
		long result = 0;

		while (left <= right)
		{
			long middle = (left + right) / 2;
			long square = middle * middle;

			if (square <= value)
			{
				result = middle;
				left = middle + 1;
			}
			else
			{
				right = middle - 1;
			}
		}

		return (int)result;
	}
}
