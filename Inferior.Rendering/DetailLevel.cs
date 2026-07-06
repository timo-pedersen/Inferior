namespace Inferior.Rendering;

// A property of the draw call, not an identity tied to whichever render tier happens to be
// calling — keeps future additions (a hyperspace-transit ship render, an intermediate level
// for something that doesn't map cleanly to an existing tier) additive, not a change to the
// tier system itself.
public enum DetailLevel { Full, Medium, Minimal }
