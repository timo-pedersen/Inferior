using Inferior.Core.Math;
using Microsoft.Xna.Framework;

namespace Inferior.Gameplay.Cockpit;

public sealed record CockpitPresentationSnapshot(
    string DefinitionId,
    DVec3 WorldPosition,
    Quaternion WorldOrientation,
    bool CanopyLightsOn,
    bool CockpitLightsOn);
