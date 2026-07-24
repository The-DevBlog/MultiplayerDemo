using System.Net.Http;
using Godot;

public partial class Signals : Node
{
	public static Signals Instance;

	// [Signal] public delegate void ClientConnectedEventHandler();
	[Signal] public delegate void UpdateLobbyEventHandler();

	public override void _EnterTree()
	{
		Instance = this;
	}

	public void EmitUpdateLobby()
	{
		EmitSignal(SignalName.UpdateLobby);
	}

	// public void EmitClientConnected()
	// {
	// 	EmitSignal(SignalName.ClientConnected);
	// }
}
