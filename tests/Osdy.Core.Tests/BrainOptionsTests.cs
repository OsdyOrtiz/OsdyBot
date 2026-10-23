using Osdy.Core.Vitals;

namespace Osdy.Core.Tests;

public class BrainOptionsTests
{
    [Fact]
    public void Default_options_are_valid()
    {
        Assert.Empty(new BrainOptions().Validate());
    }

    [Fact]
    public void Invalid_options_list_every_problem_and_creation_is_refused()
    {
        var options = new BrainOptions
        {
            RobotName = " ",
            Vitals = new VitalsOptions { RestThreshold = 0.9, WakeThreshold = 0.8 },
            Runtime = new RuntimeOptions { TickInterval = TimeSpan.Zero },
        };

        Assert.Equal(3, options.Validate().Count);
        var error = Assert.Throws<ArgumentException>(() => Brain.CreateNew(options, TestBrains.T0));
        Assert.Contains("RobotName", error.Message, StringComparison.Ordinal);
    }
}
