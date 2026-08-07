using System;
using System.Collections.Generic;
using Godot;
using static DeterministicMath;
using FixedVector = DeterministicMath.FixedVector;

public readonly struct AvoidanceAgentSnapshot
{
    public int UnitID { get; }
    public int MoveGroupID { get; }
    public Vector2I Position { get; }
    public Vector2I Velocity { get; }
    public int Radius { get; }
    public int Priority { get; }
    public bool IsMoving { get; }

    public AvoidanceAgentSnapshot(
        int unitID,
        int moveGroupID,
        Vector2I position,
        Vector2I velocity,
        int radius,
        int priority,
        bool isMoving)
    {
        UnitID = unitID;
        MoveGroupID = moveGroupID;
        Position = position;
        Velocity = velocity;
        Radius = radius;
        Priority = priority;
        IsMoving = isMoving;
    }
}

public static class Avoidance
{
    private const long Epsilon = FixedScale / 100;
    private const long LowSpeedRatio = FixedScale / 4;
    private const long PassingBias = FixedScale / 2;
    private const long SameGroupRadiusScale = FixedScale * 4 / 5;

    private readonly struct AvoidanceLine
    {
        public FixedVector Point { get; }
        public FixedVector Direction { get; }

        public AvoidanceLine(FixedVector point, FixedVector direction)
        {
            Point = point;
            Direction = direction;
        }
    }

