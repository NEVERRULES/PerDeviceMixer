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
            _notifiedComponents.Clear();
            return null;
        }

        var battery = status.Battery;
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
        if (!percent.HasValue || percent.Value > ThresholdPercent)
        {
            _notifiedComponents.Remove(key);
            return;
        }

        if (charging || !_notifiedComponents.Add(key)) return;
        newlyLow.Add($"{displayName}仅剩 {percent.Value}%");
    }
}
