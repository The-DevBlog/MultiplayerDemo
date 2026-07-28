using Godot;

public partial class Unit : Node3D
{
	private const string UnitsGroup = "units";

	[Export] public int UnitID { get; set; }
	[Export] public int PlayerID { get; set; }
	[Export] public float Speed { get; set; } = 8.0f;
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";

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

		int x = Mathf.RoundToInt(GlobalPosition.X);
		int y = Mathf.RoundToInt(GlobalPosition.Y);
		int z = Mathf.RoundToInt(GlobalPosition.Z);
	}

	public override void _PhysicsProcess(double delta)
	{
		Move(delta);
	}

	public void SetSelected(bool selected)
	{
		_isSelected = selected;

		if (_mesh == null)
		{
			return;
		}

		_mesh.MaterialOverride = selected ? _selectedMaterial : _defaultMaterialOverride;
	}

	public void MoveToCell(Vector2I targetCellPos)
	{
		_navGrid.BuildField(targetCellPos);
		_isMoving = true;
		_hasWaypoint = false;
		_stopAfterWaypoint = false;

		// GD.Print($"Moving unit to position: ({targetCellPos.X}, {targetCellPos.Y})");
	}

	private void Move(double delta)
	{
		if (!_isMoving || _navGrid == null)
			return;

		if (!_hasWaypoint)
		{
			Vector2I currentCellPos = _navGrid.WorldToCell(GlobalPosition);
			Vector2I direction = _navGrid.GetDirection(currentCellPos);

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

		Vector2 currentFlatPos = new Vector2(GlobalPosition.X, GlobalPosition.Z);
		Vector2 waypointFlatPos = new Vector2(_currentWaypoint.X, _currentWaypoint.Z);
		Vector2 toWaypoint = waypointFlatPos - currentFlatPos;

		float distanceThisFrame = Speed * (float)delta;

		if (toWaypoint.Length() <= distanceThisFrame)
		{
			GlobalPosition = _currentWaypoint;

			_hasWaypoint = false;

			if (_stopAfterWaypoint)
				_isMoving = false;

			return;
		}

		Vector2 moveDirection = toWaypoint.Normalized();
		Vector2 moveAmount = moveDirection * distanceThisFrame;
		Vector2 nextFlatPosition = currentFlatPos + moveAmount;
		float nextY = _navGrid.GetTerrainHeight(nextFlatPosition);

		GlobalPosition = new Vector3(
			nextFlatPosition.X,
			nextY,
			nextFlatPosition.Y
		);
	}
}
