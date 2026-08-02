using System;
using System.Collections.Generic;
using Godot;

public readonly struct OrcaAgentSnapshot
{
	public int UnitID { get; }
	public int MoveGroupID { get; }
	public Vector2 Position { get; }
	public Vector2 Velocity { get; }
	public float Radius { get; }
	public float Priority { get; }

	public OrcaAgentSnapshot(
		int unitID,
		int moveGroupID,
		Vector2 position,
		Vector2 velocity,
		float radius,
		float priority)
	{
		UnitID = unitID;
		MoveGroupID = moveGroupID;
		Position = position;
		Velocity = velocity;
		Radius = radius;
		Priority = priority;
	}
}

public static class OrcaAvoidanceSolver
{
	private const float Epsilon = 0.00001f;
	private const float LowSpeedRatio = 0.25f;
	private const float PassingBias = 0.5f;
	private const float SameGroupRadiusScale = 0.8f;

	private readonly struct OrcaLine
	{
		public Vector2 Point { get; }
		public Vector2 Direction { get; }

		public OrcaLine(Vector2 point, Vector2 direction)
		{
			Point = point;
			Direction = direction;
		}
	}

	public static Vector2 Solve(
		OrcaAgentSnapshot agent,
		IReadOnlyList<OrcaAgentSnapshot> neighbors,
		Vector2 preferredVelocity,
		float maxSpeed,
		float timeHorizon,
		float groupTimeHorizon)
	{
		var lines = new List<OrcaLine>(neighbors.Count);

		foreach (OrcaAgentSnapshot neighbor in neighbors)
		{
			Vector2 relativePosition = neighbor.Position - agent.Position;
			Vector2 relativeVelocity = agent.Velocity - neighbor.Velocity;
			float distanceSquared = relativePosition.LengthSquared();
			bool isSameMoveGroup =
				agent.MoveGroupID != -1 &&
				agent.MoveGroupID == neighbor.MoveGroupID;
			float radiusScale = isSameMoveGroup ? SameGroupRadiusScale : 1.0f;
			float combinedRadius = (agent.Radius + neighbor.Radius) * radiusScale;
			float combinedRadiusSquared = combinedRadius * combinedRadius;
			float activeTimeHorizon = isSameMoveGroup
				? Math.Min(timeHorizon, groupTimeHorizon)
				: timeHorizon;

			Vector2 lineDirection;
			Vector2 correction;

			if (distanceSquared > combinedRadiusSquared)
			{
				float inverseTimeHorizon = 1.0f / Math.Max(activeTimeHorizon, Epsilon);
				Vector2 offsetVelocity = relativeVelocity - inverseTimeHorizon * relativePosition;
				float offsetLengthSquared = offsetVelocity.LengthSquared();
				float projection = offsetVelocity.Dot(relativePosition);

				if (projection < 0.0f &&
					projection * projection > combinedRadiusSquared * offsetLengthSquared)
				{
					float offsetLength = Mathf.Sqrt(offsetLengthSquared);
					Vector2 unitOffset = NormalizeOrFallback(offsetVelocity, agent.UnitID, neighbor.UnitID);
					lineDirection = new Vector2(unitOffset.Y, -unitOffset.X);
					correction = (combinedRadius * inverseTimeHorizon - offsetLength) * unitOffset;
				}
				else
				{
					float leg = Mathf.Sqrt(Math.Max(0.0f, distanceSquared - combinedRadiusSquared));

					if (Determinant(relativePosition, offsetVelocity) > 0.0f)
					{
						lineDirection = new Vector2(
							relativePosition.X * leg - relativePosition.Y * combinedRadius,
							relativePosition.X * combinedRadius + relativePosition.Y * leg
						) / distanceSquared;
					}
					else
					{
						lineDirection = -new Vector2(
							relativePosition.X * leg + relativePosition.Y * combinedRadius,
							-relativePosition.X * combinedRadius + relativePosition.Y * leg
						) / distanceSquared;
					}

					float velocityProjection = relativeVelocity.Dot(lineDirection);
					correction = velocityProjection * lineDirection - relativeVelocity;
				}
			}
			else
			{
				Vector2 offsetVelocity = relativeVelocity - relativePosition;
				float offsetLength = offsetVelocity.Length();
				Vector2 unitOffset = NormalizeOrFallback(offsetVelocity, agent.UnitID, neighbor.UnitID);
				lineDirection = new Vector2(unitOffset.Y, -unitOffset.X);
				correction = (combinedRadius - offsetLength) * unitOffset;
			}

			float responsibility = GetResponsibility(agent.Priority, neighbor.Priority);
			lines.Add(new OrcaLine(
				agent.Velocity + responsibility * correction,
				lineDirection
			));
		}

		int failedLine = LinearProgram2(lines, maxSpeed, preferredVelocity, false, out Vector2 result);
		if (failedLine < lines.Count)
			LinearProgram3(lines, failedLine, maxSpeed, ref result);

		float preferredSpeedSquared = preferredVelocity.LengthSquared();
		float lowSpeedThresholdSquared =
			preferredSpeedSquared * LowSpeedRatio * LowSpeedRatio;

		if (preferredSpeedSquared > Epsilon * Epsilon &&
			result.LengthSquared() < lowSpeedThresholdSquared)
		{
			Vector2 passingOffset = new Vector2(-preferredVelocity.Y, preferredVelocity.X);
			Vector2 passingPreferredVelocity =
				(preferredVelocity + PassingBias * passingOffset).Normalized() *
				Math.Min(maxSpeed, Mathf.Sqrt(preferredSpeedSquared));

			int passingFailedLine = LinearProgram2(
				lines,
				maxSpeed,
				passingPreferredVelocity,
				false,
				out Vector2 passingResult
			);
			if (passingFailedLine < lines.Count)
				LinearProgram3(lines, passingFailedLine, maxSpeed, ref passingResult);

			bool improvesSpeed = passingResult.LengthSquared() > result.LengthSquared();
			bool preservesProgress =
				passingResult.Dot(preferredVelocity) + Epsilon >=
				result.Dot(preferredVelocity);

			if (improvesSpeed && preservesProgress)
				result = passingResult;
		}

		return result;
	}

