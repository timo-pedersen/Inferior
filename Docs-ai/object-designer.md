# Inferior Object Designer

> Active implementation reference for the Beren JSON authoring/object-designer slice.

## Purpose

`Inferior.ObjectDesigner` is a standalone MonoGame executable for authoring physical object definitions used by Inferior. The first supported asset is the Beren ship hull.

This is not a general CAD system. The current checkpoint is architecturally proven and visually functioning, but not yet usability-accepted. It proves one end-to-end workflow:

```text
Assets/Ships/beren.ship.json
    -> shared JSON load and validation
    -> runtime HullDefinition
    -> existing semantic hull renderer
    -> orthographic vertex edits
    -> valid edits update the 3D preview
    -> command undo/redo
    -> deterministic save/reload
    -> game loads the same asset
```

## Boundaries

Shared authoring code lives in `Inferior.Gameplay/Hull/Authoring`. A separate `Inferior.ObjectDefinitions` project was not created in this slice because the runtime domain types (`HullDefinition`, `SemanticHullGeometry`, cockpit mounts, engine attachment ports) currently live in `Inferior.Gameplay`; splitting conversion into a new lower-level project would require a larger domain move.

The authoring layer contains DTOs, JSON serializer options, asset path probing, validation diagnostics, and conversion into immutable runtime `HullDefinition`.

Editor state stays in `Inferior.ObjectDesigner`:

- selected vertices and active vertex;
- active face;
- projection mode;
- camera state;
- command history;
- dirty state;
- UI layout.

No editor state is saved into the asset.

## Source Asset

Current source asset:

```text
Assets/Ships/beren.ship.json
```

The game and designer both copy this loose asset into output as:

```text
Assets/Ships/beren.ship.json
```

The loader also probes upward from `AppContext.BaseDirectory` and the current directory, so tests and development runs can resolve the repository asset without depending on the process working directory.

## Schema

Current schema version: `1`.

The root contains:

```json
{
  "schemaVersion": 1,
  "assetId": "beren",
  "objectKind": "ship"
}
```

The ship hull document currently stores:

- hull metadata, size class, dimensions, mass and aerodynamics;
- transitional cockpit offset/pose plus physical cockpit mount;
- component slots and default engine/cockpit references;
- cargo arrangement and individual container placements;
- semantic vertices and faces with stable IDs;
- face roles, material groups, normals, panel slot IDs and assembly IDs;
- cargo-door assembly metadata;
- engine attachment ports and clearance metadata.

## Loading Path

`BerenHullDefinitionFactory.Create()` is now a thin loader-backed adapter:

```text
BerenHullDefinitionFactory.AssetPath
    -> ShipAuthoringJson.LoadHull(...)
    -> ShipAuthoringConverter.ToHullDefinition(...)
    -> ShipAuthoringValidator.Validate(...)
    -> HullDefinitionLibrary.Register(...)
```

The old hard-coded Beren geometry factory body has been removed. Aries, Cosmo, Asterisk, Antega, Sidewinder and Cobra remain on their existing paths.

## Validation

Validation ownership:

- `ShipAuthoringValidator` handles schema, object kind, duplicate document IDs, cargo-door references, default cockpit references and default engine references.
- `HullDefinition.Validate()` and `SemanticHullGeometry.Validate()` remain the runtime semantic validation path for hull geometry, cockpit mounts, attachment ports, closed hull checks and triangulation prerequisites.
- `AuthoringDiagnostic` now carries stable `Code`, `Summary`, optional details, measured/tolerance values, and related entity IDs in addition to the legacy message/entity fields.
- Invalid authoring assets fail load with actionable diagnostics.
- The designer permits invalid in-memory edits, keeps the last renderable preview hull visible, and blocks save while errors exist without throwing through the UI.

## Editing

Current editable operations:

- click-select or Shift-toggle semantic vertices in the active orthographic projection;
- drag a selected semantic vertex or selected vertex group by one shared constrained displacement;
- select an active incident face for the active vertex in the Properties panel;
- marquee-select vertices by dragging empty space;
- clear selection with Escape;
- edit exact X/Y/Z values for the active vertex through text boxes;
- constrain drag movement to view plane, X/Y/Z axis, or the explicit active face plane;
- inspect and cycle active-vertex incident face IDs in the properties panel;
- rebuild validation and GPU hull mesh immediately;
- save/reload the JSON asset.

Projection mapping:

- top edits X/Z;
- side edits Z/Y;
- front edits X/Y;
- the hidden coordinate is preserved.
- mouse wheel zooms the 2D editor;
- middle mouse pans the 2D editor.
- `G` cycles the next incident face for the active vertex; `Shift+G` cycles backward.

Command history:

