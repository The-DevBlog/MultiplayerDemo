using System;
using System.Collections.Generic;
using Godot;

public partial class NavGrid : Node
{
    public int Width { get; set; }
    public int Height { get; set; }

    private Vector2I _gridOrigin;
    private NavCell[,] _cells;
    private int CellSize = 5;
    private readonly NavGridDebugRenderer _debugRenderer = new();
    private LocalResources _localResources;
    private readonly Vector2I[] _offsets =
    [
        new Vector2I(0, -1), // north
		new Vector2I(0, 1),  // south
		new Vector2I(1, 0),  // east
		new Vector2I(-1, 0), // west
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
            Width = _localResources.MapSize.X / CellSize;
            Height = _localResources.MapSize.Y / CellSize;
            _gridOrigin = new Vector2I(-_localResources.MapSize.X / 2, -_localResources.MapSize.Y / 2);
        }

        InitGrid();
        _debugRenderer.DrawGrid(this, _gridOrigin, Width, Height, CellSize, _cells);
    }

    public void BuildField(Vector2I targetPosition)
    {
        bool isIntegrationFieldBuilt = BuildIntegrationField(targetPosition);
        if (isIntegrationFieldBuilt)
        {
            BuildFlowField();
            _debugRenderer.DrawGrid(this, _gridOrigin, Width, Height, CellSize, _cells, targetPosition);
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

    private void InitGrid()
    {
        _cells = new NavCell[Width, Height];

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
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
                int newIntegrationCost = currentCell.IntegrationCost + neighborCell.Cost;
                if (newIntegrationCost < neighborCell.IntegrationCost)
                {
                    neighborCell.IntegrationCost = newIntegrationCost;
                    queue.Enqueue(neighborCell);
                }
            }
        }

        return true;
    }

    public NavCell GetCell(Vector2I cellPosition)
    {
        bool isInBounds = IsInBounds(cellPosition);
        if (!isInBounds)
            return null;

        return _cells[cellPosition.X, cellPosition.Y];
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

        if (x >= Width || y >= Height || x < 0 || y < 0)
            return false;

        return true;
    }

}
