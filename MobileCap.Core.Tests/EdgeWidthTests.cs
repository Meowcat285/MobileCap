using MobileCap.Core.Sharpness;

namespace MobileCap.Core.Tests;

public class EdgeWidthTests
{
    private const int W = 640, H = 480;

    [Fact]
    public void Matches_spirula_on_the_same_pixels()
    {
        // Spirula's edge_width_px and frame_dhash, run on these pixels as RGB with r = g = b.
        var sharp = TestImages.Blocks(W, H, 16, seed: 1);
        var smeared = TestImages.SmearX(sharp, W, H, 13);

        Assert.Equal(10f, EdgeWidth.Measure(sharp, W, H));
        Assert.Equal(17.45f, EdgeWidth.Measure(smeared, W, H), 5);
        Assert.Equal(0x5d983a23a430365dul, DifferenceHash.Compute(sharp, W, H));
        Assert.Equal(0x1d99ba23b430265dul, DifferenceHash.Compute(smeared, W, H));
    }

    [Fact]
    public void Smearing_along_x_widens_edges_by_about_the_smear()
    {
        var sharp = TestImages.Blocks(W, H, 16, seed: 2);
        var sharpWidth = EdgeWidth.Measure(sharp, W, H);
        var smeared = EdgeWidth.Measure(TestImages.SmearX(sharp, W, H, 9), W, H);

        Assert.InRange(smeared - sharpWidth, 4f, 12f);
    }

    [Fact]
    public void Too_few_edges_or_too_small_is_negative()
    {
        Assert.True(EdgeWidth.Measure(new byte[W * H], W, H) < 0);
        Assert.True(EdgeWidth.Measure(TestImages.Blocks(100, 100, 8, 3), 100, 100) < 0);
    }
}
