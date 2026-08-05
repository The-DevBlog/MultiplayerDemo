using Godot;

public partial class LocalResources : Node3D
{
	[Export] public Vector2I MapSize { get; set; }
	[Export] public int UnitCount { get; set; } = 300;
	[Export] public PackedScene UnitScene { get; set; }
	[ExportGroup("Debug")]
	[Export]
	public bool ShowAvoidanceVisualization
	{
		get => _showAvoidanceVisualization;
		set
		{
			_showAvoidanceVisualization = value;

			if (!IsInsideTree())
				return;

			foreach (Node node in GetTree().GetNodesInGroup("units"))
			{
				if (node is Unit unit)
					unit.SetAvoidanceVisualization(value);
			}
		}
	}

	private MeshInstance3D _ground;
	private float _space = 7.0f;
	private bool _showAvoidanceVisualization;
	private const string DefaultUnitScenePath = "res://Scenes/unit.tscn";

	public override void _Ready()
	{
		_ground = GetNode<MeshInstance3D>("%Ground");
		if (_ground == null)
		{
			GD.PrintErr("[LocalResources.Ready()] Ground could not be found");
		}
		else
		{
			QuadMesh mesh = _ground.Mesh as QuadMesh;
			mesh.Size = new Vector2(MapSize.X, MapSize.Y);
			_ground.Mesh = mesh;
		}
	}

	public void CreateUnits()
	{
		if (UnitCount <= 0)
			return;

		PackedScene unitScene = UnitScene ?? GD.Load<PackedScene>(DefaultUnitScenePath);
		if (unitScene == null)
		{
			GD.PrintErr("[LocalResources.Init()] Unit scene could not be found");
			return;
		}

		int unitsPerSide = Mathf.CeilToInt(Mathf.Sqrt(UnitCount));
		float formationSize = (unitsPerSide - 1) * _space;
		Vector3 formationOrigin = new(-formationSize / 2.0f, 0.0f, -formationSize / 2.0f);

		for (int unitIndex = 0; unitIndex < UnitCount; unitIndex++)
		{
			Unit unit = unitScene.Instantiate<Unit>();
			int row = unitIndex / unitsPerSide;
			int column = unitIndex % unitsPerSide;

			unit.UnitID = unitIndex;
			unit.Position = formationOrigin + new Vector3(column * _space, 0.0f, row * _space);
			unit.SetAvoidanceVisualization(_showAvoidanceVisualization);

			AddChild(unit);
		}
	}
}
