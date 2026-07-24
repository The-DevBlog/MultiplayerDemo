using Godot;

public partial class Menu : Control
{
	private Button HostBtn;
	private Button JoinBtn;
	private NetworkManager _networkManager;
	private Signals _signals;

	public override void _Ready()
	{
		_networkManager = NetworkManager.Instance;
		_signals = Signals.Instance;
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
		Multiplayer.ConnectedToServer += OnClientConnected;
	}

	public override void _ExitTree()
	{
		HostBtn.Pressed -= OnHostBtnPressed;
		JoinBtn.Pressed -= OnJoinBtnPressed;
		Multiplayer.ConnectedToServer -= OnClientConnected;
	}

	private void OnHostBtnPressed()
	{
		bool host = _networkManager.Host();

		// // hosting succeeded
		if (host)
		{
			GetTree().ChangeSceneToFile("res://Scenes/Lobby.tscn");
		}
	}

	private void OnJoinBtnPressed()
	{
		_networkManager.Join();
	}

	// Listens to ConnectedToServer
	private void OnClientConnected()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Lobby.tscn");
	}
}