    public static Vector2I Solve(
        AvoidanceAgentSnapshot agent,
        IReadOnlyList<AvoidanceAgentSnapshot> neighbors,
        Vector2I preferredVelocity,
        int maxSpeed,
        int timeHorizon,
        int groupTimeHorizon)
    {
        FixedVector agentPosition = FixedVector.FromVector2I(agent.Position);
        FixedVector agentVelocity = FixedVector.FromVector2I(agent.Velocity);
        var lines = new List<AvoidanceLine>(neighbors.Count);

        foreach (AvoidanceAgentSnapshot neighbor in neighbors)
        {
            if (agent.IsMoving && !neighbor.IsMoving)
                continue;

            FixedVector relativePosition =
                FixedVector.FromVector2I(neighbor.Position) - agentPosition;
            FixedVector relativeVelocity =
                agentVelocity - FixedVector.FromVector2I(neighbor.Velocity);
            long distanceSquared = LengthSquared(relativePosition);
            bool isSameMoveGroup =
                agent.MoveGroupID != -1 &&
                agent.MoveGroupID == neighbor.MoveGroupID;
            long radiusScale = isSameMoveGroup ? SameGroupRadiusScale : FixedScale;
            long combinedRadius = Multiply(
                ToFixed(agent.Radius + neighbor.Radius),
                radiusScale
            );
            long combinedRadiusSquared = Multiply(combinedRadius, combinedRadius);
            int activeTimeHorizon = isSameMoveGroup
                ? Math.Min(timeHorizon, groupTimeHorizon)
                : timeHorizon;
            long inverseTimeHorizon = Divide(
                FixedScale,
                ToFixed(Math.Max(activeTimeHorizon, 1))
            );

            FixedVector lineDirection;
            FixedVector correction;

            if (distanceSquared > combinedRadiusSquared)
            {
                FixedVector offsetVelocity = relativeVelocity -
                    Multiply(relativePosition, inverseTimeHorizon);
                long offsetLengthSquared = LengthSquared(offsetVelocity);
                long projection = Dot(offsetVelocity, relativePosition);

                if (projection < 0 &&
                    (Int128)projection * projection >
                    (Int128)combinedRadiusSquared * offsetLengthSquared)
                {
                    long offsetLength = SqrtFixed(offsetLengthSquared);
                    FixedVector unitOffset = NormalizeOrFallback(
                        offsetVelocity,
                        agent.UnitID,
                        neighbor.UnitID
                    );
                    lineDirection = new FixedVector(unitOffset.Y, -unitOffset.X);
                    correction = Multiply(
                        unitOffset,
                        Multiply(combinedRadius, inverseTimeHorizon) - offsetLength
                    );
                }
                else
                {
                    long leg = SqrtFixed(Math.Max(0, distanceSquared - combinedRadiusSquared));

                    if (Determinant(relativePosition, offsetVelocity) > 0)
                    {
                        lineDirection = new FixedVector(
                            Divide(
                                Multiply(relativePosition.X, leg) -
                                Multiply(relativePosition.Y, combinedRadius),
                                distanceSquared
                            ),
                            Divide(
                                Multiply(relativePosition.X, combinedRadius) +
                                Multiply(relativePosition.Y, leg),
                                distanceSquared
                            )
                        );
                    }
                    else
                    {
                        lineDirection = -new FixedVector(
                            Divide(
                                Multiply(relativePosition.X, leg) +
                                Multiply(relativePosition.Y, combinedRadius),
                                distanceSquared
                            ),
                            Divide(
                                -Multiply(relativePosition.X, combinedRadius) +
                                Multiply(relativePosition.Y, leg),
                                distanceSquared
                            )
                        );
                    }

                    long velocityProjection = Dot(relativeVelocity, lineDirection);
                    correction = Multiply(lineDirection, velocityProjection) - relativeVelocity;
                }
            }
            else
            {
                FixedVector offsetVelocity = relativeVelocity - relativePosition;
                long offsetLength = SqrtFixed(LengthSquared(offsetVelocity));
                FixedVector unitOffset = NormalizeOrFallback(
                    offsetVelocity,
                    agent.UnitID,
                    neighbor.UnitID
                );
                lineDirection = new FixedVector(unitOffset.Y, -unitOffset.X);
                correction = Multiply(unitOffset, combinedRadius - offsetLength);
            }

            long responsibility = GetResponsibility(agent, neighbor);
            lines.Add(new AvoidanceLine(
                agentVelocity + Multiply(correction, responsibility),
                lineDirection
            ));
        }

        long fixedMaxSpeed = ToFixed(Math.Max(maxSpeed, 0));
        FixedVector fixedPreferredVelocity = FixedVector.FromVector2I(preferredVelocity);
        int failedLine = LinearProgram2(
            lines,
            fixedMaxSpeed,
            fixedPreferredVelocity,
            false,
            out FixedVector result
        );
        if (failedLine < lines.Count)
            LinearProgram3(lines, failedLine, fixedMaxSpeed, ref result);

        long preferredSpeedSquared = LengthSquared(fixedPreferredVelocity);
        long lowSpeedThresholdSquared = Multiply(
            preferredSpeedSquared,
            Multiply(LowSpeedRatio, LowSpeedRatio)
        );

        if (preferredSpeedSquared > Multiply(Epsilon, Epsilon) &&
            LengthSquared(result) < lowSpeedThresholdSquared)
        {
            FixedVector passingOffset = new FixedVector(
                -fixedPreferredVelocity.Y,
                fixedPreferredVelocity.X
            );
            FixedVector passingDirection = Normalize(
                fixedPreferredVelocity + Multiply(passingOffset, PassingBias)
            );
            FixedVector passingPreferredVelocity = Multiply(
                passingDirection,
                Math.Min(fixedMaxSpeed, SqrtFixed(preferredSpeedSquared))
            );

            int passingFailedLine = LinearProgram2(
                lines,
                fixedMaxSpeed,
                passingPreferredVelocity,
                false,
                out FixedVector passingResult
            );
            if (passingFailedLine < lines.Count)
                LinearProgram3(lines, passingFailedLine, fixedMaxSpeed, ref passingResult);

            bool improvesSpeed = LengthSquared(passingResult) > LengthSquared(result);
            bool preservesProgress =
                Dot(passingResult, fixedPreferredVelocity) >=
                Dot(result, fixedPreferredVelocity);

            if (improvesSpeed && preservesProgress)
                result = passingResult;
        }

        return ClampMagnitude(result.ToVector2I(), maxSpeed);
    }

    private static long GetResponsibility(
        AvoidanceAgentSnapshot agent,
        AvoidanceAgentSnapshot neighbor)
    {
        if (!agent.IsMoving && neighbor.IsMoving)
            return FixedScale;

        int safePriority = Math.Max(agent.Priority, 1);
        int safeNeighborPriority = Math.Max(neighbor.Priority, 1);
        return Divide(
            ToFixed(safeNeighborPriority),
            ToFixed(safePriority + safeNeighborPriority)
        );
    }

    private static FixedVector NormalizeOrFallback(
        FixedVector value,
        int unitID,
        int neighborID)
    {
        if (LengthSquared(value) > Multiply(Epsilon, Epsilon))
            return Normalize(value);

        return unitID < neighborID
            ? new FixedVector(-FixedScale, 0)
            : new FixedVector(FixedScale, 0);
    }

