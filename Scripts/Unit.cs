using Godot;
using System.Collections.Generic;

public partial class Unit : Node3D
{
	private const string UnitsGroup = "units";

	[Export] public int UnitID { get; set; }
	[Export] public int PlayerID { get; set; }
	[Export] public int Speed { get; set; } = 10;
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";
	public int FlowFieldID { get; set; } = -1;

	// [ExportGroup("Avoidance")]
	// [Export]
	// private

	// Lockstep Simulation
	private const int SimScale = 1000; // converts floats to ints
	private const int MinSpeedPerTick = 50;
	private const int MaxSpeedPerTick = 800;
	private int _speedPerTick
	{
		get
		{
			return MinSpeedPerTick + (Speed - 1) * (MaxSpeedPerTick - MinSpeedPerTick) / 19;
		}
	}
	public Vector2I SimPosition { get; private set; }

	private NavGrid _navGrid;
	private MeshInstance3D _mesh;
	private Material _defaultMaterialOverride;
	private StandardMaterial3D _selectedMaterial;
	private bool _isMoving;
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

		_defaultMaterialOverride = _mesh?.MaterialOverride;
		_selectedMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(1.0f, 0.85f, 0.2f),
			EmissionEnabled = true,
			Emission = new Color(1.0f, 0.65f, 0.05f),
		};

		SimPosition = WorldToSimPosition(GlobalPosition);
	}

	public void SimTick()
	{
		Move();
	}

	public void SetSelected(bool selected)
	{
		_isSelected = selected;

		if (_mesh == null)
			return;

		_mesh.MaterialOverride = selected ? _selectedMaterial : _defaultMaterialOverride;
	}

	public void FollowFlowField(int flowFieldID)
	{
		if (flowFieldID < 0)
			return;

		FlowFieldID = flowFieldID;
		_isMoving = true;
		_hasWaypoint = false;
		_stopAfterWaypoint = false;
	}

	public Vector3 GetSimWorldPosition()
	{
		Vector2 flatWorldPos = SimToFlatWorldPosition(SimPosition);
		return new Vector3(flatWorldPos.X, 0.0f, flatWorldPos.Y);
	}

	private void Move()
	{
		if (!_isMoving || _navGrid == null || FlowFieldID < 0)
			return;

		if (!_hasWaypoint)
		{
			Vector2I currentCellPos = _navGrid.WorldToCell(GetSimWorldPosition());
			Vector2I direction = _navGrid.GetDirection(FlowFieldID, currentCellPos);

			if (direction == Vector2I.Zero)
			{
				_currentWaypointSim = WorldToSimPosition(_navGrid.CellToWorld(currentCellPos));
				_stopAfterWaypoint = true;
			}
			else
			{
				Vector2I nextCellPos = currentCellPos + direction;
				_currentWaypointSim = WorldToSimPosition(_navGrid.CellToWorld(nextCellPos));
				_stopAfterWaypoint = false;
			}

			_hasWaypoint = true;
		}

		Vector2I previousSimPos = SimPosition;
		SimPosition = MoveTowards(SimPosition, _currentWaypointSim, _speedPerTick);
		ApplySimPositionToWorld();

		if (SimPosition == _currentWaypointSim)
		{
			_hasWaypoint = false;

			if (_stopAfterWaypoint)
				_isMoving = false;
		}

		if (SimPosition == previousSimPos)
		{
			_hasWaypoint = false;

			if (_stopAfterWaypoint)
				_isMoving = false;
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
