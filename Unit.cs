using Godot;

public partial class Unit : Node3D
{
	private const string UnitsGroup = "units";
	private const float ArriveDistance = 0.15f;

	[Export] public int UnitID { get; set; }
	[Export] public int PlayerID { get; set; }
	[Export] public float MoveSpeed { get; set; } = 8.0f;
	[Export] public NodePath MeshPath { get; set; } = "MeshInstance3D";
	[Export] public NodePath NavigationAgentPath { get; set; } = "NavigationAgent3D";

	private MeshInstance3D _mesh;
	private NavigationAgent3D _navigationAgent;
	private Material _defaultMaterialOverride;
	private StandardMaterial3D _selectedMaterial;
	private Vector3I _targetPosition;
	private bool _hasTarget;
	private bool _isSelected;

	public bool IsSelected => _isSelected;

	public override void _Ready()
	{
		AddToGroup(UnitsGroup);

		_mesh = GetNodeOrNull<MeshInstance3D>(MeshPath);
		_navigationAgent = GetNodeOrNull<NavigationAgent3D>(NavigationAgentPath);
		if (_navigationAgent == null)
		{
			_navigationAgent = new NavigationAgent3D
			{
				Name = "NavigationAgent3D",
			};
			AddChild(_navigationAgent);
		}

		_navigationAgent.PathDesiredDistance = ArriveDistance;
		_navigationAgent.TargetDesiredDistance = ArriveDistance;
		_navigationAgent.MaxSpeed = MoveSpeed;

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

		_targetPosition = new Vector3I(x, y, z);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_hasTarget)
		{
			return;
		}

		Vector3 toTarget = GetFlatDirectionTo(_targetPosition);

		if (HasArrived(toTarget))
		{
			StopMoving();
			return;
		}

		MoveTowardTarget(GetNextMovePosition(), delta);
	}

	private Vector3 GetNextMovePosition()
	{
		if (_navigationAgent.IsNavigationFinished())
		{
			return _targetPosition;
		}

		Vector3 nextPathPosition = _navigationAgent.GetNextPathPosition();
		return HasArrived(GetFlatDirectionTo(nextPathPosition)) ? _targetPosition : nextPathPosition;
	}

	private Vector3 GetFlatDirectionTo(Vector3 worldPosition)
	{
		Vector3 currentPosition = GlobalPosition;
		Vector3 flatCurrentPosition = new(currentPosition.X, 0.0f, currentPosition.Z);
		Vector3 flatTargetPosition = new(worldPosition.X, 0.0f, worldPosition.Z);

		return flatTargetPosition - flatCurrentPosition;
	}

	private static bool HasArrived(Vector3 toTarget)
	{
		return toTarget.Length() <= ArriveDistance;
	}

	private void StopMoving()
	{
		_hasTarget = false;
	}

	private void MoveTowardTarget(Vector3 nextPathPosition, double delta)
	{
		Vector3 toTarget = GetFlatDirectionTo(nextPathPosition);
		float distanceThisFrame = MoveSpeed * (float)delta;

		if (toTarget.Length() <= distanceThisFrame)
		{
			GlobalPosition = new Vector3(nextPathPosition.X, GlobalPosition.Y, nextPathPosition.Z);
			return;
		}

		Vector3 direction = toTarget.Normalized();
		GlobalPosition += new Vector3(direction.X, 0.0f, direction.Z) * distanceThisFrame;
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

	public void MoveTo(Vector3I worldPosition)
	{
		_targetPosition = worldPosition;
		_hasTarget = true;
		_navigationAgent.TargetPosition = _targetPosition;
	}
}
