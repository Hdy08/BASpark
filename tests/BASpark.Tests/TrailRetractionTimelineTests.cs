namespace BASpark.Tests;

public class TrailRetractionTimelineTests
{
    [Theory]
    [InlineData(0.5, 25)]
    [InlineData(1.0, 50)]
    [InlineData(2.0, 100)]
    public void HeldStroke_WaitsForFixedDelayThenAdvancesAtConfiguredSpeed(double speed, double expectedCutoff)
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 300);

        Assert.Equal(0, timeline.Advance(299, speed), 12);
        Assert.Equal(expectedCutoff, timeline.Advance(350, speed), 12);
    }

    [Fact]
    public void ReleasedStroke_ContinuesFromHeldCutoffWithoutJumping()
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 300);
        double cutoffAtRelease = timeline.Advance(350, speed: 2);

        timeline.Ended = true;

        Assert.Equal(cutoffAtRelease, timeline.Advance(350, speed: 2), 12);
        Assert.Equal(cutoffAtRelease + 100, timeline.Advance(400, speed: 2), 12);
    }

    [Fact]
    public void ReleaseBeforeDelay_PreservesRemainingDelay()
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 300)
        {
            Ended = true
        };

        Assert.Equal(0, timeline.Advance(100, speed: 3), 12);
        Assert.Equal(0, timeline.Advance(299, speed: 3), 12);
        Assert.Equal(150, timeline.Advance(350, speed: 3), 12);
    }

    [Fact]
    public void SpeedChange_OnlyAffectsFutureRetractionTime()
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 300);

        Assert.Equal(100, timeline.Advance(400, speed: 1), 12);
        Assert.Equal(100, timeline.Advance(400, speed: 2), 12);
        Assert.Equal(200, timeline.Advance(450, speed: 2), 12);
    }

    [Fact]
    public void ZeroDelay_StartsRetractionImmediatelyWithoutDiscardingSpeed()
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 0);

        Assert.Equal(25, timeline.Advance(50, speed: 0.5), 12);
    }

    private sealed class TrailTimeline(double startedAt, double delayMs)
    {
        public double CutoffBorn { get; private set; } = startedAt;
        public double LastAdvancedAt { get; private set; } = startedAt;
        public double RetractAfter { get; } = startedAt + delayMs;
        public bool Ended { get; set; }

        public double Advance(double now, double speed)
        {
            double currentTime = Math.Max(LastAdvancedAt, now);
            double advanceFrom = Math.Max(LastAdvancedAt, RetractAfter);
            if (currentTime > advanceFrom)
            {
                CutoffBorn += (currentTime - advanceFrom) * speed;
            }
            LastAdvancedAt = currentTime;
            return CutoffBorn;
        }
    }
}
