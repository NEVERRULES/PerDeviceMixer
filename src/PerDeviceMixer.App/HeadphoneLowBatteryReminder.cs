namespace PerDeviceMixer.App;

internal sealed record HeadphoneLowBatteryAlert(string Title, string Message);

internal sealed class HeadphoneLowBatteryReminder
{
    internal const int ThresholdPercent = 20;

    private readonly HashSet<string> _notifiedComponents =
        new(StringComparer.Ordinal);

    public HeadphoneLowBatteryAlert? Evaluate(HeadphoneBatteryStatus status)
    {
        if (!status.IsSupportedDeviceActive || status.Battery is null)
        {
            return null;
        }

        var battery = status.Battery;
        // Missing broadcasts, reconnects and coarse percentage fluctuations are not charging.
        RearmComponent("left", battery.LeftBatteryPercent, battery.LeftCharging);
        RearmComponent("right", battery.RightBatteryPercent, battery.RightCharging);
        RearmComponent("case", battery.CaseBatteryPercent, battery.CaseCharging);
        if (_notifiedComponents.Count != 0) return null;

        var newlyLow = new List<string>();
        EvaluateComponent("left", "左耳", battery.LeftBatteryPercent, battery.LeftCharging, newlyLow);
        EvaluateComponent("right", "右耳", battery.RightBatteryPercent, battery.RightCharging, newlyLow);
        EvaluateComponent("case", "充电盒", battery.CaseBatteryPercent, battery.CaseCharging, newlyLow);

        if (newlyLow.Count == 0) return null;
        return new HeadphoneLowBatteryAlert(
            "Beats Fit Pro 电量低",
            string.Join("，", newlyLow) + "，请及时充电。");
    }

    private void EvaluateComponent(
        string key,
        string displayName,
        int? percent,
        bool charging,
        List<string> newlyLow)
    {
        if (!percent.HasValue || percent.Value > ThresholdPercent) return;

        if (charging || !_notifiedComponents.Add(key)) return;
        newlyLow.Add($"{displayName}仅剩 {percent.Value}%");
    }

    private void RearmComponent(string key, int? percent, bool charging)
    {
        // Only valid charging observations for the alerted components end this cycle.
        if (percent.HasValue && charging) _notifiedComponents.Remove(key);
    }
}
