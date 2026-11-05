using System.Numerics;
using MobileCap.Core.Ar;

namespace MobileCap.Core.Tests;

/// <summary>Synthetic frames and poses: no recorded data is checked in.</summary>
internal static class TestImages
{
    /// <summary>Blocks of random grey levels, so edges run both ways at many contrasts.</summary>
    public static byte[] Blocks(int width, int height, int cell, int seed)
    {
        var rng = new Random(seed);
        int cols = width / cell + 1, rows = height / cell + 1;
        var level = new byte[cols * rows];
        rng.NextBytes(level);
        var img = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[y * width + x] = (byte)(30 + level[y / cell * cols + x / cell] * 200 / 255);
        return img;
    }

    /// <summary>A horizontal box blur of <paramref name="length"/> pixels: motion along x.</summary>
    public static byte[] SmearX(byte[] img, int width, int height, int length)
    {
        var o = new byte[img.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                for (int i = 0; i < length; i++) sum += img[y * width + Math.Clamp(x - length / 2 + i, 0, width - 1)];
                o[y * width + x] = (byte)(sum / length);
            }
        return o;
    }

    public static byte[] Ramp(int width, int height, bool rising)
    {
        var img = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[y * width + x] = (byte)((rising ? x : width - 1 - x) * 255 / (width - 1));
        return img;
    }

    public static readonly CameraIntrinsics Intrinsics = new(Fx: 3000, Fy: 3000, Cx: 2016, Cy: 1512, Width: 4032, Height: 3024);

    /// <summary>Camera-to-world: turned <paramref name="yawDegrees"/> about world up, then placed at <paramref name="position"/>.</summary>
    public static Matrix4x4 Pose(Vector3 position, double yawDegrees = 0) =>
        Matrix4x4.CreateRotationY((float)(yawDegrees * Math.PI / 180)) * Matrix4x4.CreateTranslation(position);

    public static ArFrame Frame(double seconds, Matrix4x4 cameraToWorld, double exposure = 0.01,
        TrackingState tracking = TrackingState.Normal) =>
        new(TimeSpan.FromSeconds(seconds), tracking, cameraToWorld, Intrinsics, exposure);

    /// <summary>The smallest JPEG framing the writer needs: SOI, a JFIF APP0, opaque data, EOI.</summary>
    public static byte[] Jpeg() =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0,
        0xFF, 0xDA, 0x00, 0x04, 0x12, 0x34,
        0xFF, 0xD9,
    ];
}
