using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Inferior.Game.Containers;
using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public readonly record struct MegastationPadSurface(
    Vector3 Centre,
    Vector3 Normal,
    Vector3 Right,
    Vector3 PreferredHeading);

public readonly record struct MegastationBerthClearance(
    float RightMinimum,
    float RightMaximum,
    float ForwardMinimum,
    float ForwardMaximum)
{
    public bool Intersects(MegastationBerthClearance other)
        => RightMinimum < other.RightMaximum && RightMaximum > other.RightMinimum
            && ForwardMinimum < other.ForwardMaximum && ForwardMaximum > other.ForwardMinimum;

    public bool Contains(MegastationBerthClearance other)
        => other.RightMinimum >= RightMinimum && other.RightMaximum <= RightMaximum
            && other.ForwardMinimum >= ForwardMinimum && other.ForwardMaximum <= ForwardMaximum;
}

public static class MegastationLandingPadAssemblyStandards
{
    public const float ApronThickness = .16f;
    public const float PadTopHeightAboveApron = 1f;
    public const float PadSlabThickness = .5f;
    public const float UnderPadClearGap = .5f;
    public const float PersonnelStairWidth = 2.2f;
    public const float PersonnelStairRun = 1.5f;
    public const float CargoRampWidth = 6f;
    public const float CargoRampRun = 8f;
    public const float FixtureLightIntensity = .34f;
    public const float FixtureLightRange = 5.5f;
    public const float FixtureLightAngularCutoffCosine = 0f;
    public static readonly Color UpperFixtureColour = new(76, 196, 255);
    public static readonly Color LowerFixtureColour = new(255, 174, 72);

    public static IReadOnlyList<MegastationLandingPadFixturePlan> Fixtures(
        MegastationLandingPadPlan pad)
    {
        Vector3 up = pad.PadSurface.Normal;
        Vector3 right = pad.PadSurface.Right;
        Vector3 forward = pad.PadSurface.PreferredHeading;
        float lampForward = pad.NominalSize.Y * .5f - 2.4f;
        float lampRight = pad.NominalSize.X * .5f - 2.2f;
        var fixtures = new List<MegastationLandingPadFixturePlan>(4);
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
        {
            Color colour = sz > 0 ? UpperFixtureColour : LowerFixtureColour;
            Vector3 centre = pad.PadSurface.Centre
                + right * sx * lampRight + forward * sz * lampForward + up * .18f;
            Vector3 inward = Vector3.Normalize(-right * sx - forward * sz);
            Vector3 source = centre + inward * .55f + up * .22f;
            Vector3 sourceForward = Vector3.Normalize(inward * .65f - up * .76f);
            fixtures.Add(new(
                $"interior/landing-district:v1/{pad.PadId}/fixture:{sx}:{sz}",
                centre,
                source,
                sourceForward,
                colour));
        }
        return fixtures;
    }

    public static (Vector3 StairTop, Vector3 RampTop, Vector3 ServiceDirection) AccessAnchors(
        MegastationLandingPadPlan pad)
    {
        Vector3 serviceDirection = -pad.PadSurface.PreferredHeading;
        Vector3 rearEdge = pad.PadSurface.Centre
            + serviceDirection * (pad.NominalSize.Y * .5f);
        return (
            rearEdge - pad.PadSurface.Right * (pad.NominalSize.X * .24f),
            rearEdge + pad.PadSurface.Right * (pad.NominalSize.X * .22f),
            serviceDirection);
    }
}

public sealed record MegastationLandingPadFixturePlan(
    string Identity,
    Vector3 Centre,
    Vector3 SourcePosition,
    Vector3 SourceForward,
    Color Colour);

public sealed record MegastationLandingPadPlan(
    string PadId,
    MegastationPadSurface PadSurface,
    Vector2 NominalSize,
    IReadOnlyList<Vector3> FutureSupportPolygon,
    MegastationBerthClearance HardClearance,
    MegastationBerthClearance OperationalApron,
    MegastationBerthClearance BuildingSetbackClearance,
    MegastationBerthClearance FutureBerthClearance,
    bool IsLarge,
    int PresentationSeed);

public enum MegastationPersonnelAccessKind { Ground, Stair, Ramp }
public enum MegastationCargoEntranceStyle { Flat, ShallowRecess }
public enum MegastationFrontageWindowStyle { CompactBand, ObservationStrip }

public sealed record MegastationPersonnelEntrancePlan(
    string Identity,
    Vector3 Centre,
    Vector2 Size,
    MegastationPersonnelAccessKind AccessKind);

public sealed record MegastationCargoEntrancePlan(
    string Identity,
    Vector3 Centre,
    Vector2 Size,
    MegastationCargoEntranceStyle Style,
    float RecessDepth);

public sealed record MegastationFrontageWindowGroupPlan(
    string Identity,
    Vector3 Centre,
    MegastationFrontageWindowStyle Style,
    int WindowCount,
    Vector2 WindowSize,
    float Spacing,
    Color Colour);

public sealed record MegastationFacilityIdentifierPlan(
    string Text,
    Vector3 Origin,
    Vector3 ReadingDirection,
    float PixelSize);

public sealed record MegastationFrontageFloodlightPlan(
    string Identity,
    Vector3 FixtureCentre,
    Vector3 SourcePosition,
    Color Colour,
    float Intensity,
    float Range,
    Vector3 Forward,
    float AngularCutoffCosine);

public sealed record MegastationOperationalFrontagePlan(
    string Identity,
    Vector3 Normal,
    Vector3 Right,
    Vector3 Up,
    IReadOnlyList<string> ServedPadIds,
    IReadOnlyList<MegastationPersonnelEntrancePlan> PersonnelEntrances,
    IReadOnlyList<MegastationCargoEntrancePlan> CargoEntrances,
    IReadOnlyList<MegastationFrontageWindowGroupPlan> WindowGroups,
    IReadOnlyList<MegastationFrontageFloodlightPlan> Floodlights,
    MegastationFacilityIdentifierPlan? FacilityIdentifier,
    int Seed);

public sealed record MegastationLandingServiceBuilding(
    string Identity,
    Vector3 Centre,
    Vector3 Size,
    int Seed,
    MegastationOperationalFrontagePlan Frontage);

public sealed record MegastationLandingContainerPlan(
    string Identity,
    Vector3 Centre,
    Vector3 Size,
    MegastationBerthClearance Footprint,
    int Seed);

public sealed record MegastationLoadingAreaPlan(
    string Identity,
    string PadId,
    string ServiceBuildingIdentity,
    Vector3 Centre,
    Vector2 Size,
    MegastationBerthClearance Bounds,
    string Label,
    IReadOnlyList<MegastationLandingContainerPlan> Containers,
    int Seed);

public enum MegastationKeepClearPurpose
{
    PersonnelStair,
    CargoRamp,
    PersonnelDoor,
    CargoDoor,
}

public sealed record MegastationKeepClearZonePlan(
    string Identity,
    Vector3 Centre,
    Vector2 Size,
    MegastationBerthClearance Bounds,
    MegastationKeepClearPurpose Purpose,
    bool ShowLabel);

public sealed record MegastationLandingDistrictDiagnostics(
    int PadCount,
    int StandardPadCount,
    int LargePadCount,
    Vector2 ApronSize,
    int ServiceBuildingCount,
    int ArtificialLightCount,
    int LoadingAreaCount,
    int ContainerCount,
    int KeepClearZoneCount,
    int VisibleVertexCount,
    int VisibleTriangleCount,
    int ShadowVertexCount,
    int ShadowTriangleCount,
    string Signature,
    int ApronReceiverVertexCount = 0,
    int ApronReceiverTriangleCount = 0,
    float ApronReceiverMaximumSpacing = 0f);

public sealed record MegastationLandingDistrictPlan(
    int AlgorithmVersion,
    int Seed,
    Vector3 FloorNormal,
    Vector3 DistrictRight,
    Vector3 PreferredHeading,
    Vector3 ApronCentre,
    Vector2 ApronSize,
    IReadOnlyList<MegastationLandingPadPlan> Pads,
    IReadOnlyList<MegastationLandingServiceBuilding> ServiceBuildings,
    IReadOnlyList<MegastationLoadingAreaPlan> LoadingAreas,
    IReadOnlyList<MegastationKeepClearZonePlan> KeepClearZones,
    IReadOnlyList<MegastationArtificialLight> ArtificialLights,
    MegastationLandingDistrictDiagnostics Diagnostics);

public static class MegastationLandingDistrictPlanner
{
    public const int AlgorithmVersion = 6;
    public const float StandardPadSize = 36f;
    public const float LargePadLength = 72f;
    public const float CornerClip = 1f;
    public const float BerthMargin = 5f;
    public const float BuildingSetback = 10f;
    public const float OperationalApronDepth = 14f;
    public const float LoadingAreaOutlineWidth = .10f;
    public static readonly Vector3 StandardContainerSize = new(6f, 2.5f, 2.5f);