- `IEditCommand` with `Execute`, `Undo`, and `Description`;
- vertex drag commits one `MoveVerticesCommand` on mouse release;
- grouped vertex drag snapshots selected stable IDs, original positions and the active constraint at drag start; Face mode additionally captures the active face ID, plane origin, plane normal and mouse/plane intersection; undo/redo owns the captured positions and does not depend on later selection state;
- numeric coordinate entry also uses `MoveVertexCommand`;
- redo is cleared by a new command after undo;
- save marks the current command position clean;
- undoing back to the save point clears dirty state.

## UI and Rendering

The tool uses MonoGame and `Inferior.UI`.

Current layout:

- top menu/toolbar;
- active 2D editor on the left;
- 3D preview on the right;
- collapsible properties/diagnostics below the 3D preview;
- full-width status bar.

The editor panes are UI-owned `DesignerSurfaceControl`s. Drawing, hit testing, fit-to-view and clipping all use the arranged surface content bounds. The UI tree is the authoritative draw path for UI composition: each surface draws its panel background, optional content, then its own border/title foreground. The 2D editor still uses `UIRenderer.DrawCustomContent` to suspend the UI batch for direct primitive drawing inside `ContentBounds ∩ EffectiveClipBounds`. The 3D preview is prepared before the backbuffer clear/UI pass into a pane-sized render target, then the perspective surface samples that prepared texture through ordinary SpriteBatch UI drawing without switching render targets during UI composition. Popups/tooltips remain in the UI overlay layer and draw last.

3D preview controls:

- left drag orbits;
- middle drag pans the target;
- right drag rotates the editor light;
- wheel zooms.

The UI foundation now includes generic `OverflowMode`, `Thickness`, `GridPanel`, `StackPanel`, `CollapsiblePanel`, `ScrollPanel`, `TextBlock`, exclusive toggle grouping, and simple menu/popup controls.

Menus use `UIManager`'s overlay layer, so popups are not clipped by ordinary controls and draw above editor surfaces. Projection and movement-constraint buttons use authoritative `ChoiceGroup<T>` values; buttons reflect the group value and cannot independently remain selected. `UIRenderer.DrawCustomContent` restores render targets, viewport, scissor rectangle, rasterizer, blend, depth/stencil and sampler state before resuming the UI SpriteBatch.

The active face is session-owned as a stable semantic face ID. It remains valid only while the active vertex belongs to that face. When the active vertex changes, the face is preserved if still incident, cleared if not, and auto-selected only when the new vertex has exactly one incident face. The selected vertex group is independent of the active face; Face mode moves the whole captured selection by one delta computed from the fixed captured face plane. Missing active face, degenerate face, or edge-on orthographic projection blocks Face drag with a status message rather than falling back to another constraint.

Rendering reuses:

- `SemanticHullMeshBuilder`;
- `ShipMeshRenderer`;
- `MeshRenderer`;
- `SceneLighting`;
- installed engine rendering;
- installed cockpit rendering.

`ShipMeshRenderer.Draw` is the authoritative shared ship-rendering entry point for both the main game and the Object Designer preview. It accepts an optional in-memory `HullDefinition` override and optional local render scale for editor preview. The game path keeps the existing registry lookup and default universe render scale.

The editor owns preview-specific scene inputs only: camera pose, pane-sized render target, background, debug mode, and the movable preview light direction via `SceneLighting`. Ship material interpretation remains in `Inferior.Rendering`: the preview uses the same `DynamicLitMaterialSettings.Tight` specular preset as the in-game default ship path, and passes its local preview camera as the DynamicLit eye position because its offscreen view matrix is not the normal origin-shifted `Camera3D.ViewMatrix`.

Current material scope: ship hulls, installed engines, installed cockpits, containers, calibration cube and station hulls all use `MeshRenderer.DrawDynamicLit*` over `LitSurface.fx` and can receive the shared specular parameters. Per-texel gloss and derivative bump are station-hull material-map features today. Ship rendering still uses the neutral 1x1 material map and `BumpStrength = 0`; do not describe ship bump mapping or ship per-texel gloss as implemented.

Editor-only selection overlays draw after normal ship rendering in the preview target: active face outline and active vertex cross. The same active face data is used for the 2D stronger face perimeter and active-vertex marker. This overlay state is not part of gameplay snapshots or ship materials.

## Checkpoint Usability State

The Object Designer foundation is viable and visually working, but still rough and unsuitable for serious hull production. Known defects and review items deliberately preserved for the next refinement pass:

- selected-button border state is visually misleading despite the LED/group state;
- substantial editing-workflow refinement remains;
- full functionality review is pending Timo's later workout.

## Deferred

Deferred by this slice:

- panel authoring and panel catalogue;
- face creation/deletion;
- edge extrusion;
- mount editing;
- engine/cockpit geometry editing;
- object cloning or new objects;
- file browser;
- multiple simultaneous documents;
- final universal object schema.

Architecture and rendering are proven at this checkpoint. Visual functioning has been confirmed by Timo, but usability acceptance and detailed workflow review remain pending.
