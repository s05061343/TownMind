using System.IO;

namespace AgePilot.Core.Configuration;

public sealed class AppSettings
{
    public string HudProfilePath { get; set; } = "config/hud/aoe2de-zh-tw-2560x1440-50.json";
    public double OverlayOpacity { get; set; } = 0.93;
    public int ScanIntervalMilliseconds { get; set; } = 500;
    public bool EnableSessionRecording { get; set; } = true;
    public bool EnableLocalDiagnostics { get; set; } = true;
    public string AutomationStartHotKey { get; set; } = "Ctrl+F10";
    public string AutomationStopHotKey { get; set; } = "Ctrl+F12";
    public string VillagerProductionSequence { get; set; } = "H,Q";
    public bool EnableMilitaryAutomation { get; set; }
    public string BarracksProductionSequence { get; set; } = string.Empty;
    public string ArcheryRangeProductionSequence { get; set; } = string.Empty;
    public string StableProductionSequence { get; set; } = string.Empty;
    public int EconomyActionIntervalMilliseconds { get; set; } = 5000;
    public int MilitaryActionIntervalMilliseconds { get; set; } = 8000;
    public string IdleVillagerSelectionSequence { get; set; } = ".";
    public string HouseBuildSequence { get; set; } = "B,Q";
    public string MarketBuildSequence { get; set; } = "B,D";
    public string BlacksmithBuildSequence { get; set; } = "B,S";
    public string FeudalUpgradeSequence { get; set; } = "H,Z";
    public string CastleUpgradeSequence { get; set; } = "H,X";
    public int StrategicActionIntervalMilliseconds { get; set; } = 12000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(HudProfilePath)) throw new InvalidDataException("HUD profile path is required.");
        if (OverlayOpacity is < 0.4 or > 1) throw new InvalidDataException("Overlay opacity must be between 0.4 and 1.0.");
        if (ScanIntervalMilliseconds is < 250 or > 5000) throw new InvalidDataException("Scan interval must be between 250 and 5000 milliseconds.");
        _ = Automation.InputSequence.ParseHotKey(AutomationStartHotKey);
        _ = Automation.InputSequence.ParseHotKey(AutomationStopHotKey);
        if (AutomationStartHotKey.Equals(AutomationStopHotKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("自動操作的開啟與關閉熱鍵不可相同。");
        _ = Automation.InputSequence.Parse(VillagerProductionSequence);
        _ = Automation.InputSequence.Parse(BarracksProductionSequence, allowEmpty: true);
        _ = Automation.InputSequence.Parse(ArcheryRangeProductionSequence, allowEmpty: true);
        _ = Automation.InputSequence.Parse(StableProductionSequence, allowEmpty: true);
        _ = Automation.InputSequence.Parse(IdleVillagerSelectionSequence);
        _ = Automation.InputSequence.Parse(HouseBuildSequence);
        _ = Automation.InputSequence.Parse(MarketBuildSequence);
        _ = Automation.InputSequence.Parse(BlacksmithBuildSequence);
        _ = Automation.InputSequence.Parse(FeudalUpgradeSequence);
        _ = Automation.InputSequence.Parse(CastleUpgradeSequence);
        if (EconomyActionIntervalMilliseconds is < 1000 or > 60000)
            throw new InvalidDataException("經濟操作間隔必須介於 1000 到 60000 毫秒。");
        if (MilitaryActionIntervalMilliseconds is < 1000 or > 60000)
            throw new InvalidDataException("軍事操作間隔必須介於 1000 到 60000 毫秒。");
        if (StrategicActionIntervalMilliseconds is < 1000 or > 60000)
            throw new InvalidDataException("策略操作間隔必須介於 1000 到 60000 毫秒。");
    }
}
