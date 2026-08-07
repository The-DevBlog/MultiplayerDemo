using Godot;
using System;
using System.Collections.Generic;
using static DeterministicMath;

public partial class Unit : Node3D
{
	private const string UnitsGroup = "units";

	[Export] public int UnitID { get; set; }
	[Export] public int PlayerID { get; set; }
	[Export] public Vector2I InitialSimPosition { get; set; }
	[Export] public int Speed { get; set; } = 10;
	[Export(PropertyHint.Range, "0,720,1")] public int RotationSpeed { get; set; } = 180;
	[Export(PropertyHint.Range, "0,180,1")] public int MovementFacingTolerance { get; set; } = 15;
	[Export(PropertyHint.Range, "-180,180,1")] public int InitialFacingDegrees { get; set; }
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";
	[ExportGroup("Avoidance")]
	[Export(PropertyHint.Range, "10,1000,5")]
	public int AgentRadius
	{
		get => _agentRadius;
		set
		{
			_agentRadius = value;
			UpdateAvoidanceVisualization();
		}
	}
	[Export(PropertyHint.Range, "50,1000,10")]
	public int AvoidanceRadius
	{
		get => _avoidanceRadius;
		set
		{
			_avoidanceRadius = value;
			UpdateAvoidanceVisualization();
		}
	}
	[Export(PropertyHint.Range, "1,100,1")] public int AvoidancePriority { get; set; } = 100;
	[Export(PropertyHint.Range, "1,100,1")] public int IdleAvoidancePriority { get; set; } = 10;
	[Export(PropertyHint.Range, "1,20,1")] public int AvoidanceTimeHorizon { get; set; } = 8;
	[Export(PropertyHint.Range, "1,20,1")] public int GroupAvoidanceTimeHorizon { get; set; } = 2;
	[Export(PropertyHint.Range, "0,100,1")] public int GroupDirectionInfluencePercent { get; set; } = 35;
	public int FlowFieldID { get; set; } = -1;
	public Vector2I DestinationCell { get; private set; }
	private int _agentRadius = 65;
	private int _avoidanceRadius = 250;

	// Lockstep Simulation
	private const int SimScale = DeterministicMath.SimScale;
	private const int RotationScale = DeterministicMath.RotationScale;
	private const int DefaultSimulationTickRate = 30;
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
	public int SimRotation { get; private set; }
	internal int AgentRadiusSim => AgentRadius * SimScale / 100;
	internal int AvoidanceRadiusSim => AvoidanceRadius * SimScale / 100;

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
	private int _previousSimRotation;
	private int _simulationTickRate = DefaultSimulationTickRate;

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

