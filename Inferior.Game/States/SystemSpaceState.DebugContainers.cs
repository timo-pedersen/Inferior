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

    // level is accepted but not yet used — no container LOD variants exist yet.
    private void DrawTestContainers(DetailLevel level)
    {
        if (_testContainers.Count == 0 || _meshRenderer == null) return;

        float  rs   = (float)Camera3D.RenderScale;
        Matrix view = _effect.View;
        // Active pass's projection (_effect.Projection), not camera.ProjectionMatrix —
        // that's only a representative mid-tier projection now that rendering uses three
        // independent per-pass projections. Same fix as ShipMeshRenderer.Draw needed.
        Matrix proj = _effect.Projection;

        foreach (var tc in _testContainers)
        {
            DVec3 stPos = DVec3.Zero;
            foreach (var (s, sPos) in _stationPositions)
                if (ReferenceEquals(s, tc.Station)) { stPos = sPos; break; }

            DVec3   universePos = stPos + tc.Offset;
            Vector3 renderPos   = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            Matrix world = Matrix.CreateScale(rs)
                         * Matrix.CreateFromQuaternion(tc.Orientation)
                         * Matrix.CreateTranslation(renderPos);

            _meshRenderer.DrawDynamicColored(tc.Vb, tc.Ib, world, view, proj,
                SceneLighting.SunDirection, new Color(SceneLighting.SunColour));
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

    // TODO: remove SpawnTestContainers — debug helper for radar testing
    private void SpawnTestContainers()
    {
        int globalIdx = 0;
        foreach (var (station, _) in _stationPositions)
        {
            int seed  = station.Name.GetHashCode() ^ (int)(_star.GalacticPos.X * 1000.0);
            var rng   = new Inferior.Core.Random.SeededRandom(seed);
            int count = rng.NextInt(3, 7);  // 3–6 containers per station

            for (int i = 0; i < count; i++)
            {
                double angle  = rng.NextDouble() * System.Math.Tau;
                double dist   = 20.0 + rng.NextDouble() * 480.0;  // 20–500 m from station
                double elevM  = (rng.NextDouble() - 0.5) * 60.0;  // ±30 m vertical
                var    offset = new DVec3(System.Math.Cos(angle) * dist, elevM, System.Math.Sin(angle) * dist);

                // Lock grade — seeded so the same system always produces the same containers
                var grade = (Containers.LockGrade)rng.NextInt(0, 3);

                float wear        = rng.NextFloat(0f, 1f);
                int   patternSeed = rng.NextInt(int.MinValue, int.MaxValue);

                var container = Containers.ShippingContainerFactory.Generate(
                    GetContainerLockColour(grade), wear, patternSeed, lockGrade: grade);

                var vb = new VertexBuffer(_gd, VertexPositionNormalColorTexture.VertexDeclaration,
                    container.Vertices.Length, BufferUsage.WriteOnly);
                vb.SetData(container.Vertices);

                var ib = new IndexBuffer(_gd, IndexElementSize.SixteenBits,
                    container.Indices.Length, BufferUsage.WriteOnly);
                ib.SetData(container.Indices);

                // Angular velocity — seeded slow tumble
                var    tumbleRng = new Inferior.Core.Random.SeededRandom(globalIdx + 1);
                double rate      = 0.01 + tumbleRng.NextDouble() * 0.04;
                var    axis      = new DVec3(
                    tumbleRng.NextDouble() * 2.0 - 1.0,
                    tumbleRng.NextDouble() * 2.0 - 1.0,
                    tumbleRng.NextDouble() * 2.0 - 1.0).Normalized();
                DVec3 angVel = axis * rate;

                _testContainers.Add(new TestContainerEntry
                {
                    Id              = $"ctn:{globalIdx}",
                    Name            = $"Ctn-{globalIdx:D2}",
                    Station         = station,
                    Offset          = offset,
                    LockGrade       = grade,
                    AngularVelocity = angVel,
                    Container       = container,
                    Vb              = vb,
                    Ib              = ib,
                });
                globalIdx++;
            }
        }
    }

    // ── TODO: remove — debug container entry for radar testing
    private sealed class TestContainerEntry
    {
        public required string         Id              { get; init; }
        public required string         Name            { get; init; }
        public required Galaxy.Station Station         { get; init; }
        public required DVec3          Offset          { get; init; }
        public required Containers.LockGrade LockGrade { get; init; }
        public required DVec3          AngularVelocity { get; init; }
        public          Quaternion     Orientation     { get; set; } = Quaternion.Identity;
        public required Containers.ShippingContainer Container { get; init; }
        public required VertexBuffer   Vb              { get; init; }
        public required IndexBuffer    Ib              { get; init; }
    }
}
