using Godot;

public readonly struct MoveCommand
{
	public const int PositionScale = 1000;

	private MoveCommand(long applyTick, int playerId, int[] unitIds, int targetX, int targetZ)
	{
		ApplyTick = applyTick;
		PlayerId = playerId;
		UnitIds = unitIds;
		TargetX = targetX;
		TargetZ = targetZ;
	}

	public long ApplyTick { get; }
	public int PlayerId { get; }
	public int[] UnitIds { get; }
	public int TargetX { get; }
	public int TargetZ { get; }

	public Vector3 TargetPosition => new(TargetX / (float)PositionScale, 0.0f, TargetZ / (float)PositionScale);

	public static MoveCommand Create(long applyTick, int playerId, int[] unitIds, Vector3 targetPosition)
	{
		return new MoveCommand(
			applyTick,
			playerId,
			unitIds,
			Quantize(targetPosition.X),
			Quantize(targetPosition.Z));
	}

	public static MoveCommand CreateQuantized(long applyTick, int playerId, int[] unitIds, int targetX, int targetZ)
	{
		return new MoveCommand(applyTick, playerId, unitIds, targetX, targetZ);
	}

	public static int Quantize(float value)
	{
		return Mathf.RoundToInt(value * PositionScale);
	}
}
