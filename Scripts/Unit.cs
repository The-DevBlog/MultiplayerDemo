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

	public void MoveTo(Vector3 worldPos)
	{
		Vector2I targetCellPos = _navGrid.WorldToCell(worldPos);

		_navGrid.BuildField(targetCellPos);
		_isMoving = true;
	}

	private void Move(double delta)
	{
		if (!_isMoving || _navGrid == null)
			return;

		Vector2I currentCellPos = _navGrid.WorldToCell(GlobalPosition);
		Vector2I direction = _navGrid.GetDirection(currentCellPos);

		if (direction == Vector2I.Zero)
		{
			_isMoving = false;
			return;
		}

		Vector2I nextCellPos = currentCellPos + direction;
		Vector3 nextWorldPos = _navGrid.CellToWorld(nextCellPos);

		Vector2 currentFlatPos = new Vector2(GlobalPosition.X, GlobalPosition.Z);
		Vector2 nextFlatPos = new Vector2(nextWorldPos.X, nextWorldPos.Z);

		Vector2 toNext = nextFlatPos - currentFlatPos;
		float distanceThisFrame = Speed * (float)delta;

		if (toNext.Length() <= distanceThisFrame)
		{
			float y = GlobalPosition.Y;
			GlobalPosition = new Vector3(nextWorldPos.X, y, nextWorldPos.Z);
		}
		else
		{
			Vector2 moveDirection = toNext.Normalized();
			var moveAmount = moveDirection * distanceThisFrame;

			float x = GlobalPosition.X + moveAmount.X;
			float y = GlobalPosition.Y;
			float z = GlobalPosition.Z + moveAmount.Y;

			GlobalPosition = new Vector3(x, y, z);
		}
	}
}
