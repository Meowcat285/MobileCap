namespace MobileCap.Core.Sharpness;

/// <summary>
/// Spirula Studio's blur measure (src/app/FrameSharpness.cpp <c>edge_width_px</c>, constants from
/// src/blur/EdgeWidth.h), read off 8-bit grey instead of RGB so the app and Spirula agree on
/// what sharp means. Keep the constants and the walk in step with those files.
/// </summary>
public static class EdgeWidth
{
    private const int MagBins = 4096;
    private const float MagPerLevel = 4f;
    private const int Reach = 60;
    private const float Step = 12f;
    private const int WidthBins = 2 * Reach + 1;

    /// <summary>
    /// How wide the sharpest of the frame's strong edges are, in its own pixels: the wider of
    /// across-x and across-y, since motion smears one. Negative where too few edges were found.
    /// </summary>
    public static float Measure(ReadOnlySpan<byte> grey, int width, int height)
    {
        int w = width / 2, h = height / 2;
        if (w < 64 || h < 64 || grey.Length < width * height) return -1f;
        var g = HalfGrey(grey, width, w, h);
        float x = AxisWidth(g, w, h, alongX: true), y = AxisWidth(g, w, h, alongX: false);
        if (x < 0f || y < 0f) return -1f;
        return 2f * Math.Max(x, y);
    }

    // Half size, then a 5-tap Gaussian of sigma 0.8: sensor noise makes false extrema an edge
    // walk would stop at. The two-pixel rim is left unsmoothed; no edge is walked from there.
    private static float[] HalfGrey(ReadOnlySpan<byte> grey, int width, int w, int h)
    {
        var raw = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            var a = grey.Slice(2 * y * width);
            var b = grey.Slice((2 * y + 1) * width);
            for (int x = 0; x < w; x++)
                raw[y * w + x] = 0.25f * (a[2 * x] + a[2 * x + 1] + b[2 * x] + b[2 * x + 1]);
        }

        const float k0 = 0.4992f, k1 = 0.2285f, k2 = 0.0219f;
        var row = (float[])raw.Clone();
        for (int y = 0; y < h; y++)
        {
            int r = y * w;
            for (int x = 2; x < w - 2; x++)
                row[r + x] = k0 * raw[r + x] + k1 * (raw[r + x - 1] + raw[r + x + 1]) + k2 * (raw[r + x - 2] + raw[r + x + 2]);
        }

        var smooth = (float[])row.Clone();
        for (int y = 2; y < h - 2; y++)
        {
            int r = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = r + x;
                smooth[i] = k0 * row[i] + k1 * (row[i - w] + row[i + w]) + k2 * (row[i - 2 * w] + row[i + 2 * w]);
            }
        }
        return smooth;
    }

    // The mean width of the sharpest of the strong edges across one axis (alongX: edges a row
    // crosses); negative for too few.
    private static float AxisWidth(float[] d, int w, int h, bool alongX)
    {
        void Grad(int x, int y, out float across, out float other)
        {
            int p = y * w + x;
            float gx = (d[p + 1 - w] + 2 * d[p + 1] + d[p + 1 + w]) - (d[p - 1 - w] + 2 * d[p - 1] + d[p - 1 + w]);
            float gy = (d[p + w - 1] + 2 * d[p + w] + d[p + w + 1]) - (d[p - w - 1] + 2 * d[p - w] + d[p - w + 1]);
            across = alongX ? gx : gy;
            other = alongX ? gy : gx;
        }

        // The 99th percentile of the gradient, off every fourth pixel each way.
        var mag = new uint[MagBins];
        ulong n = 0;
        for (int y = 1; y < h - 1; y += 4)
            for (int x = 1; x < w - 1; x += 4)
            {
                Grad(x, y, out var a, out _);
                mag[MagBin(MathF.Abs(a))]++;
                n++;
            }
        if (n < 100) return -1f;
        float strong = StrongFromHist(mag, n);

        int step = alongX ? 1 : w, len = alongX ? w : h;
        var widths = new uint[WidthBins];
        for (int y = 3; y < h - 3; y += 2)
            for (int x = 3; x < w - 3; x += 2)
            {
                Grad(x, y, out var a, out var o);
                if (!(MathF.Abs(a) > strong && MathF.Abs(a) > 2f * MathF.Abs(o))) continue;
                bool up = a > 0f;
                int p = y * w + x, pos = alongX ? x : y;
                int lo = 0, hi = 0;
                while (pos - lo > 0 && lo < Reach &&
                       (up ? d[p - (lo + 1) * step] < d[p - lo * step] : d[p - (lo + 1) * step] > d[p - lo * step]))
                    lo++;
                while (pos + hi < len - 1 && hi < Reach &&
                       (up ? d[p + (hi + 1) * step] > d[p + hi * step] : d[p + (hi + 1) * step] < d[p + hi * step]))
                    hi++;
                if (MathF.Abs(d[p + hi * step] - d[p - lo * step]) > Step) widths[lo + hi]++;
            }
        return WidthFromHist(widths);
    }

    private static int MagBin(float a)
    {
        int b = (int)(a * MagPerLevel);
        return b < MagBins - 1 ? b : MagBins - 1;
    }

    // The lower edge of the bin holding the 99th percentile; not the upper, since a synthetic
    // ramp puts all its edges in one bin and that would leave none.
    private static float StrongFromHist(uint[] hist, ulong n)
    {
        ulong at = n * 99 / 100, c = 0;
        for (int b = 0; b < MagBins; b++)
        {
            c += hist[b];
            if (c > at) return b / MagPerLevel;
        }
        return (MagBins - 1) / MagPerLevel;
    }

    // The mean of the sharpest fifth of the widths, less the narrowest twentieth. Negative for
    // fewer than 30 edges.
    private static float WidthFromHist(uint[] hist)
    {
        ulong n = 0;
        foreach (var c in hist) n += c;
        if (n < 30) return -1f;
        ulong lo = n / 20, hi = n / 4, rank = 0;
        double sum = 0;
        for (int b = 0; b < WidthBins && rank < hi; b++)
        {
            ulong from = Math.Max(rank, lo), to = Math.Min(rank + hist[b], hi);
            if (to > from) sum += (double)b * (to - from);
            rank += hist[b];
        }
        return (float)(sum / (hi - lo));
    }
}
