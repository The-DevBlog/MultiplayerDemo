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
	private int _space = 7;
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

		int unitsPerSide = DeterministicMath.IntegerSqrt(UnitCount);
		if ((long)unitsPerSide * unitsPerSide < UnitCount)
			unitsPerSide++;

		int spacingSim = _space * DeterministicMath.SimScale;
		int formationSizeSim = (unitsPerSide - 1) * spacingSim;
		Vector2I formationOriginSim = new(-formationSizeSim / 2, -formationSizeSim / 2);

		for (int unitIndex = 0; unitIndex < UnitCount; unitIndex++)
		{
			Unit unit = unitScene.Instantiate<Unit>();
			int row = unitIndex / unitsPerSide;
			int column = unitIndex % unitsPerSide;

			unit.UnitID = unitIndex;
			unit.SetInitialSimPosition(
				formationOriginSim + new Vector2I(column * spacingSim, row * spacingSim)
			);
			unit.SetAvoidanceVisualization(_showAvoidanceVisualization);

			AddChild(unit);
		}
	}
}
