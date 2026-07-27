using Godot;

public class NavGridDebugRenderer
{
    private MeshInstance3D _gridMesh;

    public void DrawGrid(Node parent, Vector2I gridOrigin, int width, int height, int cellSize, NavCell[,] cells, Vector2I? targetCell = null)
    {
        ClearGrid();

        ImmediateMesh mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        int left = gridOrigin.X;
        int right = gridOrigin.X + width * cellSize;
        int top = gridOrigin.Y;
        int bottom = gridOrigin.Y + height * cellSize;
        float y = 0.03f;

        for (int x = 0; x <= width; x++)
        {
            float worldX = left + x * cellSize;

            mesh.SurfaceAddVertex(new Vector3(worldX, y, top));
            mesh.SurfaceAddVertex(new Vector3(worldX, y, bottom));
        }

        for (int z = 0; z <= height; z++)
        {
            float worldZ = top + z * cellSize;

            mesh.SurfaceAddVertex(new Vector3(left, y, worldZ));
            mesh.SurfaceAddVertex(new Vector3(right, y, worldZ));
        }

        DrawDirectionArrows(mesh, gridOrigin, cellSize, cells);
        DrawDestinationDiamond(mesh, gridOrigin, cellSize, targetCell);

        mesh.SurfaceEnd();

        _gridMesh = new MeshInstance3D
        {
            Name = "NavGridDebugMesh",
            Mesh = mesh,
            MaterialOverride = CreateGridMaterial(),
        };

        parent.AddChild(_gridMesh);
    }

    public void ClearGrid()
    {
        if (_gridMesh == null || !GodotObject.IsInstanceValid(_gridMesh))
        {
            _gridMesh = null;
            return;
        }

        _gridMesh.QueueFree();
        _gridMesh = null;
    }

    private static StandardMaterial3D CreateGridMaterial()
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(1, 1, 1, 1),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
        };
    }

    private static void DrawDirectionArrows(ImmediateMesh mesh, Vector2I gridOrigin, int cellSize, NavCell[,] cells)
    {
        foreach (NavCell cell in cells)
        {
            if (cell.Direction == Vector2I.Zero)
                continue;

            Vector2 direction = new Vector2(cell.Direction.X, cell.Direction.Y).Normalized();
            Vector2 perpendicular = new Vector2(-direction.Y, direction.X);

            Vector2 center = new Vector2(
                gridOrigin.X + cell.Position.X * cellSize + cellSize / 2.0f,
                gridOrigin.Y + cell.Position.Y * cellSize + cellSize / 2.0f
            );

            float arrowLength = cellSize * 0.55f;
            float arrowHeadLength = cellSize * 0.18f;
            float arrowHeadWidth = cellSize * 0.12f;
            float y = 0.06f;

            Vector2 start = center - direction * arrowLength * 0.5f;
            Vector2 end = center + direction * arrowLength * 0.5f;
            Vector2 headLeft = end - direction * arrowHeadLength + perpendicular * arrowHeadWidth;
            Vector2 headRight = end - direction * arrowHeadLength - perpendicular * arrowHeadWidth;

            AddLine(mesh, start, end, y);
            AddLine(mesh, end, headLeft, y);
            AddLine(mesh, end, headRight, y);
        }
    }

    private static void AddLine(ImmediateMesh mesh, Vector2 start, Vector2 end, float y)
    {
        mesh.SurfaceAddVertex(new Vector3(start.X, y, start.Y));
        mesh.SurfaceAddVertex(new Vector3(end.X, y, end.Y));
    }

    private static void DrawDestinationDiamond(ImmediateMesh mesh, Vector2I gridOrigin, int cellSize, Vector2I? targetCell)
    {
        if (!targetCell.HasValue)
            return;

        Vector2I cell = targetCell.Value;
        Vector2 center = new Vector2(
            gridOrigin.X + cell.X * cellSize + cellSize / 2.0f,
            gridOrigin.Y + cell.Y * cellSize + cellSize / 2.0f
        );

        float radius = cellSize * 0.175f;
        float y = 0.08f;

        Vector2 top = center + new Vector2(0, -radius);
        Vector2 right = center + new Vector2(radius, 0);
        Vector2 bottom = center + new Vector2(0, radius);
        Vector2 left = center + new Vector2(-radius, 0);

        AddLine(mesh, top, right, y);
        AddLine(mesh, right, bottom, y);
        AddLine(mesh, bottom, left, y);
        AddLine(mesh, left, top, y);
    }
}