using Godot;

public partial class Lobby : Control
{
	private VBoxContainer _playerListCtr;
	private Label _playerLabel;
	private PlayerManager _playerManager;
	private Signals _signals;

	public override void _Ready()
	{
		_playerManager = PlayerManager.Instance;
		_signals = Signals.Instance;

		_playerListCtr = GetNode<VBoxContainer>("%PlayerListCtr");
		_playerLabel = GetNode<Label>("%PlayerLabel");

		if (_playerListCtr == null)
		{
			GD.PushError("PlayerListCtr could not be found");
		}

		if (_playerLabel == null)
		{
			GD.PushError("PlayerLabel could not be found");
		}

		RefreshLobby();

		_signals.UpdateLobby += RefreshLobby;
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

			string name = player.Value.PlayerName;
			int team = player.Value.Team;
			playerLabel.Text = $"{name} | Team {team}";

			_playerListCtr.AddChild(playerLabel);
		}
	}
}