    private static bool LinearProgram1(
        IReadOnlyList<AvoidanceLine> lines,
        int lineIndex,
        long radius,
        FixedVector optimalVelocity,
        bool directionOptimal,
        ref FixedVector result)
    {
        AvoidanceLine line = lines[lineIndex];
        long dotProduct = Dot(line.Point, line.Direction);
        long discriminant =
            Multiply(dotProduct, dotProduct) +
            Multiply(radius, radius) -
            LengthSquared(line.Point);

        if (discriminant < 0)
            return false;

        long squareRoot = SqrtFixed(discriminant);
        long left = -dotProduct - squareRoot;
        long right = -dotProduct + squareRoot;

        for (int index = 0; index < lineIndex; index++)
        {
            long denominator = Determinant(line.Direction, lines[index].Direction);
            long numerator = Determinant(
                lines[index].Direction,
                line.Point - lines[index].Point
            );

            if (Math.Abs(denominator) <= Epsilon)
            {
                if (numerator < 0)
                    return false;

                continue;
            }

            long intersection = Divide(numerator, denominator);
            if (denominator >= 0)
                right = Math.Min(right, intersection);
            else
                left = Math.Max(left, intersection);

            if (left > right)
                return false;
        }

        if (directionOptimal)
        {
            result = Dot(optimalVelocity, line.Direction) > 0
                ? line.Point + Multiply(line.Direction, right)
                : line.Point + Multiply(line.Direction, left);
        }
        else
        {
            long projection = Dot(line.Direction, optimalVelocity - line.Point);
            result = line.Point + Multiply(
                line.Direction,
                Math.Clamp(projection, left, right)
            );
        }

        return true;
    }

    private static int LinearProgram2(
        IReadOnlyList<AvoidanceLine> lines,
        long radius,
        FixedVector optimalVelocity,
        bool directionOptimal,
        out FixedVector result)
    {
        if (directionOptimal)
        {
            result = Multiply(optimalVelocity, radius);
        }
        else if (LengthSquared(optimalVelocity) > Multiply(radius, radius))
        {
            result = Multiply(Normalize(optimalVelocity), radius);
        }
        else
        {
            result = optimalVelocity;
        }

        for (int index = 0; index < lines.Count; index++)
        {
            if (Determinant(lines[index].Direction, lines[index].Point - result) <= 0)
                continue;

            FixedVector previousResult = result;
            if (!LinearProgram1(
                lines,
                index,
                radius,
                optimalVelocity,
                directionOptimal,
                ref result))
            {
                result = previousResult;
                return index;
            }
        }

        return lines.Count;
    }

    private static void LinearProgram3(
        IReadOnlyList<AvoidanceLine> lines,
        int beginLine,
        long radius,
        ref FixedVector result)
    {
        long distance = 0;

        for (int lineIndex = beginLine; lineIndex < lines.Count; lineIndex++)
        {
            long violation = Determinant(
                lines[lineIndex].Direction,
                lines[lineIndex].Point - result
            );
            if (violation <= distance)
                continue;

            var projectedLines = new List<AvoidanceLine>(lineIndex);
            for (int previousIndex = 0; previousIndex < lineIndex; previousIndex++)
            {
                long determinant = Determinant(
                    lines[lineIndex].Direction,
                    lines[previousIndex].Direction
                );
                FixedVector point;

                if (Math.Abs(determinant) <= Epsilon)
                {
                    if (Dot(
                        lines[lineIndex].Direction,
                        lines[previousIndex].Direction) > 0)
                    {
                        continue;
                    }

                    point = new FixedVector(
                        (lines[lineIndex].Point.X + lines[previousIndex].Point.X) / 2,
                        (lines[lineIndex].Point.Y + lines[previousIndex].Point.Y) / 2
                    );
                }
                else
                {
                    point = lines[lineIndex].Point + Multiply(
                        lines[lineIndex].Direction,
                        Divide(
                            Determinant(
                                lines[previousIndex].Direction,
                                lines[lineIndex].Point - lines[previousIndex].Point
                            ),
                            determinant
                        )
                    );
                }

                FixedVector direction = Normalize(
                    lines[previousIndex].Direction - lines[lineIndex].Direction
                );
                projectedLines.Add(new AvoidanceLine(point, direction));
            }

            FixedVector previousResult = result;
            FixedVector optimalDirection = new FixedVector(
                -lines[lineIndex].Direction.Y,
                lines[lineIndex].Direction.X
            );

            if (LinearProgram2(
                projectedLines,
                radius,
                optimalDirection,
                true,
                out result) < projectedLines.Count)
            {
                result = previousResult;
            }

            distance = Determinant(
                lines[lineIndex].Direction,
                lines[lineIndex].Point - result
            );
        }
    }

}