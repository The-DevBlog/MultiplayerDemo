using System.Collections.Generic;
using Godot;

public class NavSector
{
    public Vector2I Position { get; set; }
    public Vector2I MinCell { get; set; }
    public Vector2I MaxCell { get; set; }
    public List<NavPortal> Portals { get; set; }

    public NavSector(Vector2I position, Vector2I minCell, Vector2I maxCell)
    {
        Position = position;
        MinCell = minCell;
        MaxCell = maxCell;
        Portals = new List<NavPortal>();
    }
}