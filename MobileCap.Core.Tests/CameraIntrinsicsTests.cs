using MobileCap.Core.Ar;

namespace MobileCap.Core.Tests;

public class CameraIntrinsicsTests
{
    [Fact]
    public void ScaledTo_half_size_halves_every_term()
    {
        var full = new CameraIntrinsics(Fx: 3000, Fy: 3000, Cx: 2016, Cy: 1512, Width: 4032, Height: 3024);

        var half = full.ScaledTo(2016, 1512);

        Assert.Equal(new CameraIntrinsics(1500, 1500, 1008, 756, 2016, 1512), half);
    }
}
