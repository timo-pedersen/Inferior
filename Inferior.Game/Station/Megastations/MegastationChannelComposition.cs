using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public enum MegastationChannelAssociationKind
{
    Independent,
    ChannelEdge,
    ChannelNode,
    ChannelEndpoint,
}

public readonly record struct MegastationChannelPlacement(
    MegastationChannelAssociationKind Kind,
    string FeatureIdentity,
    float U,
    float V,
    bool AlongU);

/// <summary>
/// Derives buildable relationships from the authoritative SC2 plan. This is deliberately
/// placement-only: channel planning and geometry remain owned by the SC2 implementation.
/// </summary>
public static class MegastationChannelComposition
{
    public static bool TryPlace(
        MegastationPlanarRegion region,
        MegastationServiceChannelPlan channels,
        int seed,
        float originalU,
        float originalV,
        float alongSpan,
        float crossSpan,
        float allocation,
        bool broadBand,
        out MegastationChannelPlacement placement)
    {
        placement = default;
        MegastationServiceChannelNetwork? network = channels.Networks.FirstOrDefault(
            candidate => candidate.SurfaceStableId == region.StableId);
        if (network is null || Sample(seed, "sc3:allocated") >= allocation)
            return false;

        MegastationServiceChannelNode[] endpoints = network.Nodes
            .Where(node => node.Endpoint.HasValue)
            .OrderBy(node => DistanceSquared(node.Position, originalU, originalV))
            .ThenBy(node => node.Identity, StringComparer.Ordinal)
            .ToArray();
        MegastationServiceChannelNode[] junctions = network.Nodes
            .Where(node => node.Kind is MegastationServiceChannelNodeKind.TJunction
                or MegastationServiceChannelNodeKind.FourWay)
            .OrderBy(node => DistanceSquared(node.Position, originalU, originalV))
            .ThenBy(node => node.Identity, StringComparer.Ordinal)
            .ToArray();

        float choice = Sample(seed, "sc3:relationship");
        float endpointThreshold = broadBand ? .20f : .24f;
        float nodeThreshold = broadBand ? .42f : .58f;
        if (endpoints.Length > 0 && choice < endpointThreshold
            && TryEndpoint(network, endpoints[0], seed, alongSpan, crossSpan, broadBand,
                out placement))
            return true;
        if (junctions.Length > 0 && choice < nodeThreshold
            && TryNode(junctions[0], seed, alongSpan, crossSpan, broadBand, out placement))
            return true;

        MegastationServiceChannelRun[] usableRuns = network.Runs
            .Where(candidate => candidate.Length >= alongSpan + 16f)
            .OrderBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .ToArray();
        MegastationServiceChannelRun[] developedRuns = usableRuns
            .Where(candidate => Sample(network.Seed,
                $"sc3:{(broadBand ? "fabric" : "g2")}:developed:{candidate.Identity}")
                < (broadBand ? .55f : .62f))
            .ToArray();
        MegastationServiceChannelRun? run = (developedRuns.Length > 0 ? developedRuns : usableRuns)
            .OrderBy(candidate => DistanceSquaredToRun(candidate, originalU, originalV))
            .ThenBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .FirstOrDefault();
        return run is not null
            && TryEdge(run, seed, originalU, originalV, alongSpan, crossSpan, broadBand,
                out placement);
    }

    public static bool OverlapsReserved(
        MegastationPlanarRegion region,
        MegastationServiceChannelPlan channels,
        float minU,
        float maxU,
        float minV,
        float maxV,
        float clearance = 2f)
    {
        MegastationServiceChannelNetwork? network = channels.Networks.FirstOrDefault(
            candidate => candidate.SurfaceStableId == region.StableId);
        if (network is null)
            return false;

        foreach (MegastationServiceChannelRun run in network.Runs)
        {
            float half = run.Width * .5f + clearance;
            float runMinU = MathF.Min(run.Start.X, run.End.X) - half;
            float runMaxU = MathF.Max(run.Start.X, run.End.X) + half;
            float runMinV = MathF.Min(run.Start.Y, run.End.Y) - half;
            float runMaxV = MathF.Max(run.Start.Y, run.End.Y) + half;
            if (Intersects(minU, maxU, minV, maxV, runMinU, runMaxU, runMinV, runMaxV))
                return true;
        }

        foreach (MegastationServiceChannelNode node in network.Nodes)
        {
            float halfU = (node.MainAlongU ? node.HousingLength : node.HousingWidth) * .5f
                + clearance;
            float halfV = (node.MainAlongU ? node.HousingWidth : node.HousingLength) * .5f
                + clearance;
            if (Intersects(minU, maxU, minV, maxV,
                    node.Position.X - halfU, node.Position.X + halfU,
                    node.Position.Y - halfV, node.Position.Y + halfV))
                return true;
        }
        foreach (MegastationServiceChannelBridge bridge in network.Bridges)
        {
            MegastationServiceChannelRun run = network.Runs.Single(
                candidate => candidate.Identity == bridge.RunIdentity);
            Vector2 centre = Vector2.Lerp(run.Start, run.End, bridge.PositionAlongRun);
            float lipWidth = Math.Clamp(run.Width * .115f, 1.15f, 2.6f);
            float halfCross = (bridge.DeckWidth + lipWidth * 1.4f) * .5f + clearance;
            const float approachHalfLength = 8.75f;
            float bridgeMinU = centre.X - (run.AlongU ? approachHalfLength : halfCross);
            float bridgeMaxU = centre.X + (run.AlongU ? approachHalfLength : halfCross);
            float bridgeMinV = centre.Y - (run.AlongU ? halfCross : approachHalfLength);
            float bridgeMaxV = centre.Y + (run.AlongU ? halfCross : approachHalfLength);
            if (Intersects(minU, maxU, minV, maxV,
                    bridgeMinU, bridgeMaxU, bridgeMinV, bridgeMaxV))
                return true;
        }
        return false;
    }

