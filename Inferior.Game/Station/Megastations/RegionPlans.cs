namespace Inferior.Game.StationGen.Megastations;

public sealed record EdgeRegionPlan(
    string Id,
    GridDirection A,
    GridDirection B,
    GridAxis LengthAxis,
    int StartCornerDepthA,
    int StartCornerDepthB,
    int EndCornerDepthA,
    int EndCornerDepthB,
    int[] DepthA,
    int[] DepthB,
    string ProfileSummary);

public sealed record CornerRegionPlan(
    string Id,
    GridDirection A,
    GridDirection B,
    GridDirection C,
    int DepthA,
    int DepthB,
    int DepthC,
    string Summary);

public static class RegionIdentity
{
    public static string Face(GridDirection direction) => $"face.{Direction.Id(direction)}";

    public static string Edge(GridDirection a, GridDirection b)
    {
        var ordered = Ordered([a, b]);
        return $"edge.{Direction.Id(ordered[0])}.{Direction.Id(ordered[1])}";
    }

    public static string Corner(GridDirection a, GridDirection b, GridDirection c)
    {
        var ordered = Ordered([a, b, c]);
        return $"corner.{Direction.Id(ordered[0])}.{Direction.Id(ordered[1])}.{Direction.Id(ordered[2])}";
    }

    private static GridDirection[] Ordered(GridDirection[] directions)
        => directions.OrderBy(d => (int)Direction.PrimaryAxis(d)).ThenBy(d => Direction.Sign(d)).ToArray();
}
