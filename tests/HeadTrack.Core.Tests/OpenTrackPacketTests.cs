using HeadTrack.Core;

namespace HeadTrack.Core.Tests;

public class OpenTrackPacketTests
{
    private static byte[] Packet(params double[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (int i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i]).CopyTo(bytes, i * 8);
        }
        return bytes;
    }

    [Fact]
    public void Parses_six_little_endian_doubles_in_order()
    {
        byte[] packet = Packet(1.0, -2.5, 60.0, -10.0, 5.5, 0.125);

        Assert.True(OpenTrackPacket.TryParse(packet, packet.Length, out OpenTrackPose pose));
        Assert.Equal(1.0, pose.X);
        Assert.Equal(-2.5, pose.Y);
        Assert.Equal(60.0, pose.Z);
        Assert.Equal(-10.0, pose.Yaw);
        Assert.Equal(5.5, pose.Pitch);
        Assert.Equal(0.125, pose.Roll);
    }

    [Fact]
    public void The_first_double_is_read_as_little_endian_regardless_of_the_host()
    {
        // 1.0 as a little-endian IEEE 754 double, as tobii-opentrack sends it.
        byte[] packet = new byte[48];
        packet[6] = 0xf0;
        packet[7] = 0x3f;

        Assert.True(OpenTrackPacket.TryParse(packet, 48, out OpenTrackPose pose));
        Assert.Equal(1.0, pose.X);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(47)]
    [InlineData(49)]
    [InlineData(96)]
    public void Rejects_a_datagram_that_is_not_48_bytes(int length)
    {
        byte[] buffer = new byte[Math.Max(length, 48)];

        Assert.False(OpenTrackPacket.TryParse(buffer, length, out _));
    }

    [Theory]
    [InlineData(0, double.NaN)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(2, double.NegativeInfinity)]
    [InlineData(3, double.NaN)]
    [InlineData(4, double.PositiveInfinity)]
    [InlineData(5, double.NegativeInfinity)]
    public void Rejects_a_non_finite_value_in_any_field(int field, double value)
    {
        double[] values = { 1, 2, 60, 3, 4, 5 };
        values[field] = value;
        byte[] packet = Packet(values);

        Assert.False(OpenTrackPacket.TryParse(packet, packet.Length, out _));
    }

    [Theory]
    [InlineData(0, 1.0e6)]
    [InlineData(2, -2000.0)]
    [InlineData(3, 1.0e5)]
    public void Rejects_magnitudes_no_head_tracker_produces(int field, double value)
    {
        double[] values = { 1, 2, 60, 3, 4, 5 };
        values[field] = value;
        byte[] packet = Packet(values);

        Assert.False(OpenTrackPacket.TryParse(packet, packet.Length, out _));
    }

    [Fact]
    public void Eye_position_is_minus_x_y_z_in_millimetres()
    {
        // tobii-opentrack sends TX = -x/10, TY = y/10, TZ = z/10 for a display-frame
        // position in mm (tobii_opentrack.rs, opentrack_pose).
        var pose = new OpenTrackPose(3.15, 8.4, 57.15, 2.1, 10.5, -2.8);

        Vec3 eye = pose.EyeMillimetres;

        Assert.Equal(-31.5, eye.X, 9);
        Assert.Equal(84.0, eye.Y, 9);
        Assert.Equal(571.5, eye.Z, 9);
    }

    [Fact]
    public void Encode_writes_what_TryParse_reads()
    {
        var pose = new OpenTrackPose(-1.25, 3.5, 61.0, 7.0, -8.0, 9.0);

        byte[] packet = OpenTrackPacket.Encode(pose);

        Assert.Equal(OpenTrackPacket.Size, packet.Length);
        Assert.True(OpenTrackPacket.TryParse(packet, packet.Length, out OpenTrackPose read));
        Assert.True(read.BitwiseEquals(pose));
    }

    [Fact]
    public void Bitwise_equality_tells_repeated_packets_from_changed_ones()
    {
        var a = new OpenTrackPose(1, 2, 3, 4, 5, 6);
        var b = new OpenTrackPose(1, 2, 3, 4, 5, 6);
        var c = new OpenTrackPose(1, 2, 3, 4, 5, 6.000000001);

        Assert.True(a.BitwiseEquals(b));
        Assert.False(a.BitwiseEquals(c));
    }
}
