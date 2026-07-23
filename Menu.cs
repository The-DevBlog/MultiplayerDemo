using Godot;

public partial class Menu : Control
{
	private Button HostBtn;
	private Button JoinBtn;

	public override void _Ready()
	{
		HostBtn = GetNode<Button>("%HostBtn");
		JoinBtn = GetNode<Button>("%JoinBtn");

		if (HostBtn == null)
		{
			GD.PushError("HostBtn not found");
		}

		if (JoinBtn == null)
		{
			GD.PushError("JoinBtn not found");
		}

		HostBtn.Pressed += OnHostBtnPressed;
		JoinBtn.Pressed += OnJoinBtnPressed;
	}

	public override void _ExitTree()
	{
		HostBtn.Pressed -= OnHostBtnPressed;
		JoinBtn.Pressed -= OnJoinBtnPressed;
	}

	private void OnHostBtnPressed()
	{
		GetTree().ChangeSceneToFile("res://Play.tscn");
	}

	private void OnJoinBtnPressed()
	{
		GetTree().ChangeSceneToFile("res://Play.tscn");
	}
}
