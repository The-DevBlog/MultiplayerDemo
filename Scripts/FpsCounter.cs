using Godot;

public partial class FpsCounter : Label
{
    public override void _Ready()
    {
        UpdateText();
    }

    public override void _Process(double delta)
    {
        UpdateText();
    }

    private void UpdateText()
    {
        Text = $"FPS: {Engine.GetFramesPerSecond():0}";
    }
}