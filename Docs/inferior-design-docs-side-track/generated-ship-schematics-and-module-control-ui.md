# Generated Ship Schematics and Module Control UI

This document defines automatically generated orthographic ship drawings and the module-control interface built around them.

The purpose is not merely to display attractive blueprints. The system makes installed ship systems visible, selectable, understandable and commandable in game.

---

# Purpose

As ships gain engines, cockpits, cargo interfaces, reactors, power systems, artificial gravity, panels and other modules, much of the configured ship becomes invisible to the player.

A ship-control view is required to answer:

- what is installed;
- where it is installed;
- how much it weighs;
- whether it is active;
- what it consumes;
- what it contributes;
- whether it is damaged;
- what commands it accepts;
- how the complete ship configuration is arranged.

The primary visual foundation is a set of generated orthographic drawings derived from the actual configured ship.

---

# Design goals

The system should:

- generate ship views from real hull and installed-module geometry;
- remain accurate when modules are replaced or rotated;
- provide top, side and other orthographic views;
- let the player select modules directly from the drawings;
- highlight one module across all views;
- expose module state and commands;
- show mass, centre of mass and propulsion information;
- obey simulation/presentation ownership rules;
- cache results when the configuration is unchanged;
- fit into the existing in-game UI system.

The system should not:

- require manually drawn blueprints for every ship configuration;
- treat the drawing as authoritative physical data;
- let UI mutate live module state directly;
- depend on perspective camera framing;
- require detailed interior geometry;
- rebuild schematic textures every frame;
- force all module management into a spreadsheet-like text list.

---

# Ownership model

The simulation owns:

- installed module instances;
- operational state;
- mass and centre of mass;
- commands and command results;
- attachment state;
- damage and wear later.

The UI consumes immutable snapshots.

The schematic renderer consumes:

- immutable hull/module visual definitions;
- installed configuration snapshot;
- stable module-instance IDs;
- presentation state needed for overlays.

The schematic image is derived presentation data. It is never a second authority for installation or module state.

---

# Orthographic views

The system should support six canonical views:

- top;
- bottom;
- port;
- starboard;
- front;
- aft.

The first usable version may expose only:

- top;
- side;

But the rendering architecture should support all six without special cases.

Each view uses:

- orthographic projection;
- fixed ship-local camera orientation;
- no perspective distortion;
- consistent scale;
- consistent framing margins;
- no world background;
- no station or celestial lighting dependency.

---

# View conventions

Canonical ship axes must be stated visibly in the UI.

Recommended labels:

- FWD;
- AFT;
- PORT;
- STARBOARD;
- TOP;
- BOTTOM.

The drawings must remain ship-local. They do not rotate with current world orientation.

A configured ship shown in the top view should always point in the same screen direction.

The exact screen orientation should follow existing project conventions and remain consistent across all ships.

---

# Schematic rendering style

Use a dedicated schematic style rather than the ordinary world render.

Recommended baseline:

- flat category colours;
- restrained outlines;
- no shadows;
- no perspective;
- no stars or environment;
- transparent or UI-coloured background;
- optional subtle depth ordering;
- high contrast against the current UI theme;
- readable at instrument-screen scale.

Possible category colours:

- hull structure;
- cockpit;
- engines;
- reactor/power;
- cargo;
- life support;
- artificial gravity/inertial dampening;
- sensors;
- damaged or offline state;
- selected/highlighted state.

Exact colours belong to UI design and accessibility review.

## Geometry source

The renderer should use actual configured exterior geometry where practical:

```text
Hull geometry
+ installed cockpit geometry
+ installed engines
+ installed external modules
+ attached cargo containers where appropriate
```

Interior-only modules without exterior geometry still require selectable schematic representations. These may use authored simplified volumes or markers tied to their installation positions.

Decorative geometry without gameplay identity should not become separately selectable.

---

# Configuration cache key

Schematic base textures need regeneration only when physical configuration changes.

A cache key should include:

- hull definition ID;
- installed module definition IDs;
- stable mount IDs;
- installation rotations;
- installed external panel configuration;
- attached container arrangement if shown;
- relevant visual-variant IDs.

Operational state such as on/off should normally be drawn as a dynamic overlay, not baked into the base image.

The cache must not depend on current ship position, velocity or world orientation.

---

# Component identification buffer

A visible drawing alone is insufficient for interaction.

Alongside the visible schematic, render a hidden component-identification buffer.

Each selectable physical component is drawn with a unique encoded ID:

