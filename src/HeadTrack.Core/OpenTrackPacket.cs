using System;

namespace HeadTrack.Core
{
    /// <summary>
    /// One head pose as OpenTrack's "UDP over network" protocol carries it:
    /// X, Y, Z in cm, then yaw, pitch, roll in degrees.
    /// </summary>
    public readonly struct OpenTrackPose
    {
        public OpenTrackPose(double x, double y, double z, double yaw, double pitch, double roll)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double Yaw { get; }

        public double Pitch { get; }

        public double Roll { get; }

        /// <summary>
        /// The eye position in mm in the display frame (+x right, +y up, +z
        /// towards the user). tobii-opentrack sends TX = -x/10, TY = y/10 and
        /// TZ = z/10, as OpenTrack's tracker-tobii plugin does.
        /// </summary>
        public Vec3 EyeMillimetres => new Vec3(-10.0 * X, 10.0 * Y, 10.0 * Z);

        /// <summary>
        /// True when all six values have identical bits: how a repeated packet
        /// (OpenTrack re-sends the last pose at 250 Hz) is told from a new one.
        /// </summary>
        public bool BitwiseEquals(OpenTrackPose other) =>
            Same(X, other.X) && Same(Y, other.Y) && Same(Z, other.Z)
            && Same(Yaw, other.Yaw) && Same(Pitch, other.Pitch) && Same(Roll, other.Roll);

        private static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    }

    /// <summary>The 48-byte datagram: six little-endian IEEE 754 doubles.</summary>
    public static class OpenTrackPacket
    {
        public const int Size = 48;

        /// <summary>Positions beyond 10 m are not a head in front of a screen.</summary>
        public const double MaxPositionCm = 1000.0;

        /// <summary>Angles beyond ten turns are garbage, not a pose.</summary>
        public const double MaxAngleDegrees = 3600.0;

        /// <summary>
        /// Reads a datagram of <paramref name="count"/> bytes. Anything but a
        /// 48-byte packet of finite, plausible values is rejected.
        /// </summary>
        public static bool TryParse(byte[] buffer, int count, out OpenTrackPose pose)
        {
            pose = default;
            if (buffer == null || count != Size || buffer.Length < Size)
            {
                return false;
            }
            var values = new double[6];
            for (int i = 0; i < 6; i++)
            {
                double value = ReadLittleEndian(buffer, i * 8);
                double limit = i < 3 ? MaxPositionCm : MaxAngleDegrees;
                if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > limit)
                {
                    return false;
                }
                values[i] = value;
            }
            pose = new OpenTrackPose(values[0], values[1], values[2], values[3], values[4], values[5]);
            return true;
        }

        public static byte[] Encode(OpenTrackPose pose)
        {
            var packet = new byte[Size];
            double[] values = { pose.X, pose.Y, pose.Z, pose.Yaw, pose.Pitch, pose.Roll };
            for (int i = 0; i < 6; i++)
            {
                byte[] bytes = BitConverter.GetBytes(values[i]);
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }
                bytes.CopyTo(packet, i * 8);
            }
            return packet;
        }

        private static double ReadLittleEndian(byte[] buffer, int offset)
        {
            if (BitConverter.IsLittleEndian)
            {
                return BitConverter.ToDouble(buffer, offset);
            }
            var bytes = new byte[8];
            Array.Copy(buffer, offset, bytes, 0, 8);
            Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }
    }
}
