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

    private void DrawTestContainers()
    {
        if (_testContainers.Count == 0 || _meshRenderer == null
            || _containerVb == null || _containerIb == null) return;

        float  rs   = (float)Camera3D.RenderScale;
        Matrix view = _camera.ViewMatrix;
        Matrix proj = _camera.ProjectionMatrix;

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

            _meshRenderer.DrawDynamic(
                _containerVb, _containerIb,
                world, view, proj,
                GetContainerLockColour(tc.LockGrade),
                SceneLighting.SunDirection,
                new Color(SceneLighting.SunColour));
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
                });
                globalIdx++;
            }
        }
    }

    // ── Container mesh builder ────────────────────────────────────────────────

    private static (VertexBuffer vb, IndexBuffer ib) BuildContainerMesh(GraphicsDevice gd)
    {
        // 2.5 × 2.5 × 6.0 m container centred at origin, 0.1 m chamfer on all edges/corners.
        const float hx = 1.25f, hy = 1.25f, hz = 3.0f;   // half-extents
        const float c  = 0.10f;                            // chamfer width
        float ix = hx - c, iy = hy - c, iz = hz - c;      // inner half-extents (main face corners)

        var gb = new GeometryBuilder();

        // 6 main faces
        gb.AddConvexFace(new( hx,  iy,  iz), new( hx, -iy,  iz), new( hx, -iy, -iz), new( hx,  iy, -iz)); // +X
        gb.AddConvexFace(new(-hx,  iy,  iz), new(-hx,  iy, -iz), new(-hx, -iy, -iz), new(-hx, -iy,  iz)); // -X
        gb.AddConvexFace(new( ix,  hy,  iz), new(-ix,  hy,  iz), new(-ix,  hy, -iz), new( ix,  hy, -iz)); // +Y
        gb.AddConvexFace(new( ix, -hy,  iz), new( ix, -hy, -iz), new(-ix, -hy, -iz), new(-ix, -hy,  iz)); // -Y
        gb.AddConvexFace(new( ix,  iy,  hz), new(-ix,  iy,  hz), new(-ix, -iy,  hz), new( ix, -iy,  hz)); // +Z
        gb.AddConvexFace(new( ix,  iy, -hz), new( ix, -iy, -hz), new(-ix, -iy, -hz), new(-ix,  iy, -hz)); // -Z

        // 12 edge chamfer strips (4 along each axis)
        // Z-axis edges (XY corners)
        gb.AddConvexFace(new( hx,  iy,  iz), new( hx,  iy, -iz), new( ix,  hy, -iz), new( ix,  hy,  iz)); // +X+Y
        gb.AddConvexFace(new(-ix,  hy,  iz), new(-ix,  hy, -iz), new(-hx,  iy, -iz), new(-hx,  iy,  iz)); // -X+Y
        gb.AddConvexFace(new( hx, -iy,  iz), new( ix, -hy,  iz), new( ix, -hy, -iz), new( hx, -iy, -iz)); // +X-Y
        gb.AddConvexFace(new(-hx, -iy,  iz), new(-hx, -iy, -iz), new(-ix, -hy, -iz), new(-ix, -hy,  iz)); // -X-Y
        // X-axis edges (YZ corners)
        gb.AddConvexFace(new( ix,  hy,  iz), new(-ix,  hy,  iz), new(-ix,  iy,  hz), new( ix,  iy,  hz)); // +Y+Z
        gb.AddConvexFace(new( ix, -iy,  hz), new(-ix, -iy,  hz), new(-ix, -hy,  iz), new( ix, -hy,  iz)); // -Y+Z
        gb.AddConvexFace(new( ix,  iy, -hz), new(-ix,  iy, -hz), new(-ix,  hy, -iz), new( ix,  hy, -iz)); // +Y-Z
        gb.AddConvexFace(new( ix, -hy, -iz), new(-ix, -hy, -iz), new(-ix, -iy, -hz), new( ix, -iy, -hz)); // -Y-Z
        // Y-axis edges (XZ corners)
        gb.AddConvexFace(new( hx,  iy,  iz), new( ix,  iy,  hz), new( ix, -iy,  hz), new( hx, -iy,  iz)); // +X+Z
        gb.AddConvexFace(new(-ix,  iy,  hz), new(-hx,  iy,  iz), new(-hx, -iy,  iz), new(-ix, -iy,  hz)); // -X+Z
        gb.AddConvexFace(new( hx,  iy, -iz), new( hx, -iy, -iz), new( ix, -iy, -hz), new( ix,  iy, -hz)); // +X-Z
        gb.AddConvexFace(new(-hx,  iy, -iz), new(-ix,  iy, -hz), new(-ix, -iy, -hz), new(-hx, -iy, -iz)); // -X-Z

        // 8 corner triangles
        gb.AddConvexFace(new( hx,  iy,  iz), new( ix,  hy,  iz), new( ix,  iy,  hz)); // +X+Y+Z
        gb.AddConvexFace(new(-ix,  hy,  iz), new(-hx,  iy,  iz), new(-ix,  iy,  hz)); // -X+Y+Z
        gb.AddConvexFace(new( hx, -iy,  iz), new( ix, -iy,  hz), new( ix, -hy,  iz)); // +X-Y+Z
        gb.AddConvexFace(new(-hx, -iy,  iz), new(-ix, -hy,  iz), new(-ix, -iy,  hz)); // -X-Y+Z
        gb.AddConvexFace(new( hx,  iy, -iz), new( ix,  iy, -hz), new( ix,  hy, -iz)); // +X+Y-Z
        gb.AddConvexFace(new(-ix,  hy, -iz), new(-ix,  iy, -hz), new(-hx,  iy, -iz)); // -X+Y-Z
        gb.AddConvexFace(new( hx, -iy, -iz), new( ix, -hy, -iz), new( ix, -iy, -hz)); // +X-Y-Z
        gb.AddConvexFace(new(-hx, -iy, -iz), new(-ix, -iy, -hz), new(-ix, -hy, -iz)); // -X-Y-Z

        return gb.BuildDynamic(gd);
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
    }
}
