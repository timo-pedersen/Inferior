using Inferior.Core;
using Inferior.Core.DataBus;
using Inferior.Core.Math;
using Inferior.Core.Simulation;
using Inferior.Galaxy;
using Inferior.Game.Hyperspace;
using Inferior.Game.StationGen;
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

    // Pushes planets, moons, and stations into TargetingSystem each frame so the
    // C-key and click-to-target handlers have contacts to work with.
    // Positions are already galaxy-space from the current frame's _bodyPositions /
    // _stationPositions.  RelativePosition is the vector from camera to contact in
    // metres (float precision is fine for screen-space projection).
    private void FeedRadarContacts()
    {
        DVec3 camPos = _camera.UniversePosition;

        // Remove any stale body:* contacts (planets/moons are not shown on radar)
        var staleBodyIds = new List<string>();
        foreach (var id in _radarContactIds)
            if (id.StartsWith("body:", StringComparison.Ordinal))
                staleBodyIds.Add(id);
        foreach (var id in staleBodyIds)
        {
            _targeting.OnContactLost(id);
            _cockpitUI.NotifyRadarContactLost(id);
            _radarContactIds.Remove(id);
        }

        foreach (var (station, galaxyPos) in _stationPositions)
        {
            string id      = $"station:{station.Name}";
            DVec3  del     = galaxyPos - camPos;
            var    contact = new RadarContact(
                id, station.Name,
                new Vector3((float)del.X, (float)del.Y, (float)del.Z),
                Vector3.Zero, ContactType.Station);
            _targeting.OnContactUpdated(contact);
            _cockpitUI.NotifyRadarContact(contact);
            _radarContactIds.Add(id);
        }

        // TODO: remove test containers — debug contacts for radar testing
        foreach (var tc in _testContainers)
        {
            DVec3 stPos = DVec3.Zero;
            foreach (var (s, sPos) in _stationPositions)
                if (ReferenceEquals(s, tc.Station)) { stPos = sPos; break; }
            DVec3 pos   = stPos + tc.Offset;
            DVec3 del   = pos - camPos;
            var   contact = new RadarContact(
                tc.Id, tc.Name,
                new Vector3((float)del.X, (float)del.Y, (float)del.Z),
                Vector3.Zero, ContactType.Debris);
            _targeting.OnContactUpdated(contact);
            _cockpitUI.NotifyRadarContact(contact);
            _radarContactIds.Add(tc.Id);
        }
    }

    // Computes pad world position from current station orbit + orientation, then
    // publishes Docking.* topics to the Instruments bus.
    private void UpdatePadTargetPosition()
    {
        if (!_targeting.HasPadTarget)
        {
            DataBus.Instruments.Publish(Topics.Docking.PadTargeted, 0.0);
            _padWorldPos  = DVec3.Zero;
            _padDistance  = 0.0;
            _padDirection = DVec3.Zero;
            _simulation.SetPadTarget(null);
            return;
        }

        var station = _targeting.TargetedPadStation!;
        var pad     = _targeting.TargetedPad!;

        // Find station's current world position
        DVec3 stationPos = DVec3.Zero;
        foreach (var (s, pos) in _stationPositions)
            if (s == station) { stationPos = pos; break; }

        // Transform pad local position and normal by station orientation
        Quaternion ori       = station.GetOrientation(_gameTimeSeconds);
        var localPos         = new Vector3((float)pad.LocalPosition.X, (float)pad.LocalPosition.Y, (float)pad.LocalPosition.Z);
        var localNrm         = new Vector3((float)pad.LocalNormal.X,   (float)pad.LocalNormal.Y,   (float)pad.LocalNormal.Z);
        var offset           = Vector3.Transform(localPos, ori);
        var worldNrmV        = Vector3.Normalize(Vector3.TransformNormal(localNrm, Matrix.CreateFromQuaternion(ori)));
        _padWorldPos         = stationPos + new DVec3(offset.X, offset.Y, offset.Z);
        DVec3 worldNormal    = new DVec3(worldNrmV.X, worldNrmV.Y, worldNrmV.Z);

        DVec3 shipPos        = _frameShipSnap?.Position ?? _camera.UniversePosition;
        DVec3 delta          = _padWorldPos - shipPos;
        _padDistance         = delta.Length;
        _padDirection        = _padDistance > 1.0 ? delta * (1.0 / _padDistance) : DVec3.Zero;

        // Rotate the station-local forward axis (stored by StationGenerator to match the visual arrow)
        // to world space via the station orientation quaternion.
        var localFwdV = new Vector3((float)pad.LocalForward.X, (float)pad.LocalForward.Y, (float)pad.LocalForward.Z);
        var worldFwdV = Vector3.Normalize(Vector3.TransformNormal(localFwdV, Matrix.CreateFromQuaternion(ori)));
        DVec3 padForward = new DVec3(worldFwdV.X, worldFwdV.Y, worldFwdV.Z);

        // Push to sim thread for LandingSupportSystem
        var padData = new LandingPadData(
            WorldPosition: _padWorldPos,
            WorldNormal:   worldNormal,
            ForwardAxis:   padForward,
            PadSize:       pad.PadSize,
            BayId:         $"PAD {pad.PadIndex + 1:D2}",
            StationName:   station.Name);
        _simulation.SetPadTarget(padData);

        // Publish to Instruments bus so Step 3 docking instrument can subscribe
        DataBus.Instruments.Publish(Topics.Docking.PadTargeted,   1.0);
        DataBus.Instruments.Publish(Topics.Docking.PadDistance,   _padDistance);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionX, _padDirection.X);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionY, _padDirection.Y);
        DataBus.Instruments.Publish(Topics.Docking.PadDirectionZ, _padDirection.Z);
        DataBus.Instruments.Publish(Topics.Docking.PadSizeClass,  pad.PadSize == Galaxy.PadSize.Large ? 1.0 : 0.0);
    }
}
