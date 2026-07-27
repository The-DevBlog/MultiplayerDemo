using System;
using System.Collections.Generic;
using Godot;

public partial class NavGrid : Node
{
    [Export] private bool _drawNavGrid;
    private int _width { get; set; }
    private int _height { get; set; }
    private Vector2I _gridOrigin;
    private NavCell[,] _cells;
    private int CellSize = 5;
    private int _diagonalCost = 14;
    private int _straightCost = 10;
    private readonly NavGridDebugRenderer _debugRenderer = new();
    private LocalResources _localResources;
    private readonly Vector2I[] _offsets =
    [
        new Vector2I(0, -1),  // N
		new Vector2I(0, 1),   // S
		new Vector2I(1, 0),   // E
		new Vector2I(-1, 0),  // W

		new Vector2I(1, -1),  // NE
		new Vector2I(-1, -1), // NW
		new Vector2I(1, 1),   // SE
		new Vector2I(-1, 1)   // SW
	];

    public override void _Ready()
    {
        _localResources = GetTree().CurrentScene as LocalResources;
        if (_localResources == null)
        {
            GD.PrintErr("[NavGrid.Ready()] Could not find LocalResources");
        }
        else
        {
            _width = _localResources.MapSize.X / CellSize;
            _height = _localResources.MapSize.Y / CellSize;
            _gridOrigin = new Vector2I(-_localResources.MapSize.X / 2, -_localResources.MapSize.Y / 2);
        }

        InitGrid();
        LoadObstacles();

        if (_drawNavGrid)
        {
            _debugRenderer.DrawGridLines(this, _gridOrigin, _width, _height, CellSize);
            _debugRenderer.DrawBlockedCells(this, _gridOrigin, CellSize, _cells);
        }
    }

    public void BuildField(Vector2I targetPosition)
    {
        bool isIntegrationFieldBuilt = BuildIntegrationField(targetPosition);
        if (isIntegrationFieldBuilt)
        {
            BuildFlowField();

            if (_drawNavGrid)
            {
                _debugRenderer.DrawFlowArrows(this, _gridOrigin, CellSize, _cells);
                _debugRenderer.DrawTarget(this, _gridOrigin, CellSize, targetPosition);
            }
        }
    }

    public Vector2I GetDirection(Vector2I cellPosition)
    {
        NavCell cell = GetCell(cellPosition);
        if (cell == null)
            return Vector2I.Zero;

        return cell.Direction;
    }

    public void SetWalkable(Vector2I cellPosition, bool walkable)
    {
        NavCell cell = GetCell(cellPosition);
        if (cell == null)
            return;

        cell.Walkable = walkable;
    }

    public Vector2I WorldToCell(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt((worldPosition.X - _gridOrigin.X) / CellSize);
        int z = Mathf.FloorToInt((worldPosition.Z - _gridOrigin.Y) / CellSize);

        return new Vector2I(x, z);
    }

    public Vector3 CellToWorld(Vector2I cellPosition)
    {
        float x = _gridOrigin.X + cellPosition.X * CellSize + CellSize / 2.0f;
        float z = _gridOrigin.Y + cellPosition.Y * CellSize + CellSize / 2.0f;
        float y = 0;

        return new Vector3(x, y, z);
    }

    public NavCell GetCell(Vector2I cellPosition)
    {
        bool isInBounds = IsInBounds(cellPosition);
        if (!isInBounds)
            return null;

        return _cells[cellPosition.X, cellPosition.Y];
    }

