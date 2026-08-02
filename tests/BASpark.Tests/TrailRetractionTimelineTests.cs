namespace BASpark.Tests;

public class TrailRetractionTimelineTests
{
    private const double GameRetractionDelayMs = 300.00001192092896;

    [Theory]
    [InlineData(0.5, 25)]
    [InlineData(1.0, 50)]
    [InlineData(2.0, 100)]
    public void TrailSpeed_ScalesBothRetractionDelayAndAdvance(double speed, double expectedCutoff)
    {
        var timeline = new TrailTimeline(startedAt: 0, speed);
        timeline.Append(100);

        double retractAfter = GameRetractionDelayMs / speed;
        Assert.Equal(retractAfter, timeline.RetractAfter, 9);
        Assert.Equal(0, timeline.Advance(retractAfter - 0.001), 9);
        Assert.Equal(expectedCutoff, timeline.Advance(retractAfter + 50), 9);
    }

    [Fact]
    public void SpeedIncreaseDuringDelay_RescalesRemainingDelayByOldOverNew()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 1);
        timeline.Append(300);

        timeline.SetSpeed(speed: 2, now: 100);

        double expectedRetractAfter = 100 + (GameRetractionDelayMs - 100) / 2;
        Assert.Equal(expectedRetractAfter, timeline.RetractAfter, 9);
        Assert.Equal(0, timeline.Advance(expectedRetractAfter - 0.001), 9);
        Assert.Equal(20, timeline.Advance(expectedRetractAfter + 10), 9);
    }

    [Fact]
    public void SpeedDecreaseDuringDelay_RescalesRemainingDelayByOldOverNew()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 2);
        timeline.Append(300);

        timeline.SetSpeed(speed: 0.5, now: 50);

        double oldRetractAfter = GameRetractionDelayMs / 2;
        double expectedRetractAfter = 50 + (oldRetractAfter - 50) * 2 / 0.5;
        Assert.Equal(expectedRetractAfter, timeline.RetractAfter, 9);
        Assert.Equal(0, timeline.Advance(expectedRetractAfter - 0.001), 9);
        Assert.Equal(5, timeline.Advance(expectedRetractAfter + 10), 9);
    }

    [Fact]
    public void SpeedChangeAfterRetractionStarts_OnlyAffectsFutureAdvance()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 1);
        timeline.Append(300);

        Assert.Equal(100, timeline.Advance(GameRetractionDelayMs + 100), 9);
        timeline.SetSpeed(speed: 2, now: GameRetractionDelayMs + 100);

        Assert.Equal(100, timeline.CutoffBorn, 9);
        Assert.Equal(200, timeline.Advance(GameRetractionDelayMs + 150), 9);
    }

    [Fact]
    public void ReleasedStroke_ContinuesFromHeldCutoffWithoutJumping()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 2);
        timeline.Append(300);
        double releaseAt = GameRetractionDelayMs / 2 + 50;
        double cutoffAtRelease = timeline.Advance(releaseAt);

        timeline.Ended = true;

        Assert.Equal(cutoffAtRelease, timeline.Advance(releaseAt), 9);
        Assert.Equal(cutoffAtRelease + 100, timeline.Advance(releaseAt + 50), 9);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void ResumedInput_RearmsRepeatedFullyRetractedRunsWithoutTimelineDrift(double speed)
    {
        var timeline = new TrailTimeline(startedAt: 0, speed);
        double expectedDuration = (GameRetractionDelayMs + 100) / speed;

        for (int cycle = 0; cycle < 4; cycle++)
        {
            double startedAt = cycle * 2000;
            if (cycle > 0)
            {
                timeline.ResumeInput(startedAt);
            }
            timeline.Append(startedAt + 100);

            Assert.Equal(startedAt, timeline.Advance(startedAt + GameRetractionDelayMs / speed - 0.001), 9);
            Assert.False(timeline.Dormant);
            Assert.Equal(startedAt + 100, timeline.Advance(startedAt + expectedDuration), 9);
            Assert.True(timeline.Dormant);

            double settledCutoff = timeline.CutoffBorn;
            Assert.Equal(settledCutoff, timeline.Advance(startedAt + expectedDuration + 1000), 9);
            Assert.Equal(timeline.LatestBorn, timeline.CutoffBorn, 9);
        }
    }

    private sealed class TrailTimeline
    {
        public TrailTimeline(double startedAt, double speed)
        {
            Speed = speed;
            Rearm(startedAt);
        }

        public double CutoffBorn { get; private set; }
        public double LastAdvancedAt { get; private set; }
        public double RetractAfter { get; private set; }
        public double LatestBorn { get; private set; }
        public double Speed { get; private set; }
        public bool Ended { get; set; }
        public bool Dormant { get; private set; }

        public void Append(double born)
        {
            LatestBorn = Math.Max(LatestBorn, born);
        }

        public void ResumeInput(double now)
        {
            Advance(now);
            if (Dormant)
            {
                Rearm(now);
            }
        }

        public void SetSpeed(double speed, double now)
        {
            Advance(now);
            if (!Dormant && now < RetractAfter)
            {
                RetractAfter = now + (RetractAfter - now) * Speed / speed;
            }
            Speed = speed;
        }

        public double Advance(double now)
        {
            if (Dormant)
            {
                return CutoffBorn;
            }

            double currentTime = Math.Max(LastAdvancedAt, now);
            double advanceFrom = Math.Max(LastAdvancedAt, RetractAfter);
            if (currentTime > advanceFrom)
            {
                CutoffBorn = Math.Min(LatestBorn, CutoffBorn + (currentTime - advanceFrom) * Speed);
            }
            LastAdvancedAt = currentTime;
            if (currentTime >= RetractAfter && CutoffBorn >= LatestBorn - 0.000001)
            {
                CutoffBorn = LatestBorn;
                Dormant = true;
            }
            return CutoffBorn;
        }

        private void Rearm(double startedAt)
        {
            CutoffBorn = startedAt;
            LastAdvancedAt = startedAt;
            RetractAfter = startedAt + GameRetractionDelayMs / Speed;
            LatestBorn = startedAt;
            Ended = false;
            Dormant = false;
        }
    }
}
