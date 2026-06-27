using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Rendering;

/// <summary>
/// Builds the Type-1 player ship mesh as three separate GPU buffer pairs so each
/// component can be drawn with a distinct diffuse colour under dynamic lighting.
///
/// Vertex coordinates assume Y=up, Z=forward (nose), X=right — ship-local space
/// with the centre of mass at the origin.  The world matrix must include a
/// CreateRotationY(PI) to align this +Z-forward model with the engine's -Z-forward
/// ship orientation before applying the ship quaternion.
/// </summary>
public static class Type1HullFactory
{
    public static readonly Color HullColour    = new Color(100, 115, 120);
    public static readonly Color NacelleColour = new Color( 85,  98, 108);
    public static readonly Color PylonColour   = new Color( 90, 105, 112);

    public static (
        (VertexBuffer vb, IndexBuffer ib) hull,
        (VertexBuffer vb, IndexBuffer ib) nacelles,
        (VertexBuffer vb, IndexBuffer ib) pylons
    ) BuildAll(GraphicsDevice gd)
        => (BuildHull(gd), BuildNacelles(gd), BuildPylons(gd));

    // ── Hull ──────────────────────────────────────────────────────────────────

    private static (VertexBuffer vb, IndexBuffer ib) BuildHull(GraphicsDevice gd)
    {
        // ── NOSE ──────────────────────────────────────────────────────────────
        var vNose    = new Vector3(  0f,   0.5f,  16f);

        // ── FRONT RING at Z=12 ────────────────────────────────────────────────
        var vFcTopL  = new Vector3( -3.5f,  2.5f,  12f);
        var vFcTopR  = new Vector3(  3.5f,  2.5f,  12f);
        var vFcBotL  = new Vector3( -4f,   -0.5f,  12f);
        var vFcBotR  = new Vector3(  4f,   -0.5f,  12f);

        // ── CANOPY PEAK at Z=8 ────────────────────────────────────────────────
        var vCpyL    = new Vector3( -2.5f,  4f,     8f);
        var vCpyR    = new Vector3(  2.5f,  4f,     8f);

        // ── WING LEADING EDGE at Z=4 ──────────────────────────────────────────
        var vWLeTopL = new Vector3( -5f,    1.5f,   4f);
        var vWLeTopR = new Vector3(  5f,    1.5f,   4f);
        var vWLeBotL = new Vector3( -5f,   -1f,     4f);
        var vWLeBotR = new Vector3(  5f,   -1f,     4f);
        var vWTipL   = new Vector3(-11f,    0f,     4f);
        var vWTipR   = new Vector3( 11f,    0f,     4f);

        // ── WING TRAILING EDGE at Z=−1 ────────────────────────────────────────
        var vWTrTopL = new Vector3( -5f,    1.5f,  -1f);
        var vWTrTopR = new Vector3(  5f,    1.5f,  -1f);
        var vWTrBotL = new Vector3( -5f,   -1f,    -1f);
        var vWTrBotR = new Vector3(  5f,   -1f,    -1f);
        var vWTrTipL = new Vector3(-10f,    0f,    -1f);
        var vWTrTipR = new Vector3( 10f,    0f,    -1f);

        // ── REAR BODY at Z=−12 ────────────────────────────────────────────────
        var vRrTopL  = new Vector3( -4.5f,  1.5f, -12f);
        var vRrTopR  = new Vector3(  4.5f,  1.5f, -12f);
        var vRrBotL  = new Vector3( -4.5f, -1.5f, -12f);
        var vRrBotR  = new Vector3(  4.5f, -1.5f, -12f);

        // ── TAIL at Z=−16 ─────────────────────────────────────────────────────
        var vTailL   = new Vector3( -1.5f,  0f,   -16f);
        var vTailR   = new Vector3(  1.5f,  0f,   -16f);

        var gb = new GeometryBuilder();

        // ── NOSE (4 triangles) ────────────────────────────────────────────────
        gb.AddConvexFace(vNose,   vFcTopL,  vFcTopR);
        gb.AddConvexFace(vNose,   vFcTopL,  vFcBotL);
        gb.AddConvexFace(vNose,   vFcBotR,  vFcTopR);
        gb.AddConvexFace(vNose,   vFcBotR,  vFcBotL);

        // ── CANOPY (4 panels) ─────────────────────────────────────────────────
        gb.AddConvexFace(vFcTopL, vCpyL,    vCpyR,    vFcTopR);
        gb.AddConvexFace(vCpyL,   vWLeTopL, vWLeTopR, vCpyR);
        gb.AddConvexFace(vFcTopL, vWLeTopL, vCpyL);
        gb.AddConvexFace(vFcTopR, vCpyR,    vWLeTopR);

        // ── TOP SURFACE (3 quads) ─────────────────────────────────────────────
        gb.AddConvexFace(vWLeTopL, vWTrTopL, vWTrTopR, vWLeTopR);
        gb.AddConvexFace(vWTrTopL, vRrTopL,  vRrTopR,  vWTrTopR);
        gb.AddConvexFace(vRrTopL,  vTailL,   vTailR,   vRrTopR);

        // ── BOTTOM SURFACE (4 quads) ──────────────────────────────────────────
        gb.AddConvexFace(vFcBotL,  vFcBotR,  vWLeBotR, vWLeBotL);
        gb.AddConvexFace(vWLeBotL, vWLeBotR, vWTrBotR, vWTrBotL);
        gb.AddConvexFace(vWTrBotL, vWTrBotR, vRrBotR,  vRrBotL);
        gb.AddConvexFace(vRrBotL,  vRrBotR,  vTailR,   vTailL);

        // ── LEFT SIDE (3 quads + 1 tri) ───────────────────────────────────────
        gb.AddConvexFace(vFcBotL,  vWLeBotL, vWLeTopL, vFcTopL);
        gb.AddConvexFace(vWLeBotL, vWTrBotL, vWTrTopL, vWLeTopL);
        gb.AddConvexFace(vWTrBotL, vRrBotL,  vRrTopL,  vWTrTopL);
        gb.AddConvexFace(vRrBotL,  vTailL,   vRrTopL);

        // ── RIGHT SIDE (3 quads + 1 tri) ──────────────────────────────────────
        gb.AddConvexFace(vFcBotR,  vFcTopR,  vWLeTopR, vWLeBotR);
        gb.AddConvexFace(vWLeBotR, vWLeTopR, vWTrTopR, vWTrBotR);
        gb.AddConvexFace(vWTrBotR, vWTrTopR, vRrTopR,  vRrBotR);
        gb.AddConvexFace(vRrBotR,  vRrTopR,  vTailR);

        // ── WINGS — LEFT ──────────────────────────────────────────────────────
        gb.AddConvexFace(vWTipL,   vWLeTopL, vWLeBotL);
        gb.AddConvexFace(vWLeTopL, vWTipL,   vWTrTipL, vWTrTopL);
        gb.AddConvexFace(vWLeBotL, vWTrBotL, vWTrTipL, vWTipL);
        gb.AddConvexFace(vWTrTipL, vWTrBotL, vWTrTopL);

        // ── WINGS — RIGHT ─────────────────────────────────────────────────────
        gb.AddConvexFace(vWTipR,   vWLeBotR, vWLeTopR);
        gb.AddConvexFace(vWLeTopR, vWTrTopR, vWTrTipR, vWTipR);
        gb.AddConvexFace(vWLeBotR, vWTipR,   vWTrTipR, vWTrBotR);
        gb.AddConvexFace(vWTrTipR, vWTrTopR, vWTrBotR);

        return gb.BuildDynamic(gd, HullColour);
    }

