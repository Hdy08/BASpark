namespace BASpark.Tests;

public class CurveTrailGeometryTests
{
    private const double RenderSegmentPx = 0.5;
    private const double CurveCollinearTurnRadians = Math.PI / 360;
    private const double CurveMaxControlTurnRadians = Math.PI / 60;
    private const double CurveFlatnessRatio = 0.05;
    private const int CurveMaxSubdivisionDepth = 12;

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
        });
        for (int index = 1; index < points.Count; index++)
        {
            Assert.True(points[index].Born >= points[index - 1].Born);
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

        Assert.Equal(3, SimplifyCurveTrailPath(lowTip).Count);
        Assert.Equal(3, SimplifyCurveTrailPath(highTip).Count);
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
    public void CurveInputSampling_CommitsMotionShorterThanVertexSpacing()
    {
        IReadOnlyList<TrailPoint> points = SampleAcceptedCurveInput(
            new(20, 30, 10),
            new(22, 31, 20),
            5.4);

        Assert.Single(points);
        Assert.Equal(new TrailPoint(22, 31, 20), points[0]);
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
            double beforeX = point.X - previous.X;
            double beforeY = point.Y - previous.Y;
            double afterX = next.X - point.X;
            double afterY = next.Y - point.Y;
            if (Magnitude(beforeX, beforeY) < 0.0001 || Magnitude(afterX, afterY) < 0.0001)
            {
                continue;
            }

            double turn = Math.Abs(Math.Atan2(
                beforeX * afterY - beforeY * afterX,
                beforeX * afterX + beforeY * afterY));
            if (turn > CurveCollinearTurnRadians)
            {
                simplified.Add(point);
            }
        }

        simplified.Add(source[^1]);
        return simplified;
    }

    private static List<TrailPoint> SampleAcceptedCurveInput(TrailPoint origin, TrailPoint target, double spacing)
    {
        double distance = Distance(origin, target);
        int count = Math.Max(1, (int)Math.Ceiling(distance / spacing));
        var result = new List<TrailPoint>(count);
        for (int index = 1; index <= count; index++)
        {
            double progress = (double)index / count;
            result.Add(new(
                Lerp(origin.X, target.X, progress),
                Lerp(origin.Y, target.Y, progress),
                Lerp(origin.Born, target.Born, progress)));
        }

        return result;
    }

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

            result.Add(new(x, y, Lerp(a0.Born, a1.Born, progress)));
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
                        double tangentScale = Math.Min(candidateLength, segmentLength * 2) / candidateLength;
                        tangent1X = candidateX * tangentScale;
                        tangent1Y = candidateY * tangentScale;
                    }
                }
            }

            double cp1X = a0.X + tangent0X / 3;
            double cp1Y = a0.Y + tangent0Y / 3;
            double cp2X = a1.X - tangent1X / 3;
            double cp2Y = a1.Y - tangent1Y / 3;
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

    private static double Distance(TrailPoint a, TrailPoint b) => Distance(a.X, a.Y, b.X, b.Y);

    private static double Magnitude(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double Distance(double x0, double y0, double x1, double y1) =>
        Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private readonly record struct TrailPoint(double X, double Y, double Born);
}
