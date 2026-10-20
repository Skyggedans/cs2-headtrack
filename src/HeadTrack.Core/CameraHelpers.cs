using System;

namespace HeadTrack.Core
{
    /// <summary>The two values the game's CPU pre-culling frustum is built from: cot(vertical half-angle) and the aspect.</summary>
    public readonly struct PlanePacketArguments
    {
        public PlanePacketArguments(double fovFactor, double aspect)
        {
            FovFactor = fovFactor;
            Aspect = aspect;
        }

        public double FovFactor { get; }

        public double Aspect { get; }
    }

    /// <summary>
    /// The game pre-culls on the CPU with a symmetric pyramid around the
    /// camera axis (FrustumPlanes.BuildPlanePacket: tanV = 1/fovFactor,
    /// tanH = aspect·tanV). An off-axis frustum pokes out of it on the
    /// shifted side, so objects there would vanish; this widens the pyramid
    /// to contain it.
    /// </summary>
    public static class CoverageFrustum
    {
        /// <summary>Changes of shape at which the safety margin reaches its full size.</summary>
        private const double FullMarginChange = 0.1;

        /// <summary>
        /// Covering arguments, scaled from the game's own
        /// (<paramref name="gameFovFactor"/>, <paramref name="gameAspect"/>)
        /// so a zero offset gives exactly them back, and never narrower
        /// than the off-axis frustum itself.
        /// </summary>
        public static PlanePacketArguments Cover(double gameFovFactor, double gameAspect, FrustumTangents rendered, FrustumTangents view)
        {
            double gameTanV = 1.0 / gameFovFactor;
            double gameTanH = gameAspect * gameTanV;
            double renderedV = Math.Max(Math.Abs(rendered.Top), Math.Abs(rendered.Bottom));
            double renderedH = Math.Max(Math.Abs(rendered.Left), Math.Abs(rendered.Right));
            double viewV = Math.Max(Math.Abs(view.Top), Math.Abs(view.Bottom));
            double viewH = Math.Max(Math.Abs(view.Left), Math.Abs(view.Right));

            double change = Math.Max(
                Math.Max(Math.Abs(Shift(view.Left, view.Right) - Shift(rendered.Left, rendered.Right)),
                         Math.Abs(Shift(view.Bottom, view.Top) - Shift(rendered.Bottom, rendered.Top))),
                Math.Abs(Math.Log(viewV / renderedV)));
            double k = Math.Min(1.0, change / FullMarginChange);

            double tanV = Math.Max(gameTanV * viewV / renderedV, viewV) * (1 + 0.01 * k);
            double tanH = Math.Max(gameTanH * viewH / renderedH, viewH) * (1 + 0.02 * k);
            return new PlanePacketArguments(1.0 / tanV, tanH / tanV);
        }

        /// <summary>The normalized centre offset of an extent pair: (max + min)/(max - min).</summary>
        private static double Shift(double min, double max) => (max + min) / (max - min);
    }

