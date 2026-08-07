using Godot;

AssertEqual(0, DeterministicMath.Atan2MilliDegrees(0, 1), "positive X axis");
AssertEqual(90000, DeterministicMath.Atan2MilliDegrees(1, 0), "positive Y axis");
AssertEqual(180000, DeterministicMath.Atan2MilliDegrees(0, -1), "negative X axis");
AssertEqual(-90000, DeterministicMath.Atan2MilliDegrees(-1, 0), "negative Y axis");
AssertEqual(45000, DeterministicMath.Atan2MilliDegrees(1, 1), "first diagonal");
AssertEqual(135000, DeterministicMath.Atan2MilliDegrees(1, -1), "second diagonal");
AssertEqual(-135000, DeterministicMath.Atan2MilliDegrees(-1, -1), "third diagonal");
AssertEqual(-45000, DeterministicMath.Atan2MilliDegrees(-1, 1), "fourth diagonal");
AssertEqual(0, DeterministicMath.GetMovementRotation(new Vector2I(0, -1)), "forward movement rotation");
AssertEqual(270000, DeterministicMath.GetMovementRotation(new Vector2I(1, 0)), "right movement rotation");

AssertEqual(new Vector2I(600, 800), DeterministicMath.Normalize(new Vector2I(300, 400)), "integer normalization");
AssertEqual(25L, DeterministicMath.LengthSquared(new Vector2I(3, 4)), "integer vector length squared");
AssertEqual(
    new Vector2I(3, 4),
    DeterministicMath.MoveTowards(Vector2I.Zero, new Vector2I(6, 8), 5),
    "integer vector move towards"
);
AssertEqual(unchecked(17 * 31 + 42), DeterministicMath.CombineHash(17, 42), "hash combination");

var agent = new AvoidanceAgentSnapshot(
    1,
    -1,
    Vector2I.Zero,
    Vector2I.Zero,
    500,
    100,
    true
);

Vector2I unclutteredVelocity = Avoidance.Solve(
    agent,
    Array.Empty<AvoidanceAgentSnapshot>(),
    new Vector2I(600, 800),
    500,
    8,
    2
);
AssertEqual(new Vector2I(300, 400), unclutteredVelocity, "fixed avoidance speed clamp");

var headOnNeighbors = new List<AvoidanceAgentSnapshot>
{
    new(
        2,
        -1,
        new Vector2I(1500, 0),
        new Vector2I(-100, 0),
        500,
        100,
        true
    )
};

Vector2I expectedHeadOnVelocity = Avoidance.Solve(
    agent,
    headOnNeighbors,
    new Vector2I(500, 0),
    500,
    8,
    2
);
AssertMagnitudeAtMost(expectedHeadOnVelocity, 500, "head-on speed clamp");

for (int iteration = 0; iteration < 1000; iteration++)
{
    Vector2I actualVelocity = Avoidance.Solve(
        agent,
        headOnNeighbors,
        new Vector2I(500, 0),
        500,
        8,
        2
    );
    AssertEqual(expectedHeadOnVelocity, actualVelocity, "repeatable head-on avoidance");
}

var overlappingNeighbors = new List<AvoidanceAgentSnapshot>
{
    new(2, -1, Vector2I.Zero, Vector2I.Zero, 500, 100, true)
};
Vector2I overlappingVelocity = Avoidance.Solve(
    agent,
    overlappingNeighbors,
    new Vector2I(500, 0),
    500,
    8,
    2
);
AssertMagnitudeAtMost(overlappingVelocity, 500, "overlap fallback speed clamp");

Console.WriteLine(
    $"Determinism harness passed. Head-on={expectedHeadOnVelocity}, overlap={overlappingVelocity}"
);

static void AssertEqual<T>(T expected, T actual, string scenario)
    where T : IEquatable<T>
{
    if (!actual.Equals(expected))
        throw new InvalidOperationException(
            $"{scenario}: expected {expected}, received {actual}."
        );
}

static void AssertMagnitudeAtMost(Vector2I value, int maxLength, string scenario)
{
    long lengthSquared = (long)value.X * value.X + (long)value.Y * value.Y;
    long maxLengthSquared = (long)maxLength * maxLength;
    if (lengthSquared > maxLengthSquared)
        throw new InvalidOperationException(
            $"{scenario}: {value} exceeds magnitude {maxLength}."
        );
}