    public static MegastationLandingDistrictPlan Plan(MegastationInteriorPlan interior)
    {
        int seed = MegastationSeed.Derive(interior.Seed, "landing-district:v1");
        Vector3 right = Vector3.Normalize(interior.PortalRight);
        Vector3 up = Vector3.Normalize(interior.PortalUp);
        Vector3 forward = Vector3.Normalize(interior.OutwardNormal);
        Vector3 inward = -forward;
        (float rightMin, float rightMax) = Span(interior.CavityEnvelope, right);
        (float upMin, _) = Span(interior.CavityEnvelope, up);
        (_, float depthMax) = Span(interior.CavityEnvelope, inward);

        // The deep third is deliberately used: H1's optional stepped floor occurs only
        // in the middle third, while this region is adjacent to the authoritative rear wall.
        float districtRight = (rightMin + rightMax) * .5f;
        // L1b moves the pad/apron composition another four metres toward the open bay.
        // The rear-wall buildings are already at their physical limit, so this is the
        // smallest change that preserves the ten-metre building setback while leaving
        // a useful 14 m one-sided loading apron behind every preferred service edge.
        float rearRowDepth = depthMax - 84f;
        float frontRowDepth = rearRowDepth - 86f;
        float[] columns = [-70f, 0f, 70f];
        var pads = new List<MegastationLandingPadPlan>(6);
        AddPad(1, columns[0], frontRowDepth, StandardPadSize, false);
        AddPad(2, columns[1], frontRowDepth, StandardPadSize, false);
        AddPad(3, columns[2], frontRowDepth, StandardPadSize, false);
        AddPad(4, columns[0], rearRowDepth, LargePadLength, true);
        AddPad(5, columns[1], rearRowDepth, StandardPadSize, false);
        AddPad(6, columns[2], rearRowDepth, LargePadLength, true);

        float apronRightSpan = 206f;
        float apronDepthSpan = 194f;
        float apronDepth = (frontRowDepth + rearRowDepth) * .5f + 4f;
        Vector3 apronCentre = Compose(districtRight, upMin + .08f, apronDepth);

        var buildings = new List<MegastationLandingServiceBuilding>(3);
        float buildingDepth = depthMax - 18f;
        AddBuilding("west", -66f, 54f, 28f, 22f);
        AddBuilding("operations", 0f, 62f, 34f, 28f);
        AddBuilding("east", 66f, 48f, 25f, 20f);
        foreach (MegastationLandingPadPlan pad in pads)
        foreach (MegastationLandingServiceBuilding building in buildings)
        {
            MegastationBerthClearance footprint = Envelope(
                building.Centre,
                right,
                forward,
                building.Size.X,
                building.Size.Z,
                0f);
            if (pad.BuildingSetbackClearance.Intersects(footprint))
                throw new InvalidOperationException(
                    $"Landing district building {building.Identity} violates {pad.PadId}'s building setback.");
            if (pad.OperationalApron.Intersects(footprint))
                throw new InvalidOperationException(
                    $"Landing district building {building.Identity} intrudes into {pad.PadId}'s operational apron.");
        }

        (IReadOnlyList<MegastationLoadingAreaPlan> loadingAreas,
            IReadOnlyList<MegastationKeepClearZonePlan> keepClearZones) =
            PlanOperationalFloor(pads, buildings, right, up, forward, seed);

        int lightingSeed = MegastationSeed.Derive(seed, "lighting");
        var lights = new List<MegastationArtificialLight>(14);
        foreach (MegastationLandingPadPlan pad in pads)
        {
            int lightSeed = MegastationSeed.Derive(lightingSeed, pad.PadId);
            float intensity = .92f + Unit(lightSeed, "intensity") * .16f;
            float range = pad.IsLarge ? 126f : 108f;
            lights.Add(new(
                $"interior/landing-district:v1/{pad.PadId}/overhead",
                pad.PadSurface.Centre + up * (pad.IsLarge ? 27f : 24f),
                new Color(220, 235, 255),
                intensity,
                range));
            foreach (MegastationLandingPadFixturePlan fixture in
                     MegastationLandingPadAssemblyStandards.Fixtures(pad))
            {
                lights.Add(new(
                    fixture.Identity,
                    fixture.SourcePosition,
                    fixture.Colour,
                    MegastationLandingPadAssemblyStandards.FixtureLightIntensity,
                    MegastationLandingPadAssemblyStandards.FixtureLightRange,
                    fixture.SourceForward,
                    MegastationLandingPadAssemblyStandards.FixtureLightAngularCutoffCosine));
            }
        }
        for (int side = -1; side <= 1; side += 2)
        {
            lights.Add(new(
                $"interior/landing-district:v1/service:{side}",
                Compose(districtRight + side * 72f, upMin + 15f, depthMax - 35f),
                new Color(205, 228, 255),
                .82f,
                118f));
        }
        foreach (MegastationLandingServiceBuilding building in buildings)
        foreach (MegastationFrontageFloodlightPlan floodlight in building.Frontage.Floodlights)
        {
            lights.Add(new(
                floodlight.Identity,
                floodlight.SourcePosition,
                floodlight.Colour,
                floodlight.Intensity,
                floodlight.Range,
                floodlight.Forward,
                floodlight.AngularCutoffCosine));
        }

        string signature = Signature(
            seed, pads, buildings, loadingAreas, keepClearZones, lights);
        var diagnostics = new MegastationLandingDistrictDiagnostics(
            pads.Count,
            pads.Count(pad => !pad.IsLarge),
            pads.Count(pad => pad.IsLarge),
            new(apronRightSpan, apronDepthSpan),
            buildings.Count,
            lights.Count,
            loadingAreas.Count,
            loadingAreas.Sum(area => area.Containers.Count),
            keepClearZones.Count,
            0, 0, 0, 0,
            signature);
        return new(
            AlgorithmVersion,
            seed,
            up,
            right,
            forward,
            apronCentre,
            new(apronRightSpan, apronDepthSpan),
            pads,
            buildings,
            loadingAreas,
            keepClearZones,
            lights,
            diagnostics);

        void AddPad(int number, float rightOffset, float inwardDepth, float length, bool large)
        {
            string id = $"LD-{number:00}";
            // PadSurface is the future landing authority, so it follows the actual top
            // of the installed component rather than the bay floor beneath it.
            Vector3 centre = Compose(
                districtRight + rightOffset,
                upMin + MegastationLandingPadAssemblyStandards.ApronThickness
                    + MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron,
                inwardDepth);
            Vector3[] support = SupportPolygon(centre, right, forward, StandardPadSize, length);
            MegastationBerthClearance hardClearance = Envelope(
                centre, right, forward, StandardPadSize, length, BerthMargin);
            float centreForward = Vector3.Dot(centre, forward);
            float rearEdge = centreForward - length * .5f;
            float operationalDepth = number == 5 ? 18f : OperationalApronDepth;
            pads.Add(new(
                id,
                new(centre, up, right, forward),
                new(StandardPadSize, length),
                support,
                hardClearance,
                new(
                    Vector3.Dot(centre, right) - StandardPadSize * .5f - BerthMargin,
                    Vector3.Dot(centre, right) + StandardPadSize * .5f + BerthMargin,
                    rearEdge - operationalDepth,
                    rearEdge),
                Envelope(centre, right, forward, StandardPadSize, length, BuildingSetback),
                hardClearance,
                large,
                MegastationSeed.Derive(seed, $"pad:{id}")));
        }

        void AddBuilding(string identity, float rightOffset, float width, float depth, float height)
        {
            int child = MegastationSeed.Derive(seed, $"service:{identity}");
            float adjustedHeight = height + Unit(child, "height") * 5f;
            string fullIdentity = $"landing-district/service/{identity}";
            Vector3 centre = Compose(
                districtRight + rightOffset,
                upMin + adjustedHeight * .5f,
                buildingDepth);
            Vector3 size = new(width, adjustedHeight, depth);
            buildings.Add(new(
                fullIdentity,
                centre,
                size,
                child,
                PlanOperationalFrontage(
                    fullIdentity, identity, centre, size, child, right, up, forward)));
        }

        Vector3 Compose(float r, float u, float d) => right * r + up * u + inward * d;
    }

