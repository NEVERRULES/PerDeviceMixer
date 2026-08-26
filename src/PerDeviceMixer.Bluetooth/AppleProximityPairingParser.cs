namespace PerDeviceMixer.Bluetooth;

public static class AppleProximityPairingParser
{
    public const int PacketLength = 27;
    public const byte MessageType = 0x07;
    public const byte RemainingLength = PacketLength - 2;

    public static bool TryParse(
        ReadOnlySpan<byte> manufacturerData,
        short signalStrengthDbm,
        DateTimeOffset observedAtUtc,
        out HeadphoneBatteryState? state)
    {
        state = null;
        if (manufacturerData.Length != PacketLength ||
            manufacturerData[0] != MessageType ||
            manufacturerData[1] != RemainingLength)
        {
            return false;
        }

        // Apple stores the product identifier little-endian in this packet.
        // Beats Fit Pro is broadcast as bytes 12 20 and is exposed by Windows
        // Plug and Play as PID 2012.
        var productId = (ushort)(manufacturerData[3] | (manufacturerData[4] << 8));
        if (productId != AppleHeadphoneIds.BeatsFitProProductId) return false;

        var status = manufacturerData[5];
        var broadcastingSide = (status & 0x20) != 0
            ? HeadphoneSide.Left
            : HeadphoneSide.Right;

        var batteryByte = manufacturerData[6];
        var currentPodBattery = DecodeBattery(batteryByte & 0x0F);
        var otherPodBattery = DecodeBattery((batteryByte >> 4) & 0x0F);

        var caseAndCharging = manufacturerData[7];
        // The low nibble is the case battery. The high nibble contains the
        // charging flags; treating these halves in reverse turns an unavailable
        // case value (0xF) into false charging flags and a made-up case level.
        var caseBattery = DecodeBattery(caseAndCharging & 0x0F);
        var chargingFlags = (caseAndCharging >> 4) & 0x0F;
        // The charging bits follow the same current/other orientation as the
        // battery nibbles for Beats Fit Pro: bit 0 is the broadcasting pod and
        // bit 1 is the other pod. This is intentionally resolved through the
        // status flip below instead of treating the bits as fixed left/right.
        var currentPodCharging = (chargingFlags & 0x01) != 0;
        var otherPodCharging = (chargingFlags & 0x02) != 0;

        var leftBattery = broadcastingSide == HeadphoneSide.Left
            ? currentPodBattery
            : otherPodBattery;
        var rightBattery = broadcastingSide == HeadphoneSide.Right
            ? currentPodBattery
            : otherPodBattery;
        var leftCharging = broadcastingSide == HeadphoneSide.Left
            ? currentPodCharging
            : otherPodCharging;
        var rightCharging = broadcastingSide == HeadphoneSide.Right
            ? currentPodCharging
            : otherPodCharging;

        state = new HeadphoneBatteryState(
            productId,
            "Beats Fit Pro",
            leftBattery,
            rightBattery,
            caseBattery,
            leftCharging,
            rightCharging,
            (chargingFlags & 0x04) != 0,
            broadcastingSide,
            signalStrengthDbm,
            observedAtUtc);
        return true;
    }

    private static int? DecodeBattery(int value) => value is >= 0 and <= 10
        ? value * 10
        : null;
}
