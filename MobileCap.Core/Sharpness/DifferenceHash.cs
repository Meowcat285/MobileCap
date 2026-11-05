using System.Numerics;

namespace MobileCap.Core.Sharpness;

/// <summary>
/// Spirula Studio's 64-bit difference hash (src/app/FramePick.h <c>frame_dhash</c>) on 8-bit grey:
/// 9x8 cells of mean brightness sampled on a lattice, one bit per cell brighter than its right
/// neighbour.
/// </summary>
public static class DifferenceHash
{
    private const int Taps = 16;

    /// <summary>0 for an image too small to hash; 0 matches nothing.</summary>
    public static ulong Compute(ReadOnlySpan<byte> grey, int width, int height)
    {
        if (width < 9 || height < 8 || grey.Length < width * height) return 0;
        Span<uint> cell = stackalloc uint[8 * 9];
        for (int cy = 0; cy < 8; cy++)
            for (int cx = 0; cx < 9; cx++)
            {
                uint sum = 0;
                for (int j = 0; j < Taps; j++)
                {
                    int y = (int)(((long)(cy * Taps + j) * 2 + 1) * height / (16 * Taps));
                    for (int i = 0; i < Taps; i++)
                    {
                        int x = (int)(((long)(cx * Taps + i) * 2 + 1) * width / (18 * Taps));
                        sum += grey[y * width + x];
                    }
                }
                cell[cy * 9 + cx] = sum;
            }

        ulong bits = 0;
        for (int cy = 0; cy < 8; cy++)
            for (int cx = 0; cx < 8; cx++)
                bits = (bits << 1) | (cell[cy * 9 + cx] > cell[cy * 9 + cx + 1] ? 1ul : 0ul);
        return bits;
    }

    /// <summary>Bits that differ; 64 when either hash is 0.</summary>
    public static int Distance(ulong a, ulong b) => a == 0 || b == 0 ? 64 : BitOperations.PopCount(a ^ b);
}
