using Godot;

public class NavFlowField
{
    public Vector2I TargetCell { get; }
    public Vector2I[,] Directions { get; }

    public NavFlowField(Vector2I targetCell, Vector2I[,] directions)
    {
        TargetCell = targetCell;
        Directions = directions;
    }

    public Vector2I GetDirection(Vector2I cellPos)
    {
        if (cellPos.X < 0 || cellPos.Y < 0)
            return Vector2I.Zero;

        if (cellPos.X >= Directions.GetLength(0) || cellPos.Y >= Directions.GetLength(1))
            return Vector2I.Zero;

        return Directions[cellPos.X, cellPos.Y];
    }
}