    private void InitGrid()
    {
        _cells = new NavCell[_width, _height];

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                Vector2I position = new Vector2I(x, y);
                NavCell cell = new NavCell(position);
                _cells[x, y] = cell;
            }
        }
    }

    private void BuildFlowField()
    {
        foreach (NavCell cell in _cells)
        {
            if (!cell.Walkable)
            {
                cell.Direction = Vector2I.Zero;
                continue;
            }

            NavCell[] neighbors = GetNeighbors(cell.Position);
            if (neighbors.Length == 0)
                continue;

            NavCell bestNeighbor = null;
            int bestCost = cell.IntegrationCost;

            foreach (NavCell neighbor in neighbors)
            {
                if (neighbor.IntegrationCost < bestCost)
                {
                    bestNeighbor = neighbor;
                    bestCost = neighbor.IntegrationCost;
                }
            }

            if (bestNeighbor != null)
                cell.Direction = bestNeighbor.Position - cell.Position;
            else
                cell.Direction = Vector2I.Zero;
        }
    }

    private bool BuildIntegrationField(Vector2I targetPosition)
    {
        ResetIntegrationCosts();

        NavCell targetCell = GetCell(targetPosition);
        if (targetCell == null)
            return false;

        targetCell.IntegrationCost = 0;

        var queue = new Queue<NavCell>();
        queue.Enqueue(targetCell);

        while (queue.Count > 0)
        {
            NavCell currentCell = queue.Dequeue();
            NavCell[] neighbors = GetNeighbors(currentCell.Position);

            foreach (NavCell neighborCell in neighbors)
            {
                int moveCost = GetMoveCost(currentCell.Position, neighborCell.Position);
                int newIntegrationCost = currentCell.IntegrationCost + moveCost * neighborCell.Cost;
                if (newIntegrationCost < neighborCell.IntegrationCost)
                {
                    neighborCell.IntegrationCost = newIntegrationCost;
                    queue.Enqueue(neighborCell);
                }
            }
        }

        return true;
    }

    private void LoadObstacles()
    {
        foreach (Node node in GetTree().GetNodesInGroup("nav_obstacles"))
        {
            var meshes = new List<MeshInstance3D>();
            GetMeshes(node, meshes);

            foreach (var mesh in meshes)
            {
                MarkMeshUnwalkable(mesh);
            }
        }
    }

    private void MarkMeshUnwalkable(MeshInstance3D mesh)
    {
        Aabb aabb = mesh.GetAabb();

        Vector3 min = aabb.Position;
        Vector3 max = aabb.End;

        Vector3[] corners =
        [
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),

            new Vector3(min.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z),
        ];

        Vector2[] footprint = GetMeshWorldFootprint(mesh, corners);
        if (footprint.Length < 3)
            return;

        float minWorldX = float.PositiveInfinity;
        float maxWorldX = float.NegativeInfinity;
        float minWorldZ = float.PositiveInfinity;
        float maxWorldZ = float.NegativeInfinity;

        foreach (Vector2 point in footprint)
        {
            minWorldX = Mathf.Min(minWorldX, point.X);
            maxWorldX = Mathf.Max(maxWorldX, point.X);
            minWorldZ = Mathf.Min(minWorldZ, point.Y);
            maxWorldZ = Mathf.Max(maxWorldZ, point.Y);
        }

        const float maxCellEpsilon = 0.001f;
        Vector2I minCell = WorldToCell(new Vector3(minWorldX, 0, minWorldZ));
        Vector2I maxCell = WorldToCell(new Vector3(maxWorldX - maxCellEpsilon, 0, maxWorldZ - maxCellEpsilon));

        for (int x = minCell.X; x <= maxCell.X; x++)
        {
            for (int y = minCell.Y; y <= maxCell.Y; y++)
            {
                Vector2I cellPosition = new Vector2I(x, y);
                if (CellOverlapsFootprint(cellPosition, footprint))
                    SetWalkable(cellPosition, false);
            }
        }
    }

    private Vector2[] GetMeshWorldFootprint(MeshInstance3D mesh, Vector3[] localCorners)
    {
        var points = new List<Vector2>();

        foreach (Vector3 corner in localCorners)
        {
            Vector3 worldCorner = mesh.GlobalTransform * corner;
            AddUniquePoint(points, new Vector2(worldCorner.X, worldCorner.Z));
        }

        if (points.Count < 3)
            return points.ToArray();

        Vector2 center = Vector2.Zero;
        foreach (Vector2 point in points)
        {
            center += point;
        }

        center /= points.Count;
        points.Sort((a, b) => MathF.Atan2(a.Y - center.Y, a.X - center.X).CompareTo(MathF.Atan2(b.Y - center.Y, b.X - center.X)));

        return points.ToArray();
    }

    private static void AddUniquePoint(List<Vector2> points, Vector2 point)
    {
        const float pointEpsilon = 0.001f;
        float epsilonSquared = pointEpsilon * pointEpsilon;

        foreach (Vector2 existingPoint in points)
        {
            if ((existingPoint - point).LengthSquared() <= epsilonSquared)
                return;
        }

        points.Add(point);
    }

    private bool CellOverlapsFootprint(Vector2I cellPosition, Vector2[] footprint)
    {
        float left = _gridOrigin.X + cellPosition.X * CellSize;
        float right = left + CellSize;
        float top = _gridOrigin.Y + cellPosition.Y * CellSize;
        float bottom = top + CellSize;

        Vector2[] cellCorners =
        [
            new Vector2(left, top),
            new Vector2(right, top),
            new Vector2(right, bottom),
            new Vector2(left, bottom),
        ];

        Vector2[] first = cellCorners;
        Vector2[] second = footprint;

        return !HasSeparatingAxis(first, first, second) && !HasSeparatingAxis(second, first, second);
    }

    private static bool HasSeparatingAxis(Vector2[] axisSource, Vector2[] first, Vector2[] second)
    {
        for (int i = 0; i < axisSource.Length; i++)
        {
            Vector2 start = axisSource[i];
            Vector2 end = axisSource[(i + 1) % axisSource.Length];
            Vector2 edge = end - start;

            if (edge.LengthSquared() <= 0.000001f)
                continue;

            Vector2 axis = new Vector2(-edge.Y, edge.X).Normalized();

            ProjectPolygon(first, axis, out float firstMin, out float firstMax);
            ProjectPolygon(second, axis, out float secondMin, out float secondMax);

            if (firstMax < secondMin || secondMax < firstMin)
                return true;
        }

        return false;
    }

    private static void ProjectPolygon(Vector2[] polygon, Vector2 axis, out float min, out float max)
    {
        float firstProjection = polygon[0].Dot(axis);
        min = firstProjection;
        max = firstProjection;

        for (int i = 1; i < polygon.Length; i++)
        {
            float projection = polygon[i].Dot(axis);
            min = Mathf.Min(min, projection);
            max = Mathf.Max(max, projection);
        }
    }

    private void GetMeshes(Node node, List<MeshInstance3D> meshes)
    {
        if (node is MeshInstance3D mesh)
            meshes.Add(mesh);

        foreach (Node child in node.GetChildren())
            GetMeshes(child, meshes);
    }

    private NavCell[] GetNeighbors(Vector2I cellPosition)
    {
        bool isInBounds = IsInBounds(cellPosition);
        if (!isInBounds)
            return Array.Empty<NavCell>();

        var neighbors = new List<NavCell>();

        foreach (Vector2I offset in _offsets)
        {
            NavCell cell = GetCell(cellPosition + offset);
            if (cell != null && cell.Walkable)
            {
                neighbors.Add(cell);
            }
        }

        return neighbors.ToArray();
    }

    private int GetMoveCost(Vector2I from, Vector2I to)
    {
        Vector2I delta = to - from;

        bool isDiagonal = delta.X != 0 && delta.Y != 0;
        return isDiagonal ? _diagonalCost : _straightCost;
    }

    private void ResetIntegrationCosts()
    {
        foreach (NavCell cell in _cells)
        {
            cell.ResetIntegrationCost();
        }
    }

    private bool IsInBounds(Vector2I cellPosition)
    {
        int x = cellPosition.X;
        int y = cellPosition.Y;

        if (x >= _width || y >= _height || x < 0 || y < 0)
            return false;

        return true;
    }
}