    // ── Nacelles ──────────────────────────────────────────────────────────────

    private static (VertexBuffer vb, IndexBuffer ib) BuildNacelles(GraphicsDevice gd)
    {
        var gb = new GeometryBuilder();
        AddNacelle(gb, new Vector3(-11f, -1.5f, -11f), 7f, 0.75f);
        AddNacelle(gb, new Vector3( 11f, -1.5f, -11f), 7f, 0.75f);
        return gb.BuildDynamic(gd, NacelleColour);
    }

    private static void AddNacelle(GeometryBuilder gb, Vector3 centre, float length, float radius)
    {
        // Hex cross-section: 6 vertices at 30°, 90°, 150°, 210°, 270°, 330°
        // Rotated 30° from default so the top face is flat.
        const int sides   = 6;
        float     halfLen = length / 2f;

        var fwd  = new Vector3[sides];  // intake ring (+Z)
        var rear = new Vector3[sides];  // exhaust ring (−Z)

        for (int i = 0; i < sides; i++)
        {
            float angle = (i * 60f + 30f) * MathF.PI / 180f;
            float x = radius * MathF.Cos(angle);
            float y = radius * MathF.Sin(angle);
            fwd[i]  = centre + new Vector3(x, y, +halfLen);
            rear[i] = centre + new Vector3(x, y, -halfLen);
        }

        // 6 rectangular side faces — nacelle is offset from CoM so explicit normals required
        for (int i = 0; i < sides; i++)
        {
            int   next      = (i + 1) % sides;
            float midAngle  = (i * 60f + 60f) * MathF.PI / 180f;  // bisector angle
            var   faceNormal = new Vector3(MathF.Cos(midAngle), MathF.Sin(midAngle), 0f);
            gb.AddFace(fwd[i], fwd[next], rear[next], rear[i], faceNormal);
        }

        // Intake cap (+Z, faces toward nose)
        var intakeCentre = centre + new Vector3(0, 0, +halfLen);
        var intakeNormal = new Vector3(0, 0, 1);
        for (int i = 0; i < sides; i++)
            gb.AddFace(intakeCentre, fwd[i], fwd[(i + 1) % sides], intakeNormal);

        // Exhaust cap (−Z, faces toward tail)
        var exhaustCentre = centre + new Vector3(0, 0, -halfLen);
        var exhaustNormal = new Vector3(0, 0, -1);
        for (int i = 0; i < sides; i++)
            gb.AddFace(exhaustCentre, rear[(i + 1) % sides], rear[i], exhaustNormal);
    }

