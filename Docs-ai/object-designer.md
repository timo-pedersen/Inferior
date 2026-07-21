# Inferior Object Designer

> Active implementation reference for the first Beren JSON authoring slice.

## Purpose

`Inferior.ObjectDesigner` is a standalone MonoGame executable for authoring physical object definitions used by Inferior. The first supported asset is the Beren ship hull.

This is not a general CAD system. The current tool proves one end-to-end workflow:

```text
Assets/Ships/beren.ship.json
    -> shared JSON load and validation
    -> runtime HullDefinition
    -> existing semantic hull renderer
    -> orthographic vertex edit
    -> command undo/redo
    -> deterministic save/reload
    -> game loads the same asset
```

## Boundaries

Shared authoring code lives in `Inferior.Gameplay/Hull/Authoring`. A separate `Inferior.ObjectDefinitions` project was not created in this slice because the runtime domain types (`HullDefinition`, `SemanticHullGeometry`, cockpit mounts, engine attachment ports) currently live in `Inferior.Gameplay`; splitting conversion into a new lower-level project would require a larger domain move.

The authoring layer contains DTOs, JSON serializer options, asset path probing, validation diagnostics, and conversion into immutable runtime `HullDefinition`.

Editor state stays in `Inferior.ObjectDesigner`:

- selected vertex;
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

The old hard-coded Beren geometry factory body has been removed. Aries, Asterisk, Antega, Sidewinder and Cobra remain on their existing paths.

## Validation

Validation ownership:

- `ShipAuthoringValidator` handles schema, object kind, duplicate document IDs, cargo-door references, default cockpit references and default engine references.
- `HullDefinition.Validate()` and `SemanticHullGeometry.Validate()` remain the runtime semantic validation path for hull geometry, cockpit mounts, attachment ports, closed hull checks and triangulation prerequisites.
- Invalid authoring assets fail load with actionable diagnostics.
- The designer permits invalid in-memory edits but blocks save while errors exist.

## Editing

Current editable operation:

- select one existing semantic vertex in the active orthographic projection;
- move it by mouse drag;
- edit exact X/Y/Z values through text boxes;
- rebuild validation and GPU hull mesh immediately;
- save/reload the JSON asset.

Projection mapping:

- top edits X/Z;
- side edits Z/Y;
- front edits X/Y;
- the hidden coordinate is preserved.

Command history:

- `IEditCommand` with `Execute`, `Undo`, and `Description`;
- vertex drag commits one `MoveVertexCommand` on mouse release;
- numeric coordinate entry also uses `MoveVertexCommand`;
- redo is cleared by a new command after undo;
- save marks the current command position clean;
- undoing back to the save point clears dirty state.

## UI and Rendering

The tool uses MonoGame and `Inferior.UI`.

Current layout:

- toolbar;
- perspective viewport;
- active orthographic viewport;
- right-side hierarchy/properties/validation panel.

Rendering reuses:

- `SemanticHullMeshBuilder`;
- `ShipMeshRenderer`;
- `MeshRenderer`;
- `SceneLighting`;
- installed engine rendering;
- installed cockpit rendering.

`ShipMeshRenderer.Draw` accepts an optional in-memory `HullDefinition` override and optional local render scale for editor preview. The game path keeps the existing registry lookup and default universe render scale.

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
- UI library split;
- final universal object schema.

Visual equivalence and usability remain pending Timo's in-engine/manual confirmation.
