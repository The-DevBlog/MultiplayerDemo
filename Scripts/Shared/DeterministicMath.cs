using System;
using Godot;

public static class DeterministicMath
{
    public const int SimScale = 1000;
    public const int RotationScale = 1000;
    public const int FullRotation = 360 * RotationScale;
    public const int HalfRotation = FullRotation / 2;
    public const long FixedScale = 1L << 16;

    public readonly struct FixedVector
    {
        public long X { get; }
        public long Y { get; }

        public FixedVector(long x, long y)
        {
            X = x;
            Y = y;
        }

        public static FixedVector FromVector2I(Vector2I value)
        {
            return new FixedVector(value.X * FixedScale, value.Y * FixedScale);
        }

        public Vector2I ToVector2I()
        {
            return new Vector2I(
                RoundFixedToInt(X),
                RoundFixedToInt(Y)
            );
        }

        public static FixedVector operator +(FixedVector left, FixedVector right)
        {
            return new FixedVector(left.X + right.X, left.Y + right.Y);
        }

        public static FixedVector operator -(FixedVector left, FixedVector right)
        {
            return new FixedVector(left.X - right.X, left.Y - right.Y);
        }

        public static FixedVector operator -(FixedVector value)
        {
            return new FixedVector(-value.X, -value.Y);
        }
    }

    private const int CordicInputShift = 30;
    private static readonly int[] CordicAngles =
    [
        45000, 26565, 14036, 7125, 3576, 1790, 895, 448, 224,
        112, 56, 28, 14, 7, 3, 2, 1
    ];

    public static int Atan2MilliDegrees(int y, int x)
    {
        if (y == 0)
            return x < 0 ? HalfRotation : 0;

        if (x == 0)
            return y > 0 ? 90 * RotationScale : -90 * RotationScale;

        long cordicX = (long)x << CordicInputShift;
        long cordicY = (long)y << CordicInputShift;
        int angle = 0;

        if (cordicX < 0)
        {
            bool isUpperHalf = cordicY >= 0;
            cordicX = -cordicX;
            cordicY = -cordicY;
            angle = isUpperHalf ? HalfRotation : -HalfRotation;
        }

        for (int shift = 0; shift < CordicAngles.Length && cordicY != 0; shift++)
        {
            long previousX = cordicX;
            if (cordicY > 0)
            {
                cordicX += cordicY >> shift;
                cordicY -= previousX >> shift;
                angle += CordicAngles[shift];
            }
            else
            {
                cordicX -= cordicY >> shift;
                cordicY += previousX >> shift;
                angle -= CordicAngles[shift];
            }
        }

        return angle;
    }

    public static int NormalizeRotation(int rotation)
    {
        rotation %= FullRotation;
        return rotation < 0 ? rotation + FullRotation : rotation;
    }

    public static int GetShortestRotationDelta(int fromRotation, int toRotation)
    {
        int delta = NormalizeRotation(toRotation) - NormalizeRotation(fromRotation);
        if (delta > HalfRotation)
            delta -= FullRotation;
        else if (delta < -HalfRotation)
            delta += FullRotation;

        return delta;
    }

    public static int GetMovementRotation(Vector2I velocity)
    {
        return NormalizeRotation(
            Atan2MilliDegrees(-velocity.X, -velocity.Y)
        );
    }

    public static float SimRotationToRadians(int rotation)
    {
        return Mathf.DegToRad(rotation / (float)RotationScale);
    }

    public static Vector2 SimToFlatWorldPosition(Vector2I simPosition)
    {
        return new Vector2(
            simPosition.X / (float)SimScale,
            simPosition.Y / (float)SimScale
        );
    }

    public static int IntegerSqrt(long value)
    {
        if (value <= 0)
            return 0;

        long left = 1;
        long right = Math.Min(value, int.MaxValue);
        long result = 0;

        while (left <= right)
        {
            long middle = left + (right - left) / 2;

            if (middle <= value / middle)
            {
                result = middle;
                left = middle + 1;
            }
            else
            {
                right = middle - 1;
            }
        }

        return (int)result;
    }

    public static int IntegerSqrtCeiling(long value)
    {
        int root = IntegerSqrt(value);
        return (long)root * root < value ? root + 1 : root;
    }

