namespace BASpark.Tests;

public class CurveTrailGeometryTests
{
    private const double RenderSegmentPx = 0.5;
    private const double CurveCollinearTurnRadians = Math.PI / 360;
    private const double CurveMaxControlTurnRadians = Math.PI / 60;
    private const double CurveFlatnessRatio = 0.05;
    private const int CurveMaxSubdivisionDepth = 12;
    private const double CurveFitTolerancePx = 1.0125;
    private const double CurveTangentScale = 0.75;

    [Fact]
    public void CentripetalCurve_RemainsDenseAndRoundedAcrossRapidTurns()
    {
        TrailPoint[][] cases =
        [
            [
                new(0, 0, 0),
                new(96, 0, 33.3),
                new(96, 1, 66.7)
            ],
            [
                new(0, 0, 0),
                new(120, 0, 33.3),
                new(121, 1, 66.7),
                new(241, 1, 100)
            ],
            [
                new(0, 0, 0),
                new(100, 0, 33.3),
                new(0, 10, 66.7)
            ],
            [
                new(0, 0, 0),
                new(100, 0, 33.3),
                new(100, 100, 66.7)
            ]
        ];

        foreach (TrailPoint[] source in cases)
        {
            IReadOnlyList<TrailPoint> points = CurveTrailPath(source, RenderSegmentPx);

            Assert.Equal(source[0], points[0]);
            Assert.Equal(source[^1], points[^1]);
            Assert.All(points, point =>
            {
                Assert.True(double.IsFinite(point.X));
                Assert.True(double.IsFinite(point.Y));
                Assert.True(double.IsFinite(point.Born));
                Assert.True(double.IsFinite(point.TrailBorn));
            });
            double maxStep = MaxStep(points);
            double maxTurnDegrees = MaxTurnDegrees(points);
            Assert.True(
                maxStep <= RenderSegmentPx * 1.5,
                $"Curve sample step {maxStep:F4}px exceeded the density limit.");
            Assert.True(
                maxTurnDegrees < 6,
                $"Curve sample turn {maxTurnDegrees:F2} degrees remained visibly angular.");
            Assert.True(points.Count <= 600, $"Curve subdivision produced {points.Count} points.");

            for (int index = 1; index < points.Count; index++)
            {
                Assert.True(points[index].Born >= points[index - 1].Born);
                Assert.True(points[index].TrailBorn >= points[index - 1].TrailBorn);
            }
        }
    }

    [Fact]
    public void CentripetalCurve_DoesNotLoopAroundShortCornerSegments()
    {
        TrailPoint[] source =
        [
            new(0, 0, 0),
            new(100, 0, 100),
            new(100, 1, 101)
        ];

        IReadOnlyList<TrailPoint> points = CurveTrailPath(source, RenderSegmentPx);

        Assert.True(MaxDistanceOutsideBounds(points, source) <= 2);
    }

