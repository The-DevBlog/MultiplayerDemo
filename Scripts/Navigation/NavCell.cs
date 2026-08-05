using Godot;

public class NavCell
{
    public Vector2I Position { get; set; }
    public bool Walkable { get; set; }
    public int Cost { get; set; }
    public int IntegrationCost { get; set; }
    public Vector2I Direction { get; set; }
    public ushort SlopeTenths { get; set; } // eg: 0 = 0deg, 250 = 25deg, 427 = 42.7deg
    public const int MaxIntegrationCost = 999999;

    public NavCell(Vector2I position)
    {
        Position = position;
        Walkable = true;
        Cost = 1;
        IntegrationCost = MaxIntegrationCost;
    }

    public void ResetIntegrationCost()
    {
        IntegrationCost = MaxIntegrationCost;
    }
}