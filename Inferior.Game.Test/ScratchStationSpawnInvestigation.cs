using Inferior.Core.Math;
using Inferior.Galaxy;
using Xunit;
using Xunit.Abstractions;

namespace Inferior.Game.Test;

// SCRATCH — investigating the double-click-to-station spawn bug. Replicates the exact
// position/orientation math from SystemSpaceState.OnEnter's TargetStation branch, without
// needing a GraphicsDevice or the live game window (this layer is pure data — no rendering).
// Delete once the investigation concludes.
public class ScratchStationSpawnInvestigation(ITestOutputHelper output)
{
    [Fact]
    public void ReplicateTargetStationSpawnMath()
    {
        var stars = GalaxyGenerator.Generate();
        int checkedSystems = 0, checkedStations = 0;

        foreach (var star in stars)
        {
            var system = StarSystem.Generate(star, GalaxyGenerator.SystemSeed(star));
            if (system.Stations.Count == 0) continue;
            checkedSystems++;

            DVec3 EclipticToGalaxy(DVec3 pos) => CoordinateTransforms.EclipticToGalaxy(
                pos, system.EclipticTiltAzimuthRadians, system.EclipticTiltRadians);

            const double gameTime = 12345.678;

            foreach (var station in system.Stations)
            {
                checkedStations++;

                // Copied verbatim from SystemSpaceState.cs's TargetStation branch.
                DVec3 parentEcliptic = DVec3.Zero;
                if (station.OrbitParent != null)
                {
                    DVec3 grandparent = DVec3.Zero;
                    foreach (var planet in system.Planets)
                        if (planet.Children.Any(c => c.Name == station.OrbitParent.Name))
                            grandparent = planet.GetPosition(gameTime, DVec3.Zero);
                    parentEcliptic = station.OrbitParent.GetPosition(gameTime, grandparent);
                }
                DVec3 stationEcliptic = station.GetPosition(gameTime, parentEcliptic);
                DVec3 stationGalaxy   = EclipticToGalaxy(stationEcliptic);

                DVec3 eclipticUp = EclipticToGalaxy(DVec3.UnitY) - EclipticToGalaxy(DVec3.Zero);
                DVec3 spawnPos   = stationGalaxy + eclipticUp * 2000.0;

                double upLen   = eclipticUp.Length;
                double distSep = (spawnPos - stationGalaxy).Length;

                string parentKind = station.OrbitParent == null ? "star"
                    : system.Planets.Any(p => p.Children.Contains(station.OrbitParent)) ? "moon"
                    : "planet";

                if (checkedStations <= 15 || System.Math.Abs(distSep - 2000.0) > 0.5 || System.Math.Abs(upLen - 1.0) > 1e-6)
                {
                    output.WriteLine(
                        $"star={star.Name} station={station.Name} parentKind={parentKind} " +
                        $"tilt={system.EclipticTiltRadians:F4}rad az={system.EclipticTiltAzimuthRadians:F4}rad " +
                        $"|eclipticUp|={upLen:F6} dist(spawn,station)={distSep:F3}m " +
                        $"stationGalaxy={stationGalaxy} spawnPos={spawnPos}");
                }

                Assert.True(System.Math.Abs(upLen - 1.0) < 1e-6, $"eclipticUp not unit length: {upLen} for {star.Name}/{station.Name}");
                Assert.True(System.Math.Abs(distSep - 2000.0) < 0.5, $"spawn distance != 2000m: {distSep} for {star.Name}/{station.Name}");
            }

            if (checkedSystems >= 25) break;
        }

        output.WriteLine($"Checked {checkedSystems} systems, {checkedStations} stations total.");
        Assert.True(checkedStations > 0, "No stations found to check — widen the star search.");
    }
}
