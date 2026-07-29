using System;
using System.Collections.Generic;
using Godot;

public partial class NavGrid : Node
{
    [Export] private bool _drawNavGrid;
    [Export] public Color GridColor { get; set; } = new(1.0f, 1.0f, 1.0f, 0.35f);
    [Export] public Color SectorColor { get; set; } = new(1.0f, 0.55f, 0.1f, 0.9f);
    [Export] public Color BlockedColor { get; set; } = new(1.0f, 0.05f, 0.05f, 0.9f);
    [Export] public Color FlowColor { get; set; } = new(0.1f, 0.75f, 1.0f, 0.9f);
    [Export] public Color PortalColor { get; set; } = new(0.2f, 1.0f, 0.35f, 0.95f);
    [Export] public Color TargetColor { get; set; } = new(1.0f, 0.9f, 0.1f, 1.0f);
    [Export] private uint _terrainCollisionMask = uint.MaxValue;
    [Export] private float _terrainRaycastHeight = 1000.0f;
    private int _width { get; set; }
    private int _height { get; set; }
    private int _sectorSize = 16;
    private int _sectorWidth;
    private int _sectorHeight;
    private Vector2I _gridOrigin;
    private NavCell[,] _cells;
    private NavSector[,] _sectors;
    private int CellSize = 2;
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

            _sectorWidth = Mathf.CeilToInt(_width / (float)_sectorSize);
            _sectorHeight = Mathf.CeilToInt(_height / (float)_sectorSize);