    [Fact]
    public void CentripetalCurve_KeepsRepeatedPointsFinite()
    {
        TrailPoint[] source =
        [
            new(0, 0, 0),
            new(0, 0, 1),
            new(10, 0, 2),
            new(10, 0, 3)
        ];

        IReadOnlyList<TrailPoint> points = CurveTrailPath(source, RenderSegmentPx);

        Assert.Equal(source[0], points[0]);
        Assert.Equal(source[^1], points[^1]);
        Assert.All(points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.True(double.IsFinite(point.Born));
            Assert.True(double.IsFinite(point.TrailBorn));
        });
        for (int index = 1; index < points.Count; index++)
        {
            Assert.True(points[index].Born >= points[index - 1].Born);
            Assert.True(points[index].TrailBorn >= points[index - 1].TrailBorn);
        }
    }

    [Fact]
    public void CurveSimplification_PreservesSharpAnchorAcrossSmallTipMotion()
    {
        TrailPoint[] lowTip =
        [
            new(0, 0, 0),
            new(100, 0, 33.3),
            new(100, 1.35, 66.7)
        ];
        TrailPoint[] highTip =
        [
            new(0, 0, 0),
            new(100, 0, 33.3),
            new(100, 1.5, 66.7)
        ];

        Assert.Equal(3, StabilizeCurveTrailPath(lowTip).Count);
        Assert.Equal(3, StabilizeCurveTrailPath(highTip).Count);
    }

    [Fact]
    public void CurveInputSampling_CommitsEachAcceptedPointerAnchor()
    {
        IReadOnlyList<TrailPoint> horizontal = SampleAcceptedCurveInput(
            new(0, 0, 0),
            new(100, 0, 33.3),
            5.4);
        IReadOnlyList<TrailPoint> vertical = SampleAcceptedCurveInput(
            horizontal[^1],
            new(100, 60, 66.7),
            5.4);

        Assert.Equal(new TrailPoint(100, 0, 33.3), horizontal[^1]);
        Assert.Equal(new TrailPoint(100, 60, 66.7), vertical[^1]);
        Assert.All(vertical, point => Assert.Equal(100, point.X, 10));
    }

    [Fact]
    public void CurveInputSampling_LeavesSubSpacingMotionToTheLiveTip()
    {
        IReadOnlyList<TrailPoint> points = SampleAcceptedCurveInput(
            new(20, 30, 10),
            new(22, 31, 20),
            5.4);

        Assert.Empty(points);
    }

    [Fact]
    public void CurveInputSampling_DoesNotSplitBarelyAcceptedMotionIntoShortAnchors()
    {
        TrailPoint target = new(5.5, 0.2, 20);

        IReadOnlyList<TrailPoint> points = SampleAcceptedCurveInput(
            new(0, 0, 10),
            target,
            5.4);

        Assert.Single(points);
        Assert.Equal(target, points[0]);
    }

    [Fact]
    public void SpatialSimplification_RemovesCollinearDiagonalAnchors()
    {
        TrailPoint[] source =
        [
            new(0, 0, 0),
            new(5.4, 1, 10),
            new(10.8, 2, 20)
        ];

        IReadOnlyList<TrailPoint> simplified = SimplifyTrailPath(source, CurveFitTolerancePx);

        Assert.Equal([source[0], source[^1]], simplified);
    }

    [Fact]
    public void CurveFit_SuppressesSlowQuantizedStraightLineWaves()
    {
        var source = new List<TrailPoint>();
        for (int index = 0; index <= 24; index++)
        {
            double x = index * 5.4;
            source.Add(new(x, Math.Round(x * 0.2), index * 16.7));
        }

        List<TrailPoint> stabilized = StabilizeCurveTrailPath(source);
        IReadOnlyList<TrailPoint> sampled = CurveTrailPath(stabilized, RenderSegmentPx);

        Assert.InRange(stabilized.Count, 2, 3);
        Assert.True(
            MaxDistanceFromChord(sampled) <= CurveFitTolerancePx,
            "Quantized slow input was amplified into a visible wave.");
    }

    [Fact]
    public void CurveFit_SuppressesAlternatingLowSpeedPointerNoise()
    {
        var source = new List<TrailPoint>();
        for (int index = 0; index <= 24; index++)
        {
            double y = index == 0 || index == 24 ? 0 : (index % 2 == 0 ? -0.45 : 0.45);
            source.Add(new(index * 5.4, y, index * 16.7));
        }

        List<TrailPoint> stabilized = StabilizeCurveTrailPath(source);
        IReadOnlyList<TrailPoint> sampled = CurveTrailPath(stabilized, RenderSegmentPx);

        Assert.Equal(2, stabilized.Count);
        Assert.True(
            MaxDistanceFromChord(sampled) < 0.0001,
            "Alternating low-speed noise survived as a fitted wave.");
    }

    [Fact]
    public void CurveFit_RoundsRapidTurnWithoutExcessiveOvershoot()
    {
        TrailPoint[] source =
        [
            new(0, 0, 0),
            new(100, 0, 33.3),
            new(100, 100, 66.7)
        ];

        IReadOnlyList<TrailPoint> sampled = CurveTrailPath(source, RenderSegmentPx);

        Assert.True(
            MaxDistanceOutsideBounds(sampled, source) <= 8,
            "The fitted corner expanded too far outside the pointer path.");
        Assert.True(MaxTurnDegrees(sampled) < 6, "The moderated curve became visibly angular.");
    }

    [Fact]
    public void CurveRetraction_PreservesCurvatureAfterCutoffPassesPenultimateAnchor()
    {
        TrailPoint[] source =
        [
            new(0, 0, 0),
            new(100, 0, 100),
            new(100, 100, 200)
        ];

        IReadOnlyList<TrailPoint> sampled = CurveTrailPath(source, RenderSegmentPx);
        IReadOnlyList<TrailPoint> clipped = ClipTrailPathToCutoff(sampled, 150);

        Assert.True(clipped.Count > 2, "The final curved source segment collapsed to a line.");
        Assert.Equal(150, clipped[0].TrailBorn, 10);
        Assert.Equal(source[^1], clipped[^1]);
        Assert.All(clipped, point => Assert.True(point.TrailBorn >= 150));
        Assert.True(
            MaxDistanceFromChord(clipped) > 0.5,
            "The visible final segment no longer retains its original curvature.");
    }

    [Fact]
    public void CurveRetraction_PreservesExistingGeometryForMinimalHistoryFixture()
    {
        double[] y = [-0.9, -0.8, -1.8, -1.2, -2.7];
        var source = new List<TrailPoint>();
        for (int index = 0; index < y.Length; index++)
        {
            source.Add(new(
                index * 5.4,
                y[index],
                1000 + index * 17,
                index));
        }

        int firstAlive = source.FindIndex(point => point.TrailBorn >= 2.25);
        Assert.Equal(0, CurveTrailHistoryStart(
            source,
            0,
            source.Count,
            firstAlive,
            CurveFitTolerancePx));

        AssertRetractionPruningPreservesGeometry(
            source,
            Enumerable.Range(1, 15).Select(index => index * 0.25));
    }

    [Fact]
    public void CurveRetraction_PreservesExistingGeometryForSlowNoisyCurve()
    {
        var source = new List<TrailPoint>();
        for (int index = 0; index < 45; index++)
        {
            double y = 20 * Math.Sin(index / 8.0) + (index % 2 == 0 ? -0.45 : 0.45);
            source.Add(new(
                index * 5.4,
                y,
                5000 + index * 16.7,
                index));
        }

        AssertRetractionPruningPreservesGeometry(
            source,
            Enumerable.Range(1, 175).Select(index => index * 0.25),
            requireDestructivePrune: true);
    }

    [Fact]
    public void CurveRetraction_PreservesExistingGeometryWithLiveTipContext()
    {
        double[] y = [-0.1, 0.7, -0.7, -0.5, 0.4, -1.1, -0.7];
        var source = new List<TrailPoint>();
        for (int index = 0; index < y.Length; index++)
        {
            source.Add(new(
                index * 5.4,
                y[index],
                9000 + index * 13,
                index));
        }
        var liveTip = new TrailPoint(7 * 5.4, 0, 9100, 7);

        int firstAlive = source.FindIndex(point => point.TrailBorn >= 4.25);
        Assert.Equal(1, CurveTrailHistoryStart(
            source,
            0,
            source.Count,
            firstAlive,
            CurveFitTolerancePx));
        Assert.Equal(0, CurveTrailHistoryStart(
            source,
            0,
            source.Count,
            firstAlive,
            CurveFitTolerancePx,
            liveTip));

        AssertRetractionPruningPreservesGeometry(
            source,
            Enumerable.Range(17, 11).Select(index => index * 0.25),
            liveTip);
    }

    [Fact]
    public void CurveRetraction_PreservesTurnAnchorAfterDenseHistoryPruning()
    {
        var source = new List<TrailPoint>();
        double trailBorn = 0;
        for (double x = 0; x <= 100; x += 5)
        {
            source.Add(new(x, 0, 1000 + trailBorn * 10, trailBorn++));
        }
        for (double y = 5; y <= 100; y += 5)
        {
            source.Add(new(100, y, 1000 + trailBorn * 10, trailBorn++));
        }

        const double firstCutoffBorn = 23.5;
        int firstAlive = source.FindIndex(point => point.TrailBorn >= firstCutoffBorn);
        List<TrailPoint> oldRawHistory = source[Math.Max(0, firstAlive - 2)..];
        IReadOnlyList<TrailPoint> oldClipped = ClipTrailPathToCutoff(
            CurveTrailPath(StabilizeCurveTrailPath(oldRawHistory), RenderSegmentPx),
            firstCutoffBorn);

        var retained = new List<TrailPoint>(source);
        for (double cutoffBorn = firstCutoffBorn; cutoffBorn <= 39.5; cutoffBorn += 0.5)
        {
            firstAlive = retained.FindIndex(point => point.TrailBorn >= cutoffBorn);
            int historyStart = CurveTrailHistoryStart(
                retained,
                0,
                retained.Count,
                firstAlive,
                CurveFitTolerancePx);
            retained = retained[historyStart..];

            IReadOnlyList<TrailPoint> actual = ClipTrailPathToCutoff(
                CurveTrailPath(StabilizeCurveTrailPath(retained), RenderSegmentPx),
                cutoffBorn);
            IReadOnlyList<TrailPoint> expected = ClipTrailPathToCutoff(
                CurveTrailPath(StabilizeCurveTrailPath(source), RenderSegmentPx),
                cutoffBorn);

            AssertPathEqual(expected, actual);
            Assert.True(actual.Count >= 2, $"The curve vanished early at cutoff {cutoffBorn:F1}.");
            if (cutoffBorn == firstCutoffBorn)
            {
                Assert.True(MaxDistanceFromChord(actual) > 4, "The retained final curve became visually straight.");
            }
        }

        Assert.True(MaxDistanceFromChord(oldClipped) < 0.0001, "The regression fixture no longer reproduces the old straight tail.");
    }

    [Fact]
    public void CurveCapacityCompaction_PreservesHistoricalTurnAnchors()
    {
        const double inputSpacing = 5.4;
        var source = new List<TrailPoint>();
        double born = 0;
        for (int x = 0; x <= 400; x++)
        {
            source.Add(new(x * inputSpacing, 0, born++));
        }
        for (int y = 1; y <= 599; y++)
        {
            source.Add(new(400 * inputSpacing, y * inputSpacing, born++));
        }

        var compacted = new List<TrailPoint>();
        foreach (TrailPoint point in source)
        {
            compacted.Add(point);
            compacted = CompactCurveTrailCapacity(compacted, 512);
        }
        List<TrailPoint> simplified = StabilizeCurveTrailPath(compacted);
        List<TrailPoint> expected = StabilizeCurveTrailPath(source);
        List<TrailPoint> oldHardTrim = source[^512..];

        Assert.True(compacted.Count <= 512);
        Assert.Equal(3, simplified.Count);
        AssertPathEqual(expected, simplified);
        Assert.Equal(source[0], simplified[0]);
        Assert.Equal(source[400], simplified[1]);
        Assert.True(MaxDistanceFromChord(CurveTrailPath(simplified, RenderSegmentPx)) > 5);
        Assert.Equal(2, StabilizeCurveTrailPath(oldHardTrim).Count);
    }

    [Fact]
    public void CurveRetraction_RemainsStableAfterRepeatedCapacityCompaction()
    {
        const int maximumPoints = 512;
        var retained = new List<TrailPoint>();
        for (int index = 0; index < 1024; index++)
        {
            double y = 30 * Math.Sin(index / 35.0) + (index % 2 == 0 ? -0.45 : 0.45);
            retained.Add(new(
                index * 5.4,
                y,
                5000 + index * 16.7,
                index));
            retained = CompactCurveTrailCapacity(retained, maximumPoints);
        }

        Assert.True(retained.Count <= maximumPoints);
        double firstTrailBorn = retained[0].TrailBorn;
        double retainedSpan = retained[^1].TrailBorn - firstTrailBorn;
        AssertRetractionPruningPreservesGeometry(
            retained,
            Enumerable.Range(1, 24).Select(index =>
                firstTrailBorn + retainedSpan * index / 30),
            requireDestructivePrune: true);
    }

    private static void AssertRetractionPruningPreservesGeometry(
        IReadOnlyList<TrailPoint> source,
        IEnumerable<double> cutoffs,
        TrailPoint? liveTip = null,
        bool requireDestructivePrune = false)
    {
        var fullSource = new List<TrailPoint>(source);
        if (liveTip.HasValue)
        {
            fullSource.Add(liveTip.Value);
        }
        IReadOnlyList<TrailPoint> fixedCurve = CurveTrailPath(
            StabilizeCurveTrailPath(fullSource),
            RenderSegmentPx);
        var retained = new List<TrailPoint>(source);
        IReadOnlyList<TrailPoint>? previousFrame = null;
        bool pruned = false;

        foreach (double cutoffTrailBorn in cutoffs)
        {
            int firstAlive = retained.FindIndex(point => point.TrailBorn >= cutoffTrailBorn);
            if (firstAlive < 0)
            {
                firstAlive = retained.Count;
            }
            int historyStart = CurveTrailHistoryStart(
                retained,
                0,
                retained.Count,
                firstAlive,
                CurveFitTolerancePx,
                liveTip);
            pruned |= historyStart > 0;
            retained = retained[historyStart..];

            var frameSource = new List<TrailPoint>(retained);
            if (liveTip.HasValue)
            {
                frameSource.Add(liveTip.Value);
            }
            IReadOnlyList<TrailPoint> actual = ClipTrailPathToCutoff(
                CurveTrailPath(StabilizeCurveTrailPath(frameSource), RenderSegmentPx),
                cutoffTrailBorn);
            IReadOnlyList<TrailPoint> expected = ClipTrailPathToCutoff(
                fixedCurve,
                cutoffTrailBorn);

            AssertPathEqual(expected, actual);
            if (previousFrame is not null)
            {
                AssertCommonGeometryEqual(previousFrame, actual, cutoffTrailBorn);
            }
            previousFrame = actual;
        }

        if (requireDestructivePrune)
        {
            Assert.True(pruned, "The fixture did not exercise destructive history pruning.");
        }
    }

    private static void AssertCommonGeometryEqual(
        IReadOnlyList<TrailPoint> previous,
        IReadOnlyList<TrailPoint> current,
        double cutoffTrailBorn)
    {
        if (previous.Count < 2 || current.Count < 2)
        {
            return;
        }

        double endTrailBorn = Math.Min(previous[^1].TrailBorn, current[^1].TrailBorn);
        int comparisonCount = 0;
        for (double trailBorn = cutoffTrailBorn + 0.125;
             trailBorn <= endTrailBorn + 0.000000001;
             trailBorn += 0.125)
        {
            TrailPoint previousPoint = InterpolateAtTrailBorn(previous, trailBorn);
            TrailPoint currentPoint = InterpolateAtTrailBorn(current, trailBorn);
            Assert.Equal(previousPoint.X, currentPoint.X, 8);
            Assert.Equal(previousPoint.Y, currentPoint.Y, 8);
            Assert.Equal(previousPoint.Born, currentPoint.Born, 8);
            Assert.Equal(trailBorn, currentPoint.TrailBorn, 8);
            comparisonCount++;
        }
        Assert.True(comparisonCount > 0, "Successive frames did not retain any comparable curve geometry.");
    }

    private static TrailPoint InterpolateAtTrailBorn(
        IReadOnlyList<TrailPoint> source,
        double trailBorn)
    {
        if (trailBorn <= source[0].TrailBorn)
        {
            return source[0];
        }
        for (int index = 1; index < source.Count; index++)
        {
            TrailPoint after = source[index];
            if (after.TrailBorn < trailBorn)
            {
                continue;
            }

            TrailPoint before = source[index - 1];
            double progress = Math.Clamp(
                (trailBorn - before.TrailBorn) /
                    Math.Max(0.001, after.TrailBorn - before.TrailBorn),
                0,
                1);
            return new(
                Lerp(before.X, after.X, progress),
                Lerp(before.Y, after.Y, progress),
                Lerp(before.Born, after.Born, progress),
                trailBorn);
        }
        return source[^1];
    }

    private static List<TrailPoint> SimplifyCurveTrailPath(IReadOnlyList<TrailPoint> source)
    {
        if (source.Count < 3)
        {
            return [.. source];
        }

        var simplified = new List<TrailPoint> { source[0] };
        for (int index = 1; index < source.Count - 1; index++)
        {
            TrailPoint previous = simplified[^1];
            TrailPoint point = source[index];
            TrailPoint next = source[index + 1];
            if (CurveTrailAnchorNeeded(previous, point, next))
            {
                simplified.Add(point);
            }
        }

        simplified.Add(source[^1]);
        return simplified;
    }

    private static bool CurveTrailAnchorNeeded(TrailPoint previous, TrailPoint point, TrailPoint next)
    {
        double beforeX = point.X - previous.X;
        double beforeY = point.Y - previous.Y;
        double afterX = next.X - point.X;
        double afterY = next.Y - point.Y;
        if (Magnitude(beforeX, beforeY) < 0.0001 || Magnitude(afterX, afterY) < 0.0001)
        {
            return false;
        }

        double turn = Math.Abs(Math.Atan2(
            beforeX * afterY - beforeY * afterX,
            beforeX * afterX + beforeY * afterY));
        return turn > CurveCollinearTurnRadians;
    }

    private static int CurveTrailHistoryStart(
        IReadOnlyList<TrailPoint> source,
        int strokeStart,
        int strokeEnd,
        int firstAlive,
        double tolerancePx,
        TrailPoint? virtualEnd = null)
    {
        int effectiveStrokeEnd = strokeEnd + (virtualEnd.HasValue ? 1 : 0);
        if (firstAlive <= strokeStart || effectiveStrokeEnd - strokeStart < 3)
        {
            return strokeStart;
        }

        List<TrailPoint> fitSource = source
            .Skip(strokeStart)
            .Take(strokeEnd - strokeStart)
            .ToList();
        if (virtualEnd.HasValue)
        {
            fitSource.Add(virtualEnd.Value);
        }
        List<TrailPoint> anchors = StabilizeCurveTrailPath(fitSource, tolerancePx);
        int olderAnchorIndex = strokeStart;
        int newerAnchorIndex = strokeStart;
        foreach (TrailPoint anchor in anchors)
        {
            int index = virtualEnd.HasValue && anchor.Equals(virtualEnd.Value)
                ? strokeEnd
                : IndexOf(source, anchor, strokeStart, strokeEnd);
            if (index < strokeStart || index >= firstAlive)
            {
                break;
            }
            olderAnchorIndex = newerAnchorIndex;
            newerAnchorIndex = index;
        }
        return olderAnchorIndex;
    }

    private static List<TrailPoint> CompactCurveTrailCapacity(
        IReadOnlyList<TrailPoint> source,
        int maximumPoints)
    {
        if (source.Count <= maximumPoints)
        {
            return [.. source];
        }

        const int historyBudget = 2;
        int firstKeep = source.Count - (maximumPoints - historyBudget);
        int historyStart = CurveTrailHistoryStart(
            source,
            0,
            source.Count,
            firstKeep,
            CurveFitTolerancePx);
        List<TrailPoint> historySource = source
            .Skip(historyStart)
            .Take(firstKeep - historyStart + 1)
            .ToList();
        List<TrailPoint> simplifiedHistory = StabilizeCurveTrailPath(historySource);
        List<TrailPoint> history = simplifiedHistory[..^1].TakeLast(historyBudget).ToList();
        history.AddRange(source.Skip(firstKeep));
        return history;
    }

    private static List<TrailPoint> SampleAcceptedCurveInput(TrailPoint origin, TrailPoint target, double spacing)
    {
        double distance = Distance(origin, target);
        if (distance < spacing)
        {
            return [];
        }

        int count = Math.Max(1, (int)Math.Floor(distance / spacing));
        var result = new List<TrailPoint>(count);
        for (int index = 1; index <= count; index++)
        {
            double progress = (double)index / count;
            result.Add(new(
                Lerp(origin.X, target.X, progress),
                Lerp(origin.Y, target.Y, progress),
                Lerp(origin.Born, target.Born, progress),
                Lerp(origin.TrailBorn, target.TrailBorn, progress)));
        }

        return result;
    }

    private static List<TrailPoint> SimplifyTrailPath(
        IReadOnlyList<TrailPoint> source,
        double tolerancePx)
    {
        if (source.Count < 3 || tolerancePx <= 0)
        {
            return [.. source];
        }

        double toleranceSquared = tolerancePx * tolerancePx;
        var simplified = new List<TrailPoint> { source[0] };
        for (int index = 1; index < source.Count - 1; index++)
        {
            TrailPoint first = simplified[^1];
            TrailPoint point = source[index];
            TrailPoint last = source[index + 1];
            double dx = last.X - first.X;
            double dy = last.Y - first.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0.000001)
            {
                simplified.Add(point);
                continue;
            }

            double offsetX = point.X - first.X;
            double offsetY = point.Y - first.Y;
            double projection = offsetX * dx + offsetY * dy;
            double cross = Math.Abs(dx * offsetY - offsetX * dy);
            double distanceSquared = cross * cross / lengthSquared;
            bool liesBetweenNeighbors = projection >= 0 && projection <= lengthSquared;
            if (!liesBetweenNeighbors || distanceSquared > toleranceSquared)
            {
                simplified.Add(point);
            }
        }

        simplified.Add(source[^1]);
        return simplified;
    }

    private static List<TrailPoint> StabilizeCurveTrailPath(
        IReadOnlyList<TrailPoint> source,
        double tolerancePx = CurveFitTolerancePx) =>
        SimplifyCurveTrailPath(SimplifyTrailPath(source, tolerancePx));

    private static List<TrailPoint> CurveTrailPath(IReadOnlyList<TrailPoint> source, double renderSegmentPx)
    {
        if (source.Count < 3)
        {
            return [.. source];
        }

        var result = new List<TrailPoint> { source[0] };
        int lastIndex = source.Count - 1;
        double sampleSpacing = Math.Max(0.05, renderSegmentPx);

        void AppendSample(TrailPoint a0, TrailPoint a1, double x, double y, double progress)
        {
            TrailPoint previousPoint = result[^1];
            if (Distance(previousPoint.X, previousPoint.Y, x, y) < 0.0001 && progress < 1)
            {
                return;
            }

            result.Add(new(
                x,
                y,
                Lerp(a0.Born, a1.Born, progress),
                Lerp(a0.TrailBorn, a1.TrailBorn, progress)));
        }

        void SampleCubic(
            TrailPoint a0,
            TrailPoint a1,
            double p0X,
            double p0Y,
            double p1X,
            double p1Y,
            double p2X,
            double p2Y,
            double p3X,
            double p3Y,
            double t0,
            double t1,
            int depth)
        {
            double edge0X = p1X - p0X;
            double edge0Y = p1Y - p0Y;
            double edge1X = p2X - p1X;
            double edge1Y = p2Y - p1Y;
            double edge2X = p3X - p2X;
            double edge2Y = p3Y - p2Y;
            double controlPolygonLength =
                Magnitude(edge0X, edge0Y) +
                Magnitude(edge1X, edge1Y) +
                Magnitude(edge2X, edge2Y);
            double chordX = p3X - p0X;
            double chordY = p3Y - p0Y;
            double chordLength = Magnitude(chordX, chordY);
            double flatness = chordLength < 0.00000001
                ? Math.Max(Magnitude(p1X - p0X, p1Y - p0Y), Magnitude(p2X - p0X, p2Y - p0Y))
                : Math.Max(
                    Math.Abs(chordX * (p1Y - p0Y) - chordY * (p1X - p0X)) / chordLength,
                    Math.Abs(chordX * (p2Y - p0Y) - chordY * (p2X - p0X)) / chordLength);
            double maximumControlTurn = Math.Max(
                VectorTurn(edge0X, edge0Y, edge1X, edge1Y),
                VectorTurn(edge1X, edge1Y, edge2X, edge2Y));
            bool shouldSubdivide = depth < CurveMaxSubdivisionDepth && (
                maximumControlTurn > CurveMaxControlTurnRadians ||
                flatness > sampleSpacing * CurveFlatnessRatio);

            if (shouldSubdivide)
            {
                double p01X = (p0X + p1X) * 0.5;
                double p01Y = (p0Y + p1Y) * 0.5;
                double p12X = (p1X + p2X) * 0.5;
                double p12Y = (p1Y + p2Y) * 0.5;
                double p23X = (p2X + p3X) * 0.5;
                double p23Y = (p2Y + p3Y) * 0.5;
                double p012X = (p01X + p12X) * 0.5;
                double p012Y = (p01Y + p12Y) * 0.5;
                double p123X = (p12X + p23X) * 0.5;
                double p123Y = (p12Y + p23Y) * 0.5;
                double midpointX = (p012X + p123X) * 0.5;
                double midpointY = (p012Y + p123Y) * 0.5;
                double midpointT = (t0 + t1) * 0.5;
                SampleCubic(
                    a0, a1,
                    p0X, p0Y, p01X, p01Y, p012X, p012Y, midpointX, midpointY,
                    t0, midpointT, depth + 1);
                SampleCubic(
                    a0, a1,
                    midpointX, midpointY, p123X, p123Y, p23X, p23Y, p3X, p3Y,
                    midpointT, t1, depth + 1);
                return;
            }

            int sampleCount = Math.Max(1, (int)Math.Ceiling(controlPolygonLength / sampleSpacing));
            for (int sample = 1; sample <= sampleCount; sample++)
            {
                double localProgress = (double)sample / sampleCount;
                AppendSample(
                    a0,
                    a1,
                    Cubic(p0X, p1X, p2X, p3X, localProgress),
                    Cubic(p0Y, p1Y, p2Y, p3Y, localProgress),
                    Lerp(t0, t1, localProgress));
            }
        }

        for (int index = 0; index < lastIndex; index++)
        {
            TrailPoint a0 = source[index];
            TrailPoint a1 = source[index + 1];
            TrailPoint previous = index > 0
                ? source[index - 1]
                : new(2 * a0.X - a1.X, 2 * a0.Y - a1.Y, 0);
            TrailPoint next = index < lastIndex - 1
                ? source[index + 2]
                : new(2 * a1.X - a0.X, 2 * a1.Y - a0.Y, 0);
            double previousInterval = KnotInterval(previous, a0);
            double segmentInterval = KnotInterval(a0, a1);
            double nextInterval = KnotInterval(a1, next);
            double previousSpan = previousInterval + segmentInterval;
            double nextSpan = segmentInterval + nextInterval;
            double tangent0X = segmentInterval * (
                (a0.X - previous.X) / previousInterval -
                (a1.X - previous.X) / previousSpan +
                (a1.X - a0.X) / segmentInterval);
            double tangent0Y = segmentInterval * (
                (a0.Y - previous.Y) / previousInterval -
                (a1.Y - previous.Y) / previousSpan +
                (a1.Y - a0.Y) / segmentInterval);
            double tangent1X = segmentInterval * (
                (a1.X - a0.X) / segmentInterval -
                (next.X - a0.X) / nextSpan +
                (next.X - a1.X) / nextInterval);
            double tangent1Y = segmentInterval * (
                (a1.Y - a0.Y) / segmentInterval -
                (next.Y - a0.Y) / nextSpan +
                (next.Y - a1.Y) / nextInterval);
            if (index == lastIndex - 1 && index > 0)
            {
                double previousLength = Distance(previous, a0);
                double segmentLength = Distance(a0, a1);
                double lengthRatio = segmentLength / Math.Max(0.0001, previousLength);
                if (lengthRatio >= 0.5 && lengthRatio <= 2)
                {
                    double candidateX = (3 * a1.X - 4 * a0.X + previous.X) * 0.5;
                    double candidateY = (3 * a1.Y - 4 * a0.Y + previous.Y) * 0.5;
                    double candidateLength = Magnitude(candidateX, candidateY);
                    double forwardProjection = (
                        candidateX * (a1.X - a0.X) + candidateY * (a1.Y - a0.Y)
                    ) / Math.Max(0.0001, segmentLength);
                    if (candidateLength >= segmentLength * 0.25 && forwardProjection > 0)
                    {
                        double tangentScale = Math.Min(candidateLength, segmentLength) / candidateLength;
                        tangent1X = candidateX * tangentScale;
                        tangent1Y = candidateY * tangentScale;
                    }
                }
            }

            double cp1X = a0.X + tangent0X * CurveTangentScale / 3;
            double cp1Y = a0.Y + tangent0Y * CurveTangentScale / 3;
            double cp2X = a1.X - tangent1X * CurveTangentScale / 3;
            double cp2Y = a1.Y - tangent1Y * CurveTangentScale / 3;
            SampleCubic(
                a0,
                a1,
                a0.X,
                a0.Y,
                cp1X,
                cp1Y,
                cp2X,
                cp2Y,
                a1.X,
                a1.Y,
                0,
                1,
                0);
        }

        return result;
    }

    private static List<TrailPoint> ClipTrailPathToCutoff(
        IReadOnlyList<TrailPoint> source,
        double cutoffTrailBorn)
    {
        if (source.Count < 2 || !double.IsFinite(cutoffTrailBorn))
        {
            return [.. source];
        }

        int firstAlive = 0;
        while (firstAlive < source.Count && source[firstAlive].TrailBorn < cutoffTrailBorn)
        {
            firstAlive++;
        }
        if (firstAlive == 0)
        {
            return [.. source];
        }
        if (firstAlive == source.Count)
        {
            return [];
        }

        TrailPoint before = source[firstAlive - 1];
        TrailPoint after = source[firstAlive];
        double progress = Math.Clamp(
            (cutoffTrailBorn - before.TrailBorn) /
                Math.Max(0.001, after.TrailBorn - before.TrailBorn),
            0,
            1);
        double x = Lerp(before.X, after.X, progress);
        double y = Lerp(before.Y, after.Y, progress);
        double born = Lerp(before.Born, after.Born, progress);
        var result = new List<TrailPoint>(source.Count - firstAlive + 1);
        if (Distance(x, y, after.X, after.Y) >= 0.0001)
        {
            result.Add(new(x, y, born, cutoffTrailBorn));
        }
        for (int index = firstAlive; index < source.Count; index++)
        {
            result.Add(source[index]);
        }
        return result;
    }

    private static double KnotInterval(TrailPoint a, TrailPoint b) =>
        Math.Max(0.0001, Math.Sqrt(Distance(a.X, a.Y, b.X, b.Y)));

    private static double Cubic(double p0, double p1, double p2, double p3, double t)
    {
        double inverse = 1 - t;
        return inverse * inverse * inverse * p0 +
            3 * inverse * inverse * t * p1 +
            3 * inverse * t * t * p2 +
            t * t * t * p3;
    }

    private static double VectorTurn(double ax, double ay, double bx, double by)
    {
        if (Magnitude(ax, ay) < 0.00000001 || Magnitude(bx, by) < 0.00000001)
        {
            return 0;
        }

        return Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by));
    }

    private static double MaxStep(IReadOnlyList<TrailPoint> points)
    {
        double maximum = 0;
        for (int index = 1; index < points.Count; index++)
        {
            maximum = Math.Max(maximum, Distance(points[index - 1], points[index]));
        }

        return maximum;
    }

    private static double MaxTurnDegrees(IReadOnlyList<TrailPoint> points)
    {
        double maximum = 0;
        for (int index = 1; index < points.Count - 1; index++)
        {
            double beforeX = points[index].X - points[index - 1].X;
            double beforeY = points[index].Y - points[index - 1].Y;
            double afterX = points[index + 1].X - points[index].X;
            double afterY = points[index + 1].Y - points[index].Y;
            double beforeLength = Math.Sqrt(beforeX * beforeX + beforeY * beforeY);
            double afterLength = Math.Sqrt(afterX * afterX + afterY * afterY);
            if (beforeLength < 0.00000001 || afterLength < 0.00000001)
            {
                continue;
            }

            double cosine = Math.Clamp(
                (beforeX * afterX + beforeY * afterY) / (beforeLength * afterLength),
                -1,
                1);
            maximum = Math.Max(maximum, Math.Acos(cosine) * 180 / Math.PI);
        }

        return maximum;
    }

    private static double MaxDistanceOutsideBounds(
        IReadOnlyList<TrailPoint> points,
        IReadOnlyList<TrailPoint> source)
    {
        double minimumX = source.Min(point => point.X);
        double maximumX = source.Max(point => point.X);
        double minimumY = source.Min(point => point.Y);
        double maximumY = source.Max(point => point.Y);

        return points.Max(point =>
        {
            double outsideX = Math.Max(Math.Max(minimumX - point.X, 0), point.X - maximumX);
            double outsideY = Math.Max(Math.Max(minimumY - point.Y, 0), point.Y - maximumY);
            return Math.Sqrt(outsideX * outsideX + outsideY * outsideY);
        });
    }

    private static double MaxDistanceFromChord(IReadOnlyList<TrailPoint> points)
    {
        TrailPoint start = points[0];
        TrailPoint end = points[^1];
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Max(0.000001, Magnitude(dx, dy));
        return points.Max(point => Math.Abs(
            dx * (start.Y - point.Y) - (start.X - point.X) * dy) / length);
    }

    private static void AssertPathEqual(
        IReadOnlyList<TrailPoint> expected,
        IReadOnlyList<TrailPoint> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].X, actual[index].X, 9);
            Assert.Equal(expected[index].Y, actual[index].Y, 9);
            Assert.Equal(expected[index].Born, actual[index].Born, 9);
            Assert.Equal(expected[index].TrailBorn, actual[index].TrailBorn, 9);
        }
    }

    private static int IndexOf(
        IReadOnlyList<TrailPoint> source,
        TrailPoint point,
        int start,
        int end)
    {
        for (int index = start; index < end; index++)
        {
            if (source[index].Equals(point))
            {
                return index;
            }
        }
        return -1;
    }

    private static double Distance(TrailPoint a, TrailPoint b) => Distance(a.X, a.Y, b.X, b.Y);

    private static double Magnitude(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double Distance(double x0, double y0, double x1, double y1) =>
        Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private readonly record struct TrailPoint(
        double X,
        double Y,
        double Born,
        double TrailBorn)
    {
        public TrailPoint(double x, double y, double born)
            : this(x, y, born, born)
        {
        }
    }
}