    public static Vector2I Normalize(Vector2I value, int targetLength = SimScale)
    {
        long lengthSquared = LengthSquared(value);
        int length = IntegerSqrtCeiling(lengthSquared);
        if (length == 0)
            return Vector2I.Zero;

        return new Vector2I(
            (int)((long)value.X * targetLength / length),
            (int)((long)value.Y * targetLength / length)
        );
    }

    public static long LengthSquared(Vector2I value)
    {
        return (long)value.X * value.X + (long)value.Y * value.Y;
    }

    public static Vector2I MoveTowards(Vector2I current, Vector2I target, int maxDistance)
    {
        Vector2I toTarget = target - current;
        long distanceSquared = LengthSquared(toTarget);

        if (distanceSquared <= (long)maxDistance * maxDistance)
            return target;

        int distance = IntegerSqrtCeiling(distanceSquared);
        if (distance == 0)
            return target;

        return current + new Vector2I(
            (int)((long)toTarget.X * maxDistance / distance),
            (int)((long)toTarget.Y * maxDistance / distance)
        );
    }

    public static FixedVector Normalize(FixedVector value)
    {
        long length = SqrtFixed(LengthSquared(value));
        return length == 0
            ? new FixedVector(0, 0)
            : new FixedVector(Divide(value.X, length), Divide(value.Y, length));
    }

    public static FixedVector Multiply(FixedVector value, long scalar)
    {
        return new FixedVector(
            Multiply(value.X, scalar),
            Multiply(value.Y, scalar)
        );
    }

    public static long Dot(FixedVector first, FixedVector second)
    {
        return Multiply(first.X, second.X) + Multiply(first.Y, second.Y);
    }

    public static long Determinant(FixedVector first, FixedVector second)
    {
        return Multiply(first.X, second.Y) - Multiply(first.Y, second.X);
    }

    public static long LengthSquared(FixedVector value)
    {
        return Dot(value, value);
    }

    public static long ToFixed(int value)
    {
        return value * FixedScale;
    }

    public static long Multiply(long left, long right)
    {
        return ClampToLong((Int128)left * right / FixedScale);
    }

    public static long Divide(long numerator, long denominator)
    {
        if (denominator == 0)
            throw new DivideByZeroException();

        return ClampToLong((Int128)numerator * FixedScale / denominator);
    }

    public static long SqrtFixed(long value)
    {
        if (value <= 0)
            return 0;

        return IntegerSqrt((UInt128)value * (UInt128)FixedScale);
    }

    public static long IntegerSqrt(UInt128 value)
    {
        UInt128 result = 0;
        UInt128 bit = (UInt128)1 << 126;

        while (bit > value)
            bit >>= 2;

        while (bit != 0)
        {
            if (value >= result + bit)
            {
                value -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return result > (UInt128)long.MaxValue
            ? long.MaxValue
            : (long)result;
    }

    public static Vector2I ClampMagnitude(Vector2I value, int maxLength)
    {
        if (maxLength <= 0)
            return Vector2I.Zero;

        long lengthSquared =
            (long)value.X * value.X +
            (long)value.Y * value.Y;
        long maxLengthSquared = (long)maxLength * maxLength;
        if (lengthSquared <= maxLengthSquared)
            return value;

        int length = IntegerSqrtCeiling(lengthSquared);
        if (length == 0)
            return Vector2I.Zero;

        return new Vector2I(
            (int)((long)value.X * maxLength / length),
            (int)((long)value.Y * maxLength / length)
        );
    }

    public static int FloorDivide(long dividend, int divisor)
    {
        if (divisor <= 0)
            throw new ArgumentOutOfRangeException(nameof(divisor));

        long quotient = dividend / divisor;
        if (dividend % divisor < 0)
            quotient--;

        return (int)Math.Clamp(quotient, int.MinValue, int.MaxValue);
    }

    public static int CombineHash(int hash, int value)
    {
        return unchecked(hash * 31 + value);
    }

    private static long ClampToLong(Int128 value)
    {
        if (value > long.MaxValue)
            return long.MaxValue;
        if (value < long.MinValue)
            return long.MinValue;

        return (long)value;
    }

    private static int RoundFixedToInt(long value)
    {
        long rounded = value >= 0
            ? (value + FixedScale / 2) / FixedScale
            : -((-value + FixedScale / 2) / FixedScale);

        return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
    }
}