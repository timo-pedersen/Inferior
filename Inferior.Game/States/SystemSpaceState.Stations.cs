using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
using Inferior.Game.StationGen.Megastations;
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
    private static bool ShouldUseMegastationPrototype(
        Galaxy.Station station,
        Galaxy.Station? starterStation,
        MegastationDevelopmentSelection selection)
    {
        if (selection.ForceStarterStation && starterStation != null && ReferenceEquals(station, starterStation))
            return true;

        return selection.Mode switch
        {
            MegastationPrototypeSelectionMode.Frequent =>
                StableProbability(station.PersistenceId ?? station.Name) < selection.MegastationProbability,
            MegastationPrototypeSelectionMode.ForceStarterStation =>
                starterStation != null && ReferenceEquals(station, starterStation),
            _ => false,
        };
    }

    private static double StableProbability(string value)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in value)
            {
                h ^= c;
                h *= 16777619u;
            }
            return (h % 10_000u) / 10_000.0;
        }
    }

    private static void PublishMegastationPrototypeDiagnostics(
        MegastationPrototypeDiagnostics d,
        MegastationPrototypeSelectionMode mode)
    {
        DataBus.System.Publish(Topics.System.All, new SystemMessage(
            $"Megastation Prototype C2 [{mode}] id={d.StationPersistenceId}; build={d.BuildIdentifier}; v={d.GeneratorVersion}; seedCompat={d.SeedCompatibilityVersion}; topoReg={d.TopologyRegularisationAlgorithmVersion}; boundary={d.BoundaryTopologyAlgorithmVersion}; chamfer={d.StructuralChamferAlgorithmVersion}; path={d.MeshPath}; " +
            $"seed={d.RootSeed}; slices={d.XSliceCount}x{d.YSliceCount}x{d.ZSliceCount}; " +
            $"cells={d.GridCellCount}; structural={d.StructuralOccupiedCellCount}; rawUrban={d.UrbanOccupiedCellCount}; regularised={d.RegularisedOccupiedCellCount}; repairs+={d.TopologyRepairAddedCellCount}; " +
            $"faces={d.UrbanizedFaceCount}; faceCells={d.FaceRegionOccupiedCellCount}; edgeCells={d.EdgeRegionOccupiedCellCount}; cornerCells={d.CornerRegionOccupiedCellCount}; " +
            $"districts={d.DistrictCount}; maxDepth={d.MaximumUrbanDepth}; rawComponents={d.ConnectedComponentsBeforeValidation}; regComponents={d.RegularisedConnectedComponents}; " +
            $"edgeCritical={d.EdgeCriticalConfigurationsBeforeRegularisation}->{d.EdgeCriticalConfigurationsAfterRegularisation}; vertexCritical={d.VertexCriticalConfigurationsBeforeRegularisation}->{d.VertexCriticalConfigurationsAfterRegularisation}; " +
            $"sealed={d.HasSealedCavity}->{d.RegularisedHasSealedCavity}; boundaryFaces={d.BoundaryFaceCount}; edges flat/convex/concave/invalid={d.FlatContinuationEdgeCount}/{d.ConvexExteriorEdgeCount}/{d.ConcaveExteriorEdgeCount}/{d.InvalidDiagonalEdgeCount}; " +
            $"vertices simple/straight/concave/complex/nonmanifold={d.SimpleConvexVertexCount}/{d.StraightConvexContinuationVertexCount}/{d.SimpleConcaveVertexCount}/{d.ComplexVertexCount}/{d.NonManifoldVertexCount}; " +
            $"eligible={d.EligibleChamferSegmentCount}; suppressed={d.SuppressedConvexSegmentCount}; runs={d.ChamferRunCount}/{d.SuppressedChamferRunCount}; renderedBevels={d.BevelQuadCount}; renderedCaps={d.CornerCapCount}; chamferSemantic taperOnly/nearZero/missingRetract={d.ChamferSemanticValidation.TaperOnlyRenderedRunCount}/{d.ChamferSemanticValidation.NearZeroAreaRenderedRunCount}/{d.ChamferSemanticValidation.MissingFaceRetractionRunCount}; quads={d.ExposedQuadCount}; " +
            $"tris={d.TriangleCount}; verts={d.VertexCount}; pages={d.MeshPageCount}; topoMs={d.BoundaryTopologyBuildMilliseconds}; meshMs={d.BoundaryMeshBuildMilliseconds}; genMs={d.GenerationMilliseconds}",
            SystemMessagePriority.NB));
    }

    private static void PublishMegastationSemanticZoningDiagnostics(
        string stationIdentity,
        MegastationSemanticZoningDiagnostics diagnostics)
    {
        string roles = string.Join("; ", Enum.GetValues<MegastationZoneRole>().Select(role =>
        {
            float percentage = diagnostics.TotalSurfaceArea <= 0f
                ? 0f
                : diagnostics.AreaByRole.GetValueOrDefault(role) / diagnostics.TotalSurfaceArea * 100f;
            return $"{role}={percentage:F1}%";
        }));
        DataBus.System.Publish(Topics.System.All, new SystemMessage(
            $"[MegastationZoning] station={stationIdentity}; zones={diagnostics.ZoneCount}; " +
            $"surfaceFaces={diagnostics.SurfaceFaceCount}; surfaceArea={diagnostics.TotalSurfaceArea:F1}m2; " +
            $"zoningMs={diagnostics.ZoningMilliseconds}; {roles}; " +
            $"anchors core/faceDistricts/edges/corners={diagnostics.CoreAnchorCount}/" +
            $"{diagnostics.FaceDistrictAnchorCount}/{diagnostics.EdgeAnchorCount}/{diagnostics.CornerAnchorCount}; " +
            $"repairFragments={diagnostics.RepairFragmentCount}; fragmentsMerged={diagnostics.FragmentsMerged}",
            SystemMessagePriority.NB));
    }

    private static void PublishMegastationWindowDiagnostics(
        string stationIdentity,
        MegastationWindowDiagnostics diagnostics)
    {
        string message = $"[MegastationWindows] station={stationIdentity}; " +
            $"habitationZones={diagnostics.HabitationZoneCount}; " +
            $"eligibleRegions={diagnostics.EligibleRegionCount}; activeRegions={diagnostics.ActiveRegionCount}; " +
            $"darkRegions={diagnostics.DarkRegionCount}; blocks={diagnostics.BlockCount}; " +
            $"windows={diagnostics.WindowCount}; lit={diagnostics.LitWindowCount}; " +
            $"dim={diagnostics.DimWindowCount}; dark={diagnostics.DarkWindowCount}; " +
            $"absentCandidates={diagnostics.AbsentCandidateCount}; " +
            $"eligibleArea={diagnostics.EligibleHabitationWallArea:F1}m2; " +
            $"activeArea={diagnostics.ActiveWindowArea:F1}m2; " +
            $"meshVertices={diagnostics.MeshVertexCount}; meshTriangles={diagnostics.MeshTriangleCount}; " +
            $"meshBytes={diagnostics.MeshBytes}; planningMs={diagnostics.PlanningMilliseconds}; " +
            $"meshBuildMs={diagnostics.MeshBuildMilliseconds}";
        PublishStationResidencyMessage(message, SystemMessagePriority.NB);
    }

    // ── 3D drawing ────────────────────────────────────────────────────────────

    // ── Station drawing ───────────────────────────────────────────────────────

    // Brief S2c-2: derivative bump strength, locked after Timo's in-engine A/B — Whisper
    // (0.3) confirmed as the right amount ("looking swell now... the others are too
    // strong"); Off/Subtle/Default/Strong presets and the J-key runtime cycle are removed
    // now that the value is settled, not deferred as future tuning.
    private const float StationBumpStrength = 0.3f;

    private static float StationPhysicalRadius(Galaxy.Station s) => s.Size switch
    {
        Galaxy.StationSize.Small  =>  250f,
        Galaxy.StationSize.Medium =>  800f,
        Galaxy.StationSize.Large  => 2500f,
        _                         =>  250f,
    };

    private IEnumerable<(Galaxy.Station station, DVec3 universePosition)> ResidentStationEntries()
    {
        if (TryGetResidentStation(out _, out Galaxy.Station station, out DVec3 position))
            yield return (station, position);
    }

    private bool ResidentVisualIntersectsDepthTier(DetailLevel level)
    {
        if (!TryGetResidentStation(
            out StationVisualPackage visual,
            out _,
            out DVec3 stationPosition))
            return false;

        double centre = (stationPosition - _camera.UniversePosition).Length;
        double nearest = Math.Max(centre - visual.RenderBoundsRadiusMeters, 0.0);
        double farthest = centre + visual.RenderBoundsRadiusMeters;
        return level switch
        {
            DetailLevel.Full => nearest <= NearTierFar && farthest >= NearTierNear,
            DetailLevel.Medium => nearest <= MidTierFar && farthest >= MidTierNear,
            _ => farthest >= MidTierFar,
        };
    }

    private void DrawStations(DetailLevel level)
    {
        if (_stationPositions.Count == 0 || _meshRenderer == null) return;
        if (!ResidentVisualIntersectsDepthTier(level)) return;

        float rs = (float)Camera3D.RenderScale;
        Matrix view = _effect.View;
        Matrix proj = _effect.Projection;
        var    sunCol = new Color(SceneLighting.SunColour);
        var (specStrength, specShininess) = SpecularParamsFor(_specularPreset);

        // Hull pass — real-time LitSurface.fx DynamicLit (ambient + saturate(N.L)) with
        // procedural texture; MaterialColor left White (matches the old
        // BasicEffect.DiffuseColor = Vector3.One) so all tint comes from the texture.
        // Brief S1: station hulls share DynamicLit with ships/containers/the calibration
        // cube, so they pick up the same specular term (station decoration below stays
        // BakedColorLit* and untouched until S2 — a deliberate scope call, not a gap).
        // Brief U1: this loop needs no module-kind branch at all — _hullMeshes holds an
        // entry for every module, box or MeshFactory alike (see the OnEnter rebuild in
        // SystemSpaceState.cs), so a docking-bay's hull draws through the exact same path
        // as a hab-block's, material map (gloss/bump) included.
        foreach (var (station, universePos) in ResidentStationEntries())
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;
            IReadOnlyList<PlacedModule> modules = ResidentStationVisual!.Modules;

            bool useShadow = _stationShadowContext != null
                && ReferenceEquals(_stationShadowContext.Station, station)
                && _stationShadowMap != null;

            Matrix stationRot;
            if (useShadow)
                stationRot = _stationShadowContext!.StationRotation;
            else
            {
                var sysQ   = station.GetOrientation(_gameTimeSeconds);
                var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
                stationRot = Matrix.CreateFromQuaternion(stRotQ);
            }

            foreach (var mod in modules)
            {
                if (!_hullMeshes.TryGetValue(mod, out var hull)) continue;
                if (mod.TextureInstance == null) continue;

                // mod.Transform used directly, not decomposed-then-rebuilt: the shadow
                // caster (RenderStationShadowMap) and the receiver's ModuleToStationLocal
                // parameter both use mod.Transform raw, so this world matrix must be built
                // from that exact same matrix too, not a numerically-reconstructed
                // approximation of it — otherwise the on-screen vertex position and the
                // position the shadow system evaluates for that vertex quietly disagree by
                // a mm-scale amount (compounded by Decompose's own precision), which eats
                // into the shadow-correction budget independently of anything the
                // receiver-plane correction can fix. See SystemSpaceState.Shadows.cs.
                Matrix world = mod.Transform * Matrix.CreateScale(rs) * stationRot
                             * Matrix.CreateTranslation(renderPos);

                if (_megastationZoningDebug
                    && ResidentStationVisual.MegastationSemanticZoning is { } zoning
                    && mod.Definition.Category == "megastation-prototype")
                {
                    IReadOnlyDictionary<MegastationZoneRole, IndexBuffer> debugBuffers =
                        ResidentStationVisual.EnsureSemanticDebugIndexBuffers(_gd);
                    foreach (MegastationSemanticIndexGroup group in zoning.DebugIndexGroups)
                    {
                        if (!debugBuffers.TryGetValue(group.Role, out IndexBuffer? debugIndices))
                            continue;
                        _meshRenderer.DrawDebugFlatColorRange(
                            hull.vb,
                            debugIndices,
                            0,
                            debugIndices.IndexCount,
                            world,
                            view,
                            proj,
                            MegastationZoneDebugColor(group.Role));
                    }
                    int semanticIndexCount = zoning.Diagnostics.SurfaceFaceCount * 6;
                    if (semanticIndexCount < hull.ib.IndexCount)
                    {
                        _meshRenderer.DrawDebugFlatColorRange(
                            hull.vb,
                            hull.ib,
                            semanticIndexCount,
                            hull.ib.IndexCount - semanticIndexCount,
                            world,
                            view,
                            proj,
                            MegastationZoneDebugColor(MegastationZoneRole.Structural));
                    }
                    continue;
                }

                if (useShadow)
                {
                    var ctx = _stationShadowContext!;
                    // Normalized depth units — StationShadowBiasMetres is expressed in
                    // metres; LitSurface.fx's ShadowBiasDepth compares in the same
                    // normalized space as receiverDepth/storedDepth.
                    float shadowBiasDepth = StationShadowBiasMetres / ctx.DepthSpan;
                    _meshRenderer.DrawDynamicLitShadowed(hull.vb, hull.ib, world, view, proj,
                        Color.White, SceneLighting.SunDirection, sunCol, SceneLighting.Ambient,
                        specStrength, specShininess,
                        mod.TextureInstance, _stationShadowMap!, mod.Transform,
                        ctx.StationLocalToLightView, ctx.MinXY, ctx.InvSize, ctx.Near,
                        ctx.DepthSpan,
                        new Vector2(1f / _stationShadowMapResolution, 1f / _stationShadowMapResolution),
                        StationShadowCorrectionLimit, shadowBiasDepth,
                        _stationShadowBinaryView, _stationShadowDeltaView,
                        ShadowKernelRadiusFor(_shadowKernelMode),
                        mod.MaterialInstance, StationBumpStrength);
                }
                else
                {
                    _meshRenderer.DrawDynamicLit(hull.vb, hull.ib, world, view, proj,
                        Color.White, SceneLighting.SunDirection, sunCol, SceneLighting.Ambient,
                        specStrength, specShininess,
                        mod.TextureInstance, mod.MaterialInstance, StationBumpStrength);
                }
            }
        }

        // Decoration pass — vertex colour is albedo x AO (+ self-illumination floor S in
        // alpha); the sun term is computed here every frame from the real world normal
        // (LitSurface.fx BakedColorLit technique), so a rotating station is lit correctly.
        // Full uses the wear/ambient-occlusion-graded mesh; Medium/Minimal use the flat
        // (ungraded) variant built before that pass ran — same generator, fewer steps,
        // same principle already established for containers and station decoration.
        var decoMeshesForLevel = level == DetailLevel.Full ? _decoMeshes : _decoMeshesFlat;

        foreach (var (station, universePos) in ResidentStationEntries())
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            IReadOnlyList<PlacedModule> modules = ResidentStationVisual!.Modules;

            bool useShadow = _stationShadowContext != null
                && ReferenceEquals(_stationShadowContext.Station, station)
                && _stationShadowMap != null;

            Matrix stationRot;
            if (useShadow)
                stationRot = _stationShadowContext!.StationRotation;
            else
            {
                var sysQ   = station.GetOrientation(_gameTimeSeconds);
                var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
                stationRot = Matrix.CreateFromQuaternion(stRotQ);
            }

            foreach (var mod in modules)
            {
                if (!decoMeshesForLevel.TryGetValue(mod, out var deco)) continue;

                // See the hull pass above — mod.Transform used directly, matching the
                // caster and ModuleToStationLocal exactly, not decomposed-then-rebuilt.
                Matrix world = mod.Transform * Matrix.CreateScale(rs) * stationRot
                             * Matrix.CreateTranslation(renderPos);

                // StationTextureRegistry.Get(SurfaceTexture) fallback removed (Brief S2b-1,
                // Report S2a §5): the upload step assigns TextureInstance to
                // every module, so this branch was provably dead. Kept a defensive null
                // fallback (not a crash) in case a future module kind ever skips
                // texture upload — White reads as a flat unlit panel, not a missing-texture
                // artifact.
                Texture2D tex = mod.TextureInstance ?? StationTextureRegistry.White;

                // Brief U1: mod.Mesh is decoration only, for every module kind — a
                // MeshFactory module's hull now lives in its own separate mesh (drawn in
                // the hull pass above, alongside box modules), so this is always a single,
                // unconditional decoration draw, exactly as it was before Brief F1's
                // now-deleted hull/decoration index-range split.
                if (useShadow)
                {
                    var ctx = _stationShadowContext!;
                    float shadowBiasDepth = StationShadowBiasMetres / ctx.DepthSpan;
                    _meshRenderer.DrawBakedColorLitShadowed(deco.vb, deco.ib, world, view, proj,
                        SceneLighting.SunDirection, sunCol, SceneLighting.Ambient, tex,
                        _stationShadowMap!, mod.Transform, ctx.StationLocalToLightView,
                        ctx.MinXY, ctx.InvSize, ctx.Near, ctx.DepthSpan,
                        new Vector2(1f / _stationShadowMapResolution, 1f / _stationShadowMapResolution),
                        StationShadowCorrectionLimit, shadowBiasDepth,
                        _stationShadowBinaryView, _stationShadowDeltaView,
                        ShadowKernelRadiusFor(_shadowKernelMode));
                }
                else
                {
                    _meshRenderer.DrawBakedColorLit(deco.vb, deco.ib, world, view, proj,
                        SceneLighting.SunDirection, sunCol, SceneLighting.Ambient, tex);
                }
            }
        }

        // Glass pass — windows, portholes; unlit, unchanged (Docs/station-lighting-pipeline-spec.md
        // D5: glass is a separate mesh, explicitly out of scope for this migration). Still
        // BasicEffect; explicit state set here since the deco pass above no longer touches _effect.
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.TextureEnabled     = true;
        _effect.Texture            = StationTextureRegistry.White;

        foreach (var (station, universePos) in ResidentStationEntries())
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > 30_000f) continue;

            IReadOnlyList<PlacedModule> modules = ResidentStationVisual!.Modules;

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);
            Matrix stationRot = Matrix.CreateFromQuaternion(stRotQ);

            foreach (var mod in modules)
            {
                if (!_glassMeshes.TryGetValue(mod, out var glass)) continue;

                mod.Transform.Decompose(out _, out Quaternion modRot, out Vector3 posMetres);

                _effect.World =
                    Matrix.CreateScale(rs) *
                    Matrix.CreateFromQuaternion(modRot) *
                    stationRot *
                    Matrix.CreateTranslation(Vector3.Transform(posMetres, stationRot) * rs) *
                    Matrix.CreateTranslation(renderPos);

                _gd.SetVertexBuffer(glass.vb);
                _gd.Indices = glass.ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        baseVertex: 0, startIndex: 0,
                        primitiveCount: glass.triCount);
                }
            }
        }

        _effect.TextureEnabled     = false;
        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;

        // MeshRenderer's Draw() leaves rasterizer/depth state set for its own techniques;
        // restore what the rest of this frame's 3D passes expect (matches DrawContainers'
        // and ShipMeshRenderer.Draw's post-draw restore).
        _gd.RasterizerState   = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }

    private static Color MegastationZoneDebugColor(MegastationZoneRole role) => role switch
    {
        MegastationZoneRole.Structural => new Color(78, 88, 98),
        MegastationZoneRole.Habitation => new Color(55, 170, 225),
        MegastationZoneRole.Industrial => new Color(220, 125, 42),
        MegastationZoneRole.Logistics => new Color(215, 195, 62),
        MegastationZoneRole.Utilities => new Color(130, 78, 180),
        MegastationZoneRole.Strategic => new Color(220, 55, 75),
        _ => Color.White,
    };

    private void DrawStationOrbitRings()
    {
        _effect.LightingEnabled    = false;
        _effect.VertexColorEnabled = true;
        _effect.World              = Matrix.Identity;

        var ringColor = new Color(20, 30, 50, 120);

        foreach (var (station, _) in _stationPositions)
        {
            // Station orbit ring is centred on its parent body's render pos
            DVec3 parentEcliptic = station.OrbitParent != null
                ? station.OrbitParent.GetPosition(_gameTimeSeconds, DVec3.Zero)
                : DVec3.Zero;
            DVec3   parentUniverse = EclipticToGalaxy(parentEcliptic);
            Vector3 parentRender   = _camera.ToRenderSpace(parentUniverse);

            float ringR = (float)(station.OrbitalRadius * Camera3D.RenderScale);
            if (ringR < 0.0001f || ringR > 5_000f) continue;

            _effect.World = Matrix.CreateScale(ringR)
                          * _eclipticRotation
                          * Matrix.CreateTranslation(parentRender);
            _ringPrimitive.Draw(_gd, _effect, ringColor);
        }

        _effect.VertexColorEnabled = false;
        _effect.LightingEnabled    = true;
    }

    // Station dot icons — 3×3 pixel screen-space marker, visible up to 1 million km.
    // Drawn on top of all 3D geometry so stations are always locatable.
    private void DrawStationDots(SpriteBatch sb)
    {
        const float MaxDistRU = 1.0f;   // 1 million km → 1.0 render unit

        var viewProj = Matrix.Multiply(_effect.View, _camera.ProjectionMatrix);
        int w = _gd.Viewport.Width;
        int h = _gd.Viewport.Height;

        foreach (var (_, universePos) in _stationPositions)
        {
            Vector3 renderPos = _camera.ToRenderSpace(universePos);
            if (renderPos.Length() > MaxDistRU) continue;

            Vector4 clip = Vector4.Transform(new Vector4(renderPos, 1f), viewProj);
            if (clip.W <= 0f) continue;

            float sx = ( clip.X / clip.W * 0.5f + 0.5f) * w;
            float sy = (-clip.Y / clip.W * 0.5f + 0.5f) * h;
            if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;

            sb.Draw(_pixel, new Rectangle((int)sx - 1, (int)sy - 1, 3, 3), new Color(160, 190, 210, 220));
        }
    }

    // Draws additive screen-space glow sprites over all station nav lights and warning
    // strobes. Called once per render pass (see DrawFarPassContent/DrawMidPassContent/
    // DrawNearPassContent), filtered to that pass's own real-metre distance range —
    // required because each pass clears and rebuilds its own depth buffer, so a light's
    // glow can only be correctly depth-tested against the SAME pass that drew its host
    // geometry; testing it against a later pass's buffer would compare it against
    // "cleared to far" everywhere that pass didn't itself draw anything, i.e. almost
    // everywhere for lights outside that pass's own range, defeating the depth test.
    // Must run after DrawStations() in the same pass so the additive blend brightens
    // visible geometry and depth-tests against it correctly.
    private void DrawStationGlows(SpriteBatch sb, float nearBoundReal, float farBoundReal)
    {
        if (ResidentStationVisual == null) return;

        // Active pass's projection (_effect.Projection), not camera.ProjectionMatrix —
        // that's only a representative mid-tier projection now that rendering uses three
        // independent per-pass projections. Same fix as ShipMeshRenderer/DrawContainers.
        Matrix   viewProj  = _effect.View * _effect.Projection;
        Viewport viewport  = _gd.Viewport;
        Vector2  texCentre = new(_navGlowTex.Width * 0.5f, _navGlowTex.Height * 0.5f);

        // DepthRead so these sprites are occluded by hull geometry in front of them —
        // read-only depth test (DepthBufferEnable=true, DepthBufferWriteEnable=false),
        // since they're a 2D overlay, not real geometry that should write new depth.
        sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, null, DepthStencilState.DepthRead);
        foreach (var (station, universePos) in ResidentStationEntries())
        {
            IReadOnlyList<PlacedModule> modules = ResidentStationVisual!.Modules;
            Vector3 stationRel = (universePos - _camera.UniversePosition).ToVector3(); // metres

            var sysQ   = station.GetOrientation(_gameTimeSeconds);
            var stRotQ = new Quaternion(sysQ.X, sysQ.Y, sysQ.Z, sysQ.W);

            foreach (var mod in modules)
            {
                foreach (var light in mod.GlowLights)
                {
                    Vector3 relPos   = stationRel + Vector3.Transform(light.WorldPosition, stRotQ);
                    float   distance = relPos.Length();
                    if (distance < 0.1f) continue;
                    if (distance < nearBoundReal || distance >= farBoundReal) continue;

                    Vector2? screen = TargetingSystem.ProjectToScreen(relPos, viewProj, viewport);
                    if (screen == null) continue;

                    float intensity = ComputeGlowIntensity(light);
                    if (intensity < 0.01f) continue;

                    float baseSize = light.Type switch
                    {
                        StationGen.GlowType.NavigationLight => 1200f,
                        StationGen.GlowType.WarningStrobe   => 700f,
                        StationGen.GlowType.AviationWarning => 800f,
                        StationGen.GlowType.AmbientMarker   => 400f,
                        StationGen.GlowType.DockGuidance    => 600f,   // AmbientMarker x1.5, per Timo's ask
                        _                                   => 400f,
                    };
                    float size  = MathHelper.Clamp(baseSize / distance, 6f, 140f);
                    float scale = size / _navGlowTex.Width;

                    // Real depth for this pass's depth test. Without this every sprite
                    // draws at layerDepth 0 (nearest possible depth value), which would
                    // always pass DepthRead regardless of what's actually in front of it —
                    // the state change alone (above) isn't sufficient without this.
                    Vector3 renderPos  = relPos * (float)Camera3D.RenderScale;
                    Vector4 clip       = Vector4.Transform(new Vector4(renderPos, 1f), viewProj);
                    float   layerDepth = MathHelper.Clamp(clip.Z / clip.W, 0f, 1f);

                    sb.Draw(_navGlowTex, screen.Value, null,
                            light.Colour * intensity, 0f, texCentre, scale,
                            SpriteEffects.None, layerDepth);
                }
            }
        }
        sb.End();
    }

    private static float ComputeGlowIntensity(StationLightInfo light)
    {
        if (light.Rate <= 0f) return light.BaseIntensity;
        float t = (float)((GameClock.SimTime * light.Rate + light.Phase) % 1.0);
        return light.Pattern switch
        {
            LightPattern.Strobe    => t < 0.18f ? light.BaseIntensity : 0f,
            LightPattern.SlowPulse => (MathF.Sin(t * MathF.Tau) * 0.5f + 0.5f) * light.BaseIntensity,
            LightPattern.Heartbeat => t < 0.10f ? light.BaseIntensity
                                    : t < 0.22f ? 0f
                                    : t < 0.32f ? light.BaseIntensity * 0.65f
                                    : 0f,
            _ => light.BaseIntensity,
        };
    }
}