		SimPosition = InitialSimPosition;
		SimRotation = DeterministicMath.NormalizeRotation(InitialFacingDegrees * RotationScale);
		_previousSimRotation = SimRotation;
		_previousWorldPosition = GlobalPosition;
		_currentWorldPosition = GlobalPosition;
		UpdateAvoidanceVisualization();
	}

	public void SetInitialSimPosition(Vector2I simPosition)
	{
		InitialSimPosition = simPosition;
		Position = new Vector3(
			simPosition.X / (float)SimScale,
			Position.Y,
			simPosition.Y / (float)SimScale
		);
	}

	public void SimTick()
	{
		PrepareSimTick();
		PrepareAvoidanceVelocity(Array.Empty<Unit>());
		ApplySimTick();
	}

	public void PrepareSimTick(int simulationTickRate = DefaultSimulationTickRate)
	{
		_simulationTickRate = Math.Max(simulationTickRate, 1);
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

		Vector2I preferredVelocity = GetGroupAlignedPreferredVelocity(neighbors);
		_nextSimVelocity = Avoidance.Solve(
			GetAvoidanceSnapshot(),
			neighborSnapshots,
			preferredVelocity,
			_speedPerTick,
			AvoidanceTimeHorizon,
			GroupAvoidanceTimeHorizon
		);
	}

	public void ApplySimTick()
	{
		Vector2I previousSimPosition = SimPosition;
		bool stoppedAtWaypoint = false;
		_previousSimRotation = SimRotation;
		_nextSimVelocity = GetFacingAdjustedVelocity(_nextSimVelocity);
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

		Vector3 visualRotation = GlobalRotation;
		visualRotation.Y = Mathf.LerpAngle(
			SimRotationToRadians(_previousSimRotation),
			SimRotationToRadians(SimRotation),
			Mathf.Clamp(interpolationFraction, 0.0f, 1.0f)
		);
		GlobalRotation = visualRotation;
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

	internal long GetDistanceSquared(Unit other)
	{
		return LengthSquared(other.SimPosition - SimPosition);
	}

	internal int GetDeterministicStateHash()
	{
		unchecked
		{
			int hash = 17;
			hash = CombineHash(hash, UnitID);
			hash = CombineHash(hash, PlayerID);
			hash = CombineHash(hash, InitialSimPosition.X);
			hash = CombineHash(hash, InitialSimPosition.Y);
			hash = CombineHash(hash, Speed);
			hash = CombineHash(hash, RotationSpeed);
			hash = CombineHash(hash, MovementFacingTolerance);
			hash = CombineHash(hash, InitialFacingDegrees);
			hash = CombineHash(hash, AgentRadius);
			hash = CombineHash(hash, AvoidanceRadius);
			hash = CombineHash(hash, AvoidancePriority);
			hash = CombineHash(hash, IdleAvoidancePriority);
			hash = CombineHash(hash, AvoidanceTimeHorizon);
			hash = CombineHash(hash, GroupAvoidanceTimeHorizon);
			hash = CombineHash(hash, GroupDirectionInfluencePercent);
			hash = CombineHash(hash, SimPosition.X);
			hash = CombineHash(hash, SimPosition.Y);
			hash = CombineHash(hash, SimRotation);
			hash = CombineHash(hash, _simVelocity.X);
			hash = CombineHash(hash, _simVelocity.Y);
			hash = CombineHash(hash, _preferredSimVelocity.X);
			hash = CombineHash(hash, _preferredSimVelocity.Y);
			hash = CombineHash(hash, _nextSimVelocity.X);
			hash = CombineHash(hash, _nextSimVelocity.Y);
			hash = CombineHash(hash, _simulationTickRate);
			hash = CombineHash(hash, FlowFieldID);
			hash = CombineHash(hash, DestinationCell.X);
			hash = CombineHash(hash, DestinationCell.Y);
			hash = CombineHash(hash, _destinationSectorRegion.Position.X);
			hash = CombineHash(hash, _destinationSectorRegion.Position.Y);
			hash = CombineHash(hash, _destinationSectorRegion.Size.X);
			hash = CombineHash(hash, _destinationSectorRegion.Size.Y);
			hash = CombineHash(hash, _isMoving ? 1 : 0);
			hash = CombineHash(hash, _moveGroupID);
			hash = CombineHash(hash, _isSettlingAtDestination ? 1 : 0);
			hash = CombineHash(hash, _currentWaypointSim.X);
			hash = CombineHash(hash, _currentWaypointSim.Y);
			hash = CombineHash(hash, _hasWaypoint ? 1 : 0);
			hash = CombineHash(hash, _stopAfterWaypoint ? 1 : 0);
			hash = CombineHash(hash, _finalPathIndex);
			hash = CombineHash(hash, _hasFinalPath ? 1 : 0);
			hash = CombineHash(hash, _finalPath.Count);

			foreach (Vector2I pathCell in _finalPath)
			{
				hash = CombineHash(hash, pathCell.X);
				hash = CombineHash(hash, pathCell.Y);
			}

			return hash;
		}
	}

	private Vector2I GetPreferredVelocity()
	{
		if (!_isMoving || _navGrid == null || FlowFieldID < 0)
			return Vector2I.Zero;

		Vector2I currentCellPos = _navGrid.SimToCell(SimPosition);
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
				_currentWaypointSim = _navGrid.CellToSim(recoveryCell);
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

			_currentWaypointSim = _navGrid.CellToSim(waypointCell);
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

	private Vector2I GetGroupAlignedPreferredVelocity(IReadOnlyList<Unit> neighbors)
	{
		Vector2I preferredVelocity = _preferredSimVelocity;
		if (_moveGroupID == -1 || _isSettlingAtDestination || preferredVelocity == Vector2I.Zero)
			return preferredVelocity;

		Vector2I preferredDirection = DeterministicMath.Normalize(preferredVelocity);
		long groupDirectionX = preferredDirection.X;
		long groupDirectionY = preferredDirection.Y;
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

			Vector2I neighborDirection = DeterministicMath.Normalize(neighbor._preferredSimVelocity);
			groupDirectionX += neighborDirection.X;
			groupDirectionY += neighborDirection.Y;
			groupMemberCount++;
		}

		if (groupMemberCount == 1)
			return preferredVelocity;

		Vector2I groupDirection = new(
			(int)(groupDirectionX / groupMemberCount),
			(int)(groupDirectionY / groupMemberCount)
		);
		groupDirection = DeterministicMath.Normalize(groupDirection);
		if (groupDirection == Vector2I.Zero)
			return preferredVelocity;

		int influence = Math.Clamp(GroupDirectionInfluencePercent, 0, 100);
		Vector2I alignedDirection = DeterministicMath.Normalize(new Vector2I(
			(preferredDirection.X * (100 - influence) + groupDirection.X * influence) / 100,
			(preferredDirection.Y * (100 - influence) + groupDirection.Y * influence) / 100
		));
		int preferredSpeed = IntegerSqrt(LengthSquared(preferredVelocity));

		return new Vector2I(
			alignedDirection.X * preferredSpeed / SimScale,
			alignedDirection.Y * preferredSpeed / SimScale
		);
	}

	private AvoidanceAgentSnapshot GetAvoidanceSnapshot()
	{
		return new AvoidanceAgentSnapshot(
			UnitID,
			_moveGroupID,
			SimPosition,
			_simVelocity,
			AgentRadiusSim,
			_isMoving ? AvoidancePriority : IdleAvoidancePriority,
			_isMoving
		);
	}

	private bool HasReachedWaypoint(Vector2I previousSimPosition)
	{
		int arrivalDistance = Math.Max(MinSpeedPerTick, AgentRadiusSim / 5);
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

		float agentRadius = AgentRadius / 100.0f;
		float avoidanceRadius = AvoidanceRadius / 100.0f;
		quadMesh.Size = Vector2.One * avoidanceRadius * 2.0f;

		if (quadMesh.Material is ShaderMaterial shaderMaterial)
		{
			shaderMaterial.SetShaderParameter("AgentRadius", agentRadius);
			shaderMaterial.SetShaderParameter("AvoidanceRadius", avoidanceRadius);
		}
	}

	private Vector2I GetFacingAdjustedVelocity(Vector2I velocity)
	{
		if (velocity == Vector2I.Zero)
			return velocity;

		int targetRotation = GetMovementRotation(velocity);
		int rotationDelta = DeterministicMath.GetShortestRotationDelta(SimRotation, targetRotation);
		int maxRotationStep = RotationSpeed <= 0
			? 0
			: Math.Max(1, RotationSpeed * RotationScale / _simulationTickRate);

		SimRotation = DeterministicMath.NormalizeRotation(
			SimRotation + Math.Clamp(rotationDelta, -maxRotationStep, maxRotationStep)
		);

		int facingTolerance = Math.Clamp(MovementFacingTolerance, 0, 180) * RotationScale;
		int remainingRotation = Math.Abs(
			DeterministicMath.GetShortestRotationDelta(SimRotation, targetRotation)
		);

		return remainingRotation <= facingTolerance
			? velocity
			: Vector2I.Zero;
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

}
