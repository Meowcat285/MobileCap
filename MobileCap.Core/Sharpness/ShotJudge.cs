namespace MobileCap.Core.Sharpness;

public enum ShotVerdict
{
    Sharp,
    Blurry,
    Duplicate,
}

/// <param name="BlurPx">Edge width less the lens's share, full-resolution pixels; negative unmeasured.</param>
public readonly record struct ShotJudgement(float EdgeWidthPx, float BlurPx, ulong Hash, ShotVerdict Verdict);

/// <summary>
/// Spirula Studio's frame rule (src/app/FramePick.h <c>PickRule</c>) as a capture goes: blur is
/// the edge width less the capture's own sharpest frames so far, and a frame too like the last
/// one kept is a duplicate.
/// </summary>
public sealed class ShotJudge(float maxBlurPx, int similarBits)
{
    private readonly List<float> _widths = [];
    private ulong _lastKept;

    public float MaxBlurPx { get; } = maxBlurPx;

    public int SimilarBits { get; } = similarBits;

    public ShotJudgement Judge(ReadOnlySpan<byte> grey, int width, int height) =>
        Judge(EdgeWidth.Measure(grey, width, height), DifferenceHash.Compute(grey, width, height));

    /// <summary>
    /// A frame nothing could be measured on is sharp, as in Spirula. Every measured width joins
    /// the lens estimate, kept or not.
    /// </summary>
    public ShotJudgement Judge(float edgeWidthPx, ulong hash)
    {
        if (edgeWidthPx >= 0) _widths.Add(edgeWidthPx);
        var blur = BlurOf(edgeWidthPx);
        ShotVerdict verdict;
        if (SimilarBits > 0 && DifferenceHash.Distance(hash, _lastKept) < SimilarBits)
            verdict = ShotVerdict.Duplicate;
        else if (MaxBlurPx > 0 && blur > MaxBlurPx)
            verdict = ShotVerdict.Blurry;
        else
            verdict = ShotVerdict.Sharp;
        return new ShotJudgement(edgeWidthPx, blur, hash, verdict);
    }

    /// <summary>Call for the frame actually kept, which may be an earlier, blurry attempt.</summary>
    public void Kept(ulong hash) => _lastKept = hash;

    /// <summary>
    /// The width the lens alone leaves on an edge: the 10th percentile of the widths so far plus
    /// 8%, Spirula's <c>lens_width</c>. Negative when none was measured.
    /// </summary>
    public static float LensWidth(IReadOnlyList<float> widths)
    {
        var s = widths.Where(v => v >= 0).ToArray();
        if (s.Length == 0) return -1f;
        Array.Sort(s);
        return 1.08f * s[s.Length / 10];
    }

    private float BlurOf(float width)
    {
        if (width < 0) return -1f;
        var lens = LensWidth(_widths);
        return MathF.Sqrt(MathF.Max(width * width - lens * lens, 0f));
    }
}
