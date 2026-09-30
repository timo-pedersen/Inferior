using Inferior.Core.World;
using Inferior.Game.Containers;
using Inferior.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Inferior.Game.States;

public sealed partial class SystemSpaceState
{
    private const float ContainerMarkingDepthBias = .00002f;

    // level is accepted but not yet used — no container LOD variants exist yet.
    private void DrawContainers(DetailLevel level)
    {
        if (_frameWorldSnap == null
            || _frameWorldSnap.SystemGalaxyIndex != _star.GalaxyIndex
            || _frameWorldSnap.Containers.Count == 0
            || _meshRenderer == null)
            return;

        float rs = (float)Camera3D.RenderScale;
        Matrix view = _effect.View;
        Matrix proj = _effect.Projection;
        var (specStrength, specShininess) = SpecularParamsFor(_specularPreset);

        var markedContainers = new List<(RenderedContainer Visual, Matrix World)>();

        foreach (SpaceSimulation.ShippingContainerSnapshot snapshot in _frameWorldSnap.Containers)
        {
            if (!_containers.TryGetValue(snapshot.State.Id, out RenderedContainer? visual))
                continue;

            Vector3 renderPos = _camera.ToRenderSpace(snapshot.State.Position);
            if (renderPos.Length() > 30_000f) continue;

            Matrix world = Matrix.CreateScale(rs)
                         * Matrix.CreateFromQuaternion(snapshot.State.Orientation)
                         * Matrix.CreateTranslation(renderPos);

            _meshRenderer.DrawDynamicLit(visual.Vb, visual.Ib, world, view, proj,
                Color.White, SceneLighting.SunDirection, new Color(SceneLighting.SunColour), SceneLighting.Ambient,
                specStrength, specShininess);

            if (visual.MarkingVb != null && visual.MarkingIb != null)
                markedContainers.Add((visual, world));
        }

        // Projected surface markings remain a separate alpha-blended overlay. The receiver
        // mesh is untouched and every container samples the same shared atlas.
        if (markedContainers.Count > 0)
        {
            _gd.BlendState = BlendState.AlphaBlend;
            Texture2D atlas = ContainerSurfaceMarking.GetAtlas(_gd);
            foreach (var (visual, world) in markedContainers)
            {
                _meshRenderer.DrawDecalLit(visual.MarkingVb!, visual.MarkingIb!, world, view, proj,
                    Color.White, SceneLighting.SunDirection, new Color(SceneLighting.SunColour),
                    SceneLighting.Ambient, atlas, ContainerMarkingDepthBias);
            }
            _gd.BlendState = BlendState.Opaque;
        }

        _gd.RasterizerState = RasterizerState.CullCounterClockwise;
        _gd.DepthStencilState = DepthStencilState.Default;
    }

    /// <summary>
    /// Reconciles main-thread GPU resources with immutable simulation snapshots. This method
    /// never writes position, orientation, or velocity back to the simulation.
    /// </summary>
    private void SyncContainerVisuals(SpaceSimulation.WorldPresentationSnapshot? snapshot)
    {
        if (snapshot == null || snapshot.SystemGalaxyIndex != _star.GalaxyIndex)
            return;

        var seen = new HashSet<WorldObjectId>();
        foreach (SpaceSimulation.ShippingContainerSnapshot containerSnapshot in snapshot.Containers)
        {
            WorldObjectId id = containerSnapshot.State.Id;
            seen.Add(id);

            if (_containers.TryGetValue(id, out RenderedContainer? existing)
                && existing.Container == containerSnapshot.Container)
                continue;

            existing?.Dispose();
            _containers[id] = CreateContainerVisual(containerSnapshot.Container);
        }

        foreach (WorldObjectId staleId in _containers.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            _containers[staleId].Dispose();
            _containers.Remove(staleId);
        }
    }

    private RenderedContainer CreateContainerVisual(ShippingContainer container)
    {
        ShippingContainerGeometry geometry = ShippingContainerFactory.GenerateGeometry(container);

        var vb = new VertexBuffer(_gd, VertexPositionNormalColorTexture.VertexDeclaration,
            geometry.Vertices.Length, BufferUsage.WriteOnly);
        vb.SetData(geometry.Vertices);

        var ib = new IndexBuffer(_gd, IndexElementSize.SixteenBits,
            geometry.Indices.Length, BufferUsage.WriteOnly);
        ib.SetData(geometry.Indices);

        VertexBuffer? markingVb = null;
        IndexBuffer? markingIb = null;
        var (markingVertices, markingIndices) = ContainerSurfaceMarking.BuildDecal(geometry);
        if (markingIndices.Length > 0)
        {
            markingVb = new VertexBuffer(_gd, VertexPositionNormalColorTexture.VertexDeclaration,
                markingVertices.Length, BufferUsage.WriteOnly);
            markingVb.SetData(markingVertices);

            markingIb = new IndexBuffer(_gd, IndexElementSize.SixteenBits,
                markingIndices.Length, BufferUsage.WriteOnly);
            markingIb.SetData(markingIndices);
        }

        return new RenderedContainer(container, vb, ib, markingVb, markingIb);
    }

    private void DisposeContainerVisuals()
    {
        foreach (RenderedContainer visual in _containers.Values)
            visual.Dispose();
        _containers.Clear();
    }

    private sealed class RenderedContainer(
        ShippingContainer container,
        VertexBuffer vb,
        IndexBuffer ib,
        VertexBuffer? markingVb,
        IndexBuffer? markingIb) : IDisposable
    {
        public ShippingContainer Container { get; } = container;
        public VertexBuffer Vb { get; } = vb;
        public IndexBuffer Ib { get; } = ib;
        public VertexBuffer? MarkingVb { get; } = markingVb;
        public IndexBuffer? MarkingIb { get; } = markingIb;

        public void Dispose()
        {
            Vb.Dispose();
            Ib.Dispose();
            MarkingVb?.Dispose();
            MarkingIb?.Dispose();
        }
    }
}