    private static MegastationOperationalFrontagePlan PlanOperationalFrontage(
        string buildingIdentity,
        string role,
        Vector3 buildingCentre,
        Vector3 buildingSize,
        int buildingSeed,
        Vector3 right,
        Vector3 up,
        Vector3 forward)
    {
        int seed = MegastationSeed.Derive(buildingSeed, "frontage:v1");
        Vector3 floor = buildingCentre - up * (buildingSize.Y * .5f);
        Vector3 face = buildingCentre + forward * (buildingSize.Z * .5f);
        string servedPad = role switch
        {
            "west" => "LD-04",
            "operations" => "LD-05",
            _ => "LD-06",
        };

        var cargo = new List<MegastationCargoEntrancePlan>(2);
        int cargoCount = role == "west" && Unit(seed, "second-cargo") > .38f ? 2 : 1;
        for (int i = 0; i < cargoCount; i++)
        {
            int child = MegastationSeed.Derive(seed, $"cargo:{i}");
            float width = (role == "operations" ? 10f : 8.5f)
                + Unit(child, "width") * 1.3f;
            float height = 5.7f + Unit(child, "height") * .6f;
            float lateral = i == 0 ? -buildingSize.X * .12f : buildingSize.X * .16f;
            MegastationCargoEntranceStyle style =
                role == "operations" || Unit(child, "recess") > .42f
                    ? MegastationCargoEntranceStyle.ShallowRecess
                    : MegastationCargoEntranceStyle.Flat;
            cargo.Add(new(
                $"{buildingIdentity}/frontage/cargo:{i}",
                floor + right * lateral + forward * (buildingSize.Z * .5f)
                    + up * (height * .5f),
                new(width, height),
                style,
                style == MegastationCargoEntranceStyle.ShallowRecess ? .65f : 0f));
        }

        var personnel = new List<MegastationPersonnelEntrancePlan>(2);
        MegastationPersonnelAccessKind primaryAccess = role switch
        {
            "operations" => MegastationPersonnelAccessKind.Stair,
            "east" => MegastationPersonnelAccessKind.Ramp,
            _ => MegastationPersonnelAccessKind.Ground,
        };
        float accessFloor = primaryAccess == MegastationPersonnelAccessKind.Ground
            ? 0f
            : MegastationLandingDistrictMeshBuilder.AccessPlatformHeight;
        personnel.Add(new(
            $"{buildingIdentity}/frontage/personnel:0",
            floor + forward * (buildingSize.Z * .5f)
                + right * (buildingSize.X * .34f)
                + up * (accessFloor + MegastationLandingDistrictMeshBuilder.PersonnelDoorHeight * .5f),
            new(MegastationLandingDistrictMeshBuilder.PersonnelDoorWidth,
                MegastationLandingDistrictMeshBuilder.PersonnelDoorHeight),
            primaryAccess));
        if (buildingSize.X >= 52f && Unit(seed, "second-personnel") > .48f)
        {
            personnel.Add(new(
                $"{buildingIdentity}/frontage/personnel:1",
                floor + forward * (buildingSize.Z * .5f)
                    + right * (buildingSize.X * .08f)
                    + up * (MegastationLandingDistrictMeshBuilder.PersonnelDoorHeight * .5f),
                new(MegastationLandingDistrictMeshBuilder.PersonnelDoorWidth,
                    MegastationLandingDistrictMeshBuilder.PersonnelDoorHeight),
                MegastationPersonnelAccessKind.Ground));
        }

        int windowGroupCount = role == "operations"
            ? 2
            : 1 + (Unit(seed, "second-window-group") > .72f ? 1 : 0);
        var windows = new List<MegastationFrontageWindowGroupPlan>(windowGroupCount);
        for (int i = 0; i < windowGroupCount; i++)
        {
            int child = MegastationSeed.Derive(seed, $"windows:{i}");
            bool observation = role == "operations" && i == 1;
            int count = observation ? 5 : 3 + (int)(Unit(child, "count") * 3f);
            float width = observation ? 1.8f : 1.25f + Unit(child, "width") * .35f;
            float height = observation ? 1.25f : 1.0f + Unit(child, "height") * .25f;
            float lateral = i == 0 ? buildingSize.X * .15f : -buildingSize.X * .23f;
            float elevation = observation ? 11.5f : 8.2f;
            float colourRoll = Unit(child, "colour");
            Color colour = colourRoll < .48f ? StationWindowVisuals.WarmWhite
                : colourRoll < .86f ? StationWindowVisuals.NeutralWhite
                : StationWindowVisuals.CoolBlue;
            windows.Add(new(
                $"{buildingIdentity}/frontage/windows:{i}",
                face + right * lateral + up * elevation,
                observation
                    ? MegastationFrontageWindowStyle.ObservationStrip
                    : MegastationFrontageWindowStyle.CompactBand,
                count,
                new(width, height),
                width + .65f,
                colour));
        }

        // L1f: two deliberate working-area sources per active frontage. One is tied
        // to container movement at the primary cargo entrance, the other to human
        // access. The point-like H1c carrier sits forward and slightly below the
        // physical fixture so its pool favours the apron rather than the upper wall.
        var floodlights = new List<MegastationFrontageFloodlightPlan>(2);
        MegastationCargoEntrancePlan primaryCargo = cargo[0];
        MegastationPersonnelEntrancePlan primaryPersonnel = personnel[0];
        AddFloodlight(
            "cargo",
            primaryCargo.Centre
                - right * (primaryCargo.Size.X * .5f + 1.35f)
                + up * (primaryCargo.Size.Y * .5f + 1.55f));
        AddFloodlight(
            "personnel",
            primaryPersonnel.Centre - right * 1.75f
                + up * (primaryPersonnel.Size.Y * .5f + 3.25f));

        MegastationFacilityIdentifierPlan? identifier = null;
        if (role == "operations" || Unit(seed, "identifier") > .35f)
        {
            int number = 1 + (int)(Unit(seed, "identifier-number") * 98f);
            string prefix = role switch
            {
                "west" => "WAREHOUSE",
                "operations" => "SERVICE",
                _ => "CARGO",
            };
            string text = $"{prefix} {number:00}";
            const float pixel = .18f;
            float textWidth = text.Length * (BitmapFonts.CharW + 1) * pixel;
            MegastationCargoEntrancePlan anchor = cargo[0];
            // The district's placement-right axis points toward visual left when the
            // frontage is viewed from its operational side. Derive semantic text-right
            // from the intended Up/Normal frame rather than negating an axis at emission.
            Vector3 readingDirection = Vector3.Normalize(Vector3.Cross(up, forward));
            identifier = new(
                text,
                anchor.Centre - readingDirection * (textWidth * .5f)
                    + up * (anchor.Size.Y * .5f
                        + MegastationLandingDistrictMeshBuilder.CargoDoorFrameThickness
                        + MegastationLandingDistrictMeshBuilder.FacilityLabelClearance)
                    + forward * MegastationLandingDistrictMeshBuilder.FrontageMarkingOffset,
                readingDirection,
                pixel);
        }

        return new(
            $"{buildingIdentity}/frontage",
            forward,
            right,
            up,
            [servedPad],
            personnel,
            cargo,
            windows,
            floodlights,
            identifier,
            seed);

        void AddFloodlight(string purpose, Vector3 wallAnchor)
        {
            int child = MegastationSeed.Derive(seed, $"floodlight:v1/{purpose}");
            float warmth = Unit(child, "warmth");
            Color colour = new(
                (byte)MathHelper.Lerp(234f, 246f, warmth),
                (byte)MathHelper.Lerp(229f, 239f, warmth),
                (byte)MathHelper.Lerp(216f, 236f, warmth));
            Vector3 fixture = wallAnchor
                + forward * MegastationLandingDistrictMeshBuilder.FloodlightFixtureOffset;
            float downwardRadians = MathHelper.ToRadians(
                28f + Unit(child, "downward-cant") * 7f);
            Vector3 aim = Vector3.Normalize(
                forward * MathF.Cos(downwardRadians)
                - up * MathF.Sin(downwardRadians));
            floodlights.Add(new(
                $"interior/landing-district:v1/{buildingIdentity}/floodlight:{purpose}",
                fixture,
                fixture + forward * 2.15f - up * .65f,
                colour,
                .62f + Unit(child, "intensity") * .14f,
                48f + Unit(child, "range") * 12f,
                aim,
                -.35f + Unit(child, "angular-cutoff") * .10f));
        }
    }

    private static (
        IReadOnlyList<MegastationLoadingAreaPlan> LoadingAreas,
        IReadOnlyList<MegastationKeepClearZonePlan> KeepClearZones) PlanOperationalFloor(
            IReadOnlyList<MegastationLandingPadPlan> pads,
            IReadOnlyList<MegastationLandingServiceBuilding> buildings,
            Vector3 right,
            Vector3 up,
            Vector3 forward,
            int districtSeed)
    {
        int seed = MegastationSeed.Derive(districtSeed, "operational-floor:v1");
        MegastationLandingPadPlan pad = pads.Single(candidate => candidate.PadId == "LD-05");
        MegastationLandingServiceBuilding building = buildings.Single(candidate =>
            candidate.Identity.EndsWith("operations", StringComparison.Ordinal));
        Vector3 service = -forward;
        Vector3 rearEdge = pad.PadSurface.Centre + service * (pad.NominalSize.Y * .5f);
        Vector3 apronFloor = rearEdge
            - up * MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron;

        Vector2 loadingSize = new(28f, 12f);
        Vector3 loadingCentre = apronFloor + service * 19f;
        MegastationBerthClearance loadingBounds = Envelope(
            loadingCentre, right, forward, loadingSize.X, loadingSize.Y, 0f);
        int areaSeed = MegastationSeed.Derive(seed, "loading-area:LD-05");
        var containers = new List<MegastationLandingContainerPlan>(6);
        float[] lateral = [-10.5f, -3.5f, 3.5f, 10.5f];
        for (int i = 0; i < lateral.Length; i++)
        {
            int child = MegastationSeed.Derive(areaSeed, $"container:{i}");
            AddContainer($"loading-area/LD-05/container:{i}", lateral[i], -2f, 0, child);
        }
        int secondRowSeed = MegastationSeed.Derive(areaSeed, "container:second-row:0");
        AddContainer("loading-area/LD-05/container:second-row:0",
            lateral[0], 2f, 0, secondRowSeed);
        int stackSeed = MegastationSeed.Derive(areaSeed, "container:stack:1");
        AddContainer("loading-area/LD-05/container:stack:1", lateral[0], 2f, 1, stackSeed);

        var loadingArea = new MegastationLoadingAreaPlan(
            "landing-district/loading-area:LD-05",
            pad.PadId,
            building.Identity,
            loadingCentre,
            loadingSize,
            loadingBounds,
            $"LOADING AREA {pad.PadId[^2..]}",
            containers,
            areaSeed);

        (Vector3 stairTop, Vector3 rampTop, _) =
            MegastationLandingPadAssemblyStandards.AccessAnchors(pad);
        Vector3 stairLow = stairTop
            + service * MegastationLandingPadAssemblyStandards.PersonnelStairRun
            - up * MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron;
        Vector3 rampLow = rampTop
            + service * MegastationLandingPadAssemblyStandards.CargoRampRun
            - up * MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron;
        MegastationCargoEntrancePlan cargoEntrance = building.Frontage.CargoEntrances[0];
        MegastationPersonnelEntrancePlan personnelEntrance =
            building.Frontage.PersonnelEntrances[0];
        Vector3 cargoDoor = cargoEntrance.Centre
            - up * (cargoEntrance.Size.Y * .5f);
        Vector3 personnelDoor = personnelEntrance.Centre
            - up * (personnelEntrance.Size.Y * .5f);

        var zones = new List<MegastationKeepClearZonePlan>(4);
        AddZone("LD-05/stair", stairLow + service * 1.5f, new(4f, 3f),
            MegastationKeepClearPurpose.PersonnelStair, false);
        AddZone("LD-05/ramp", rampLow + service * 2f, new(8f, 4f),
            MegastationKeepClearPurpose.CargoRamp, true);
        AddZone("service/operations/personnel", personnelDoor + forward * 1.5f,
            new(4f, 3f), MegastationKeepClearPurpose.PersonnelDoor, false);
        AddZone("service/operations/cargo", cargoDoor + forward * 2.5f,
            new(12f, 5f), MegastationKeepClearPurpose.CargoDoor, true);

        foreach (MegastationLandingContainerPlan container in containers)
            if (!loadingBounds.Contains(container.Footprint))
                throw new InvalidOperationException(
                    $"Loading container {container.Identity} leaves its reserved area.");
        foreach (MegastationKeepClearZonePlan zone in zones)
        {
            if (loadingBounds.Intersects(zone.Bounds))
                throw new InvalidOperationException(
                    $"KEEP CLEAR zone {zone.Identity} overlaps the loading area.");
            foreach (MegastationLandingContainerPlan container in containers)
                if (zone.Bounds.Intersects(container.Footprint))
                    throw new InvalidOperationException(
                        $"Loading container {container.Identity} blocks {zone.Identity}.");
        }

        return ([loadingArea], zones);

        void AddContainer(string identity, float lateralOffset, float depthOffset,
            int stackLevel, int childSeed)
        {
            Vector3 size = StandardContainerSize;
            Vector3 centre = loadingCentre + right * lateralOffset + service * depthOffset
                + up * (size.Y * (.5f + stackLevel));
            containers.Add(new(
                identity,
                centre,
                size,
                Envelope(centre, right, forward, size.X, size.Z, 0f),
                childSeed));
        }

        void AddZone(string identity, Vector3 centre, Vector2 size,
            MegastationKeepClearPurpose purpose, bool showLabel)
            => zones.Add(new(
                $"landing-district/keep-clear:{identity}",
                centre,
                size,
                Envelope(centre, right, forward, size.X, size.Y, 0f),
                purpose,
                showLabel));
    }

