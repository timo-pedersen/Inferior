using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
using Inferior.Game.UI;
using Inferior.Gameplay;
using Inferior.Gameplay.Components;
using Inferior.Gameplay.Components.Power;
using Inferior.Gameplay.Sensors;
using Inferior.Gameplay.Ship;
using Inferior.Rendering;
using Inferior.UI;
using Inferior.UI.Controls;
using Inferior.UI.Controls.Cockpit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Reflection.Metadata;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    // Root seed for the container-placement stream — ASCII "CONT". Independent salt so
    // adding/changing anything else in the game never reshuffles container placement,
    // per !invariants.md §6.
    private const int ContainerSeedRoot = 0x434F4E54;

    // level is accepted but not yet used — no container LOD variants exist yet.
    private void DrawContainers(DetailLevel level)
    {
        if (_containers.Count == 0 || _meshRenderer == null) return;

        float  rs   = (float)Camera3D.RenderScale;
        Matrix view = _effect.View;
        // Active pass's projection (_effect.Projection), not camera.ProjectionMatrix —
        // that's only a representative mid-tier projection now that rendering uses three
        // independent per-pass projections. Same fix as ShipMeshRenderer.Draw needed.
        Matrix proj = _effect.Projection;
        var (specStrength, specShininess) = SpecularParamsFor(_specularPreset);

        foreach (var pc in _containers)
        {
            DVec3 stPos = DVec3.Zero;
            foreach (var (s, sPos) in _stationPositions)
                if (ReferenceEquals(s, pc.Station)) { stPos = sPos; break; }

            DVec3   universePos = stPos + pc.Offset;
            Vector3 renderPos   = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            Quaternion orientation = RailsOrientation(
                pc.SpinAxis, pc.SpinRateRadPerSec, _gameTimeSeconds, pc.InitialOrientation);

            Matrix world = Matrix.CreateScale(rs)
                         * Matrix.CreateFromQuaternion(orientation)
                         * Matrix.CreateTranslation(renderPos);

            _meshRenderer.DrawDynamicLit(pc.Vb, pc.Ib, world, view, proj,
                Color.White, SceneLighting.SunDirection, new Color(SceneLighting.SunColour), SceneLighting.Ambient,
                specStrength, specShininess);
        }

        // Restore effect state expected by subsequent draw calls
        _gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }

    private static Color GetContainerLockColour(Containers.LockGrade grade) => grade switch
    {
        Containers.LockGrade.Civilian => new Color( 80, 100, 145),
        Containers.LockGrade.Military => new Color( 75,  95,  60),
        Containers.LockGrade.Vault    => new Color(160, 135,  45),
        _                             => new Color(150, 148, 142),  // None
    };

    // Places 3-6 real ShippingContainer objects around every station, for radar/targeting
    // and visual testing. The placement policy (near stations, seeded count) exists for
    // testing; the objects it places are ordinary world objects — same generation
    // conventions, same rendering pipeline, same bookkeeping as everything else.
    private void SpawnContainers()
    {
        foreach (var (station, _) in _stationPositions)
        {
            // Stable per-station stream: derived from PersistenceId (not station.Name —
            // string.GetHashCode() is process-randomized in .NET, forbidden by
            // !invariants.md §6), salted semantically so unrelated seed streams never
            // reshuffle container placement.
            var stationRng = new Inferior.Core.Random.SeededRandom(ContainerSeedRoot)
                .Derive(station.PersistenceId!)
                .Derive("containers");
            int count = stationRng.NextInt(3, 7);  // 3–7 containers per station (NextInt is inclusive both ends)

            for (int i = 0; i < count; i++)
            {
                // Each container's own stream, derived from (station, local index) — its
                // stable identity — not a global spawn-order counter, so adding a
                // container to one station never reshuffles another station's containers.
                var containerRng = stationRng.Derive(i);

                double angle  = containerRng.NextDouble() * System.Math.Tau;
                double dist   = 20.0 + containerRng.NextDouble() * 480.0;  // 20–500 m from station
                double elevM  = (containerRng.NextDouble() - 0.5) * 60.0;  // ±30 m vertical
                var    offset = new DVec3(System.Math.Cos(angle) * dist, elevM, System.Math.Sin(angle) * dist);

                var grade = (Containers.LockGrade)containerRng.NextInt(0, 3);

                float wear        = containerRng.NextFloat(0f, 1f);
                int   patternSeed = containerRng.NextInt(int.MinValue, int.MaxValue);

                var container = Containers.ShippingContainerFactory.Generate(
                    GetContainerLockColour(grade), wear, patternSeed, lockGrade: grade);

                var vb = new VertexBuffer(_gd, VertexPositionNormalColorTexture.VertexDeclaration,
                    container.Vertices.Length, BufferUsage.WriteOnly);
                vb.SetData(container.Vertices);

                var ib = new IndexBuffer(_gd, IndexElementSize.SixteenBits,
                    container.Indices.Length, BufferUsage.WriteOnly);
                ib.SetData(container.Indices);

                // Seeded slow tumble — a sub-stream of this container's own identity-derived
                // stream (see containerRng above), not a global spawn index.
                var    tumbleRng = containerRng.Derive("tumble");
                double rate      = 0.01 + tumbleRng.NextDouble() * 0.04;
                var    axisD     = new DVec3(
                    tumbleRng.NextDouble() * 2.0 - 1.0,
                    tumbleRng.NextDouble() * 2.0 - 1.0,
                    tumbleRng.NextDouble() * 2.0 - 1.0).Normalized();
                var axis = new Vector3((float)axisD.X, (float)axisD.Y, (float)axisD.Z);

                _containers.Add(new PlacedContainer
                {
                    Id                = $"{station.PersistenceId}:container:{i}",
                    Name              = $"{station.Name} Ctn-{i + 1:D2}",
                    Station           = station,
                    Offset            = offset,
                    LockGrade         = grade,
                    SpinAxis          = axis,
                    SpinRateRadPerSec = (float)rate,
                    Container         = container,
                    Vb                = vb,
                    Ib                = ib,
                });
            }
        }
    }

    // A shipping container placed in the world — position is station-relative (fixed
    // offset), orientation is on rails (RailsOrientation, a pure function of sim time —
    // see SystemSpaceState.Helpers.cs). No mutable per-frame kinematic state.
    private sealed class PlacedContainer
    {
        public required string         Id                { get; init; }
        public required string         Name              { get; init; }
        public required Galaxy.Station Station           { get; init; }
        public required DVec3          Offset            { get; init; }
        public required Containers.LockGrade LockGrade   { get; init; }
        public required Vector3        SpinAxis          { get; init; }
        public required float          SpinRateRadPerSec { get; init; }
        public          Quaternion     InitialOrientation { get; init; } = Quaternion.Identity;
        public required Containers.ShippingContainer Container { get; init; }
        public required VertexBuffer   Vb                { get; init; }
        public required IndexBuffer    Ib                { get; init; }
    }
}
