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
        timeline.Append(200);

        Assert.Equal(0, timeline.Advance(299, speed), 12);
        Assert.Equal(expectedCutoff, timeline.Advance(350, speed), 12);
    }

    [Fact]
    public void ReleasedStroke_ContinuesFromHeldCutoffWithoutJumping()
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 300);
        timeline.Append(200);
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
        timeline.Append(100);

        Assert.Equal(0, timeline.Advance(100, speed: 3), 12);
        Assert.Equal(0, timeline.Advance(299, speed: 3), 12);
        Assert.Equal(100, timeline.Advance(350, speed: 3), 12);
        Assert.True(timeline.Dormant);
    }

    [Fact]
    public void SpeedChange_OnlyAffectsFutureRetractionTime()
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 300);
        timeline.Append(300);

        Assert.Equal(100, timeline.Advance(400, speed: 1), 12);
        Assert.Equal(100, timeline.Advance(400, speed: 2), 12);
        Assert.Equal(200, timeline.Advance(450, speed: 2), 12);
    }

    [Fact]
    public void ZeroDelay_StartsRetractionImmediatelyWithoutDiscardingSpeed()
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 0);
        timeline.Append(50);

        Assert.Equal(25, timeline.Advance(50, speed: 0.5), 12);
    }

    [Theory]
    [InlineData(0.5, 500)]
    [InlineData(2.0, 350)]
    public void HeldStroke_ResumedInputRearmsEachFullyRetractedRunWithoutTimelineDrift(
        double speed,
        double expectedDuration)
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 300);

        for (int cycle = 0; cycle < 4; cycle++)
        {
            double startedAt = cycle * 2000;
            if (cycle > 0)
            {
                timeline.ResumeInput(startedAt, speed);
            }
            timeline.Append(startedAt + 100);

            Assert.Equal(startedAt, timeline.Advance(startedAt + 299, speed), 12);
            Assert.False(timeline.Dormant);
            Assert.Equal(startedAt + 100, timeline.Advance(startedAt + expectedDuration, speed), 12);
            Assert.True(timeline.Dormant);

            double settledCutoff = timeline.CutoffBorn;
            Assert.Equal(settledCutoff, timeline.Advance(startedAt + expectedDuration + 1000, speed), 12);
            Assert.Equal(timeline.LatestBorn, timeline.CutoffBorn, 12);
        }
    }

    [Theory]
    [InlineData(0.5, 25)]
    [InlineData(2.0, 100)]
    public void ResumedInput_AdvancesDormantStateAcrossStoppedAnimationFrames(
        double speed,
        double expectedCutoff)
    {
        var timeline = new TrailTimeline(startedAt: 0, delayMs: 300);

        timeline.ResumeInput(1000, speed);
        Assert.Equal(1000, timeline.CutoffBorn, 12);
        Assert.Equal(1300, timeline.RetractAfter, 12);
        Assert.False(timeline.Dormant);

        timeline.Append(1100);
        Assert.Equal(1000, timeline.Advance(1299, speed), 12);
        Assert.Equal(1000 + expectedCutoff, timeline.Advance(1350, speed), 12);
    }

    private sealed class TrailTimeline(double startedAt, double delayMs)
    {
        private readonly double _delayMs = delayMs;

        public double CutoffBorn { get; private set; } = startedAt;
        public double LastAdvancedAt { get; private set; } = startedAt;
        public double RetractAfter { get; private set; } = startedAt + delayMs;
        public double LatestBorn { get; private set; } = startedAt;
        public bool Ended { get; set; }
        public bool Dormant { get; private set; }

        public void Append(double born)
        {
            LatestBorn = Math.Max(LatestBorn, born);
        }

        public void Rearm(double startedAt)
        {
            CutoffBorn = startedAt;
            LastAdvancedAt = startedAt;
            RetractAfter = startedAt + _delayMs;
            LatestBorn = startedAt;
            Ended = false;
            Dormant = false;
        }

        public void ResumeInput(double now, double speed)
        {
            Advance(now, speed);
            if (Dormant)
            {
                Rearm(now);
            }
        }

        public double Advance(double now, double speed)
        {
            if (Dormant)
            {
                return CutoffBorn;
            }
            double currentTime = Math.Max(LastAdvancedAt, now);
            double advanceFrom = Math.Max(LastAdvancedAt, RetractAfter);
            if (currentTime > advanceFrom)
            {
                CutoffBorn = Math.Min(LatestBorn, CutoffBorn + (currentTime - advanceFrom) * speed);
            }
            LastAdvancedAt = currentTime;
            if (currentTime >= RetractAfter && CutoffBorn >= LatestBorn - 0.000001)
            {
                CutoffBorn = LatestBorn;
                Dormant = true;
            }
            return CutoffBorn;
        }
    }
}
