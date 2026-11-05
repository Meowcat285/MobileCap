using MobileCap.Core.Sharpness;

namespace MobileCap.Core.Tests;

public class ShotJudgeTests
{
    [Fact]
    public void Lens_width_is_the_10th_percentile_plus_8_percent()
    {
        var widths = Enumerable.Range(1, 20).Select(i => (float)i).Append(-1f).ToArray();

        Assert.Equal(1.08f * 3, ShotJudge.LensWidth(widths), 4);
        Assert.Equal(-1f, ShotJudge.LensWidth([-1f]));
    }

    [Fact]
    public void Blur_is_the_width_less_the_lens_in_quadrature()
    {
        var judge = new ShotJudge(maxBlurPx: 3, similarBits: 0);
        for (int i = 0; i < 9; i++) judge.Judge(2f, hash: (ulong)i + 1);

        var wide = judge.Judge(5f, hash: 99);

        var lens = 1.08f * 2;
        Assert.Equal(MathF.Sqrt(25 - lens * lens), wide.BlurPx, 4);
        Assert.Equal(ShotVerdict.Blurry, wide.Verdict);
        Assert.Equal(ShotVerdict.Sharp, judge.Judge(2.5f, hash: 100).Verdict);
    }

    [Fact]
    public void Near_the_last_kept_hash_is_a_duplicate()
    {
        var judge = new ShotJudge(maxBlurPx: 3, similarBits: 6);
        judge.Kept(0b1111_0000);

        Assert.Equal(ShotVerdict.Duplicate, judge.Judge(2f, 0b1111_0011).Verdict);
        Assert.Equal(ShotVerdict.Sharp, judge.Judge(2f, 0b1111_0000ul << 40 | 0xFF).Verdict);
    }

    [Fact]
    public void An_unmeasured_frame_is_kept()
    {
        var judgement = new ShotJudge(maxBlurPx: 3, similarBits: 6).Judge(-1f, 0);

        Assert.Equal(ShotVerdict.Sharp, judgement.Verdict);
        Assert.Equal(-1f, judgement.BlurPx);
    }
}
