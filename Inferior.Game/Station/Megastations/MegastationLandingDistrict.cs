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
}

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

public sealed record MegastationLandingServiceBuilding(
    string Identity,
    Vector3 Centre,
    Vector3 Size,
    int Seed);

public sealed record MegastationLandingDistrictDiagnostics(
    int PadCount,
    int StandardPadCount,
    int LargePadCount,
    Vector2 ApronSize,
    int ServiceBuildingCount,
    int ArtificialLightCount,
    int VisibleVertexCount,
    int VisibleTriangleCount,
    int ShadowVertexCount,
    int ShadowTriangleCount,
    string Signature);

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
    IReadOnlyList<MegastationArtificialLight> ArtificialLights,
    MegastationLandingDistrictDiagnostics Diagnostics);

public static class MegastationLandingDistrictPlanner
{
    public const int AlgorithmVersion = 1;
    public const float StandardPadSize = 36f;
    public const float LargePadLength = 72f;
    public const float CornerClip = 1f;
    public const float BerthMargin = 5f;
    public const float BuildingSetback = 10f;
    public const float OperationalApronDepth = 14f;

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

        int lightingSeed = MegastationSeed.Derive(seed, "lighting");
        var lights = new List<MegastationArtificialLight>(8);
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

        string signature = Signature(seed, pads, buildings, lights);
        var diagnostics = new MegastationLandingDistrictDiagnostics(
            pads.Count,
            pads.Count(pad => !pad.IsLarge),
            pads.Count(pad => pad.IsLarge),
            new(apronRightSpan, apronDepthSpan),
            buildings.Count,
            lights.Count,
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
            lights,
            diagnostics);

        void AddPad(int number, float rightOffset, float inwardDepth, float length, bool large)
        {
            string id = $"LD-{number:00}";
            Vector3 centre = Compose(districtRight + rightOffset, upMin + .22f, inwardDepth);
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
            buildings.Add(new(
                $"landing-district/service/{identity}",
                Compose(districtRight + rightOffset, upMin + adjustedHeight * .5f, buildingDepth),
                new(width, adjustedHeight, depth),
                child));
        }

        Vector3 Compose(float r, float u, float d) => right * r + up * u + inward * d;
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
        IReadOnlyList<MegastationArtificialLight> lights)
    {
        var text = new StringBuilder().Append(AlgorithmVersion).Append('|').Append(seed);
        foreach (MegastationLandingPadPlan pad in pads)
            text.Append('|').Append(pad.PadId).Append(':').Append(F(pad.PadSurface.Centre.X))
                .Append(',').Append(F(pad.PadSurface.Centre.Y)).Append(',')
                .Append(F(pad.PadSurface.Centre.Z)).Append(':').Append(pad.NominalSize);
        foreach (MegastationLandingServiceBuilding building in buildings)
            text.Append('|').Append(building.Identity).Append(':').Append(building.Centre)
                .Append(':').Append(building.Size);
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
    MegastationLandingDistrictDiagnostics Diagnostics);

public static class MegastationLandingDistrictMeshBuilder
{
    internal const float BlastShieldSlabThickness = .5f;
    internal const float BlastShieldHeight = 4f;
    internal const float CargoDoorHeight = 6f;
    internal const float PersonnelDoorWidth = 1.4f;
    internal const float PersonnelDoorHeight = 2.4f;
    internal const float AccessPlatformHeight = 1.2f;
    internal const float StairRise = .20f;
    internal const float StairTread = .30f;
    internal const float RailingHeight = 1.05f;
    internal const float HumanReferenceHeight = 1.84f;
    internal static readonly Vector3 ContainerReferenceSize = new(2.5f, 2.5f, 6f);
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
        Color dominant = materials?.Palette.DominantTint ?? new Color(76, 80, 82);
        Color secondary = materials?.Palette.SecondaryTint ?? new Color(98, 99, 94);
        Color accent = materials?.Palette.AccentTint ?? new Color(132, 126, 94);
        Vector3 up = plan.FloorNormal;
        Vector3 right = plan.DistrictRight;
        Vector3 forward = plan.PreferredHeading;

        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        AddBox(mesh, Frame(plan.ApronCentre, right, up, forward),
            new(plan.ApronSize.X, .16f, plan.ApronSize.Y),
            Color.Lerp(dominant, Color.Black, .16f));

