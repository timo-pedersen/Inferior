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
        DVec3 shipPos = _frameShipSnap?.Position ?? camPos;

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
            float shipDistance = (float)(galaxyPos - shipPos).Length;
            var    contact = new RadarContact(
                id, station.Name,
                new Vector3((float)del.X, (float)del.Y, (float)del.Z),
                Vector3.Zero, ContactType.Station, shipDistance);
            _targeting.OnContactUpdated(contact);
            _cockpitUI.NotifyRadarContact(contact);
            _radarContactIds.Add(id);
        }

        foreach (var pc in _containers)
        {
            DVec3 stPos = DVec3.Zero;
            foreach (var (s, sPos) in _stationPositions)
                if (ReferenceEquals(s, pc.Station)) { stPos = sPos; break; }
            DVec3 pos   = stPos + pc.Offset;
            DVec3 del   = pos - camPos;
            float shipDistance = (float)(pos - shipPos).Length;
            var   contact = new RadarContact(
                pc.Id, pc.Name,
                new Vector3((float)del.X, (float)del.Y, (float)del.Z),
                Vector3.Zero, ContactType.Debris, shipDistance);
            _targeting.OnContactUpdated(contact);
            _cockpitUI.NotifyRadarContact(contact);
            _radarContactIds.Add(pc.Id);
        }
    }

    // Computes pad world position from current station orbit + orientation, then
    // publishes Docking.* topics to the Instruments bus.
    private void UpdatePadTargetPosition()
    {
        if (!_targeting.HasPadTarget)
        {
            DataBus.ScalarTelemetry.Publish(Topics.Docking.PadTargeted, 0.0);
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
        DataBus.ScalarTelemetry.Publish(Topics.Docking.PadTargeted,   1.0);
        DataBus.ScalarTelemetry.Publish(Topics.Docking.PadDistance,   _padDistance);
        DataBus.VectorTelemetry.Publish(Topics.Docking.PadDirection, _padDirection);
        DataBus.ScalarTelemetry.Publish(Topics.Docking.PadSizeClass,  pad.PadSize == Galaxy.PadSize.Large ? 1.0 : 0.0);
    }

    private void RequestStationProximityDiagnostic()
    {
        var snap = _frameShipSnap;
        var sim = _simulation.LastStationProximityTickDiagnostic;

        Galaxy.Station? targetStation = null;
        DVec3 displayedTargetGalaxyPos = DVec3.Zero;
        double displayedTargetDistance = double.NaN;

        if (_targeting.CurrentRadarTarget is { Type: ContactType.Station } contact)
        {
            displayedTargetDistance = contact.EffectiveShipDistanceMeters;
            string stationName = contact.DisplayName;
            foreach (var (station, pos) in _stationPositions)
            {
                if (!string.Equals(station.Name, stationName, StringComparison.Ordinal)) continue;
                targetStation = station;
                displayedTargetGalaxyPos = pos;
                break;
            }
        }

        double snapshotTime = snap?.SimTime ?? _gameTimeSeconds;
        DVec3 targetEclipticAtSnapshot = targetStation != null
            ? _system.GetStationPosition(targetStation, snapshotTime)
            : DVec3.Zero;
        DVec3 targetGalaxyAtSnapshot = targetStation != null
            ? EclipticToGalaxy(targetEclipticAtSnapshot)
            : DVec3.Zero;
        DVec3 cameraPos = _camera.UniversePosition;
        DVec3? shipSnapshotPos = snap?.Position;
        double snapshotShipToStationDistance = targetStation != null && shipSnapshotPos.HasValue
            ? (targetGalaxyAtSnapshot - shipSnapshotPos.Value).Length
            : double.NaN;

        bool sameTick = snap != null && sim != null && snap.TickSequence == sim.TickSequence;
        bool sameStationReference = targetStation != null && sim?.NearestStation != null
            && ReferenceEquals(targetStation, sim.NearestStation);
        bool sameStationName = targetStation?.Name != null && sim?.NearestStationName != null
            && string.Equals(targetStation.Name, sim.NearestStationName, StringComparison.Ordinal);
        bool sameStationId = targetStation?.PersistenceId != null && sim?.NearestStationId != null
            && string.Equals(targetStation.PersistenceId, sim.NearestStationId, StringComparison.Ordinal);
        DVec3 stationDelta = sim != null
            ? targetGalaxyAtSnapshot - sim.StationGalaxyPosition
            : DVec3.Zero;
        DVec3 shipDelta = snap != null && sim != null
            ? snap.Position - sim.SnapshotShipPosition
            : DVec3.Zero;

        string classification;
        if (snap == null)
            classification = "NO_MAIN_SHIP_SNAPSHOT";
        else if (sim == null)
            classification = "NO_SIM_TICK_DIAGNOSTIC";
        else if (!sameTick)
            classification = "NO_MATCHING_SIM_TICK_FOR_MAIN_SNAPSHOT";
        else if (!sameStationReference && !sameStationName && !sameStationId)
            classification = "DIFFERENT_STATION_IDENTITY";
        else if (stationDelta.Length > 1.0)
            classification = "SAME_TICK_DIFFERENT_STATION_POSITION";
        else if (shipDelta.Length > 1.0)
            classification = "SAME_TICK_DIFFERENT_SHIP_POSITION";
        else
            classification = "SAME_TICK_COHERENT";

        static string V(DVec3 v) => $"({v.X:R}, {v.Y:R}, {v.Z:R}) |len|={v.Length:R}";
        static string MaybeV(DVec3? v) => v.HasValue ? V(v.Value) : "<null>";
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "station_proximity_coherent.log");
        string text =
            "=== Coherent station proximity diagnostic ===\n" +
            $"requestedUtc={DateTime.UtcNow:O}\n" +
            $"classification={classification}\n\n" +
            "[Main current snapshot]\n" +
            $"snapshotTick={(snap != null ? snap.TickSequence.ToString() : "<null>")}\n" +
            $"snapshotSimTime={(snap != null ? snap.SimTime.ToString("R") : "<null>")}\n" +
            $"snapshotShipPosition={MaybeV(shipSnapshotPos)}\n" +
            $"targetName={targetStation?.Name ?? "<none>"}\n" +
            $"targetId={targetStation?.PersistenceId ?? "<null>"}\n" +
            $"targetEclipticAtSnapshot={V(targetEclipticAtSnapshot)}\n" +
            $"targetGalaxyAtSnapshot={V(targetGalaxyAtSnapshot)}\n" +
            $"displayedTargetGalaxyCached={V(displayedTargetGalaxyPos)}\n" +
            $"displayedTargetDistance={displayedTargetDistance:R}\n" +
            $"snapshotShipToStationDistance={snapshotShipToStationDistance:R}\n" +
            $"cameraUniverse={V(cameraPos)}\n\n" +
            "[Sim same-tick calculation]\n" +
            $"tick={(sim != null ? sim.TickSequence.ToString() : "<null>")}\n" +
            $"environmentSimTime={(sim != null ? sim.EnvironmentSimTime.ToString("R") : "<null>")}\n" +
            $"environmentShipPosition={(sim != null ? V(sim.EnvironmentShipPosition) : "<null>")}\n" +
            $"nearestName={sim?.NearestStationName ?? "<none>"}\n" +
            $"nearestId={sim?.NearestStationId ?? "<null>"}\n" +
            $"stationEcliptic={(sim != null ? V(sim.StationEclipticPosition) : "<null>")}\n" +
            $"stationGalaxy={(sim != null ? V(sim.StationGalaxyPosition) : "<null>")}\n" +
            $"rawCentreDistance={(sim != null ? sim.RawCentreDistance.ToString("R") : "<null>")}\n" +
            $"physicalRadius={(sim != null ? sim.PhysicalRadius.ToString("R") : "<null>")}\n" +
            $"surfaceDistance={(sim != null ? sim.SurfaceDistance.ToString("R") : "<null>")}\n" +
            $"publishedLkmZone={(sim != null ? sim.PublishedLkmZone.ToString() : "<null>")}\n" +
            $"publishedMaxGearIndex={(sim != null && sim.PublishedMaxGearIndex != int.MaxValue ? sim.PublishedMaxGearIndex.ToString() : "<none>")}\n" +
            $"snapshotSimTime={(sim != null ? sim.SnapshotSimTime.ToString("R") : "<null>")}\n" +
            $"snapshotShipPosition={(sim != null ? V(sim.SnapshotShipPosition) : "<null>")}\n" +
            $"shipMovementDuringTick={(sim != null ? V(sim.ShipMovementDuringTick) : "<null>")}\n" +
            $"publishedFlightMode={(sim != null ? sim.PublishedFlightMode.ToString() : "<null>")}\n\n" +
            "[Direct same-tick comparison]\n" +
            $"sameTick={sameTick}\n" +
            $"sameStationReference={sameStationReference}\n" +
            $"sameStationName={sameStationName}\n" +
            $"sameStationId={sameStationId}\n" +
            $"mainStationAtSnapshotMinusSimStation={V(stationDelta)}\n" +
            $"mainSnapshotShipMinusSimSnapshotShip={V(shipDelta)}\n" +
            "============================================\n\n";

        System.IO.File.AppendAllText(path, text);

        DataBus.SystemMessages.Publish(Topics.System.All,
            new SystemMessage($"Coherent station proximity diagnostic written: {path}", SystemMessagePriority.Info));
    }
}
