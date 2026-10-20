using System;

namespace HeadTrack.Core
{
    /// <summary>
    /// The segment cast to find what stands between the game's camera, which
    /// the game keeps clear, and where the head would move it: from the game's
    /// position along the move, a clearance past its end.
    /// </summary>
    public readonly struct ObstacleCast
    {
        /// <summary>A move shorter than this (m) is not worth a cast.</summary>
        public const double MinMove = 1e-6;

        private ObstacleCast(Vec3 start, Vec3 end, double length, double clearance)
        {
            Start = start;
            End = end;
            Length = length;
            Clearance = clearance;
        }

        public Vec3 Start { get; }

        public Vec3 End { get; }

        /// <summary>|End − Start| (m).</summary>
        public double Length { get; }

        /// <summary>How far (m) the camera keeps from whatever the cast hits.</summary>
        public double Clearance { get; }

        public static bool TryCreate(Vec3 basePosition, Vec3 wanted, double clearance, out ObstacleCast cast)
        {
            Vec3 move = wanted - basePosition;
            double length = move.Length;
            if (length < MinMove)
            {
                cast = default;
                return false;
            }
            double total = length + clearance;
            cast = new ObstacleCast(basePosition, basePosition + move * (total / length), total, clearance);
            return true;
        }

        /// <summary>How far (m) the camera may move along the cast when it hit something at <paramref name="hitFraction"/> of its length.</summary>
        public double ClearLength(double hitFraction) => hitFraction * Length - Clearance;
    }

    /// <summary>
    /// Keeps the head-coupled camera out of buildings: the move from the
    /// game's position is cut to the clear length the last cast found. A
    /// shorter clear length applies at once; a longer one, or none, lets the
    /// allowance grow back only at a release speed, so the camera never jumps
    /// away from a wall it was stopped at.
    /// </summary>
    public sealed class ObstacleLimiter
    {
        /// <summary>How far (m) the camera may move from the game's position.</summary>
        public double Allowed { get; private set; } = double.PositiveInfinity;

        /// <summary>
        /// Takes the clear length (m) the last cast found (+∞ when it hit
        /// nothing, null when nothing was cast); the allowance grows by at most
        /// <paramref name="releaseSpeed"/> (m/s) over <paramref name="dt"/> (s).
        /// </summary>
        public void Update(double dt, double? clearLength, double releaseSpeed)
        {
            double target = clearLength.HasValue ? Math.Max(0, clearLength.Value) : double.PositiveInfinity;
            Allowed = target <= Allowed ? target : Math.Min(target, Allowed + releaseSpeed * Math.Max(0, dt));
        }

        /// <summary><paramref name="wanted"/>, pulled back along the move towards <paramref name="basePosition"/> to the allowance.</summary>
        public Vec3 Limit(Vec3 basePosition, Vec3 wanted)
        {
            Vec3 move = wanted - basePosition;
            double length = move.Length;
            return length <= Allowed ? wanted : basePosition + move * (Allowed / length);
        }

        public void Reset()
        {
            Allowed = double.PositiveInfinity;
        }
    }
}