    private static bool TryEdge(
        MegastationServiceChannelRun run,
        int seed,
        float originalU,
        float originalV,
        float alongSpan,
        float crossSpan,
        bool broadBand,
        out MegastationChannelPlacement placement)
    {
        bool alongU = run.AlongU;
        float start = alongU ? MathF.Min(run.Start.X, run.End.X) : MathF.Min(run.Start.Y, run.End.Y);
        float end = alongU ? MathF.Max(run.Start.X, run.End.X) : MathF.Max(run.Start.Y, run.End.Y);
        float originalAlong = alongU ? originalU : originalV;
        float halfAlong = alongSpan * .5f;
        float along = Math.Clamp(originalAlong + Signed(seed, "sc3:edge-jitter") * 18f,
            start + halfAlong + 6f, end - halfAlong - 6f);
        float centreCross = alongU ? run.Start.Y : run.Start.X;
        float side = Sample(seed, "sc3:side") < .5f ? -1f : 1f;
        float setback = broadBand
            ? 10f + Sample(seed, "sc3:setback") * 18f
            : 4f + Sample(seed, "sc3:setback") * 9f;
        float cross = centreCross + side * (run.Width * .5f + setback + crossSpan * .5f);
        placement = new(
            MegastationChannelAssociationKind.ChannelEdge,
            run.Identity,
            alongU ? along : cross,
            alongU ? cross : along,
            alongU);
        return true;
    }

    private static bool TryNode(
        MegastationServiceChannelNode node,
        int seed,
        float alongSpan,
        float crossSpan,
        bool broadBand,
        out MegastationChannelPlacement placement)
    {
        bool alongU = node.MainAlongU;
        float side = Sample(seed, "sc3:node-side") < .5f ? -1f : 1f;
        float housingCross = (alongU ? node.HousingWidth : node.HousingLength) * .5f;
        float gap = broadBand ? 13f : 6f;
        float alongJitter = Signed(seed, "sc3:node-along")
            * MathF.Min(18f, alongSpan * .35f);
        float along = (alongU ? node.Position.X : node.Position.Y) + alongJitter;
        float cross = (alongU ? node.Position.Y : node.Position.X)
            + side * (housingCross + gap + crossSpan * .5f);
        placement = new(
            MegastationChannelAssociationKind.ChannelNode,
            node.Identity,
            alongU ? along : cross,
            alongU ? cross : along,
            alongU);
        return true;
    }

    private static bool TryEndpoint(
        MegastationServiceChannelNetwork network,
        MegastationServiceChannelNode node,
        int seed,
        float alongSpan,
        float crossSpan,
        bool broadBand,
        out MegastationChannelPlacement placement)
    {
        placement = default;
        MegastationServiceChannelRun? run = network.Runs.FirstOrDefault(
            candidate => node.IncidentRunIdentities.Contains(candidate.Identity, StringComparer.Ordinal));
        if (run is null)
            return false;
        bool alongU = run.AlongU;
        Vector2 other = Vector2.DistanceSquared(run.Start, node.Position)
            < Vector2.DistanceSquared(run.End, node.Position) ? run.End : run.Start;
        Vector2 outward = node.Position - other;
        if (outward.LengthSquared() < .001f)
            return false;
        outward.Normalize();
        float housingAlong = (alongU ? node.HousingLength : node.HousingWidth) * .5f;
        float gap = broadBand ? 15f : 7f;
        Vector2 centre = node.Position + outward * (housingAlong + gap + alongSpan * .5f);
        Vector2 side = new(-outward.Y, outward.X);
        centre += side * Signed(seed, "sc3:endpoint-side") * MathF.Min(10f, crossSpan * .25f);
        placement = new(
            MegastationChannelAssociationKind.ChannelEndpoint,
            node.Identity,
            centre.X,
            centre.Y,
            alongU);
        return true;
    }

    private static float DistanceSquared(Vector2 point, float u, float v)
        => Vector2.DistanceSquared(point, new Vector2(u, v));

    private static float DistanceSquaredToRun(MegastationServiceChannelRun run, float u, float v)
    {
        Vector2 point = new(u, v);
        Vector2 delta = run.End - run.Start;
        float lengthSquared = delta.LengthSquared();
        float t = lengthSquared <= .001f ? 0f
            : Math.Clamp(Vector2.Dot(point - run.Start, delta) / lengthSquared, 0f, 1f);
        return Vector2.DistanceSquared(point, run.Start + delta * t);
    }

    private static bool Intersects(
        float aMinU, float aMaxU, float aMinV, float aMaxV,
        float bMinU, float bMaxU, float bMinV, float bMaxV)
        => aMinU < bMaxU && aMaxU > bMinU && aMinV < bMaxV && aMaxV > bMinV;

    private static float Sample(int seed, string key)
        => unchecked((uint)MegastationSeed.Derive(seed, key)) / (float)uint.MaxValue;

    private static float Signed(int seed, string key) => Sample(seed, key) * 2f - 1f;
}
