using Inferior.Core.DataBus;
using Microsoft.Xna.Framework;
using Xunit;

namespace Inferior.Game.Test;

public sealed class RadarContactTests
{
    [Fact]
    public void EffectiveShipDistance_UsesShipTruthWithoutChangingCameraRelativePosition()
    {
        var contact = new RadarContact(
            "station:test",
            "Test Station",
            new Vector3(0f, 0f, 85f),
            Vector3.Zero,
            ContactType.Station,
            ShipDistanceMeters: 6500f);

        Assert.Equal(new Vector3(0f, 0f, 85f), contact.RelativePosition);
        Assert.Equal(6500f, contact.EffectiveShipDistanceMeters);
    }

    [Fact]
    public void EffectiveShipDistance_FallsBackForLegacyContacts()
    {
        var contact = new RadarContact(
            "legacy:test",
            "Legacy Contact",
            new Vector3(3f, 4f, 0f),
            Vector3.Zero,
            ContactType.Unknown);

        Assert.Equal(5f, contact.EffectiveShipDistanceMeters);
    }
}
