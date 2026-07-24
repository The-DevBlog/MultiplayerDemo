using Godot;

public partial class Lobby : Control
{
	private VBoxContainer _playerListCtr;
	private Label _playerLabel;
	private Button _playBtn;
	private PlayerManager _playerManager;
	private Signals _signals;

	public override void _Ready()
	{
		_playerManager = PlayerManager.Instance;
		_signals = Signals.Instance;

		_playerListCtr = GetNode<VBoxContainer>("%PlayerListCtr");
		_playerLabel = GetNode<Label>("%PlayerLabel");
		_playBtn = GetNode<Button>("%PlayBtn");

		if (_playerListCtr == null)
			GD.PushError("PlayerListCtr could not be found");

		if (_playerLabel == null)
			GD.PushError("PlayerLabel could not be found");

		if (_playBtn == null)
			GD.PushError("PlayBtn could not be found");

		RefreshLobby();

		if (!Multiplayer.IsServer())
			_playBtn.Visible = false;

		_signals.UpdateLobby += RefreshLobby;
		_playBtn.Pressed += OnPlayBtnPressed;
	}

	private void RefreshLobby()
	{
		foreach (var node in _playerListCtr.GetChildren())
		{
			if (node == _playerLabel)
				continue;

			node.QueueFree();
		}

		foreach (var player in _playerManager.ConnectedPlayers)
		{
			Label playerLabel = _playerLabel.Duplicate() as Label;
			playerLabel.Visible = true;

			playerLabel.Text = $"PeerID: {player.Value.PeerID}";

			_playerListCtr.AddChild(playerLabel);
		}
	}

	private void OnPlayBtnPressed()
	{
		Rpc(nameof(StartGame));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void StartGame()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Play.tscn");
	}
}
