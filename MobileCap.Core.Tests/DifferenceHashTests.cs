using MobileCap.Core.Sharpness;

namespace MobileCap.Core.Tests;

public class DifferenceHashTests
{
    [Fact]
    public void One_bit_a_cell_brighter_than_its_right_neighbour()
    {
        Assert.Equal(0ul, DifferenceHash.Compute(TestImages.Ramp(320, 240, rising: true), 320, 240));
        Assert.Equal(~0ul, DifferenceHash.Compute(TestImages.Ramp(320, 240, rising: false), 320, 240));
    }

    [Fact]
    public void Another_picture_is_many_bits_away_and_a_blurred_copy_few()
    {
        var a = TestImages.Blocks(320, 240, 20, seed: 4);
        var hash = DifferenceHash.Compute(a, 320, 240);

        Assert.True(DifferenceHash.Distance(hash, DifferenceHash.Compute(TestImages.Blocks(320, 240, 20, seed: 5), 320, 240)) >= 16);
        Assert.True(DifferenceHash.Distance(hash, DifferenceHash.Compute(TestImages.SmearX(a, 320, 240, 5), 320, 240)) <= 4);
    }

    [Fact]
    public void A_missing_hash_matches_nothing() => Assert.Equal(64, DifferenceHash.Distance(0, 0));
}
