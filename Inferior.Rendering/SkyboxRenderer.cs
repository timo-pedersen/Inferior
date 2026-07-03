using Inferior.Core.Math;
using Inferior.Galaxy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Draws the galaxy starfield background — a point cloud plus additive glow quads for
/// bright/near stars, projected onto a sphere around the current system. Rebuilt via
/// Load() whenever the current star changes (initial OnEnter, mid-session EnterSystem).
/// Caller decides whether to call Draw() at all (e.g. suppressed during flat hyperspace,
/// which Inferior.Rendering has no business knowing about).
/// </summary>
public sealed class SkyboxRenderer
{
    private readonly GraphicsDevice _gd;
    private readonly BasicEffect    _effect;

    private VertexPositionColor[] _points    = [];
    private VertexPositionColor[] _glowVerts = [];

    private const float SkyboxRadius      = 20_000f;
    private const float SkyboxGlowSize    = 12f;    // RU per unit MapDotSize
    private const float SkyboxGlowCutoff  = 0.15f;  // brightness threshold for glow quads
    private const double SkyboxTargetRadiusLY = 1000.0;  // maximum targetable distance

    public SkyboxRenderer(GraphicsDevice gd, BasicEffect effect)
    {
        _gd     = gd;
        _effect = effect;
    }

    // Desaturated sky tint — barely-coloured near-white so stars don't look like blobs.
    // These intentionally diverge from GlowColor (which is for up-close system views).
    private static Color SkyboxStarColor(SpectralClass sc, float brightness)
    {
        Vector3 tint = sc switch
        {
            SpectralClass.O           => new Vector3(0.88f, 0.92f, 1.00f),
            SpectralClass.B           => new Vector3(0.92f, 0.95f, 1.00f),
            SpectralClass.A           => new Vector3(0.97f, 0.98f, 1.00f),
            SpectralClass.F           => new Vector3(1.00f, 0.99f, 0.95f),
            SpectralClass.G           => new Vector3(1.00f, 0.97f, 0.91f),
            SpectralClass.K           => new Vector3(1.00f, 0.93f, 0.85f),
            SpectralClass.M           => new Vector3(1.00f, 0.89f, 0.80f),
            SpectralClass.WhiteDwarf  => new Vector3(0.94f, 0.96f, 1.00f),
            SpectralClass.NeutronStar => new Vector3(0.88f, 0.94f, 1.00f),
            SpectralClass.BlackHole   => new Vector3(0.08f, 0.08f, 0.10f),
            _                         => Vector3.One,
        };
        return new Color(tint * brightness);
    }

    public static (VertexPositionColor[] points, VertexPositionColor[] glowVerts,
                    (Vector3 pos, Star star)[] targetable)
        Build(Star currentStar, Star[] galaxy)
    {
        var points     = new List<VertexPositionColor>(galaxy.Length);
        var glows      = new List<VertexPositionColor>();
        var targetable = new List<(Vector3, Star)>();

        // Half the galaxy radius in ly — used for distance falloff so stars dim gradually
        // across galaxy-scale distances rather than popping.
        const double falloffScale = GalaxyGenerator.GalaxyRadiusLY * 0.4;

        foreach (var star in galaxy)
        {
            if (star.GalaxyIndex == currentStar.GalaxyIndex) continue;

            DVec3  offset = star.GalacticPos - currentStar.GalacticPos;
            double dist   = offset.Length;
            if (dist < 0.001) continue;

            var dir    = Vector3.Normalize(new Vector3(
                (float)(offset.X / dist), (float)(offset.Y / dist), (float)(offset.Z / dist)));
            var center = dir * SkyboxRadius;

            float brightness = (star.MapDotSize / 3.5f)
                             * (float)System.Math.Exp(-dist / falloffScale);
            brightness = System.Math.Clamp(brightness, 0.03f, 1.0f);

            points.Add(new VertexPositionColor(center, SkyboxStarColor(star.SpectralClass, brightness)));

            if (dist <= SkyboxTargetRadiusLY)
                targetable.Add((center, star));

            if (brightness >= SkyboxGlowCutoff)
            {
                float   size    = star.MapDotSize * SkyboxGlowSize;
                float   alpha   = brightness * 0.30f;
                Color   glowCol = SkyboxStarColor(star.SpectralClass, alpha);

                Vector3 worldUp = MathF.Abs(dir.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
                Vector3 tan     = Vector3.Normalize(Vector3.Cross(dir, worldUp));
                Vector3 bitan   = Vector3.Cross(dir, tan);

                glows.Add(new VertexPositionColor(center + (-tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + (-tan - bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan + bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + ( tan - bitan) * size, glowCol));
                glows.Add(new VertexPositionColor(center + (-tan - bitan) * size, glowCol));
            }
        }

        return ([.. points], [.. glows], [.. targetable]);
    }

    public void Load(VertexPositionColor[] points, VertexPositionColor[] glowVerts)
    {
        _points    = points;
        _glowVerts = glowVerts;
    }

    public void Draw()
    {
        if (_points.Length == 0 && _glowVerts.Length == 0) return;

        _gd.RasterizerState = RasterizerState.CullNone;
        _gd.BlendState      = BlendState.AlphaBlend;

        _effect.World              = Matrix.Identity;
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.TextureEnabled     = false;
        _effect.DiffuseColor       = Vector3.One;
        _effect.Alpha              = 1f;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            if (_glowVerts.Length >= 3)
                _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _glowVerts, 0, _glowVerts.Length / 3);

            if (_points.Length > 0)
                _gd.DrawUserPrimitives(PrimitiveType.PointList, _points, 0, _points.Length);
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
        _gd.RasterizerState        = RasterizerState.CullCounterClockwise;
        _gd.BlendState             = BlendState.Opaque;
    }
}