        foreach (MegastationLandingPadPlan pad in plan.Pads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmitPad(mesh, pad, dominant, secondary, accent, illumination);
        }

        foreach (MegastationLandingServiceBuilding building in plan.ServiceBuildings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mesh.CurrentDecorClass = DecorClass.MegastationInteriorMajor;
            SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
            AddBox(mesh, Frame(building.Centre, right, up, forward), building.Size,
                Color.Lerp(dominant, secondary, .35f));

            mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
            SetMaterial(mesh, SystemMaterialFamilyId.CleanTechnicalAlloy);
            float frontOffset = building.Size.Z * .5f + .16f;
            Vector3 buildingFloor = building.Centre - up * (building.Size.Y * .5f);
            float cargoWidth = building.Identity.EndsWith("operations", StringComparison.Ordinal)
                ? 10f : 8.5f;
            const float cargoHeight = CargoDoorHeight;
            Vector3 cargoDoor = buildingFloor + forward * frontOffset
                - right * (building.Size.X * .12f) + up * (cargoHeight * .5f);
            AddBox(mesh, Frame(cargoDoor, right, up, forward),
                new(cargoWidth, cargoHeight, .28f), Color.Lerp(secondary, Color.Black, .42f));

            const float personnelWidth = PersonnelDoorWidth;
            const float personnelHeight = PersonnelDoorHeight;
            bool raisedAccess = building.Identity.EndsWith("operations", StringComparison.Ordinal)
                || building.Identity.EndsWith("east", StringComparison.Ordinal);
            float accessFloor = raisedAccess ? AccessPlatformHeight : 0f;
            Vector3 personnelDoor = buildingFloor + forward * frontOffset
                + right * (building.Size.X * .34f)
                + up * (accessFloor + personnelHeight * .5f);
            AddBox(mesh, Frame(personnelDoor, right, up, forward),
                new(personnelWidth, personnelHeight, .25f), Color.Lerp(secondary, Color.Black, .52f));

            if (building.Identity.EndsWith("operations", StringComparison.Ordinal))
                EmitStairAccess(mesh, buildingFloor, personnelDoor, frontOffset,
                    right, up, forward, dominant, accent);
            else if (building.Identity.EndsWith("east", StringComparison.Ordinal))
                EmitRampAccess(mesh, buildingFloor, personnelDoor, frontOffset,
                    right, up, forward, dominant, accent);
        }

