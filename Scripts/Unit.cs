using Godot;
using System.Collections.Generic;

public partial class Unit : Node3D
{
	private const string UnitsGroup = "units";

	[Export] public int UnitID { get; set; }
	[Export] public int PlayerID { get; set; }
	[Export] public float Speed { get; set; } = 8.0f;
	[Export] public int FlowFieldID { get; set; } = -1;
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";

	// [ExportGroup("Avoidance")]
	// [Export] private const float A

	// Lockstep Simulation
	private const int SimScale = 1000; // converts floats to ints
	public int SimSpeedPerTick { get; set; } = 133;
	public Vector2I SimPosition { get; private set; }

	private NavGrid _navGrid;
	private MeshInstance3D _mesh;
	private Material _defaultMaterialOverride;
	private StandardMaterial3D _selectedMaterial;
	private bool _isMoving;
	private Vector3 _currentWaypoint;
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

	public override void _PhysicsProcess(double delta)
	{
		Move(delta);
	}

	public void SetSelected(bool selected)
	{
		_isSelected = selected;

		if (_mesh == null)
			return;

		_mesh.MaterialOverride = selected ? _selectedMaterial : _defaultMaterialOverride;
	}

	// public void MoveToCell(Vector2I targetCellPos)
	// {
	// 	Vector2I startCellPos = _navGrid.WorldToCell(GlobalPosition);
	// 	_navGrid.BuildField(startCellPos, targetCellPos);
	// 	_isMoving = true;
	// 	_hasWaypoint = false;
	// 	_stopAfterWaypoint = false;

	// 	// GD.Print($"Moving unit to position: ({targetCellPos.X}, {targetCellPos.Y})");
	// }

	public void FollowFlowField(int flowFieldID)
	{
		if (flowFieldID < 0)
			return;

		FlowFieldID = flowFieldID;
		_isMoving = true;
		_hasWaypoint = false;
		_stopAfterWaypoint = false;
	}

	private void Move(double delta)
	{
		if (!_isMoving || _navGrid == null || FlowFieldID < 0)
			return;

		if (!_hasWaypoint)
		{
			Vector2I currentCellPos = _navGrid.WorldToCell(GlobalPosition);
			Vector2I direction = _navGrid.GetDirection(FlowFieldID, currentCellPos);

			if (direction == Vector2I.Zero)
			{
				_currentWaypoint = _navGrid.CellToWorld(currentCellPos);
				_stopAfterWaypoint = true;
			}
			else
			{
				Vector2I nextCellPos = currentCellPos + direction;
				_currentWaypoint = _navGrid.CellToWorld(nextCellPos);
				_stopAfterWaypoint = false;
			}

			_hasWaypoint = true;
		}

		Vector2 currentFlatPos = SimToFlatWorldPosition(SimPosition);
		Vector2 waypointFlatPos = new Vector2(_currentWaypoint.X, _currentWaypoint.Z);
		Vector2 toWaypoint = waypointFlatPos - currentFlatPos;

		float distanceThisFrame = SimSpeedPerTick / (float)SimScale;

		if (toWaypoint.Length() <= distanceThisFrame)
		{
			SimPosition = WorldToSimPosition(_currentWaypoint);
			ApplySimPositionToWorld();

			_hasWaypoint = false;

			if (_stopAfterWaypoint)
				_isMoving = false;

			return;
		}

		Vector2 moveDirection = toWaypoint.Normalized();
		Vector2 moveAmount = moveDirection * distanceThisFrame;
		Vector2 nextFlatPosition = currentFlatPos + moveAmount;

		SimPosition = WorldToSimPosition(new Vector3(nextFlatPosition.X, 0.0f, nextFlatPosition.Y));
		ApplySimPositionToWorld();
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
}
