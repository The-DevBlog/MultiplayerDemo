using Godot;

public partial class LocalResources : Node3D
{
	[Export] public Vector2I MapSize { get; set; }
	private MeshInstance3D _ground;
	public override void _Ready()
	{
		GD.Print("Mapsize: " + MapSize);
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
}