        EmitCargoScaleReferences(mesh, plan, up, right, forward, dominant, accent);

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
        return new(firstFace, faces, illumination, plan.Diagnostics with
        {
            VisibleVertexCount = vertices,
            VisibleTriangleCount = triangles,
            ShadowVertexCount = casterVertices,
            ShadowTriangleCount = casterTriangles,
        });
    }

    public static void ApplyLighting(
        StationModuleMesh mesh,
        MegastationLandingDistrictMeshResult result,
        IReadOnlyList<MegastationArtificialLight> lights)
    {
        foreach ((int start, int count, float value) in result.IlluminationRanges)
        for (int face = start; face < start + count; face++)
            mesh.SetFaceIllumination(face, value);

        for (int face = result.FirstFace; face < result.FirstFace + result.FaceCount; face++)
        {
            Vector3 normal = mesh.LocalFaceNormal(face);
            Vector3[] samples = mesh.GetFaceVertexPositions(face)
                .Select(position => MegastationArtificialLighting.Evaluate(position, normal, lights))
                .ToArray();
            mesh.SetFaceArtificialLight(face, samples);
        }
    }

    private static void EmitPad(
        StationModuleMesh mesh,
        MegastationLandingPadPlan pad,
        Color dominant,
        Color secondary,
        Color accent,
        List<(int Start, int Count, float Illumination)> illumination)
    {
        Vector3 up = pad.PadSurface.Normal;
        Vector3 right = pad.PadSurface.Right;
        Vector3 forward = pad.PadSurface.PreferredHeading;
        Vector3 centre = pad.PadSurface.Centre;
        float width = pad.NominalSize.X;
        float length = pad.NominalSize.Y;

        mesh.CurrentDecorClass = DecorClass.LandingPadMarkings;
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        EmitOctagonalSurface(mesh, pad.FutureSupportPolygon, up,
            Color.Lerp(dominant, secondary, .28f));

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
        ShippingContainerFactory.AddTextGeometry(
            mesh, text, textOrigin, right, forward, up, pixel, LampColour);
        illumination.Add((textStart, mesh.FaceCount - textStart, .78f));

        int lampStart = mesh.FaceCount;
        float lampForward = length * .5f - 2.4f;
        float lampRight = width * .5f - 2.2f;
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
        {
            Color colour = sz > 0 ? LampColour : new Color(255, 204, 112);
            AddBox(mesh, Frame(
                    centre + right * sx * lampRight + forward * sz * lampForward + up * .18f,
                    right, up, forward),
                new(1.2f, .28f, 2.1f), colour);
        }
        illumination.Add((lampStart, mesh.FaceCount - lampStart, .95f));

        // Rear blast shield is pad-owned infrastructure. L1b retains its footprint and
        // relationship to the pad but replaces the bunker-like 3.2 m slab with a 0.5 m
        // vertical plate on a broader low footing.
        SetMaterial(mesh, SystemMaterialFamilyId.HeavyIndustrialPlate);
        Vector3 shield = centre - forward * (length * .5f + 2.3f);
        AddBox(mesh, Frame(shield + up * .25f,
                right, up, forward),
            new(width - 2f, .5f, 2.4f), Color.Lerp(dominant, Color.Black, .24f));
        AddBox(mesh, Frame(shield + up * 2.25f,
                right, up, forward),
            new(width - 5f, BlastShieldHeight, BlastShieldSlabThickness),
            Color.Lerp(dominant, Color.Black, .18f));
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
        Vector3 humanFeet = platformCentre + up * .12f + right * .95f;
        EmitHuman(mesh, humanFeet, right, up, forward);
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

    private static void EmitHuman(
        StationModuleMesh mesh,
        Vector3 feet,
        Vector3 right,
        Vector3 up,
        Vector3 forward)
    {
        Color suit = new(182, 132, 56);
        Color helmet = new(206, 194, 164);
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        for (int side = -1; side <= 1; side += 2)
            AddBox(mesh, Frame(feet + right * side * .105f + up * .34f, right, up, forward),
                new(.14f, .68f, .18f), suit);
        AddBox(mesh, Frame(feet + up * 1.08f, right, up, forward),
            new(.46f, .80f, .28f), suit);
        AddBox(mesh, Frame(feet + up * (HumanReferenceHeight - .18f), right, up, forward),
            new(.34f, .36f, .34f), helmet);
    }

    private static void EmitCargoScaleReferences(
        StationModuleMesh mesh,
        MegastationLandingDistrictPlan plan,
        Vector3 up,
        Vector3 right,
        Vector3 forward,
        Color dominant,
        Color accent)
    {
        MegastationLandingPadPlan pad = plan.Pads.Single(candidate => candidate.PadId == "LD-05");
        Vector3 rearEdge = pad.PadSurface.Centre - forward * (pad.NominalSize.Y * .5f);
        Vector3 floor = rearEdge - up * .22f;
        mesh.CurrentDecorClass = DecorClass.MegastationInteriorMinor;
        SetMaterial(mesh, SystemMaterialFamilyId.PaintedCoatedMetal);
        for (int container = 0; container < 2; container++)
        {
            float rearDistance = 6.75f + container * 6.5f;
            Vector3 centre = floor - forward * rearDistance + right * 10f + up * 1.25f;
            Color colour = container == 0
                ? Color.Lerp(dominant, accent, .42f)
                : Color.Lerp(dominant, new Color(116, 76, 48), .48f);
            AddBox(mesh, Frame(centre, right, up, forward), ContainerReferenceSize, colour);
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
