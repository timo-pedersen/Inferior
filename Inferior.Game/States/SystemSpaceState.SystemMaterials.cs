using Inferior.Core.DataBus;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
using Inferior.Galaxy;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    private readonly StationVisualPackageSlot<SystemMaterialLibrary> _systemMaterialLibrarySlot = new();

    private SystemMaterialLibrary? SystemMaterials => _systemMaterialLibrarySlot.Current;

    private void InitializeSystemMaterialLibrary()
    {
        _systemMaterialLibrarySlot.Clear();
        int systemSeed = GalaxyGenerator.SystemSeed(_star).Seed;
        SystemMaterialLibrary library = SystemMaterialLibrary.Create(_gd, _star.Name, systemSeed);
        _systemMaterialLibrarySlot.Install(library);
        PublishSystemMaterialLibraryDiagnostics(library);
    }

    private static void PublishSystemMaterialLibraryDiagnostics(SystemMaterialLibrary library)
    {
        SystemMaterialLibraryDiagnostics diagnostics = library.Diagnostics;
        string families = string.Join('|', library.Resources.Values
            .OrderBy(resource => resource.Recipe.FamilyId)
            .Select(resource =>
                $"{resource.Recipe.FamilyId}:tile={resource.Recipe.TileSizeMeters:F1}m," +
                $"spec={resource.Recipe.SpecularStrength:F2},shine={resource.Recipe.SpecularShininess:F0}," +
                $"bump={resource.Recipe.BumpStrength:F2},tint={resource.TintBasis.PackedValue:X8}," +
                $"pixels={resource.PixelSignature[..16]}"));
        PublishStationResidencyMessage(
            $"[SystemMaterials] system={diagnostics.SystemIdentity}; version={diagnostics.LibraryVersion}; " +
            $"families={diagnostics.FamilyCount}; textures={diagnostics.TextureCount}; " +
            $"dimensions={diagnostics.TextureSize}x{diagnostics.TextureSize}; " +
            $"generationMs={diagnostics.CpuGenerationMilliseconds:F1}; " +
            $"constructorMs={diagnostics.TextureConstructionMilliseconds:F1}; " +
            $"setDataMs={diagnostics.SetDataMilliseconds:F1}; maxSetDataMs={diagnostics.MaximumSetDataMilliseconds:F1}; " +
            $"totalMs={diagnostics.TotalMilliseconds:F1}; setDataCalls={diagnostics.SetDataCallCount}; " +
            $"bytes={diagnostics.UploadedBytes}; signature={diagnostics.Signature}; family=[{families}]",
            SystemMessagePriority.NB);
    }

    private static void PublishMegastationStationMaterialDiagnostics(
        string stationIdentity,
        MegastationSystemMaterialDiagnostics diagnostics)
    {
        static string Counts(IReadOnlyDictionary<SystemMaterialFamilyId, int> counts)
            => string.Join(',', counts.OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{pair.Value}"));
        PublishStationResidencyMessage(
            $"[MegastationMaterials] station={stationIdentity}; " +
            $"palette={diagnostics.Palette.Signature}; " +
            $"dominant={diagnostics.Palette.DominantTint.PackedValue:X8}; " +
            $"secondary={diagnostics.Palette.SecondaryTint.PackedValue:X8}; " +
            $"accent={diagnostics.Palette.AccentTint.PackedValue:X8}; " +
            $"structuralRanges={diagnostics.StructuralRangeCount}; " +
            $"structuralTriangles=[{Counts(diagnostics.StructuralTriangles)}]; " +
            $"fabricRanges={diagnostics.FabricRangeCount}; " +
            $"fabricTriangles=[{Counts(diagnostics.FabricTriangles)}]; drawCount=" +
            $"{diagnostics.StructuralRangeCount + diagnostics.FabricRangeCount}",
            SystemMessagePriority.NB);
    }
}