    private static Vector3[] SupportPolygon(
        Vector3 centre, Vector3 right, Vector3 forward, float width, float length)
    {
        float x = width * .5f;
        float z = length * .5f;
        float c = CornerClip;
        return
        [
            centre + right * (-x + c) + forward * z,
            centre + right * (x - c) + forward * z,
            centre + right * x + forward * (z - c),
            centre + right * x + forward * (-z + c),
            centre + right * (x - c) - forward * z,
            centre + right * (-x + c) - forward * z,
            centre - right * x + forward * (-z + c),
            centre - right * x + forward * (z - c),
        ];
    }

    internal static MegastationBerthClearance Envelope(
        Vector3 centre,
        Vector3 right,
        Vector3 forward,
        float width,
        float length,
        float margin)
    {
        float r = Vector3.Dot(centre, right);
        float f = Vector3.Dot(centre, forward);
        return new(
            r - width * .5f - margin,
            r + width * .5f + margin,
            f - length * .5f - margin,
            f + length * .5f + margin);
    }

    private static (float Min, float Max) Span(MegastationInteriorVolume volume, Vector3 axis)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float x in new[] { volume.Minimum.X, volume.Maximum.X })
        foreach (float y in new[] { volume.Minimum.Y, volume.Maximum.Y })
        foreach (float z in new[] { volume.Minimum.Z, volume.Maximum.Z })
        {
            float p = Vector3.Dot(new(x, y, z), axis);
            min = MathF.Min(min, p);
            max = MathF.Max(max, p);
        }
        return (min, max);
    }

    private static float Unit(int seed, string domain)
        => (unchecked((uint)MegastationSeed.Derive(seed, domain)) & 0x00ffffff) / 16777215f;

    private static string Signature(
        int seed,
        IReadOnlyList<MegastationLandingPadPlan> pads,
        IReadOnlyList<MegastationLandingServiceBuilding> buildings,
        IReadOnlyList<MegastationLoadingAreaPlan> loadingAreas,
        IReadOnlyList<MegastationKeepClearZonePlan> keepClearZones,
        IReadOnlyList<MegastationArtificialLight> lights)
    {
        var text = new StringBuilder().Append(AlgorithmVersion).Append('|').Append(seed);
        foreach (MegastationLandingPadPlan pad in pads)
            text.Append('|').Append(pad.PadId).Append(':').Append(F(pad.PadSurface.Centre.X))
                .Append(',').Append(F(pad.PadSurface.Centre.Y)).Append(',')
                .Append(F(pad.PadSurface.Centre.Z)).Append(':').Append(pad.NominalSize);
        foreach (MegastationLandingServiceBuilding building in buildings)
        {
            text.Append('|').Append(building.Identity).Append(':').Append(building.Centre)
                .Append(':').Append(building.Size);
            MegastationOperationalFrontagePlan frontage = building.Frontage;
            text.Append(':').Append(frontage.Identity).Append(':').Append(frontage.Seed);
            foreach (string pad in frontage.ServedPadIds)
                text.Append(':').Append(pad);
            foreach (MegastationCargoEntrancePlan entrance in frontage.CargoEntrances)
                text.Append(':').Append(entrance.Identity).Append('@').Append(entrance.Centre)
                    .Append(':').Append(entrance.Size).Append(':').Append(entrance.Style);
            foreach (MegastationPersonnelEntrancePlan entrance in frontage.PersonnelEntrances)
                text.Append(':').Append(entrance.Identity).Append('@').Append(entrance.Centre)
                    .Append(':').Append(entrance.AccessKind);
            foreach (MegastationFrontageWindowGroupPlan group in frontage.WindowGroups)
                text.Append(':').Append(group.Identity).Append('@').Append(group.Centre)
                    .Append(':').Append(group.WindowCount).Append(':').Append(group.WindowSize);
            foreach (MegastationFrontageFloodlightPlan floodlight in frontage.Floodlights)
                text.Append(':').Append(floodlight.Identity).Append('@')
                    .Append(floodlight.FixtureCentre).Append(':')
                    .Append(floodlight.SourcePosition).Append(':')
                    .Append(F(floodlight.Intensity)).Append(':').Append(F(floodlight.Range))
                    .Append(':').Append(floodlight.Forward).Append(':')
                    .Append(F(floodlight.AngularCutoffCosine));
            if (frontage.FacilityIdentifier is { } identifier)
                text.Append(':').Append(identifier.Text).Append('@').Append(identifier.Origin)
                    .Append(':').Append(identifier.ReadingDirection);
        }
        foreach (MegastationLoadingAreaPlan area in loadingAreas)
        {
            text.Append('|').Append(area.Identity).Append(':').Append(area.Centre)
                .Append(':').Append(area.Size).Append(':').Append(area.Label)
                .Append(':').Append(area.ServiceBuildingIdentity);
            foreach (MegastationLandingContainerPlan container in area.Containers)
                text.Append(':').Append(container.Identity).Append('@').Append(container.Centre)
                    .Append(':').Append(container.Seed);
        }
        foreach (MegastationKeepClearZonePlan zone in keepClearZones)
            text.Append('|').Append(zone.Identity).Append(':').Append(zone.Centre)
                .Append(':').Append(zone.Size).Append(':').Append(zone.Purpose);
        foreach (MegastationArtificialLight light in lights)
            text.Append('|').Append(light.Identity).Append(':').Append(light.Position)
                .Append(':').Append(F(light.Intensity)).Append(':').Append(F(light.Range));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}

public readonly record struct MegastationLandingDistrictMeshResult(
    int FirstFace,
    int FaceCount,
    IReadOnlyList<(int Start, int Count, float Illumination)> IlluminationRanges,
    IReadOnlyList<(int Start, int Count)> UntrackedArtificialLightVertexRanges,
    MegastationLandingDistrictDiagnostics Diagnostics,
    int ApronReceiverFirstFace,
    int ApronReceiverFaceCount,
    int ApronReceiverFirstVertex,
    int ApronReceiverVertexCount,
    IReadOnlyList<(int FirstFace, int FaceCount)> PadTopReceiverRanges);

public static class MegastationLandingDistrictMeshBuilder
{
    internal const float ApronReceiverSpacing = 5f;
    internal const float PadTopReceiverSpacing = 2.5f;
    internal const float CargoDoorHeight = 6f;
    internal const float PersonnelDoorWidth = 1.4f;
    internal const float PersonnelDoorHeight = 2.4f;
    internal const float CargoDoorFrameThickness = .48f;
    internal const float FacilityLabelClearance = .45f;
    internal const float FrontageMarkingOffset = .045f;
    internal const float FloodlightFixtureOffset = .21f;
    internal const float FloodlightFixtureHousingDepth = .42f;
    internal const float FloodlightEmitterOffset = .44f;
    internal const float FloodlightEmitterHeight = .28f;
    internal const float FloodlightEmitterDepth = .06f;
    internal const float AccessPlatformHeight = 1.2f;
    internal const float StairRise = .20f;
    internal const float StairTread = .30f;
    internal const float RailingHeight = 1.05f;
    internal const float HumanReferenceHeight = 1.84f;
    private static readonly Color LampColour = new(224, 240, 255);
    private static readonly Color MarkingColour = new(215, 218, 204);
    private static readonly Color WarningColour = new(196, 151, 54);

