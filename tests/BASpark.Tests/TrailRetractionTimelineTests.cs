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
        timeline.Input(100);

        double retractAfter = GameRetractionDelayMs / speed;
        Assert.Equal(0, timeline.Advance(retractAfter - 0.001), 9);
        Assert.Equal(expectedCutoff, timeline.Advance(retractAfter + 50), 9);
    }

    [Fact]
    public void SpeedIncreaseDuringDelay_RescalesRemainingDelayByOldOverNew()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 1);
        timeline.Input(50);

        timeline.SetSpeed(speed: 2, now: 100);

        double expectedRetractAfter = 100 + (GameRetractionDelayMs - 100) / 2;
        Assert.Equal(0, timeline.Advance(expectedRetractAfter - 0.001), 9);
        Assert.Equal(20, timeline.Advance(expectedRetractAfter + 10), 9);
    }

    [Fact]
    public void SpeedDecreaseDuringDelay_RescalesRemainingDelayByOldOverNew()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 2);
        timeline.Input(25);

        timeline.SetSpeed(speed: 0.5, now: 50);

        double expectedRetractAfter = 50 + (GameRetractionDelayMs - 100) / 0.5;
        Assert.Equal(0, timeline.Advance(expectedRetractAfter - 0.001), 9);
        Assert.Equal(5, timeline.Advance(expectedRetractAfter + 10), 9);
    }

    [Fact]
    public void SpeedChangeAfterRetractionStarts_OnlyAffectsFutureAdvance()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 1);
        for (double now = 100; now <= 400; now += 100)
        {
            timeline.Input(now);
        }

        double cutoffBeforeSpeedChange = 400 - GameRetractionDelayMs;
        Assert.Equal(cutoffBeforeSpeedChange, timeline.CutoffTrailBorn, 9);
        timeline.SetSpeed(speed: 2, now: 400);

        Assert.Equal(cutoffBeforeSpeedChange, timeline.CutoffTrailBorn, 9);
        Assert.Equal(cutoffBeforeSpeedChange + 100, timeline.Advance(450), 9);
    }

    [Fact]
    public void ReleasedStroke_ContinuesFromHeldCutoffWithoutJumping()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 2);
        for (double now = 50; now <= 200; now += 50)
        {
            timeline.Input(now);
        }
        double cutoffAtRelease = timeline.End(200);

        Assert.Equal(cutoffAtRelease, timeline.Advance(200), 9);
        Assert.Equal(cutoffAtRelease + 100, timeline.Advance(250), 9);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void TenSecondContinuousInput_KeepsOneGameDelayAndClearsInDelayOverSpeed(double speed)
    {
        const double releaseAt = 10_000;
        const double inputIntervalMs = 10;
        var timeline = new TrailTimeline(startedAt: 0, speed);

        for (double now = inputIntervalMs; now <= releaseAt; now += inputIntervalMs)
        {
            timeline.Input(now);
        }

        double outstandingTrailTime = timeline.LatestTrailBorn - timeline.CutoffTrailBorn;
        Assert.Equal(GameRetractionDelayMs, outstandingTrailTime, 6);

        timeline.End(releaseAt);
        double clearAt = releaseAt + GameRetractionDelayMs / speed;
        Assert.False(timeline.Dormant);
        timeline.Advance(clearAt - 0.001);
        Assert.False(timeline.Dormant);
        timeline.Advance(clearAt);
        Assert.True(timeline.Dormant);
        Assert.Equal(timeline.LatestTrailBorn, timeline.CutoffTrailBorn, 6);
    }

    [Fact]
    public void ContinuousInput_SpeedChangesKeepTrailClockAndRetractionContinuous()
    {
        var timeline = new TrailTimeline(startedAt: 0, speed: 1);
        DriveInput(timeline, fromExclusive: 0, throughInclusive: 2_990, intervalMs: 10);

        double cutoffBeforeFirstChange = timeline.CutoffTrailBorn;
        double latestBeforeFirstChange = timeline.LatestTrailBorn;
        timeline.SetSpeed(speed: 0.5, now: 3_000);

        Assert.Equal(cutoffBeforeFirstChange + 10, timeline.CutoffTrailBorn, 6);
        Assert.Equal(latestBeforeFirstChange, timeline.LatestTrailBorn, 6);
        Assert.Equal(3_000, timeline.InputTrailBorn, 6);

        DriveInput(timeline, fromExclusive: 3_000, throughInclusive: 5_990, intervalMs: 10);
        Assert.Equal(GameRetractionDelayMs, timeline.LatestTrailBorn - timeline.CutoffTrailBorn, 6);

        double cutoffBeforeSecondChange = timeline.CutoffTrailBorn;
        double latestBeforeSecondChange = timeline.LatestTrailBorn;
        timeline.SetSpeed(speed: 2, now: 6_000);

        Assert.Equal(cutoffBeforeSecondChange + 5, timeline.CutoffTrailBorn, 6);
        Assert.Equal(latestBeforeSecondChange, timeline.LatestTrailBorn, 6);
        Assert.Equal(4_500, timeline.InputTrailBorn, 6);

        DriveInput(timeline, fromExclusive: 6_000, throughInclusive: 9_000, intervalMs: 10);
        Assert.Equal(10_500, timeline.LatestTrailBorn, 6);
        Assert.Equal(GameRetractionDelayMs, timeline.LatestTrailBorn - timeline.CutoffTrailBorn, 6);

        timeline.End(9_000);
        timeline.Advance(9_000 + GameRetractionDelayMs / 2);
        Assert.True(timeline.Dormant);
        Assert.Equal(timeline.LatestTrailBorn, timeline.CutoffTrailBorn, 6);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void ResumedInput_RearmsRepeatedFullyRetractedRunsWithoutTimelineDrift(double speed)
    {
        var timeline = new TrailTimeline(startedAt: 0, speed);
        double expectedDuration = GameRetractionDelayMs / speed + 100;

        for (int cycle = 0; cycle < 4; cycle++)
        {
            double startedAt = cycle * 2_000;
            timeline.Input(startedAt);
            timeline.Input(startedAt + 100);

            Assert.Equal(startedAt, timeline.Advance(startedAt + GameRetractionDelayMs / speed - 0.001), 9);
            Assert.False(timeline.Dormant);
            Assert.Equal(startedAt + 100 * speed, timeline.Advance(startedAt + expectedDuration), 9);
            Assert.True(timeline.Dormant);

            double settledCutoff = timeline.CutoffTrailBorn;
            Assert.Equal(settledCutoff, timeline.Advance(startedAt + expectedDuration + 1_000), 9);
            Assert.Equal(timeline.LatestTrailBorn, timeline.CutoffTrailBorn, 9);
        }
    }

    private static void DriveInput(
        TrailTimeline timeline,
        double fromExclusive,
        double throughInclusive,
        double intervalMs)
    {
        for (double now = fromExclusive + intervalMs; now <= throughInclusive; now += intervalMs)
        {
            timeline.Input(now);
        }
    }

    private sealed class TrailTimeline
    {
        public TrailTimeline(double startedAt, double speed)
        {
            Speed = speed;
            Rearm(startedAt);
        }

        public double CutoffTrailBorn { get; private set; }
        public double LastAdvancedAt { get; private set; }
        public double WaitElapsedMs { get; private set; }
        public double InputTrailBorn { get; private set; }
        public double LastInputAt { get; private set; }
        public double LatestTrailBorn { get; private set; }
        public double Speed { get; private set; }
        public bool Ended { get; private set; }
        public bool Dormant { get; private set; }

        public double Input(double now)
        {
            Advance(now);
            if (Dormant)
            {
                Rearm(now);
            }

            double trailBorn = AdvanceInputClock(now);
            LatestTrailBorn = Math.Max(LatestTrailBorn, trailBorn);
            return trailBorn;
        }

        public double End(double now)
        {
            double cutoff = Advance(now);
            Ended = true;
            return cutoff;
        }

        public void SetSpeed(double speed, double now)
        {
            if (speed == Speed)
            {
                return;
            }

            Advance(now);
            if (!Ended && !Dormant)
            {
                AdvanceInputClock(now);
            }
            Speed = speed;
        }

        public double Advance(double now)
        {
            if (Dormant)
            {
                return CutoffTrailBorn;
            }

            double currentTime = Math.Max(LastAdvancedAt, now);
            double scaledElapsed = (currentTime - LastAdvancedAt) * Speed;
            if (WaitElapsedMs < GameRetractionDelayMs)
            {
                double waitRemaining = GameRetractionDelayMs - WaitElapsedMs;
                double waitAdvance = Math.Min(waitRemaining, scaledElapsed);
                WaitElapsedMs += waitAdvance;
                scaledElapsed -= waitAdvance;
            }
            if (scaledElapsed > 0)
            {
                CutoffTrailBorn = Math.Min(LatestTrailBorn, CutoffTrailBorn + scaledElapsed);
            }
            LastAdvancedAt = currentTime;
            if (WaitElapsedMs >= GameRetractionDelayMs - 0.000001 &&
                CutoffTrailBorn >= LatestTrailBorn - 0.000001)
            {
                CutoffTrailBorn = LatestTrailBorn;
                Dormant = true;
            }
            return CutoffTrailBorn;
        }

        private double AdvanceInputClock(double now)
        {
            double currentTime = Math.Max(LastInputAt, now);
            if (currentTime > LastInputAt)
            {
                InputTrailBorn += (currentTime - LastInputAt) * Speed;
            }
            LastInputAt = currentTime;
            return InputTrailBorn;
        }

        private void Rearm(double startedAt)
        {
            CutoffTrailBorn = startedAt;
            LastAdvancedAt = startedAt;
            WaitElapsedMs = 0;
            InputTrailBorn = startedAt;
            LastInputAt = startedAt;
            LatestTrailBorn = startedAt;
            Ended = false;
            Dormant = false;
        }
    }
}