    /// <summary>
    /// A physical-camera lens that renders a given frustum: gate fit None,
    /// a sensor with the frustum's aspect, the focal length for its height
    /// and the lens shift for its offset (Unity measures the shift in sensor
    /// sizes). The alternative to an explicit projection matrix.
    /// </summary>
    public readonly struct PhysicalLens
    {
        public PhysicalLens(double sensorWidth, double sensorHeight, double focalLength, double shiftX, double shiftY)
        {
            SensorWidth = sensorWidth;
            SensorHeight = sensorHeight;
            FocalLength = focalLength;
            ShiftX = shiftX;
            ShiftY = shiftY;
        }

        public double SensorWidth { get; }

        public double SensorHeight { get; }

        public double FocalLength { get; }

        public double ShiftX { get; }

        public double ShiftY { get; }

        /// <summary>The field of view Unity reports for this lens: the unshifted sensor's.</summary>
        public double VerticalFieldOfViewDegrees => 2 * Math.Atan(0.5 * SensorHeight / FocalLength) * 180 / Math.PI;

        /// <summary>The frustum this lens renders with gate fit None.</summary>
        public FrustumTangents Tangents
        {
            get
            {
                double halfWidth = 0.5 * SensorWidth / FocalLength;
                double halfHeight = 0.5 * SensorHeight / FocalLength;
                double centreX = ShiftX * SensorWidth / FocalLength;
                double centreY = ShiftY * SensorHeight / FocalLength;
                return new FrustumTangents(centreX - halfWidth, centreX + halfWidth, centreY - halfHeight, centreY + halfHeight);
            }
        }

        public static PhysicalLens FromTangents(FrustumTangents view, double sensorHeight)
        {
            double width = view.Right - view.Left;
            double height = view.Top - view.Bottom;
            double sensorWidth = sensorHeight * width / height;
            double focal = sensorHeight / height;
            return new PhysicalLens(sensorWidth, sensorHeight, focal, (view.Right + view.Left) / (2 * width), (view.Top + view.Bottom) / (2 * height));
        }
    }

    /// <summary>The window depth for the gameplay camera.</summary>
    public static class WindowDepth
    {
        /// <summary>Below this horizontal share of the view direction the camera counts as looking straight down.</summary>
        private const double MinHorizontal = 0.1;

        /// <summary>
        /// The depth along the view of the point the camera aims at: the
        /// pivot's horizontal offset along the horizontal view direction, over
        /// that direction's share of the view. On the game's orbit this is the
        /// zoom; unlike the pivot's projection on the view axis it stays
        /// positive when the player looks up, and it ignores both the game's
        /// lift over terrain and a sideways collision slide. Looking straight
        /// down, the zoom. Clamped to [max(2·near, zoomMin), far/2]; anything
        /// not finite gives the lower bound.
        /// </summary>
        public static double AimPoint(Vec3 pivot, Vec3 position, Vec3 forward, double zoom, double near, double far, double zoomMin)
        {
            double horizontal2 = forward.X * forward.X + forward.Z * forward.Z;
            double depth = zoom;
            if (horizontal2 > MinHorizontal * MinHorizontal)
            {
                double dx = pivot.X - position.X;
                double dz = pivot.Z - position.Z;
                depth = (dx * forward.X + dz * forward.Z) / horizontal2;
            }
            double min = Math.Max(2 * near, zoomMin);
            double max = 0.5 * far;
            if (double.IsNaN(depth))
            {
                return min;
            }
            return depth < min ? min : depth > max ? max : depth;
        }
    }

    /// <summary>
    /// The effect's overall weight: rises and falls smoothly when a gate
    /// opens or closes (photo mode, map view, a tool drag), drops at once on
    /// a cut (loading, a camera change).
    /// </summary>
    public sealed class EffectRamp
    {
        /// <summary>A longer gap between updates counts as this long (s).</summary>
        private const double MaxFrameSeconds = 0.1;

        private readonly double m_Seconds;
        private double m_Progress;
        private double m_Last;
        private bool m_HasLast;

        public EffectRamp(double seconds)
        {
            m_Seconds = seconds;
        }

        public double Weight => Smoothstep(m_Progress);

        public double Update(double now, bool on)
        {
            double dt = m_HasLast ? Math.Min(MaxFrameSeconds, Math.Max(0, now - m_Last)) : 0;
            m_HasLast = true;
            m_Last = now;
            double step = m_Seconds > 0 ? dt / m_Seconds : 1;
            m_Progress = on ? Math.Min(1, m_Progress + step) : Math.Max(0, m_Progress - step);
            return Weight;
        }

        /// <summary>Drops the weight to zero at <paramref name="now"/>; it ramps in from there.</summary>
        public double Cut(double now)
        {
            m_Progress = 0;
            m_Last = now;
            m_HasLast = true;
            return 0;
        }

        private static double Smoothstep(double p) => p * p * (3 - 2 * p);
    }
}
