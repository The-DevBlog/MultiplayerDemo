using Godot;

public partial class CommandUI : Control
{
	private Button _playerBtn;
	private VBoxContainer _playerBtnCtr;
	private PlayerManager _playerManager;

	public override void _Ready()
	{
		_playerBtn = GetNode<Button>("%PlayerBtn");
		_playerBtnCtr = GetNode<VBoxContainer>("%PlayerBtnCtr");
		_playerManager = PlayerManager.Instance;

		if (_playerBtn == null)
			GD.PrintErr("Could not find PlayerBtn");

		if (_playerBtnCtr == null)
			GD.PrintErr("Could not find PlayerBtnCtr");

		InitPlayerBtns();
	}

	private void InitPlayerBtns()
	{
		foreach (Player player in _playerManager.ConnectedPlayers.Values)
		{
			Button newBtn = _playerBtn.Duplicate() as Button;
			newBtn.Visible = true;
			newBtn.Text = player.PeerID.ToString();

			_playerBtnCtr.AddChild(newBtn);
		}
	}
}
