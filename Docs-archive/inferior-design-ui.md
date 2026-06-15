# Inferior — UI Design Reference

> Design notes and class sketches for the UI library (`Inferior.UI`).
> Code-level detail for individual controls is documented here alongside architecture notes.
> See `inferior-classes.md` for simulation-layer systems.

---

## Overview

Custom retained-mode UI — not a generic WPF/WinForms clone. Minimal, purposeful,
space-game aesthetic. Lives in its own assembly (`Inferior.UI`) with no dependency
on gameplay simulation code. It talks to the rest of the game only through
`DataBus` subscriptions and the `IUIRenderer` interface.

Design goals:
- `IUIRenderer` interface — swapping the renderer changes the entire aesthetic.
- Controls are self-contained: they subscribe to `DataBus` topics and update
  themselves without external wiring.
- The library is intended to be extracted into a standalone library in the future.

---

## `IUIRenderer` interface

```csharp
public interface IUIRenderer
{
    void DrawPanel(Rectangle bounds, PanelStyle style);
    void DrawButton(Rectangle bounds, string text, ButtonState state);
    void DrawTextBox(Rectangle bounds, string text, bool focused);
    void DrawLabel(Rectangle bounds, string text);
    void DrawWindow(Rectangle bounds, string title, bool focused);
}
```

---

## `Control` base class

All controls inherit from `Control`. Position is relative to parent;
`AbsoluteBounds` resolves the screen-space rectangle.

```csharp
public abstract class Control
{
    public Rectangle Bounds     { get; set; }
    public Color?    ForeColor  { get; set; }
    public Color?    BackColor  { get; set; }
    public Color?    TextColor  { get; set; }
    public bool      Visible    { get; set; }
    public bool      Enabled    { get; set; }
    public bool      Focused    { get; set; }

    // Layout
    public virtual Rectangle AbsoluteBounds { get; }
    public virtual Rectangle ContentBounds  { get; }

    // Events
    public event Action<Control>? Clicked;
}
```

---

## Controls

### Standard controls

| Control | Purpose |
|---|---|
| `Button` | Clickable button with hover state |
| `Label` | Static text |
| `Panel` | Transparent or filled container, groups children |
| `Window` | Draggable panel with title bar |

### Instrument controls

#### `InstrumentMeter`

Horizontal bar meter for a single numeric value. Auto-subscribes to a
`DataBus.Instruments` topic when `Topic` is set — no manual wiring needed.

Key properties:
```csharp
public string Label       { get; set; }   // header text
public double MinValue    { get; set; }
public double MaxValue    { get; set; }
public string Format      { get; set; }   // e.g. "F1", "F0"
public string Topic       { get; set; }   // DataBus topic — auto-subscribes on set
public double ScaleFactor { get; set; }   // multiplied against raw bus value before display
                                          // use 1e-6 for watts → MW, etc. Defaults to 1.0.
```

The meter's `MinValue`/`MaxValue` are in *display* units. `ScaleFactor` converts
from the raw bus unit to the display unit. The bus itself is unit-agnostic.

**Example**: reactor publishes watts, meter shows MW:
```csharp
new InstrumentMeter { Topic = "Reactor.Output", MaxValue = 120, ScaleFactor = 1e-6 }
```

#### `SystemConsole`

Scrolling message log. Subscribes to `DataBus.System` or receives messages manually.
Supports three line-break modes: `Clip`, `Wrap`, `Bleed`.

Key properties:
```csharp
public string        Header    { get; set; }
public int           MaxLines  { get; set; }
public LineBreakMode LineBreak { get; set; }
```

#### `DirectionBall`

Projects 3D direction vectors onto a 2D hemisphere display. The ball shows the
ship's orientation and any registered vectors (gravity, star direction, target).

- Filled dot = vector in front hemisphere
- Hollow rim dot = vector in rear hemisphere
- Central crosshair = forward axis alignment reference

```csharp
_dirBall.SetOrientation(forward, right, up);
_dirBall.SetVector("grav", gravityDirection, Color.Cyan, "g");
_dirBall.SetVector("star", toStar, Color.Yellow, "★");
```

---

## `EdgePanelHost`

Slide-out panel anchored to a screen edge, with a vertical (or horizontal) strip
of tab handles. Used for instrument panels and captain's log in `SystemSpaceState`.

```
Screen edge (right)
    │  ┌──┐
    │  │IN│  ← tab handle (INSTR)
    │  │ST│
    │  ├──┤
    │  │NA│  ← tab handle (NAV)
    │  │V │
    │  └──┘
    │
    │         ┌──────────────────────────┐
    │         │  Panel content (INSTR)   │
    │         │  [InstrumentMeters]      │
    │         └──────────────────────────┘
```

Key properties:
```csharp
public PanelEdge Edge          { get; }   // Left, Right, Top, Bottom
public int       PanelSize     { get; set; }  // content width/height in px
public int       HandleSize    { get; set; }  // tab strip thickness
public int       HandleLength  { get; set; }  // per-tab length
public bool      UiModeActive  { get; set; }  // false = handles hidden, panel flush with edge
```

Interaction:
- Click a closed tab → panel slides open to that tab
- Click the active tab → panel slides closed
- Click a different tab while open → switches content without re-animating
- `UiModeActive = false` → handles hide, panel retracts flush with screen edge
  (used when the player is in flight mode rather than UI browsing mode)

Layout restore: `CaptureState()` / `ApplyState()` — the active tab and open state
survive state transitions (stored in `CockpitLayout`).

---

## `UIManager`

Root manager. Holds the list of top-level controls (including `EdgePanelHost`
instances), drives the animation loop, dispatches input.

```csharp
var ui = new UIManager(graphicsDevice, theme);
ui.Add(rightPanel);
ui.Add(leftPanel);
ui.Add(backButton);

// Each frame:
ui.Animate(dt);                       // advance animations
ui.Update(dt, inputState);            // hit-test + dispatch input (UI mode only)
ui.Draw();                            // render all visible controls
```

Call `Animate()` every frame regardless of input mode so slide animations
complete smoothly even when the player is in flight mode.

---

## `Theme`

Central visual configuration. One theme instance controls all colors, fonts,
and sizes for every control. `Theme.InferiorDark()` is the current space theme.

```csharp
var theme = Theme.InferiorDark(font);
```

Swap the theme to change the entire aesthetic — controls read colors from it,
not from their own properties.

---

## `InputState`

Snapshot of mouse and keyboard state passed to `UIManager.Update()` each frame.
Constructed from MonoGame's `MouseState` and `KeyboardState`:

```csharp
new InputState(mouse, prevMouse, keys, prevKeys)
```

---

## Coordinate conventions

- `Bounds` — rectangle relative to parent's `ContentBounds`
- `AbsoluteBounds` — screen-space rectangle (resolved by walking parent chain)
- `ContentBounds` — usable inner area within a container (after padding)
  `EdgePanelHost` overrides this to return the animated sliding content area

---

## Changelog

| Date | Change |
|------|--------|
| 2026-06-07 | Initial document — extracted from inferior-classes.md, added EdgePanelHost, InstrumentMeter ScaleFactor, UIManager, Theme, InputState |
