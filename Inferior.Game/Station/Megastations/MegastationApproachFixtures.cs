using Microsoft.Xna.Framework;

namespace Inferior.Game.StationGen.Megastations;

public sealed record MegastationApproachFixture(
    IReadOnlyList<MegastationInteriorGuidanceElement> Elements,
    MegastationApproachGuidanceBeam Beam,
    MegastationInteriorGuidanceMarker Marker);

/// <summary>H1e's accepted fixture, shared by crown-mounted and face-mounted entrances.</summary>
public static class MegastationApproachFixtures
{
    public const float PlateDepth = 2.2f;
    public const float HousingDepth = 7f;
    public const float BarrelDepth = 5f;
    public const float EmitterDepth = .9f;
    public const float SourceClearance = .15f;
    public static Color UpColour { get; } = new(62, 186, 255);
    public static Color DownColour { get; } = new(255, 174, 42);

    public static MegastationApproachFixture Create(string prefix, int horizontal, int vertical,
        Vector3 mountingPoint, Vector3 right, Vector3 up, Vector3 outward, float plateSpan,
        Color mountColour, Color housingColour, Color barrelColour, float length, float halfAngle)
    {
        string corner = $"{horizontal}:{vertical}";
        Color colour = vertical > 0 ? UpColour : DownColour;
        float housingSpan = plateSpan * .68f;
        float barrelSpan = housingSpan * .55f;
        Vector3 plateCentre = mountingPoint + outward * (PlateDepth * .5f);
        Vector3 housingCentre = mountingPoint + outward * (PlateDepth + HousingDepth * .5f);
        Vector3 barrelCentre = mountingPoint + outward * (PlateDepth + HousingDepth + BarrelDepth * .5f);
        Vector3 emitterCentre = mountingPoint + outward * (PlateDepth + HousingDepth + BarrelDepth + EmitterDepth * .5f);
        Vector3 source = emitterCentre + outward * (EmitterDepth * .5f + SourceClearance);
        var elements = new List<MegastationInteriorGuidanceElement>();
        Add("mount", plateCentre, new(plateSpan, plateSpan, PlateDepth), mountColour,
            SystemMaterialFamilyId.HeavyIndustrialPlate, true);
        Add("housing", housingCentre, new(housingSpan, housingSpan, HousingDepth), housingColour,
            SystemMaterialFamilyId.CleanTechnicalAlloy, true);
        Add("barrel", barrelCentre, new(barrelSpan, barrelSpan, BarrelDepth), barrelColour,
            SystemMaterialFamilyId.HeavyIndustrialPlate, false);
        Add("emitter", emitterCentre, new(barrelSpan * .86f, barrelSpan * .86f, EmitterDepth), colour,
            SystemMaterialFamilyId.CleanTechnicalAlloy, false, .98f);
        return new(elements.AsReadOnly(), new($"{prefix}/beam:{corner}",
            vertical > 0 ? MegastationApproachBeamVertical.Upper : MegastationApproachBeamVertical.Lower,
            horizontal, source, outward, right, up, colour, length, halfAngle),
            new($"{prefix}/source:{corner}", MegastationInteriorGuidanceKind.ApproachFixture,
                source, colour, .82f, outward, 24f, 500f, 3000f));

        void Add(string part, Vector3 centre, Vector3 size, Color tint,
            SystemMaterialFamilyId family, bool casts, float illumination = 0f)
        {
            Vector3 z = Vector3.Normalize(Vector3.Cross(right, up));
            Matrix frame = new(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
                z.X, z.Y, z.Z, 0, centre.X, centre.Y, centre.Z, 1);
            elements.Add(new($"{prefix}/fixture:{corner}/{part}",
                MegastationInteriorGuidanceKind.ApproachFixture, frame, size, tint, illumination, family, casts));
        }
    }

    public static void Emit(StationModuleMesh mesh, IEnumerable<MegastationInteriorGuidanceElement> elements)
    {
        foreach (var element in elements)
        {
            mesh.CurrentMaterialFamily = element.MaterialFamily;
            mesh.CurrentUvScaleMeters = SystemMaterialRecipes.Get(element.MaterialFamily).TileSizeMeters;
            mesh.CurrentDecorClass = element.CastsShadow ? DecorClass.MegastationInteriorMajor : DecorClass.MegastationInteriorMinor;
            int start = mesh.FaceCount;
            mesh.AddOrientedBox(element.Frame, element.Size, element.Colour);
            for (int face = start; face < mesh.FaceCount; face++)
                mesh.SetFaceIllumination(face, element.Illumination);
        }
    }
}
