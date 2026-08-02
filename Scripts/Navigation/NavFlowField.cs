using System.Collections.Generic;
using Godot;

public class NavFlowField
{
    public Vector2I TargetCell { get; }
    public Vector2I[,] Directions { get; }
    public int[,] IntegrationCosts { get; }
    private readonly HashSet<Vector2I> _calculatedSectors;

    public NavFlowField(
        Vector2I targetCell,
        Vector2I[,] directions,
        int[,] integrationCosts,
        HashSet<Vector2I> calculatedSectors)
    {
        TargetCell = targetCell;
        Directions = directions;
        IntegrationCosts = integrationCosts;
        _calculatedSectors = new HashSet<Vector2I>(calculatedSectors);
    }

    public Vector2I GetDirection(Vector2I cellPos)
    {
        if (cellPos.X < 0 || cellPos.Y < 0)
            return Vector2I.Zero;

        if (cellPos.X >= Directions.GetLength(0) || cellPos.Y >= Directions.GetLength(1))
            return Vector2I.Zero;

        return Directions[cellPos.X, cellPos.Y];
    }

    public int GetIntegrationCost(Vector2I cellPos)
    {
        if (cellPos.X < 0 || cellPos.Y < 0)
            return NavCell.MaxIntegrationCost;

        if (cellPos.X >= IntegrationCosts.GetLength(0) || cellPos.Y >= IntegrationCosts.GetLength(1))
            return NavCell.MaxIntegrationCost;

        return IntegrationCosts[cellPos.X, cellPos.Y];
    }

    public void SetIntegrationCost(Vector2I cellPos, int integrationCost)
    {
        IntegrationCosts[cellPos.X, cellPos.Y] = integrationCost;
    }

    public void SetDirection(Vector2I cellPos, Vector2I direction)
    {
        Directions[cellPos.X, cellPos.Y] = direction;
    }

    public bool IsSectorCalculated(Vector2I sectorPos)
    {
        return _calculatedSectors.Contains(sectorPos);
    }

    public void AddCalculatedSector(Vector2I sectorPos)
    {
        _calculatedSectors.Add(sectorPos);
    }

    public bool TryGetNearestDirectedCell(
        Vector2I origin,
        int maxSearchRadius,
        out Vector2I directedCell)
    {
        for (int radius = 1; radius <= maxSearchRadius; radius++)
        {
            for (int yOffset = -radius; yOffset <= radius; yOffset++)
            {
                for (int xOffset = -radius; xOffset <= radius; xOffset++)
                {
                    if (Mathf.Abs(xOffset) != radius && Mathf.Abs(yOffset) != radius)
                        continue;

                    Vector2I candidate = origin + new Vector2I(xOffset, yOffset);
                    if (GetDirection(candidate) != Vector2I.Zero)
                    {
                        directedCell = candidate;
                        return true;
                    }
                }
            }
        }

        directedCell = default;
        return false;
    }
}