    public static MegastationLandingDistrictMeshResult Append(
        StationModuleMesh mesh,
        MegastationLandingDistrictPlan plan,
        MegastationSystemMaterialAssignment? materials,
        CancellationToken cancellationToken = default)
    {
        int firstFace = mesh.FaceCount;
        int firstVertex = mesh.VertexCount;
        int firstIndex = mesh.IndexCount;
        int firstDecorRange = mesh.DecorClassRanges.Count;
        var illumination = new List<(int Start, int Count, float Illumination)>();
        var untrackedArtificialLightVertices = new List<(int Start, int Count)>();
        var padTopReceiverRanges = new List<(int FirstFace, int FaceCount)>(plan.Pads.Count);
        Color dominant = materials?.Palette.DominantTint ?? new Color(76, 80, 82);
        Color secondary = materials?.Palette.SecondaryTint ?? new Color(98, 99, 94);
        Color accent = materials?.Palette.AccentTint ?? new Color(132, 126, 94);
        Vector3 up = plan.FloorNormal;
        Vector3 right = plan.DistrictRight;
        Vector3 forward = plan.PreferredHeading;

        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        Color apronColour = Color.Lerp(
            Color.Lerp(dominant, Color.Black, .16f), Color.Black, .25f);
        ApronReceiverMesh apron = EmitTessellatedApron(
            mesh, plan, apronColour);

        EmitOperationalFloorMarkings(mesh, plan, up, right, forward);

        foreach (MegastationLandingPadPlan pad in plan.Pads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmitPad(mesh, pad, dominant, secondary, accent, illumination,
                padTopReceiverRanges);
        }

        foreach (MegastationLandingServiceBuilding building in plan.ServiceBuildings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mesh.CurrentDecorClass = DecorClass.MegastationInteriorMajor;
            SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
            AddBox(mesh, Frame(building.Centre, right, up, forward), building.Size,
                Color.Lerp(dominant, secondary, .35f));
            EmitOperationalFrontage(
                mesh, building, dominant, secondary, accent, illumination);
        }

        EmitLoadingContainers(mesh, plan, up, right, forward, dominant, accent,
            untrackedArtificialLightVertices);

        int faces = mesh.FaceCount - firstFace;
        int vertices = mesh.VertexCount - firstVertex;
        int triangles = (mesh.IndexCount - firstIndex) / 3;
        StationMeshCpuData? caster = mesh.PrepareIndexRanges(mesh.DecorClassRanges
            .Skip(firstDecorRange)
            .Where(range => range.decorClass == DecorClass.MegastationInteriorMajor)
            .Select(range => (range.indexStart, range.indexCount))
            .ToArray());
        int casterVertices = caster?.Vertices.Length ?? 0;
        int casterTriangles = (caster?.Indices.Length ?? 0) / 3;
        return new(firstFace, faces, illumination, untrackedArtificialLightVertices,
            plan.Diagnostics with
        {
            VisibleVertexCount = vertices,
            VisibleTriangleCount = triangles,
            ShadowVertexCount = casterVertices,
            ShadowTriangleCount = casterTriangles,
            ApronReceiverVertexCount = apron.VertexCount,
            ApronReceiverTriangleCount = apron.FaceCount * 2,
            ApronReceiverMaximumSpacing = apron.MaximumSpacing,
        }, apron.FirstFace, apron.FaceCount, apron.FirstVertex, apron.VertexCount,
            padTopReceiverRanges);
    }

    private readonly record struct ApronReceiverMesh(
        int FirstFace, int FaceCount, int FirstVertex, int VertexCount,
        float MaximumSpacing);

    private static ApronReceiverMesh EmitTessellatedApron(
        StationModuleMesh mesh,
        MegastationLandingDistrictPlan plan,
        Color colour)
    {
        Vector3 right = Vector3.Normalize(plan.DistrictRight);
        Vector3 up = Vector3.Normalize(plan.FloorNormal);
        // Match Frame/AddOrientedBox exactly. PreferredHeading may differ only by sign;
        // the symmetric apron depth and its established UV phase use this right-handed axis.
        Vector3 depth = Vector3.Normalize(Vector3.Cross(right, up));
        float halfWidth = plan.ApronSize.X * .5f;
        float halfDepth = plan.ApronSize.Y * .5f;
        float halfHeight = MegastationLandingPadAssemblyStandards.ApronThickness * .5f;
        Vector3 centre = plan.ApronCentre;
        Vector3 c0 = centre - right * halfWidth - up * halfHeight - depth * halfDepth;
        Vector3 c1 = centre + right * halfWidth - up * halfHeight - depth * halfDepth;
        Vector3 c3 = centre - right * halfWidth + up * halfHeight - depth * halfDepth;
        Vector3 c4 = centre - right * halfWidth - up * halfHeight + depth * halfDepth;
        Vector3 c5 = centre + right * halfWidth - up * halfHeight + depth * halfDepth;
        Vector3 c7 = centre - right * halfWidth + up * halfHeight + depth * halfDepth;
        Vector3 c2 = centre + right * halfWidth + up * halfHeight - depth * halfDepth;
        Vector3 c6 = centre + right * halfWidth + up * halfHeight + depth * halfDepth;

        // Preserve the five non-receiver faces and their legacy per-face UV mapping.
        mesh.AddQuad(c4, c5, c6, c7, colour);
        mesh.AddQuad(c1, c0, c3, c2, colour);
        mesh.AddQuad(c0, c4, c7, c3, colour);
        mesh.AddQuad(c5, c1, c2, c6, colour);
        mesh.AddQuad(c0, c1, c5, c4, colour);

        int columns = Math.Max(1, (int)MathF.Ceiling(plan.ApronSize.X / ApronReceiverSpacing));
        int rows = Math.Max(1, (int)MathF.Ceiling(plan.ApronSize.Y / ApronReceiverSpacing));
        float cellWidth = plan.ApronSize.X / columns;
        float cellDepth = plan.ApronSize.Y / rows;
        int firstFace = mesh.FaceCount;
        int firstVertex = mesh.VertexCount;

        // These are exactly the axes AddQuad derived for the former monolithic top
        // quad. Keeping c7 as projection origin preserves both texture scale and phase.
        Vector3 arbitrary = MathF.Abs(up.Y) < .85f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 textureU = Vector3.Normalize(Vector3.Cross(up, arbitrary));
        Vector3 textureV = Vector3.Normalize(Vector3.Cross(up, textureU));
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            float u0 = column * cellWidth;
            float u1 = (column + 1) * cellWidth;
            float v0 = row * cellDepth;
            float v1 = (row + 1) * cellDepth;
            Vector3 a = c7 + right * u0 - depth * v0;
            Vector3 b = c7 + right * u1 - depth * v0;
            Vector3 c = c7 + right * u1 - depth * v1;
            Vector3 d = c7 + right * u0 - depth * v1;
            mesh.AddQuadProjected(a, b, c, d, up, textureU, textureV,
                c7, mesh.CurrentUvScaleMeters, colour);
        }