- hull;
- cockpit instance;
- each engine instance;
- each installed module;
- each replaceable panel;
- each attached container;
- future doors and equipment.

When the user clicks a point in a schematic view:

1. Read the encoded ID from the component buffer.
2. Resolve it to a stable module or component instance.
3. Select that instance in the UI.
4. Highlight it in every visible view.
5. Show details and available commands.

This avoids unreliable geometric hit testing in UI code.

## Stable identity

Every selectable installed component needs a stable runtime/persistent instance ID.

A definition ID is not enough because a ship may contain multiple instances of the same engine or panel.

---

# Selection and highlighting

Selecting a module should:

- highlight it in all schematic views;
- highlight its row in the module list;
- open or update a detail panel;
- show its mount/slot;
- show related warnings;
- optionally highlight connected systems later.

Hover may provide a lighter preview highlight.

The hull itself may be selectable to show global ship information.

Selection is UI state, not simulation authority.

---

# Module detail panel

The detail panel should present relevant data for the selected component.

Common fields:

- display name;
- definition/manufacturer;
- instance ID or serial where useful;
- module type;
- mount/slot name;
- mass;
- centre-of-mass position;
- operational state;
- power consumption/production;
- condition;
- temperature later;
- commands;
- dependencies and warnings later.

Engine-specific fields:

- primary thrust;
- down thrust;
- rotational contribution;
- current commanded output;
- operational efficiency;
- mount direction;
- single-engine penalty warning where applicable.

Cockpit-specific fields:

- mount facing;
- installation rotation;
- camera orientation;
- canopy/internal lights;
- active control status.

Container-specific fields:

- gross/tare/contents mass;
- contents and quantity where authorised;
- manufacturer and manufacture date;
- attachment point;
- wear/damage;
- access state.

---

# Ship overview panel

The ship-level view should expose the physical totals needed to understand handling:

- current total mass;
- empty/configured mass;
- cargo mass;
- maximum cargo capacity;
- current centre of mass;
- total primary thrust;
- total down thrust;
- total rotational authority;
- inertial-dampening limit;
- current acceleration capability;
- current limiting factor;
- power generation and demand later.

These values come from simulation snapshots.

---

# Physical overlays

The schematic system should support overlays that can be enabled independently.

## Initial useful overlays

- centre-of-mass marker;
- cockpit camera direction;
- ship-forward axis;
- installed engine thrust vectors;
- engine down-thrust vectors;
- cargo attachment points;
- selected module mount;
- offline/damaged indicators.

## Later overlays

- power network;
- command-bus network;
- coolant/thermal loops;
- life-support zones;
- structural stress;
- damage propagation;
- sensor coverage;
- weapon arcs;
- access routes;
- pressure compartments.

Overlays should be derived from real configured data. They must not become hand-authored diagrams disconnected from ship state.

---

# Commands

The module-control UI issues ordinary commands through the existing command-bus pattern.

Possible generic operations:

- start;
- stop;
- enable;
- disable;
- set output;
- reset;
- isolate;
- attach;
- detach;
- lock;
- unlock;
- inspect;
- request manifest;
- set operational mode.

The available command list comes from the selected module's exposed endpoints and current authorisation.

The UI does not assume every engine, cockpit or module supports the same commands.

## Command feedback

The interface should show:

- command accepted;
- command pending;
- command rejected;
- rejection reason;
- resulting state when published.

A button press must not optimistically mutate the snapshot view as if the command already succeeded.

---

# Module list and filters

The schematic view should be paired with a structured module list.

Suggested grouping:

- hull and structural;
- propulsion;
- cockpit/control;
- power;
- artificial gravity/inertial dampening;
- cargo;
- life support;
- sensors/navigation;
- utility;
- attached containers.

Useful filters:

- all;
- active;
- offline;
- damaged;
- high mass;
- high power use;
- warnings;
- external/internal;
- selected subsystem type.

The list and schematic remain synchronized.

---

# UI layout direction

The exact layout belongs to the broader UI-system design, but a practical initial screen contains:

```text
+--------------------------------------------------+
| Ship overview / totals / warnings                |
+----------------------+---------------------------+
| Orthographic views   | Module list               |
| Top / Side tabs      | Grouped and filterable    |
|                      |                           |
+----------------------+---------------------------+
| Selected module details and command endpoints    |
+--------------------------------------------------+
```

Alternative layouts may use:

- a large central schematic with collapsible rails;
- two simultaneous orthographic views;
- tabs for overview, power, cargo and systems;
- the existing CTRL rail as the entry point.

