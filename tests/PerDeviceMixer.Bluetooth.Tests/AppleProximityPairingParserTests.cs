using PerDeviceMixer.Bluetooth;

namespace PerDeviceMixer.Bluetooth.Tests;

public sealed class AppleProximityPairingParserTests
{
    [Fact]
    public void ParsesLeftBroadcastPacketAndChargingFlags()
    {
        var packet = CreatePacket(
            status: 0x20,
            podBattery: 0x78,
            caseAndCharging: 0x16);

        var parsed = AppleProximityPairingParser.TryParse(
            packet,
            -45,
            DateTimeOffset.UnixEpoch,
            out var state);

        Assert.True(parsed);
        Assert.NotNull(state);
        Assert.Equal(AppleHeadphoneIds.BeatsFitProProductId, state.ProductId);
        Assert.Equal(HeadphoneSide.Left, state.BroadcastingSide);
        Assert.Equal(80, state.LeftBatteryPercent);
        Assert.Equal(70, state.RightBatteryPercent);
        Assert.Equal(60, state.CaseBatteryPercent);
        Assert.True(state.LeftCharging);
        Assert.False(state.RightCharging);
        Assert.False(state.CaseCharging);
    }

    [Fact]
    public void ParsesRightBroadcastPacketAndChargingFlags()
    {
        var packet = CreatePacket(
            status: 0x00,
            podBattery: 0xA4,
            caseAndCharging: 0x6A);

        var parsed = AppleProximityPairingParser.TryParse(
            packet,
            -51,
            DateTimeOffset.UnixEpoch,
            out var state);

        Assert.True(parsed);
        Assert.NotNull(state);
        Assert.Equal(HeadphoneSide.Right, state.BroadcastingSide);
        Assert.Equal(100, state.LeftBatteryPercent);
        Assert.Equal(40, state.RightBatteryPercent);
        Assert.Equal(100, state.CaseBatteryPercent);
        Assert.True(state.LeftCharging);
        Assert.False(state.RightCharging);
        Assert.True(state.CaseCharging);
    }

    [Fact]
    public void ChargingFlagsFollowTheSameFlipAsPodBatteryNibbles()
    {
        var leftBroadcasting = CreatePacket(
            status: 0x20,
            podBattery: 0x78,
            caseAndCharging: 0x16);
        var rightBroadcasting = CreatePacket(
            status: 0x00,
            podBattery: 0x87,
            caseAndCharging: 0x26);

        Assert.True(AppleProximityPairingParser.TryParse(
            leftBroadcasting, -40, DateTimeOffset.UnixEpoch, out var leftState));
        Assert.True(AppleProximityPairingParser.TryParse(
            rightBroadcasting, -40, DateTimeOffset.UnixEpoch, out var rightState));

        Assert.NotNull(leftState);
        Assert.NotNull(rightState);
        Assert.True(leftState.LeftCharging);
        Assert.False(leftState.RightCharging);
        Assert.True(rightState.LeftCharging);
        Assert.False(rightState.RightCharging);
    }

    [Fact]
    public void ConvertsUnavailableBatteryValuesToNull()
    {
        var packet = CreatePacket(
            status: 0x20,
            podBattery: 0xFB,
            caseAndCharging: 0x0F);

        Assert.True(AppleProximityPairingParser.TryParse(
            packet,
            -40,
            DateTimeOffset.UnixEpoch,
            out var state));
        Assert.NotNull(state);
        Assert.Null(state.LeftBatteryPercent);
        Assert.Null(state.RightBatteryPercent);
        Assert.Null(state.CaseBatteryPercent);
    }

    [Fact]
    public void DoesNotTurnUnavailableCaseIntoChargingComponents()
    {
        var packet = CreatePacket(
            status: 0x20,
            podBattery: 0x88,
            caseAndCharging: 0x8F);

        Assert.True(AppleProximityPairingParser.TryParse(
            packet,
            -40,
            DateTimeOffset.UnixEpoch,
            out var state));
        Assert.NotNull(state);
        Assert.Equal(80, state.LeftBatteryPercent);
        Assert.Equal(80, state.RightBatteryPercent);
        Assert.Null(state.CaseBatteryPercent);
        Assert.False(state.LeftCharging);
        Assert.False(state.RightCharging);
        Assert.False(state.CaseCharging);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    [InlineData(28)]
    public void RejectsPacketsWithUnexpectedLength(int length)
    {
        Assert.False(AppleProximityPairingParser.TryParse(
            new byte[length],
            -40,
            DateTimeOffset.UnixEpoch,
            out var state));
        Assert.Null(state);
    }

    [Fact]
    public void RejectsOtherAppleModels()
    {
        var packet = CreatePacket(0x20, 0x88, 0x80);
        packet[3] = 0x14;
        packet[4] = 0x20;

        Assert.False(AppleProximityPairingParser.TryParse(
            packet,
            -40,
            DateTimeOffset.UnixEpoch,
            out var state));
        Assert.Null(state);
    }

    private static byte[] CreatePacket(byte status, byte podBattery, byte caseAndCharging)
    {
        var packet = new byte[AppleProximityPairingParser.PacketLength];
        packet[0] = AppleProximityPairingParser.MessageType;
        packet[1] = AppleProximityPairingParser.RemainingLength;
        packet[2] = 0x01;
        packet[3] = 0x12;
        packet[4] = 0x20;
        packet[5] = status;
        packet[6] = podBattery;
        packet[7] = caseAndCharging;
        return packet;
    }
}
