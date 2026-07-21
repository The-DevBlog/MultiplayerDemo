using Godot;

public partial class Unit : RigidBody3D
{
	private const string UnitsGroup = "units";
	private const float ArriveDistance = 0.15f;

	[Export] public float MoveSpeed { get; set; } = 8.0f;
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";

	private MeshInstance3D _mesh;
	private Material _defaultMaterialOverride;
	private StandardMaterial3D _selectedMaterial;
	private Vector3 _targetPosition;
	private bool _hasTarget;
	private bool _isSelected;

	public bool IsSelected => _isSelected;

	public override void _Ready()
	{
		AddToGroup(UnitsGroup);

		_mesh = GetNodeOrNull<MeshInstance3D>(MeshPath);
		_defaultMaterialOverride = _mesh?.MaterialOverride;
		_selectedMaterial = new StandardMaterial3D
		{
			AlbedoColor = new Color(1.0f, 0.85f, 0.2f),
			EmissionEnabled = true,
			Emission = new Color(1.0f, 0.65f, 0.05f),
		};

		LockRotation = true;
		_targetPosition = GlobalPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_hasTarget)
		{
			return;
		}

		Vector3 toTarget = GetFlatDirectionToTarget();

		if (HasArrived(toTarget))
		{
			StopMoving();
			return;
		}

		MoveTowardTarget(toTarget);
	}

	private Vector3 GetFlatDirectionToTarget()
	{
		Vector3 currentPosition = GlobalPosition;
		Vector3 flatCurrentPosition = new(currentPosition.X, 0.0f, currentPosition.Z);
		Vector3 flatTargetPosition = new(_targetPosition.X, 0.0f, _targetPosition.Z);

		return flatTargetPosition - flatCurrentPosition;
	}

	private static bool HasArrived(Vector3 toTarget)
	{
		return toTarget.Length() <= ArriveDistance;
	}

	private void StopMoving()
	{
		_hasTarget = false;
		LinearVelocity = new Vector3(0.0f, LinearVelocity.Y, 0.0f);
	}

	private void MoveTowardTarget(Vector3 toTarget)
	{
		Vector3 direction = toTarget.Normalized();
		LinearVelocity = new Vector3(direction.X * MoveSpeed, LinearVelocity.Y, direction.Z * MoveSpeed);
		Sleeping = false;
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

	public void MoveTo(Vector3 worldPosition)
	{
		_targetPosition = new Vector3(worldPosition.X, GlobalPosition.Y, worldPosition.Z);
		_hasTarget = true;
		Sleeping = false;
	}
}
