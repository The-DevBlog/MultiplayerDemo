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
	[ExportGroup("Avoidance")]
	[Export(PropertyHint.Range, "0.1,10.0,0.05")]
	public float AgentRadius
	{
		get => _agentRadius;
		set
		{
			_agentRadius = value;
			UpdateAvoidanceVisualization();
		}
	}
	[Export(PropertyHint.Range, "0.5,10.0,0.1")]
	public float AvoidanceRadius
	{
		get => _avoidanceRadius;
		set
		{
			_avoidanceRadius = value;
			UpdateAvoidanceVisualization();
		}
	}
	[Export(PropertyHint.Range, "0.01,1.0,0.01")] public float AvoidancePriority { get; set; } = 1.0f;
	[Export(PropertyHint.Range, "0.01,1.0,0.01")] public float IdleAvoidancePriority { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0.1,20.0,0.1")] public float AvoidanceTimeHorizon { get; set; } = 8.0f;
	[Export(PropertyHint.Range, "0.1,20.0,0.1")] public float GroupAvoidanceTimeHorizon { get; set; } = 2.0f;
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float GroupDirectionInfluence { get; set; } = 0.35f;
	public int FlowFieldID { get; set; } = -1;
	public Vector2I DestinationCell { get; private set; }
	private float _agentRadius = 0.65f;
	private float _avoidanceRadius = 2.5f;

	// Lockstep Simulation
	private const int SimScale = 1000; // converts floats to ints
	private const int MinSpeedPerTick = 50;
	private const int MaxSpeedPerTick = 800;
	private const int MaxAvoidanceNeighbors = 24;
	private const int FlowRecoverySearchRadius = 3;
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
	private Vector3 _previousWorldPosition;
	private Vector3 _currentWorldPosition;

	// navigation
	private Rect2I _destinationSectorRegion;
	private bool _isMoving;
	private int _moveGroupID = -1;
	private bool _isSettlingAtDestination;
	private Vector2I _currentWaypointSim;
	private bool _hasWaypoint;
	private bool _stopAfterWaypoint;
	private List<Vector2I> _finalPath = new();
	private int _finalPathIndex;
	private bool _hasFinalPath;

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
		_previousWorldPosition = GlobalPosition;
		_currentWorldPosition = GlobalPosition;
		UpdateAvoidanceVisualization();
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
		if (neighbors.Count == 0 || (_isMoving && _preferredSimVelocity == Vector2I.Zero))
			return;

		var neighborSnapshots = new List<AvoidanceAgentSnapshot>(Math.Min(neighbors.Count, MaxAvoidanceNeighbors));
		long neighborDistanceSquared = (long)AvoidanceRadiusSim * AvoidanceRadiusSim;

		foreach (Unit neighbor in neighbors)
		{
			if (neighbor == this ||
				(!_isMoving && !neighbor._isMoving) ||
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

		Vector2 preferredVelocity = GetGroupAlignedPreferredVelocity(neighbors);
		Vector2 avoidanceVelocity = Avoidance.Solve(
			GetAvoidanceSnapshot(),
			neighborSnapshots,
			preferredVelocity,
			_speedPerTick / (float)SimScale,
			AvoidanceTimeHorizon,
			GroupAvoidanceTimeHorizon
		);

		_nextSimVelocity = WorldVelocityToSim(avoidanceVelocity);
	}

	public void ApplySimTick()
	{
		Vector2I previousSimPosition = SimPosition;
		bool stoppedAtWaypoint = false;
		SimPosition += _nextSimVelocity;

		if (_isMoving && _hasWaypoint && HasReachedWaypoint(previousSimPosition))
		{
			SimPosition = _currentWaypointSim;
			_hasWaypoint = false;

			if (_stopAfterWaypoint)
			{
				_isMoving = false;
				stoppedAtWaypoint = true;
			}
		}

		_simVelocity = stoppedAtWaypoint
			? Vector2I.Zero
			: SimPosition - previousSimPosition;
		AdvanceWorldPositionSnapshots();
	}

	public void UpdateVisualPosition(float interpolationFraction, float visualTickFraction)
	{
		Vector3 targetWorldPosition = _previousWorldPosition.Lerp(
			_currentWorldPosition,
			Mathf.Clamp(interpolationFraction, 0.0f, 1.0f)
		);
		float maxVisualDistance =
			_speedPerTick / (float)SimScale *
			Mathf.Max(visualTickFraction, 0.0f);

		GlobalPosition = GlobalPosition.MoveToward(targetWorldPosition, maxVisualDistance);
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
		Rect2I destinationSectorRegion,
		int moveGroupID = -1)
	{
		if (flowFieldID < 0)
			return;

		FlowFieldID = flowFieldID;
		DestinationCell = destCell;
		_destinationSectorRegion = destinationSectorRegion;
		_isMoving = true;
		_moveGroupID = moveGroupID;
		_isSettlingAtDestination = false;
		_hasWaypoint = false;
		_stopAfterWaypoint = false;
		_finalPath.Clear();
		_finalPathIndex = 0;
		_hasFinalPath = false;
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
		if (!_isSettlingAtDestination &&
			_navGrid.IsCellInSectorRegion(currentCellPos, _destinationSectorRegion))
		{
			_isSettlingAtDestination = true;
			_finalPathIndex = 0;
			_hasFinalPath = _navGrid.TryFindPath(
				currentCellPos,
				DestinationCell,
				out _finalPath
			);
			_hasWaypoint = false;
			_stopAfterWaypoint = false;
		}

		if (_isSettlingAtDestination)
			return GetFinalDestinationVelocity();

		Vector2I direction = _navGrid.GetDirection(FlowFieldID, currentCellPos);
		if (direction == Vector2I.Zero)
		{
			if (_navGrid.TryGetNearestFlowCell(
				FlowFieldID,
				currentCellPos,
				FlowRecoverySearchRadius,
				out Vector2I recoveryCell))
			{
				_currentWaypointSim = WorldToSimPosition(_navGrid.CellToWorld(recoveryCell));
				_hasWaypoint = true;
				_stopAfterWaypoint = false;
				return MoveTowards(Vector2I.Zero, _currentWaypointSim - SimPosition, _speedPerTick);
			}

			_hasWaypoint = false;
			_stopAfterWaypoint = false;
			return Vector2I.Zero;
		}

		_hasWaypoint = false;
		_stopAfterWaypoint = false;
		return MoveTowards(Vector2I.Zero, direction * SimScale, _speedPerTick);
	}

	private Vector2I GetFinalDestinationVelocity()
	{
		if (!_hasFinalPath)
			return Vector2I.Zero;

		if (!_hasWaypoint)
		{
			Vector2I waypointCell = _finalPathIndex < _finalPath.Count
				? _finalPath[_finalPathIndex++]
				: DestinationCell;

			_currentWaypointSim = WorldToSimPosition(_navGrid.CellToWorld(waypointCell));
			_stopAfterWaypoint = _finalPathIndex >= _finalPath.Count;
			_hasWaypoint = true;
		}

		return MoveTowards(Vector2I.Zero, _currentWaypointSim - SimPosition, _speedPerTick);
	}

	private bool ShouldIgnoreGroupAvoidance(Unit neighbor)
	{
		return
			_moveGroupID != -1 &&
			_moveGroupID == neighbor._moveGroupID &&
			(_isSettlingAtDestination || neighbor._isSettlingAtDestination);
	}

	private Vector2 GetGroupAlignedPreferredVelocity(IReadOnlyList<Unit> neighbors)
	{
		Vector2 preferredVelocity = SimToFlatWorldPosition(_preferredSimVelocity);
		if (_moveGroupID == -1 || _isSettlingAtDestination || preferredVelocity == Vector2.Zero)
			return preferredVelocity;

		Vector2 groupDirection = preferredVelocity.Normalized();
		int groupMemberCount = 1;
		long neighborDistanceSquared = (long)AvoidanceRadiusSim * AvoidanceRadiusSim;

		foreach (Unit neighbor in neighbors)
		{
			if (neighbor == this ||
				neighbor._moveGroupID != _moveGroupID ||
				neighbor._isSettlingAtDestination ||
				neighbor._preferredSimVelocity == Vector2I.Zero ||
				GetDistanceSquared(neighbor) > neighborDistanceSquared)
			{
				continue;
			}

			groupDirection += SimToFlatWorldPosition(neighbor._preferredSimVelocity).Normalized();
			groupMemberCount++;
		}

		if (groupMemberCount == 1)
			return preferredVelocity;

		groupDirection /= groupMemberCount;
		if (groupDirection == Vector2.Zero)
			return preferredVelocity;

		Vector2 alignedDirection = preferredVelocity.Normalized().Lerp(
			groupDirection.Normalized(),
			GroupDirectionInfluence
		).Normalized();

		return alignedDirection * preferredVelocity.Length();
	}

	private AvoidanceAgentSnapshot GetAvoidanceSnapshot()
	{
		return new AvoidanceAgentSnapshot(
			UnitID,
			_moveGroupID,
			SimToFlatWorldPosition(SimPosition),
			SimToFlatWorldPosition(_simVelocity),
			AgentRadius,
			_isMoving ? AvoidancePriority : IdleAvoidancePriority,
			_isMoving
		);
	}

	private bool HasReachedWaypoint(Vector2I previousSimPosition)
	{
		int arrivalDistance = Math.Max(MinSpeedPerTick, Mathf.RoundToInt(AgentRadius * SimScale * 0.2f));
		long arrivalDistanceSquared = (long)arrivalDistance * arrivalDistance;
		Vector2I previousOffset = _currentWaypointSim - previousSimPosition;
		Vector2I currentOffset = _currentWaypointSim - SimPosition;

		if (LengthSquared(previousOffset) <= arrivalDistanceSquared ||
			LengthSquared(currentOffset) <= arrivalDistanceSquared)
		{
			return true;
		}

		Vector2I movement = SimPosition - previousSimPosition;
		long movementLengthSquared = LengthSquared(movement);
		if (movementLengthSquared == 0)
			return false;

		long projection =
			(long)previousOffset.X * movement.X +
			(long)previousOffset.Y * movement.Y;
		if (projection <= 0 || projection >= movementLengthSquared)
			return false;

		long crossMagnitude = Math.Abs(
			(long)previousOffset.X * movement.Y -
			(long)previousOffset.Y * movement.X
		);
		long maxCrossMagnitude = IntegerSqrt(arrivalDistanceSquared * movementLengthSquared);
		return crossMagnitude <= maxCrossMagnitude;
	}

	private void UpdateAvoidanceVisualization()
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

	private void AdvanceWorldPositionSnapshots()
	{
		Vector2 flatWorldPos = SimToFlatWorldPosition(SimPosition);
		float y = _navGrid.GetTerrainHeight(flatWorldPos);

		_previousWorldPosition = _currentWorldPosition;
		_currentWorldPosition = new Vector3(
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
			long middle = left + (right - left) / 2;

			if (middle <= value / middle)
			{
				result = middle;
				left = middle + 1;
			}
			else
			{
				right = middle - 1;
			}
		}

		return (int)Math.Min(result, int.MaxValue);
	}
}
