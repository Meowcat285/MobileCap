using MobileCap.Core;

namespace MobileCap.Core.Tests;

public class CaptureSettingsTests
{
    [Fact]
    public void Defaults_follow_the_plan()
    {
        var settings = new CaptureSettings();

        Assert.Equal(CaptureMode.Scene, settings.Mode);
        Assert.False(settings.WriteGpsToExif);
        Assert.Equal(0.3, settings.SceneSpacingMetres);
        Assert.Equal(15, settings.SceneSpacingDegrees);
    }
}