            _gridOrigin = new Vector2I(-_localResources.MapSize.X / 2, -_localResources.MapSize.Y / 2);
        }

        InitGrid();
        InitSectors();
        LoadObstacles();
        BuildSectorPortals();

        _debugRenderer.SetSectorSize(_sectorSize);
        _debugRenderer.SetColors(GridColor, BlockedColor, FlowColor, TargetColor, SectorColor, PortalColor);

        if (_drawNavGrid)
        {
            _debugRenderer.DrawGridLines(this, _gridOrigin, _width, _height, CellSize);
            _debugRenderer.DrawBlockedCells(this, _gridOrigin, CellSize, _cells);
            _debugRenderer.DrawPortalCells(this, _gridOrigin, _width, _height, CellSize, _sectors);
        }
    }

    public override void _Process(double delta)
    {
        if (_drawNavGrid)
        {
            _debugRenderer.SetSectorSize(_sectorSize);
            _debugRenderer.SetColors(GridColor, BlockedColor, FlowColor, TargetColor, SectorColor, PortalColor);
        }
    }

    public void BuildField(Vector2I targetPos)
    {
        bool isIntegrationFieldBuilt = BuildIntegrationField(targetPos);
        if (isIntegrationFieldBuilt)
        {
            BuildFlowField();
            _debugRenderer.SetColors(GridColor, BlockedColor, FlowColor, TargetColor, SectorColor, PortalColor);

            if (_drawNavGrid)
            {
                _debugRenderer.DrawFlowArrows(this, _gridOrigin, CellSize, _cells);
                _debugRenderer.DrawPortalCells(this, _gridOrigin, _width, _height, CellSize, _sectors);
                _debugRenderer.DrawTarget(this, _gridOrigin, CellSize, targetPos);
            }
        }
    }

    public Vector2I GetDirection(Vector2I cellPos)
    {
        NavCell cell = GetCell(cellPos);
        if (cell == null)
            return Vector2I.Zero;

        return cell.Direction;
    }

    public void SetWalkable(Vector2I cellPos, bool walkable)
    {
        NavCell cell = GetCell(cellPos);
        if (cell == null)
            return;

        cell.Walkable = walkable;
    }

    public Vector2I WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt((worldPos.X - _gridOrigin.X) / CellSize);
        int z = Mathf.FloorToInt((worldPos.Z - _gridOrigin.Y) / CellSize);

        return new Vector2I(x, z);
    }

    public Vector3 CellToWorld(Vector2I cellPos)
    {
        float x = _gridOrigin.X + cellPos.X * CellSize + CellSize / 2.0f;
        float z = _gridOrigin.Y + cellPos.Y * CellSize + CellSize / 2.0f;
        float y = GetTerrainHeight(new Vector2(x, z));

        return new Vector3(x, y, z);
    }

    private Vector2I CellToSectorPosition(Vector2I cellPos)
    {
        int sectorX = cellPos.X / _sectorSize;
        int sectorY = cellPos.Y / _sectorSize;

        return new Vector2I(sectorX, sectorY);
    }

    public float GetTerrainHeight(Vector2 worldPos)
    {
        if (TryGetTerrainPoint(worldPos, out Vector3 terrainPoint))
            return terrainPoint.Y;

        return GetFallbackGroundHeight();
    }

    public bool TryProjectToTerrain(Vector3 rayOrigin, Vector3 rayDirection, out Vector3 terrainPoint)
    {
        terrainPoint = default;

        if (rayDirection == Vector3.Zero)
            return false;

        Vector3 rayEnd = rayOrigin + rayDirection.Normalized() * _terrainRaycastHeight * 2.0f;
        return TryRaycastTerrain(rayOrigin, rayEnd, out terrainPoint);
    }

    private NavCell GetCell(Vector2I cellPos)
    {
        bool isInBounds = IsInBounds(cellPos);
        if (!isInBounds)
            return null;

        return _cells[cellPos.X, cellPos.Y];
    }

    private NavSector GetSector(Vector2I sectorPos)
    {
        if (sectorPos.X < 0 || sectorPos.Y < 0)
            return null;

        if (sectorPos.X >= _sectorWidth || sectorPos.Y >= _sectorHeight)
            return null;

        return _sectors[sectorPos.X, sectorPos.Y];
    }

    private NavSector GetSectorForCell(Vector2I cellPos)
    {
        Vector2I sectorPos = CellToSectorPosition(cellPos);
        return GetSector(sectorPos);
    }

    private void InitGrid()
    {
        _cells = new NavCell[_width, _height];

        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                Vector2I pos = new Vector2I(x, y);
                NavCell cell = new NavCell(pos);
                _cells[x, y] = cell;
            }
        }
    }

    private void InitSectors()
    {
        _sectors = new NavSector[_sectorWidth, _sectorHeight];

        for (int x = 0; x < _sectorWidth; x++)
        {
            for (int y = 0; y < _sectorHeight; y++)
            {
                Vector2I sectorPos = new Vector2I(x, y);
                _sectors[x, y] = CreateSector(sectorPos);
            }
        }
    }

    private void BuildSectorPortals()
    {
        foreach (NavSector sector in _sectors)
            sector.Portals.Clear();

        for (int sectorX = 0; sectorX < _sectorWidth; sectorX++)
        {
            for (int sectorY = 0; sectorY < _sectorHeight; sectorY++)
            {
                NavSector sector = _sectors[sectorX, sectorY];

                // EAST
                NavSector eastSector = GetSector(sector.Position + new Vector2I(1, 0));
                if (eastSector != null)
                {
                    int leftX = sector.MaxCell.X;
                    int rightX = eastSector.MinCell.X;

                    int startY = Math.Max(sector.MinCell.Y, eastSector.MinCell.Y);
                    int endY = Math.Min(sector.MaxCell.Y, eastSector.MaxCell.Y);

                    for (int borderY = startY; borderY <= endY; borderY++)
                    {
                        Vector2I leftCellPos = new Vector2I(leftX, borderY);
                        Vector2I rightCellPos = new Vector2I(rightX, borderY);

                        NavCell leftCell = GetCell(leftCellPos);
                        NavCell rightCell = GetCell(rightCellPos);

                        if (leftCell != null && rightCell != null && leftCell.Walkable && rightCell.Walkable)
                        {
                            sector.Portals.Add(new NavPortal
                            {
                                FromSector = sector.Position,
                                ToSector = eastSector.Position,
                                FromCell = leftCellPos,
                                ToCell = rightCellPos
                            });

                            eastSector.Portals.Add(new NavPortal
                            {
                                FromSector = eastSector.Position,
                                ToSector = sector.Position,
                                FromCell = rightCellPos,
                                ToCell = leftCellPos
                            });
                        }
                    }
                }

                // SOUTH
                NavSector southSector = GetSector(sector.Position + new Vector2I(0, 1));
                if (southSector != null)
                {
                    int topY = sector.MaxCell.Y;
                    int bottomY = southSector.MinCell.Y;

                    int startX = Math.Max(sector.MinCell.X, southSector.MinCell.X);
                    int endX = Math.Min(sector.MaxCell.X, southSector.MaxCell.X);

                    for (int borderX = startX; borderX <= endX; borderX++)
                    {
                        Vector2I topCellPos = new Vector2I(borderX, topY);
                        Vector2I bottomCellPos = new Vector2I(borderX, bottomY);

                        NavCell topCell = GetCell(topCellPos);
                        NavCell bottomCell = GetCell(bottomCellPos);

                        if (topCell != null && bottomCell != null && topCell.Walkable && bottomCell.Walkable)
                        {
                            sector.Portals.Add(new NavPortal
                            {
                                FromSector = sector.Position,
                                ToSector = southSector.Position,
                                FromCell = topCellPos,
                                ToCell = bottomCellPos
                            });

                            southSector.Portals.Add(new NavPortal
                            {
                                FromSector = southSector.Position,
                                ToSector = sector.Position,
                                FromCell = bottomCellPos,
                                ToCell = topCellPos
                            });
                        }
                    }
                }
            }
        }
    }

    private List<NavPortal> FindSectorPortalPath(Vector2I startCellPos, Vector2I targetCellPos)
    {
        NavSector startSector = GetSectorForCell(startCellPos);
        NavSector targetSector = GetSectorForCell(targetCellPos);

        if (startSector == null || targetSector == null)
            return new List<NavPortal>();

        if (startSector.Position == targetSector.Position)
            return new List<NavPortal>();

        var frontier = new Queue<NavSector>();
        var visited = new HashSet<Vector2I>();
        var cameFromPortal = new Dictionary<Vector2I, NavPortal>();

        frontier.Enqueue(startSector);
        visited.Add(startSector.Position);

        while (frontier.Count > 0)
        {
            NavSector currentSector = frontier.Dequeue();

            if (currentSector.Position == targetSector.Position)
                break;

            foreach (NavPortal portal in currentSector.Portals)
            {
                if (visited.Contains(portal.ToSector))
                    continue;

                NavSector nextSector = GetSector(portal.ToSector);
                if (nextSector == null)
                    continue;

                visited.Add(portal.ToSector);
                cameFromPortal[portal.ToSector] = portal;
                frontier.Enqueue(nextSector);
            }
        }

        if (!visited.Contains(targetSector.Position))
            return new List<NavPortal>();

        var path = new List<NavPortal>();
        Vector2I currnet = targetSector.Position;

        while (currnet != startSector.Position)
        {
            NavPortal portal = cameFromPortal[currnet];
            path.Add(portal);
            currnet = portal.FromSector;
        }

        path.Reverse();
        return path;
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

    private bool BuildIntegrationField(Vector2I targetPos)
    {
        ResetIntegrationCosts();

        NavCell targetCell = GetCell(targetPos);
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
                Vector2I cellPos = new Vector2I(x, y);
                if (CellOverlapsFootprint(cellPos, footprint))
                    SetWalkable(cellPos, false);
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

    private bool CellOverlapsFootprint(Vector2I cellPos, Vector2[] footprint)
    {
        float left = _gridOrigin.X + cellPos.X * CellSize;
        float right = left + CellSize;
        float top = _gridOrigin.Y + cellPos.Y * CellSize;
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

    private NavCell[] GetNeighbors(Vector2I cellPos)
    {
        bool isInBounds = IsInBounds(cellPos);
        if (!isInBounds)
            return Array.Empty<NavCell>();

        var neighbors = new List<NavCell>();

        foreach (Vector2I offset in _offsets)
        {
            NavCell cell = GetCell(cellPos + offset);
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

    private bool TryGetTerrainPoint(Vector2 worldPos, out Vector3 terrainPoint)
    {
        float fallbackHeight = GetFallbackGroundHeight();
        Vector3 from = new Vector3(worldPos.X, fallbackHeight + _terrainRaycastHeight, worldPos.Y);
        Vector3 to = new Vector3(worldPos.X, fallbackHeight - _terrainRaycastHeight, worldPos.Y);

        return TryRaycastTerrain(from, to, out terrainPoint);
    }

    private bool TryRaycastTerrain(Vector3 from, Vector3 to, out Vector3 terrainPoint)
    {
        terrainPoint = default;

        World3D world = GetViewport()?.World3D;
        if (world == null)
            return false;

        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.CollisionMask = _terrainCollisionMask;

        Godot.Collections.Dictionary result = world.DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
            return false;

        terrainPoint = (Vector3)result["position"];
        return true;
    }

    private float GetFallbackGroundHeight()
    {
        Node currentScene = GetTree()?.CurrentScene;
        return currentScene?.GetNodeOrNull<Node3D>("%Ground")?.GlobalPosition.Y ?? 0.0f;
    }

    private void ResetIntegrationCosts()
    {
        foreach (NavCell cell in _cells)
        {
            cell.ResetIntegrationCost();
        }
    }

    private NavSector CreateSector(Vector2I sectorPos)
    {
        int minCellX = sectorPos.X * _sectorSize;
        int minCellY = sectorPos.Y * _sectorSize;

        int maxCellX = minCellX + _sectorSize - 1;
        int maxCellY = minCellY + _sectorSize - 1;
        maxCellX = Math.Min(maxCellX, _width - 1);
        maxCellY = Math.Min(maxCellY, _height - 1);

        return new NavSector(
            sectorPos,
            new Vector2I(minCellX, minCellY),
            new Vector2I(maxCellX, maxCellY)
        );
    }

    private bool IsInBounds(Vector2I cellPos)
    {
        int x = cellPos.X;
        int y = cellPos.Y;

        if (x >= _width || y >= _height || x < 0 || y < 0)
            return false;

        return true;
    }
}
