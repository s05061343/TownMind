using System.IO;

namespace AgePilot.Core.Configuration;

public sealed class AppSettings
{
    public string HudProfilePath { get; set; } = "config/hud/aoe2de-zh-tw-2560x1440-50.json";
    public double OverlayOpacity { get; set; } = 0.93;
    public int ScanIntervalMilliseconds { get; set; } = 500;
    public bool EnableSessionRecording { get; set; } = true;
    public bool EnableLocalDiagnostics { get; set; } = true;
    public bool EnableAutomationInput { get; set; }
    public string GameHotKeyProfilePath { get; set; } = "config/game-hotkeys/aoe2de-tom.json";
    /// <summary>
    /// 舊版的第二層鍵盤授權。Overlay 的「啟用」現在就是本次 Session 的明確授權；
    /// 保留此欄位只為相容既有 settings.json，不再作為執行 Gate。
    /// </summary>
    public bool EnableGameKeyboardInput { get; set; }
    public GameAge TargetAge { get; set; } = GameAge.Castle;
    public string AutomationStartHotKey { get; set; } = "Ctrl+F10";
    public string AutomationStopHotKey { get; set; } = "Ctrl+F12";
    public bool EnableLocalPlanning { get; set; } = true;
    public string LlmModelPath { get; set; } = "models/Qwen3VL-8B-Instruct-Q4_K_M.gguf";
    public string VisionProjectorPath { get; set; } = "models/mmproj-Qwen3VL-8B-Instruct-Q8_0.gguf";
    public string LlamaRuntimePath { get; set; } = ".runtime/llama.cpp";
    public string LlmBackend { get; set; } = "auto";
    public int LlmPort { get; set; } = 18080;
    public int LlmContextSize { get; set; } = 8192;
    public int LlmGpuLayers { get; set; } = 99;
    public int LlmPlanningTimeoutSeconds { get; set; } = 30;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(HudProfilePath)) throw new InvalidDataException("HUD profile path is required.");
        if (OverlayOpacity is < 0.4 or > 1) throw new InvalidDataException("Overlay opacity must be between 0.4 and 1.0.");
        if (ScanIntervalMilliseconds is < 250 or > 5000) throw new InvalidDataException("Scan interval must be between 250 and 5000 milliseconds.");
        _ = Automation.GlobalHotKeyParser.Parse(AutomationStartHotKey);
        _ = Automation.GlobalHotKeyParser.Parse(AutomationStopHotKey);
        if (AutomationStartHotKey.Equals(AutomationStopHotKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("自動操作的啟用與停止熱鍵不可相同。");
        if (EnableLocalPlanning && (string.IsNullOrWhiteSpace(LlmModelPath) || string.IsNullOrWhiteSpace(VisionProjectorPath) || string.IsNullOrWhiteSpace(LlamaRuntimePath)))
            throw new InvalidDataException("本機規劃需要模型、mmproj 與 llama.cpp runtime 路徑。");
        if (TargetAge == GameAge.Dark) throw new InvalidDataException("目標時代必須是封建、城堡或帝王時代。");
        if (string.IsNullOrWhiteSpace(GameHotKeyProfilePath))
            throw new InvalidDataException("必須指定遊戲快捷鍵設定檔路徑。");
        if (LlmBackend is not ("auto" or "hip" or "vulkan"))
            throw new InvalidDataException("LLM backend 必須是 auto、hip 或 vulkan。");
        if (LlmPort is < 1024 or > 65535) throw new InvalidDataException("LLM port 必須介於 1024 與 65535。");
        if (LlmContextSize is < 2048 or > 32768) throw new InvalidDataException("LLM context size 超出支援範圍。");
        if (LlmPlanningTimeoutSeconds is < 5 or > 120) throw new InvalidDataException("LLM 規劃逾時必須介於 5 與 120 秒。");
    }
}