        return new(firstFace, rows * columns, firstVertex,
            mesh.VertexCount - firstVertex, MathF.Max(cellWidth, cellDepth));
    }

    private static void EmitOperationalFrontage(
        StationModuleMesh mesh,
        MegastationLandingServiceBuilding building,
        Color dominant,
        Color secondary,
        Color accent,
        List<(int Start, int Count, float Illumination)> illumination)
    {
        MegastationOperationalFrontagePlan frontage = building.Frontage;
        Vector3 right = frontage.Right;
        Vector3 up = frontage.Up;
        Vector3 forward = frontage.Normal;
        Vector3 buildingFloor = building.Centre - up * (building.Size.Y * .5f);
        // Preserve the accepted access landing relationship: the personnel-door skin
        // sits slightly proud of the authoritative frontage plane.
        float frontOffset = building.Size.Z * .5f + .16f;

        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        foreach (MegastationCargoEntrancePlan entrance in frontage.CargoEntrances)
            EmitCargoEntrance(mesh, entrance, right, up, forward, secondary, accent);

        foreach (MegastationPersonnelEntrancePlan entrance in frontage.PersonnelEntrances)
        {
            SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
            AddBox(mesh, Frame(entrance.Centre + forward * .13f, right, up, forward),
                new(entrance.Size.X, entrance.Size.Y, .26f),
                Color.Lerp(secondary, Color.Black, .52f));
            AddBox(mesh, Frame(
                    entrance.Centre + up * (entrance.Size.Y * .5f + .18f) + forward * .20f,
                    right, up, forward),
                new(entrance.Size.X + .55f, .25f, .40f),
                Color.Lerp(accent, Color.White, .06f));

            if (entrance.AccessKind == MegastationPersonnelAccessKind.Stair)
                EmitStairAccess(mesh, buildingFloor, entrance.Centre, frontOffset,
                    right, up, forward, dominant, accent);
            else if (entrance.AccessKind == MegastationPersonnelAccessKind.Ramp)
                EmitRampAccess(mesh, buildingFloor, entrance.Centre, frontOffset,
                    right, up, forward, dominant, accent);
        }

        foreach (MegastationFrontageFloodlightPlan floodlight in frontage.Floodlights)
            EmitFrontageFloodlight(mesh, floodlight, right, up, forward, secondary, illumination);

        int windowStart = mesh.FaceCount;
        SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
        foreach (MegastationFrontageWindowGroupPlan group in frontage.WindowGroups)
        {
            for (int i = 0; i < group.WindowCount; i++)
            {
                float offset = (i - (group.WindowCount - 1) * .5f) * group.Spacing;
                MegastationWindowMeshBuilder.AppendWindow(
                    mesh,
                    group.Centre + right * offset + forward * .035f,
                    forward,
                    up,
                    group.WindowSize.X,
                    group.WindowSize.Y,
                    group.Colour);
            }
        }
        if (mesh.FaceCount > windowStart)
            illumination.Add((windowStart, mesh.FaceCount - windowStart, .92f));

        if (frontage.FacilityIdentifier is { } identifier)
        {
            SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
            PlanarTextGeometry.Add(
                mesh,
                identifier.Text,
                identifier.Origin,
                surfaceNormal: forward,
                readingDirection: identifier.ReadingDirection,
                identifier.PixelSize,
                MarkingColour);
        }
    }

    private static void EmitCargoEntrance(
        StationModuleMesh mesh,
        MegastationCargoEntrancePlan entrance,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        Color secondary,
        Color accent)
    {
        SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
        float width = entrance.Size.X;
        float height = entrance.Size.Y;
        float frameDepth = entrance.Style == MegastationCargoEntranceStyle.ShallowRecess
            ? entrance.RecessDepth
            : .28f;
        Vector3 frameCentre = entrance.Centre + forward * (frameDepth * .5f);
        Color frame = Color.Lerp(accent, secondary, .52f);
        float halfFrame = CargoDoorFrameThickness * .5f;
        AddBox(mesh, Frame(frameCentre - right * (width * .5f + halfFrame), right, up, forward),
            new(CargoDoorFrameThickness, height + .55f, frameDepth), frame);
        AddBox(mesh, Frame(frameCentre + right * (width * .5f + halfFrame), right, up, forward),
            new(CargoDoorFrameThickness, height + .55f, frameDepth), frame);
        AddBox(mesh, Frame(frameCentre + up * (height * .5f + halfFrame), right, up, forward),
            new(width + CargoDoorFrameThickness * 2f, CargoDoorFrameThickness, frameDepth), frame);
        AddBox(mesh, Frame(entrance.Centre + forward * .055f, right, up, forward),
            new(width, height, .11f), Color.Lerp(secondary, Color.Black, .55f));

        // Coarse door leaves make the opening read as container-scale machinery without
        // adding a new door system or dense facade panel pass.
        for (int leaf = -1; leaf <= 1; leaf += 2)
            AddBox(mesh, Frame(
                    entrance.Centre + right * leaf * width * .255f + forward * .12f,
                    right, up, forward),
                new(width * .47f, height - .28f, .12f),
                Color.Lerp(secondary, Color.Black, .43f));
    }

    private static void EmitFrontageFloodlight(
        StationModuleMesh mesh,
        MegastationFrontageFloodlightPlan floodlight,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        Color structure,
        List<(int Start, int Count, float Illumination)> illumination)
    {
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
        AddBox(mesh, Frame(floodlight.FixtureCentre, right, up, forward),
            new(1.05f, .48f, FloodlightFixtureHousingDepth),
            Color.Lerp(structure, Color.Black, .38f));
        int emitterStart = mesh.FaceCount;
        Vector3 emitterUp = Vector3.Normalize(Vector3.Cross(floodlight.Forward, right));
        AddBox(mesh, Frame(
                floodlight.FixtureCentre + floodlight.Forward * FloodlightEmitterOffset,
                right,
                emitterUp,
                floodlight.Forward),
            new(.76f, FloodlightEmitterHeight, FloodlightEmitterDepth), floodlight.Colour);
        illumination.Add((emitterStart, mesh.FaceCount - emitterStart, .90f));
    }

    public static void ApplyLighting(
        StationModuleMesh mesh,
        MegastationLandingDistrictMeshResult result,
        IReadOnlyList<MegastationArtificialLight> lights,
        MegastationArtificialOcclusion? occlusion = null)
    {
        foreach ((int start, int count, float value) in result.IlluminationRanges)
        for (int face = start; face < start + count; face++)
            mesh.SetFaceIllumination(face, value);

        MegastationArtificialLight[] fixtureLights = lights.Where(light =>
            light.Identity.Contains("/fixture:", StringComparison.Ordinal)).ToArray();
        MegastationArtificialLight[] baselineLights = lights.Where(light =>
            !light.Identity.Contains("/fixture:", StringComparison.Ordinal)).ToArray();
        for (int face = result.FirstFace; face < result.FirstFace + result.FaceCount; face++)
        {
            Vector3 normal = mesh.LocalFaceNormal(face);
            Vector3[] samples = mesh.GetFaceVertexPositions(face)
                .Select(position => IsPadTopReceiver(face)
                    ? MegastationArtificialLighting.EvaluateLocalColourSpill(
                        position, normal, baselineLights, fixtureLights, occlusion)
                    : MegastationArtificialLighting.Evaluate(
                        position, normal, lights, occlusion))
                .ToArray();
            mesh.SetFaceArtificialLight(face, samples);
        }

        foreach ((int start, int count) in result.UntrackedArtificialLightVertexRanges)
            mesh.SetVertexRangeArtificialLight(start, count,
                (position, normal) => MegastationArtificialLighting.Evaluate(
                    position, normal, lights, occlusion));

        bool IsPadTopReceiver(int face)
            => result.PadTopReceiverRanges.Any(range =>
                face >= range.FirstFace && face < range.FirstFace + range.FaceCount);
    }

    private static void EmitPad(
        StationModuleMesh mesh,
        MegastationLandingPadPlan pad,
        Color dominant,
        Color secondary,
        Color accent,
        List<(int Start, int Count, float Illumination)> illumination,
        List<(int FirstFace, int FaceCount)> padTopReceiverRanges)
    {
        Vector3 up = pad.PadSurface.Normal;
        Vector3 right = pad.PadSurface.Right;
        Vector3 forward = pad.PadSurface.PreferredHeading;
        Vector3 centre = pad.PadSurface.Centre;
        float width = pad.NominalSize.X;
        float length = pad.NominalSize.Y;

        // L1c: the standardized footprint is now a physical installed component. Its
        // underside stops half a metre above the apron, leaving a real open volume
        // rather than a dark painted skirt. The slab itself participates in the static
        // station caster so the gap can read through normal bay lighting.
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMajor;
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        padTopReceiverRanges.Add(EmitOctagonalSlab(
            mesh,
            pad.FutureSupportPolygon,
            up,
            right,
            forward,
            MegastationLandingPadAssemblyStandards.PadSlabThickness,
            Color.Lerp(dominant, secondary, .38f),
            Color.Lerp(dominant, secondary, .22f),
            Color.Lerp(dominant, Color.Black, .58f)));

        mesh.CurrentDecorClass = DecorClass.LandingPadMarkings;

        // Heavy segmented border, inset from the support edge and interrupted at the
        // preferred approach end so heading is visible even without the chevrons.
        SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
        float sideLength = length - 4f;
        for (int side = -1; side <= 1; side += 2)
            AddSurfaceBar(mesh, centre + right * side * (width * .5f - 1.1f) + up * .035f,
                forward, sideLength, 1.3f, .07f, up, Color.Lerp(accent, Color.White, .18f));
        AddSurfaceBar(mesh, centre - forward * (length * .5f - 1.1f) + up * .035f,
            right, width - 4f, 1.3f, .07f, up, Color.Lerp(accent, Color.White, .18f));
        for (int side = -1; side <= 1; side += 2)
            AddSurfaceBar(mesh, centre + forward * (length * .5f - 1.1f)
                + right * side * width * .30f + up * .035f,
                right, width * .22f, 1.3f, .07f, up, WarningColour);

        // Two broad inward-pointing chevrons: operational preference, not legality.
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        // Keep heading guidance in a dedicated approach band at the front of the pad.
        // A fixed front inset preserves a clear gap from the rear pad number even on
        // the 36 m standard pad; proportional placement previously let the second
        // chevron overlap the number glyphs there.
        Vector3 chevronCentre = centre + forward * (length * .5f - 3.5f) + up * .075f;
        for (int row = 0; row < 2; row++)
        {
            Vector3 tip = chevronCentre - forward * row * 8f;
            AddSurfaceBarBetween(mesh, tip, tip - forward * 6.5f - right * 7f,
                1.7f, .08f, up, MarkingColour);
            AddSurfaceBarBetween(mesh, tip, tip - forward * 6.5f + right * 7f,
                1.7f, .08f, up, MarkingColour);
        }

        // Large, correctly oriented pad identity near the rear/service side.
        int textStart = mesh.FaceCount;
        float pixel = 1.15f;
        string text = pad.PadId[^2..];
        float textWidth = text.Length * (BitmapFonts.CharW + 1) * pixel;
        Vector3 textOrigin = centre - right * textWidth * .5f - forward * (length * .27f)
            + up * .12f;
        PlanarTextGeometry.Add(mesh, text, textOrigin,
            surfaceNormal: up, readingDirection: right, pixel, LampColour);
        illumination.Add((textStart, mesh.FaceCount - textStart, .78f));

        int lampStart = mesh.FaceCount;
        foreach (MegastationLandingPadFixturePlan fixture in
                 MegastationLandingPadAssemblyStandards.Fixtures(pad))
        {
            AddBox(mesh, Frame(fixture.Centre, right, up, forward),
                new(1.2f, .28f, 2.1f), fixture.Colour);
        }
        illumination.Add((lampStart, mesh.FaceCount - lampStart, .95f));

        EmitPadAccess(mesh, pad, dominant, accent);
    }

    private static void EmitPadAccess(
        StationModuleMesh mesh,
        MegastationLandingPadPlan pad,
        Color dominant,
        Color accent)
    {
        Vector3 up = pad.PadSurface.Normal;
        Vector3 right = pad.PadSurface.Right;
        Vector3 forward = pad.PadSurface.PreferredHeading;
        (Vector3 stairTop, Vector3 rampTop, Vector3 serviceDirection) =
            MegastationLandingPadAssemblyStandards.AccessAnchors(pad);
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);

        const int stepCount = 5;
        for (int step = 0; step < stepCount; step++)
        {
            float height = (step + 1) * StairRise;
            float distance = (stepCount - step - .5f) * StairTread;
            Vector3 stepCentre = stairTop + serviceDirection * distance
                - up * (MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron - height * .5f);
            AddBox(mesh, Frame(stepCentre, right, up, forward),
                new(MegastationLandingPadAssemblyStandards.PersonnelStairWidth, height, StairTread),
                Color.Lerp(dominant, accent, .22f));
        }

        // Railing is deliberately confined to the personnel stair. Three posts per
        // side make its human scale unambiguous without fencing the operational pad.
        Color railColour = Color.Lerp(accent, Color.White, .08f);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 sideOffset = right * side
                * (MegastationLandingPadAssemblyStandards.PersonnelStairWidth * .5f - .08f);
            Vector3 low = stairTop
                + serviceDirection * MegastationLandingPadAssemblyStandards.PersonnelStairRun
                + sideOffset
                - up * MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron;
            Vector3 high = stairTop + sideOffset;
            AddBarBetween3D(mesh, low, low + up * RailingHeight, .10f, railColour);
            AddBarBetween3D(mesh, high, high + up * RailingHeight, .10f, railColour);
            AddBarBetween3D(mesh,
                low + up * RailingHeight,
                high + up * RailingHeight,
                .10f,
                railColour);
        }

        // Broad cargo access is a single robust unrailed ramp. The pad-side end is
        // flush with the landing surface; the far end meets the apron surface.
        Vector3 rampLow = rampTop
            + serviceDirection * MegastationLandingPadAssemblyStandards.CargoRampRun
            - up * MegastationLandingPadAssemblyStandards.PadTopHeightAboveApron;
        Vector3 rampAxis = Vector3.Normalize(rampTop - rampLow);
        Vector3 rampNormal = Vector3.Normalize(Vector3.Cross(rampAxis, right));
        AddBox(mesh, Frame((rampTop + rampLow) * .5f, right, rampNormal, rampAxis),
            new(MegastationLandingPadAssemblyStandards.CargoRampWidth,
                .20f, Vector3.Distance(rampTop, rampLow)),
            Color.Lerp(dominant, accent, .18f));

        // Keep one successful scale reference beside the central pad-owned stair;
        // this remains a calibration prop rather than a simulated population.
        if (pad.PadId == "LD-05")
        {
            Vector3 humanFeet = ScaleHumanFeetPosition(pad);
            EmitScaleHuman(mesh, humanFeet, up, forward);
        }
    }

    // Returns the actual support contact point for the scale human. PadSurface.Centre is
    // authoritative for the installed pad's top plane; no bay-floor or pad-height offset
    // belongs in the human primitive.
    internal static Vector3 ScaleHumanFeetPosition(MegastationLandingPadPlan pad)
    {
        (_, _, Vector3 serviceDirection) =
            MegastationLandingPadAssemblyStandards.AccessAnchors(pad);
        return pad.PadSurface.Centre
            + serviceDirection * (MegastationLandingPadAssemblyStandards.PersonnelStairRun + .75f)
            + pad.PadSurface.Right * (-pad.NominalSize.X * .24f + 1.7f);
    }

    private static void EmitStairAccess(
        StationModuleMesh mesh,
        Vector3 buildingFloor,
        Vector3 personnelDoor,
        float frontOffset,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        Color dominant,
        Color accent)
    {
        const float platformHeight = AccessPlatformHeight;
        const float platformDepth = 2.4f;
        const float stairRise = StairRise;
        const float stairTread = StairTread;
        const int stepCount = 6;
        Vector3 doorLine = buildingFloor
            + right * Vector3.Dot(personnelDoor - buildingFloor, right)
            + forward * frontOffset;
        Vector3 platformCentre = doorLine + forward * (platformDepth * .5f)
            + up * (platformHeight - .12f);
        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        AddBox(mesh, Frame(platformCentre, right, up, forward),
            new(3.8f, .24f, platformDepth), Color.Lerp(dominant, accent, .22f));

        float platformOuter = frontOffset + platformDepth;
        for (int step = 0; step < stepCount; step++)
        {
            float height = (step + 1) * stairRise;
            float distance = (stepCount - step - .5f) * stairTread;
            Vector3 centre = buildingFloor
                + right * Vector3.Dot(personnelDoor - buildingFloor, right)
                + forward * (platformOuter + distance)
                + up * (height * .5f);
            AddBox(mesh, Frame(centre, right, up, forward),
                new(2.2f, height, stairTread), Color.Lerp(dominant, accent, .16f));
        }

        EmitRailing(mesh, doorLine + forward * platformDepth, right, up, forward,
            platformDepth, 3.8f, accent);
    }

    private static void EmitRampAccess(
        StationModuleMesh mesh,
        Vector3 buildingFloor,
        Vector3 personnelDoor,
        float frontOffset,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        Color dominant,
        Color accent)
    {
        const float rise = AccessPlatformHeight;
        const float run = 8f;
        float lateral = Vector3.Dot(personnelDoor - buildingFloor, right);
        Vector3 high = buildingFloor + right * lateral + forward * (frontOffset + .35f) + up * rise;
        Vector3 low = high + forward * run - up * rise;
        Vector3 slope = Vector3.Normalize(high - low);
        Vector3 surfaceNormal = Vector3.Normalize(Vector3.Cross(slope, right));
        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        AddBox(mesh, Frame((high + low) * .5f, right, surfaceNormal, slope),
            new(3.2f, .18f, Vector3.Distance(high, low)), Color.Lerp(dominant, accent, .18f));
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 lateralOffset = right * side * 1.5f;
            AddBarBetween3D(mesh,
                low + lateralOffset + up * RailingHeight,
                high + lateralOffset + up * RailingHeight,
                .10f,
                accent);
        }
    }

    private static void EmitRailing(
        StationModuleMesh mesh,
        Vector3 outerCentre,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float depth,
        float width,
        Color colour)
    {
        const float height = RailingHeight;
        const float thickness = .10f;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 sideOffset = right * side * (width * .5f - .08f);
            Vector3 inner = outerCentre - forward * depth + sideOffset;
            Vector3 outer = outerCentre + sideOffset;
            AddBox(mesh, Frame(inner + up * height * .5f, right, up, forward),
                new(thickness, height, thickness), colour);
            AddBox(mesh, Frame(outer + up * height * .5f, right, up, forward),
                new(thickness, height, thickness), colour);
            AddBarBetween3D(mesh, inner + up * height, outer + up * height, thickness, colour);
        }
    }

    // feetPosition is the exact contact point on the caller-owned supporting surface.
    // The primitive knows nothing about pads, floors, stairs, or their elevations.
    internal static void EmitScaleHuman(
        StationModuleMesh mesh,
        Vector3 feetPosition,
        Vector3 surfaceNormal,
        Vector3 facingDirection)
    {
        Vector3 up = Vector3.Normalize(surfaceNormal);
        Vector3 planarForward = facingDirection - up * Vector3.Dot(facingDirection, up);
        if (planarForward.LengthSquared() <= 1e-10f)
            throw new ArgumentException(
                "Scale-human facing direction must lie on its supporting surface.",
                nameof(facingDirection));
        Vector3 forward = Vector3.Normalize(planarForward);
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, forward));
        Color suit = new(182, 132, 56);
        Color helmet = new(206, 194, 164);
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        for (int side = -1; side <= 1; side += 2)
            AddBox(mesh, Frame(feetPosition + right * side * .105f + up * .34f, right, up, forward),
                new(.14f, .68f, .18f), suit);
        AddBox(mesh, Frame(feetPosition + up * 1.08f, right, up, forward),
            new(.46f, .80f, .28f), suit);
        AddBox(mesh, Frame(feetPosition + up * (HumanReferenceHeight - .18f), right, up, forward),
            new(.34f, .36f, .34f), helmet);
    }

    private static void EmitOperationalFloorMarkings(
        StationModuleMesh mesh,
        MegastationLandingDistrictPlan plan,
        Vector3 up,
        Vector3 right,
        Vector3 forward)
    {
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        foreach (MegastationLoadingAreaPlan area in plan.LoadingAreas)
        {
            EmitFloorOutline(mesh, area.Centre, area.Size, right, up, forward,
                MegastationLandingDistrictPlanner.LoadingAreaOutlineWidth, MarkingColour);

            const float pixel = .12f;
            float textHeight = BitmapFonts.CharH * pixel;
            Vector3 service = -forward;
            Vector3 origin = area.Centre
                + right * (area.Size.X * .5f + .8f)
                - service * (textHeight * .5f)
                + up * .045f;
            PlanarTextGeometry.Add(mesh, area.Label, origin,
                surfaceNormal: up, readingDirection: right, pixel, MarkingColour);
        }

        foreach (MegastationKeepClearZonePlan zone in plan.KeepClearZones)
        {
            EmitDiagonalStripes(mesh, zone.Centre, zone.Size, right, up, -forward,
                1.35f, .16f, WarningColour);
            if (!zone.ShowLabel) continue;

            const string label = "KEEP CLEAR";
            const float pixel = .10f;
            float textWidth = label.Length * (BitmapFonts.CharW + 1) * pixel;
            float textHeight = BitmapFonts.CharH * pixel;
            Vector3 origin = zone.Centre - right * (textWidth * .5f)
                + forward * (textHeight * .5f) + up * .055f;
            PlanarTextGeometry.Add(mesh, label, origin,
                surfaceNormal: up, readingDirection: right, pixel, MarkingColour);
        }
    }

    private static void EmitLoadingContainers(
        StationModuleMesh mesh,
        MegastationLandingDistrictPlan plan,
        Vector3 up,
        Vector3 right,
        Vector3 forward,
        Color dominant,
        Color accent,
        List<(int Start, int Count)> artificialLightVertexRanges)
    {
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        foreach (MegastationLoadingAreaPlan area in plan.LoadingAreas)
        foreach (MegastationLandingContainerPlan container in area.Containers)
        {
            Color colour = (unchecked((uint)container.Seed) & 1u) == 0u
                ? Color.Lerp(dominant, accent, .42f)
                : Color.Lerp(dominant, new Color(116, 76, 48), .48f);
            float wear = .16f + ((unchecked((uint)container.Seed) >> 8) & 0xffu) / 255f * .28f;
            var (vertices, indices) = ShippingContainerFactory.GenerateVertices(
                colour,
                wear,
                container.Seed,
                text: null,
                lockGrade: LockGrade.Civilian);
            int vertexStart = mesh.VertexCount;
            mesh.MergeTransformed(vertices, indices,
                Frame(container.Centre, right, up, forward));
            artificialLightVertexRanges.Add((vertexStart, mesh.VertexCount - vertexStart));
        }
    }

    private static void EmitFloorOutline(
        StationModuleMesh mesh,
        Vector3 centre,
        Vector2 size,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float width,
        Color colour)
    {
        float halfRight = size.X * .5f;
        float halfForward = size.Y * .5f;
        for (int side = -1; side <= 1; side += 2)
        {
            AddSurfaceBar(mesh,
                centre + right * side * halfRight + up * .025f,
                forward, size.Y, width, .025f, up, colour);
            AddSurfaceBar(mesh,
                centre + forward * side * halfForward + up * .025f,
                right, size.X, width, .025f, up, colour);
        }
    }

    private static void EmitDiagonalStripes(
        StationModuleMesh mesh,
        Vector3 centre,
        Vector2 size,
        Vector3 right,
        Vector3 up,
        Vector3 depth,
        float spacing,
        float width,
        Color colour)
    {
        float halfRight = size.X * .5f;
        float halfDepth = size.Y * .5f;
        float minimum = -halfRight - halfDepth;
        float maximum = halfRight + halfDepth;
        for (float k = minimum; k <= maximum + .001f; k += spacing)
        {
            var intersections = new List<Vector2>(4);
            Add(-halfRight, k + halfRight);
            Add(halfRight, k - halfRight);
            Add(k + halfDepth, -halfDepth);
            Add(k - halfDepth, halfDepth);
            if (intersections.Count < 2) continue;

            Vector2 a = intersections[0];
            Vector2 b = intersections[^1];
            AddSurfaceBarBetween(
                mesh,
                centre + right * a.X + depth * a.Y + up * .035f,
                centre + right * b.X + depth * b.Y + up * .035f,
                width,
                .025f,
                up,
                colour);

            void Add(float x, float z)
            {
                if (x < -halfRight - .001f || x > halfRight + .001f
                    || z < -halfDepth - .001f || z > halfDepth + .001f)
                    return;
                var point = new Vector2(x, z);
                if (!intersections.Any(existing => Vector2.DistanceSquared(existing, point) < 1e-6f))
                    intersections.Add(point);
            }
        }
    }

    private static void AddBarBetween3D(
        StationModuleMesh mesh,
        Vector3 a,
        Vector3 b,
        float thickness,
        Color colour)
    {
        Vector3 direction = b - a;
        float length = direction.Length();
        if (length <= 1e-5f) return;
        mesh.AddOrientedBox((a + b) * .5f, direction / length, length,
            thickness, thickness, colour);
    }

    private static void EmitOctagonalSurface(
        StationModuleMesh mesh,
        IReadOnlyList<Vector3> polygon,
        Vector3 expectedNormal,
        Color colour)
    {
        Vector3 centre = Vector3.Zero;
        foreach (Vector3 point in polygon) centre += point;
        centre /= polygon.Count;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[(i + 1) % polygon.Count];
            if (Vector3.Dot(Vector3.Cross(a - centre, b - centre), expectedNormal) < 0f)
                mesh.AddTriangle(centre, b, a, colour);
            else
                mesh.AddTriangle(centre, a, b, colour);
        }
    }

    private static (int FirstFace, int FaceCount) EmitOctagonalSlab(
        StationModuleMesh mesh,
        IReadOnlyList<Vector3> top,
        Vector3 up,
        Vector3 right,
        Vector3 forward,
        float thickness,
        Color topColour,
        Color sideColour,
        Color undersideColour)
    {
        int topFirstFace = mesh.FaceCount;
        EmitTessellatedOctagonalPadTop(mesh, top, up, right, forward, topColour);
        int topFaceCount = mesh.FaceCount - topFirstFace;
        Vector3[] bottom = top.Select(point => point - up * thickness).ToArray();

        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        EmitOctagonalSurface(mesh, bottom, -up, undersideColour);

        SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
        Vector3 centre = Vector3.Zero;
        foreach (Vector3 point in top) centre += point;
        centre /= top.Count;
        for (int i = 0; i < top.Count; i++)
        {
            int next = (i + 1) % top.Count;
            Vector3 edgeCentre = (top[i] + top[next]) * .5f;
            Vector3 outward = edgeCentre - centre;
            outward -= up * Vector3.Dot(outward, up);
            outward = Vector3.Normalize(outward);
            AddQuadFacing(mesh,
                top[i], top[next], bottom[next], bottom[i], outward, sideColour);
        }
        return (topFirstFace, topFaceCount);
    }

    private static void EmitTessellatedOctagonalPadTop(
        StationModuleMesh mesh,
        IReadOnlyList<Vector3> polygon,
        Vector3 up,
        Vector3 right,
        Vector3 forward,
        Color colour)
    {
        Vector3 centre = polygon.Aggregate(Vector3.Zero, (sum, point) => sum + point)
            / polygon.Count;
        float halfWidth = polygon.Max(point => MathF.Abs(Vector3.Dot(point - centre, right)));
        float halfLength = polygon.Max(point => MathF.Abs(Vector3.Dot(point - centre, forward)));
        float clip = MegastationLandingDistrictPlanner.CornerClip;
        Vector3 origin = centre - right * halfWidth - forward * halfLength;

        EmitGrid(-halfWidth + clip, halfWidth - clip, -halfLength, halfLength);
        EmitGrid(-halfWidth, -halfWidth + clip, -halfLength + clip, halfLength - clip);
        EmitGrid(halfWidth - clip, halfWidth, -halfLength + clip, halfLength - clip);

        EmitTriangle(-halfWidth + clip, halfLength, -halfWidth + clip, halfLength - clip,
            -halfWidth, halfLength - clip);
        EmitTriangle(halfWidth - clip, halfLength - clip, halfWidth - clip, halfLength,
            halfWidth, halfLength - clip);
        EmitTriangle(-halfWidth, -halfLength + clip, -halfWidth + clip, -halfLength + clip,
            -halfWidth + clip, -halfLength);
        EmitTriangle(halfWidth - clip, -halfLength, halfWidth - clip, -halfLength + clip,
            halfWidth, -halfLength + clip);

        void EmitGrid(float minimumRight, float maximumRight,
            float minimumForward, float maximumForward)
        {
            int columns = Math.Max(1, (int)MathF.Ceiling(
                (maximumRight - minimumRight) / PadTopReceiverSpacing));
            int rows = Math.Max(1, (int)MathF.Ceiling(
                (maximumForward - minimumForward) / PadTopReceiverSpacing));
            float width = (maximumRight - minimumRight) / columns;
            float depth = (maximumForward - minimumForward) / rows;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                float x0 = minimumRight + column * width;
                float x1 = minimumRight + (column + 1) * width;
                float z0 = minimumForward + row * depth;
                float z1 = minimumForward + (row + 1) * depth;
                AddProjectedQuadFacing(
                    Point(x0, z0), Point(x1, z0), Point(x1, z1), Point(x0, z1));
            }
        }

        void EmitTriangle(float ax, float az, float bx, float bz, float cx, float cz)
        {
            Vector3 a = Point(ax, az);
            Vector3 b = Point(bx, bz);
            Vector3 c = Point(cx, cz);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), up) < 0f)
                (b, c) = (c, b);
            Vector2 Uv(Vector3 point) => new(
                Vector3.Dot(point - origin, right) / mesh.CurrentUvScaleMeters,
                Vector3.Dot(point - origin, forward) / mesh.CurrentUvScaleMeters);
            mesh.AddTriangleWithUv(a, Uv(a), b, Uv(b), c, Uv(c), colour);
        }

        void AddProjectedQuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), up) < 0f)
                (b, d) = (d, b);
            mesh.AddQuadProjected(a, b, c, d, up, right, forward,
                origin, mesh.CurrentUvScaleMeters, colour);
        }

        Vector3 Point(float x, float z) => centre + right * x + forward * z;
    }

    private static void AddQuadFacing(
        StationModuleMesh mesh,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 expectedNormal,
        Color colour)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), expectedNormal) >= 0f)
            mesh.AddQuad(a, b, c, d, colour);
        else
            mesh.AddQuad(a, d, c, b, colour);
    }

    private static void AddSurfaceBar(
        StationModuleMesh mesh,
        Vector3 centre,
        Vector3 longAxis,
        float length,
        float width,
        float height,
        Vector3 normal,
        Color colour)
    {
        Vector3 across = Vector3.Normalize(Vector3.Cross(normal, longAxis));
        Matrix frame = Frame(centre, across, normal, longAxis);
        AddBox(mesh, frame, new(width, height, length), colour);
    }

    private static void AddSurfaceBarBetween(
        StationModuleMesh mesh,
        Vector3 a,
        Vector3 b,
        float width,
        float height,
        Vector3 normal,
        Color colour)
        => AddSurfaceBar(mesh, (a + b) * .5f, Vector3.Normalize(b - a),
            Vector3.Distance(a, b), width, height, normal, colour);

    internal static Matrix Frame(
        Vector3 centre, Vector3 right, Vector3 up, Vector3 forward)
    {
        // PortalRight is allowed to be flipped to match the grid's canonical width
        // direction. It therefore is not guaranteed to form a right-handed basis with
        // PortalUp and OutwardNormal. AddOrientedBox requires a non-reflective frame or
        // every face winding is reversed, so derive the box depth axis from Right x Up.
        // Boxes are symmetric along depth; PreferredHeading remains authoritative for
        // placement, chevrons, doors, and the rear service relationship.
        Vector3 depth = Vector3.Normalize(Vector3.Cross(right, up));
        Debug.Assert(MathF.Abs(Vector3.Dot(depth, Vector3.Normalize(forward))) > .999f);
        return new(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            depth.X, depth.Y, depth.Z, 0f,
            centre.X, centre.Y, centre.Z, 1f);
    }

    private static void AddBox(StationModuleMesh mesh, Matrix frame, Vector3 size, Color colour)
        => mesh.AddOrientedBox(frame, size, colour);

    private static void SetMaterial(StationModuleMesh mesh, SystemMaterialFamilyId family)
    {
        mesh.CurrentMaterialFamily = family;
        mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(family).TileSizeMeters;
    }
}