	private static float GetResponsibility(float priority, float neighborPriority)
	{
		float safePriority = Math.Max(priority, Epsilon);
		float safeNeighborPriority = Math.Max(neighborPriority, Epsilon);
		return safeNeighborPriority / (safePriority + safeNeighborPriority);
	}

	private static Vector2 NormalizeOrFallback(Vector2 value, int unitID, int neighborID)
	{
		float lengthSquared = value.LengthSquared();
		if (lengthSquared > Epsilon * Epsilon)
			return value / Mathf.Sqrt(lengthSquared);

		return unitID < neighborID ? Vector2.Left : Vector2.Right;
	}

	private static bool LinearProgram1(
		IReadOnlyList<OrcaLine> lines,
		int lineIndex,
		float radius,
		Vector2 optimalVelocity,
		bool directionOptimal,
		ref Vector2 result)
	{
		OrcaLine line = lines[lineIndex];
		float dotProduct = line.Point.Dot(line.Direction);
		float discriminant = dotProduct * dotProduct + radius * radius - line.Point.LengthSquared();

		if (discriminant < 0.0f)
			return false;

		float squareRoot = Mathf.Sqrt(discriminant);
		float left = -dotProduct - squareRoot;
		float right = -dotProduct + squareRoot;

		for (int index = 0; index < lineIndex; index++)
		{
			float denominator = Determinant(line.Direction, lines[index].Direction);
			float numerator = Determinant(lines[index].Direction, line.Point - lines[index].Point);

			if (Mathf.Abs(denominator) <= Epsilon)
			{
				if (numerator < 0.0f)
					return false;

				continue;
			}

			float intersection = numerator / denominator;
			if (denominator >= 0.0f)
				right = Math.Min(right, intersection);
			else
				left = Math.Max(left, intersection);

			if (left > right)
				return false;
		}

		if (directionOptimal)
		{
			result = optimalVelocity.Dot(line.Direction) > 0.0f
				? line.Point + right * line.Direction
				: line.Point + left * line.Direction;
		}
		else
		{
			float projection = line.Direction.Dot(optimalVelocity - line.Point);
			result = line.Point + Mathf.Clamp(projection, left, right) * line.Direction;
		}

		return true;
	}

	private static int LinearProgram2(
		IReadOnlyList<OrcaLine> lines,
		float radius,
		Vector2 optimalVelocity,
		bool directionOptimal,
		out Vector2 result)
	{
		if (directionOptimal)
		{
			result = optimalVelocity * radius;
		}
		else if (optimalVelocity.LengthSquared() > radius * radius)
		{
			result = optimalVelocity.Normalized() * radius;
		}
		else
		{
			result = optimalVelocity;
		}

		for (int index = 0; index < lines.Count; index++)
		{
			if (Determinant(lines[index].Direction, lines[index].Point - result) <= 0.0f)
				continue;

			Vector2 previousResult = result;
			if (!LinearProgram1(lines, index, radius, optimalVelocity, directionOptimal, ref result))
			{
				result = previousResult;
				return index;
			}
		}

		return lines.Count;
	}

	private static void LinearProgram3(
		IReadOnlyList<OrcaLine> lines,
		int beginLine,
		float radius,
		ref Vector2 result)
	{
		float distance = 0.0f;

		for (int lineIndex = beginLine; lineIndex < lines.Count; lineIndex++)
		{
			float violation = Determinant(
				lines[lineIndex].Direction,
				lines[lineIndex].Point - result
			);
			if (violation <= distance)
				continue;

			var projectedLines = new List<OrcaLine>(lineIndex);
			for (int previousIndex = 0; previousIndex < lineIndex; previousIndex++)
			{
				float determinant = Determinant(
					lines[lineIndex].Direction,
					lines[previousIndex].Direction
				);
				Vector2 point;

				if (Mathf.Abs(determinant) <= Epsilon)
				{
					if (lines[lineIndex].Direction.Dot(lines[previousIndex].Direction) > 0.0f)
						continue;

					point = (lines[lineIndex].Point + lines[previousIndex].Point) * 0.5f;
				}
				else
				{
					point = lines[lineIndex].Point +
						Determinant(
							lines[previousIndex].Direction,
							lines[lineIndex].Point - lines[previousIndex].Point
						) / determinant * lines[lineIndex].Direction;
				}

				Vector2 direction = (lines[previousIndex].Direction - lines[lineIndex].Direction).Normalized();
				projectedLines.Add(new OrcaLine(point, direction));
			}

			Vector2 previousResult = result;
			Vector2 optimalDirection = new Vector2(
				-lines[lineIndex].Direction.Y,
				lines[lineIndex].Direction.X
			);

			if (LinearProgram2(projectedLines, radius, optimalDirection, true, out result) < projectedLines.Count)
				result = previousResult;

			distance = Determinant(
				lines[lineIndex].Direction,
				lines[lineIndex].Point - result
			);
		}
	}

	private static float Determinant(Vector2 first, Vector2 second)
	{
		return first.X * second.Y - first.Y * second.X;
	}
}