    // ── Pylons ────────────────────────────────────────────────────────────────

    private static (VertexBuffer vb, IndexBuffer ib) BuildPylons(GraphicsDevice gd)
    {
        var gb = new GeometryBuilder();
        AddPylon(gb,
            hullFwd:     new Vector3( -5f,    -1f,    -7f),
            hullRear:    new Vector3( -5f,    -1f,   -13f),
            nacelleFwd:  new Vector3(-10.25f, -0.75f, -7f),
            nacelleRear: new Vector3(-10.25f, -0.75f,-13f));
        AddPylon(gb,
            hullFwd:     new Vector3(  5f,    -1f,    -7f),
            hullRear:    new Vector3(  5f,    -1f,   -13f),
            nacelleFwd:  new Vector3( 10.25f, -0.75f, -7f),
            nacelleRear: new Vector3( 10.25f, -0.75f,-13f));
        return gb.BuildDynamic(gd, PylonColour);
    }

    private static void AddPylon(GeometryBuilder gb,
        Vector3 hullFwd, Vector3 hullRear, Vector3 nacelleFwd, Vector3 nacelleRear)
    {
        // Give each pylon ~0.5 m thickness below the connection points
        var down = new Vector3(0, -0.5f, 0);
        var hf_t = hullFwd;     var hf_b = hullFwd    + down;
        var hr_t = hullRear;    var hr_b = hullRear   + down;
        var nf_t = nacelleFwd;  var nf_b = nacelleFwd  + down;
        var nr_t = nacelleRear; var nr_b = nacelleRear + down;

        gb.AddFace(hf_t, nf_t, nr_t, hr_t, Vector3.Up);
        gb.AddFace(hf_b, hr_b, nr_b, nf_b, Vector3.Down);
        gb.AddFace(hf_t, hf_b, nf_b, nf_t, new Vector3(0, 0,  1f));
        gb.AddFace(hr_t, nr_t, nr_b, hr_b, new Vector3(0, 0, -1f));
    }
}