The screen must remain usable at the game's normal resolution and UI scaling.

---

# Relationship to fitting

The first module-control screen is for observation and commands.

Fitting/removal may be added later.

When fitting is introduced, it should use the same schematic foundation:

- select mount;
- inspect compatibility;
- preview module position;
- compare mass/power/thrust changes;
- commit installation through simulation-owned commands;
- regenerate schematic after configuration changes.

Do not force fitting UI into the first control implementation.

---

# Generated blueprint output

The same orthographic renderer may produce static blueprint assets for:

- ship purchase screens;
- technical manuals;
- station terminals;
- loading screens;
- contracts;
- ship comparison;
- lore documents;
- damage reports.

Possible outputs:

- top and side thumbnails;
- all six views;
- transparent PNG-like textures in memory;
- UI-native render targets;
- vector-like line presentation later.

The runtime instrument view remains the priority.

---

# Initial implementation phases

## Phase 1: schematic renderer prototype

- render one configured ship in top and side orthographic views;
- include hull, cockpit and engines;
- fixed scale and framing;
- flat schematic materials;
- no interaction yet;
- verify Aries, Asterisk and Beren.

## Phase 2: component IDs and selection

- stable component-instance IDs;
- hidden identification buffer;
- click selection;
- highlight across views;
- synchronized basic module list.

## Phase 3: module details

- name, type, mount, mass and state;
- engine thrust data;
- cockpit state;
- ship-level mass and centre of mass;
- snapshot-driven updates.

## Phase 4: command endpoints

- enumerate available commands;
- start/stop or enable/disable selected modules;
- cockpit-light commands;
- container attach/detach later;
- command result feedback.

## Phase 5: overlays

- centre of mass;
- thrust vectors;
- cargo points;
- inertial-dampening limit;
- warnings.

## Phase 6: fitting and richer systems

- mount compatibility;
- module replacement;
- power and command-bus diagrams;
- damage and maintenance;
- six-view blueprint output.

---

# Tests

Focused tests should cover:

- identical configuration produces identical cache key;
- changing an installed module changes the cache key;
- operational on/off state does not unnecessarily rebuild base geometry;
- component-instance IDs are unique;
- clicking an identification-buffer pixel resolves the correct component;
- selecting an engine highlights the same instance in every view;
- top and side views use orthographic projection;
- world orientation does not alter schematic orientation;
- snapshot values populate detail fields without recalculating authority;
- commands target the selected stable instance ID;
- rejected commands leave displayed authoritative state unchanged until a new snapshot arrives.

Visual acceptance should verify:

- Aries, Asterisk and Beren fit the frame consistently;
- extreme side and underslung cockpits remain visible in relevant views;
- four identical Beren engines are individually selectable;
- the selected component is unambiguous;
- centre-of-mass and thrust overlays agree with configuration;
- the screen remains readable at normal play resolution.

---

# Future extensions

- animated state overlays;
- damage heatmaps;
- power and coolant networks;
- command-bus topology;
- module fitting and replacement;
- comparison between current and proposed configuration;
- touch/controller navigation;
- accessibility colour modes;
- printable/exported blueprints;
- interior deck plans where authored;
- maintenance history;
- AI-assisted diagnostics in lore;
- multi-ship fleet overview.

---

# Design invariants

1. Schematics are generated from the actual configured ship.
2. Orthographic views do not use perspective.
3. The simulation remains authority for module state.
4. The schematic image is presentation data only.
5. Every selectable installed component has a stable instance ID.
6. A component-identification buffer drives direct visual selection.
7. Selection is synchronized across views and lists.
8. Commands flow through the command bus.
9. Base schematic geometry is cached by configuration, not current world state.
10. Operational overlays update without rebuilding unchanged geometry.
11. Decorative mesh details do not automatically become manageable modules.
12. The system exists to make invisible ship systems understandable and controllable.

---

===== End of main document =========================================

# Appendix A - UI decisions still required

- exact integration point in the current UI rails/screens;
- whether top and side views are simultaneous or tabbed;
- canonical screen direction for forward in each view;
- category colours and accessibility palette;
- module-list density and grouping;
- command confirmation policy;
- controller/keyboard navigation;
- how many overlays can be visible without clutter;
- whether attached containers appear in the base schematic or a cargo overlay;
- whether hidden/internal modules use simplified volumes, icons or both;
- how fitting mode differs visually from control mode.
