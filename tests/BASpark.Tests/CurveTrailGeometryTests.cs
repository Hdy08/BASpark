namespace BASpark.Tests;

public class CurveTrailGeometryTests
{
    private const double RenderSegmentPx = 0.5;

    [Fact]
    public void CentripetalCurve_RemainsDenseAndRoundedAcrossRapidTurns()
    {
        TrailPoint[][] cases =
        [
            [
                new(0, 0, 0),
                new(100, 0, 100),
                new(100, 1, 101)
            ],
            [
                new(0, 0, 0),
                new(100, 0, 100),
                new(101, 1, 101),
                new(201, 1, 201)
            ],
            [
                new(0, 0, 0),
                new(100, 0, 100),
                new(100, 100, 200)
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
                maxTurnDegrees < 45,
                $"Curve sample turn {maxTurnDegrees:F2} degrees remained visibly angular.");

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

    private static List<TrailPoint> CurveTrailPath(IReadOnlyList<TrailPoint> source, double renderSegmentPx)
    {
        var result = new List<TrailPoint> { source[0] };
        int lastIndex = source.Count - 1;
        double sampleSpacing = Math.Max(0.05, renderSegmentPx);

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
            double cp1X = a0.X + tangent0X / 3;
            double cp1Y = a0.Y + tangent0Y / 3;
            double cp2X = a1.X - tangent1X / 3;
            double cp2Y = a1.Y - tangent1Y / 3;
            double controlPolygonLength =
                Distance(a0.X, a0.Y, cp1X, cp1Y) +
                Distance(cp1X, cp1Y, cp2X, cp2Y) +
                Distance(cp2X, cp2Y, a1.X, a1.Y);
            int sampleCount = Math.Max(1, (int)Math.Ceiling(controlPolygonLength / sampleSpacing));

            for (int sample = 1; sample <= sampleCount; sample++)
            {
                double t = (double)sample / sampleCount;
                double x = Cubic(a0.X, cp1X, cp2X, a1.X, t);
                double y = Cubic(a0.Y, cp1Y, cp2Y, a1.Y, t);
                TrailPoint previousPoint = result[^1];
                if (Distance(previousPoint.X, previousPoint.Y, x, y) < 0.0001 && sample < sampleCount)
                {
                    continue;
                }

                result.Add(new(x, y, Lerp(a0.Born, a1.Born, t)));
            }
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

    private static double Distance(double x0, double y0, double x1, double y1) =>
        Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private readonly record struct TrailPoint(double X, double Y, double Born);
}
