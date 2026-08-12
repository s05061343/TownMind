using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgePilot.Core.Configuration;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Diagnostics;

namespace AgePilot.Infrastructure.Planning;

public sealed class LlamaServerPlanner : IStrategicPlanner, IPlannerRuntimeStatusSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly AppSettings _settings;
    private readonly LocalJsonLineLogger? _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private string? _backend;
    private string? _device;
    private readonly Queue<string> _serverMessages = new();
    private PlannerRuntimeStatus _runtimeStatus = PlannerRuntimeStatus.NotConfigured();

    public PlannerRuntimeStatus RuntimeStatus => _runtimeStatus;

    public LlamaServerPlanner(AppSettings settings, LocalJsonLineLogger? logger = null)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{settings.LlmPort}/") };
    }

    public async Task<PlanningResult> PlanAsync(SituationContext context, CancellationToken cancellationToken)
    {
        if (!_settings.EnableLocalPlanning)
        {
            SetStatus(PlannerRuntimePhase.NotConfigured, "本機規劃已停用");
            return new(null, "本機規劃已停用");
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAsync(cancellationToken);
            SetStatus(PlannerRuntimePhase.Planning, $"LLM 正在產生戰局計畫（{FormatBackend()}）", FormatBackend());
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_settings.LlmPlanningTimeoutSeconds));
            var started = Stopwatch.StartNew();
            var userContent = new List<object>();
            if (context.Visual is { } visual)
            {
                userContent.AddRange(visual.Images.Select(image => (object)new
                {
                    type = "image_url",
                    image_url = new { url = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Data)}" },
                }));
            }
            userContent.Add(new { type = "text", text = JsonSerializer.Serialize(ToPromptContext(context), JsonOptions) });
            var request = new
            {
                model = "agepilot-local",
                temperature = 0.2,
                max_tokens = 1024,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = userContent.ToArray() },
                },
                response_format = ResponseFormat,
            };
            using var response = await _http.PostAsJsonAsync("v1/chat/completions", request, JsonOptions, timeout.Token);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<CompletionEnvelope>(JsonOptions, timeout.Token)
                ?? throw new InvalidDataException("模型回覆為空");
            var content = envelope.Choices.FirstOrDefault()?.Message.Content;
            var dto = JsonSerializer.Deserialize<PlanDto>(content ?? "", JsonOptions)
                ?? throw new InvalidDataException("模型未回傳有效 JSON 計畫");
            var now = DateTimeOffset.UtcNow;
            var toolAction = new VisualToolAction(dto.Action.Tool, dto.Action.Space, dto.Action.Target ?? "", dto.Action.X, dto.Action.Y,
                dto.Action.EndX, dto.Action.EndY, dto.Action.Row, dto.Action.Column);
            var decision = new VisualPlayerDecision(dto.Assessment, dto.Goal, dto.Reason, toolAction,
                dto.ExpectedResult, dto.RecheckAfterMs, dto.Confidence, dto.PreviousActionResult);
            var action = new PlannedAction(PlannedActionKind.Reobserve, 80, dto.Reason,
                RecheckSeconds: Math.Clamp((int)Math.Ceiling(dto.RecheckAfterMs / 1000d), 5, 30),
                SuccessCondition: dto.ExpectedResult);
            var plan = new GamePlan(Guid.NewGuid().ToString("N"), now, now.AddSeconds(60),
                "visual-player", dto.Goal, dto.Reason, dto.Confidence,
                [], [], [action], VisualDecision: decision);
            var validated = GamePlanValidator.Validate(plan, now);
            _logger?.Write("planning.completed", new { backend = _backend, latencyMs = started.ElapsedMilliseconds, validated = validated.Success, validated.Error });
            SetStatus(validated.Success ? PlannerRuntimePhase.Ready : PlannerRuntimePhase.Error,
                validated.Success ? $"LLM 已就緒（{FormatBackend()}）" : $"計畫格式驗證失敗：{validated.Error}", FormatBackend());
            return validated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger?.Write("planning.failure", new { backend = _backend, type = ex.GetType().Name, ex.Message });
            var tail = GetServerMessageTail();
            var message = string.IsNullOrWhiteSpace(tail) ? ex.Message : $"{ex.Message}；llama-server: {tail}";
            SetStatus(PlannerRuntimePhase.Error, message, FormatBackend());
            return new(null, message);
        }
        finally { _gate.Release(); }
    }

    public static PlannedAction NormalizeQuantities(PlannedAction action, AgePilot.Core.GameState state, MapContext? map = null)
    {
        if (action.Intent == PlannedActionKind.BuildHouse)
        {
            var quantity = Math.Max(1, action.Quantity);
            var currentCap = state.PopulationCap?.IsUsable == true ? state.PopulationCap.Value.GetValueOrDefault() : 0;
            if (currentCap > 0)
            {
                var targetCap = currentCap + quantity * 5;
                action = action with
                {
                    Quantity = quantity,
                    TargetPopulationCap = targetCap,
                    SuccessCondition = $"人口上限由 {currentCap} 提高到至少 {targetCap}",
                };
            }
        }

        var workerTotal = action.TargetFoodWorkers + action.TargetWoodWorkers + action.TargetGoldWorkers + action.TargetStoneWorkers;
        var population = state.Population?.IsUsable == true ? state.Population.Value.GetValueOrDefault() : 0;
        var estimatedWorkers = population > 1 ? population - 1 : population;
        if (workerTotal == 0 && estimatedWorkers > 0)
        {
            var ratios = state.Age switch
            {
                AgePilot.Core.GameAge.Feudal => new[] { 0.55, 0.30, 0.15, 0.0 },
                AgePilot.Core.GameAge.Castle or AgePilot.Core.GameAge.Imperial => new[] { 0.45, 0.30, 0.20, 0.05 },
                _ when map?.Archetype is MapArchetype.Island or MapArchetype.Coastal => new[] { 0.55, 0.35, 0.10, 0.0 },
                _ => new[] { 0.60, 0.30, 0.10, 0.0 },
            };
            action = action with
            {
                TargetFoodWorkers = (int)Math.Round(estimatedWorkers * ratios[0]),
                TargetWoodWorkers = (int)Math.Round(estimatedWorkers * ratios[1]),
                TargetGoldWorkers = (int)Math.Round(estimatedWorkers * ratios[2]),
                TargetStoneWorkers = (int)Math.Round(estimatedWorkers * ratios[3]),
            };
            workerTotal = action.TargetFoodWorkers + action.TargetWoodWorkers + action.TargetGoldWorkers + action.TargetStoneWorkers;
        }
        if (workerTotal > 0 && estimatedWorkers > 0 && workerTotal != estimatedWorkers)
        {
            var raw = new[] { action.TargetFoodWorkers, action.TargetWoodWorkers, action.TargetGoldWorkers, action.TargetStoneWorkers };
            var scaled = raw.Select(value => value * estimatedWorkers / (double)workerTotal).ToArray();
            var targets = scaled.Select(value => (int)Math.Floor(value)).ToArray();
            for (var remaining = estimatedWorkers - targets.Sum(); remaining > 0; remaining--)
            {
                var index = Enumerable.Range(0, targets.Length)
                    .OrderByDescending(i => scaled[i] - targets[i])
                    .ThenBy(i => i)
                    .First();
                targets[index]++;
            }
            action = action with
            {
                TargetFoodWorkers = targets[0], TargetWoodWorkers = targets[1],
                TargetGoldWorkers = targets[2], TargetStoneWorkers = targets[3],
            };
        }
        return action;
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && await IsHealthyAsync(cancellationToken))
        {
            SetStatus(PlannerRuntimePhase.Ready, $"LLM 已就緒（{FormatBackend()}）", FormatBackend());
            return;
        }
        var model = Resolve(_settings.LlmModelPath);
        var mmproj = Resolve(_settings.VisionProjectorPath);
        var runtime = Resolve(_settings.LlamaRuntimePath);
        if (!File.Exists(model))
        {
            SetStatus(PlannerRuntimePhase.NotConfigured, $"找不到模型：{model}");
            throw new FileNotFoundException("找不到本機 LLM 模型", model);
        }
        if (!File.Exists(mmproj))
        {
            SetStatus(PlannerRuntimePhase.NotConfigured, $"找不到視覺編碼器：{mmproj}");
            throw new FileNotFoundException("找不到本機 VLM mmproj", mmproj);
        }
        SetStatus(PlannerRuntimePhase.Starting, "正在啟動 llama-server");
        var backends = _settings.LlmBackend == "auto" ? new[] { "hip", "vulkan" } : new[] { _settings.LlmBackend };
        var failures = new List<string>();
        foreach (var backend in backends)
        {
            var executable = Path.Combine(runtime, backend, "llama-server.exe");
            if (!File.Exists(executable)) { failures.Add($"{backend}: executable missing"); continue; }
            try
            {
                var environmentPath = BuildBackendPath(backend);
                var device = await DetectDeviceAsync(executable, backend, environmentPath, cancellationToken);
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    Arguments = $"-m \"{model}\" --mmproj \"{mmproj}\" --mmproj-offload --image-min-tokens 1024 --image-max-tokens 1024 --host 127.0.0.1 --port {_settings.LlmPort} -c {_settings.LlmContextSize} --n-gpu-layers {_settings.LlmGpuLayers} --cache-ram 0 --alias agepilot-local --jinja -np 1 --device {device}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                startInfo.Environment["PATH"] = environmentPath;
                _process = Process.Start(startInfo);
                if (_process is null) throw new InvalidOperationException("無法啟動 llama-server");
                _process.OutputDataReceived += (_, e) => RememberServerMessage(e.Data);
                _process.ErrorDataReceived += (_, e) => RememberServerMessage(e.Data);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _backend = backend;
                _device = device;
                SetStatus(PlannerRuntimePhase.LoadingModel, $"正在以 {backend.ToUpperInvariant()} / {device} 載入模型", FormatBackend());
                var deadline = DateTimeOffset.UtcNow.AddSeconds(180);
                while (DateTimeOffset.UtcNow < deadline && _process is { HasExited: false })
                {
                    if (await IsHealthyAsync(cancellationToken))
                    {
                        SetStatus(PlannerRuntimePhase.Ready, $"LLM 已就緒（{FormatBackend()}）", FormatBackend());
                        return;
                    }
                    await Task.Delay(500, cancellationToken);
                }
                failures.Add($"{backend}: readiness failed");
            }
            catch (Exception ex) { failures.Add($"{backend}: {ex.Message}"); }
            StopProcess();
        }
        var failure = $"沒有可用的 llama.cpp backend：{string.Join("; ", failures)}";
        SetStatus(PlannerRuntimePhase.Error, failure);
        throw new InvalidOperationException(failure);
    }

    private static async Task<string> DetectDeviceAsync(
        string executable, string backend, string environmentPath, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--list-devices",
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["PATH"] = environmentPath;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("無法偵測 LLM GPU device");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdout) + Environment.NewLine + (await stderr);
        var device = ParseGpuDevice(output, backend);
        return device ?? throw new InvalidOperationException($"{backend} 未偵測到 GPU；拒絕 CPU fallback");
    }

    public static string? ParseGpuDevice(string output, string backend)
    {
        var prefix = backend.Equals("vulkan", StringComparison.OrdinalIgnoreCase) ? "Vulkan" : "ROCm";
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && line.Contains(':'))
            .Select(line => line.Split(':', 2)[0])
            .FirstOrDefault();
    }

    private static string BuildBackendPath(string backend)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (!backend.Equals("hip", StringComparison.OrdinalIgnoreCase)) return current;
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable("ROCM_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(Path.Combine(configured, "bin"));
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var root = Path.Combine(programFiles, "AMD", "ROCm");
        if (Directory.Exists(root)) candidates.AddRange(Directory.GetDirectories(root).OrderByDescending(path => path));
        var bin = candidates.Select(path => path.EndsWith("bin", StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path, "bin"))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "amdhip64_7.dll")));
        if (bin is null) throw new InvalidOperationException("找不到 AMD ROCm runtime（amdhip64_7.dll）");
        return current.Contains(bin, StringComparison.OrdinalIgnoreCase) ? current : $"{bin}{Path.PathSeparator}{current}";
    }

    public async Task<PlannerRuntimeStatus> CheckReadyAsync(CancellationToken cancellationToken)
    {
        try { await EnsureStartedAsync(cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus(PlannerRuntimePhase.Error, ex.Message, _backend);
        }
        return RuntimeStatus;
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try { using var response = await _http.GetAsync("health", cancellationToken); return response.StatusCode == HttpStatusCode.OK; }
        catch (HttpRequestException) { return false; }
    }

    private static object ToPromptContext(SituationContext context) => new
    {
        capturedAt = context.CapturedAt,
        state = new
        {
            age = context.State.Age?.ToString(),
            food = Value(context.State.Food), wood = Value(context.State.Wood), gold = Value(context.State.Gold), stone = Value(context.State.Stone),
            population = Value(context.State.Population), populationCap = Value(context.State.PopulationCap),
        },
        history = context.History,
        map = context.Map?.IsUsable == true ? context.Map : null,
        previousPlan = context.PreviousPlan is null ? null : new { context.PreviousPlan.Strategy, context.PreviousPlan.CurrentGoal },
        events = context.RecentEvents,
        visual = context.Visual is null ? null : new
        {
            context.Visual.FrameWidth,
            context.Visual.FrameHeight,
            imageOrder = context.Visual.Images.Select(image => image.Name),
            context.Visual.UiLayout,
            context.Visual.PreviousAction,
            context.Visual.PreviousResult,
        },
    };

    private static object? Value(AgePilot.Core.Observations.ObservedValue<int>? value) => value?.IsUsable == true
        ? new { value.Value, value.Confidence, value.ObservedAt }
        : null;

    private static string Resolve(string path)
    {
        if (Path.IsPathRooted(path)) return path;
        var workingDirectoryPath = Path.GetFullPath(path);
        return File.Exists(workingDirectoryPath) || Directory.Exists(workingDirectoryPath)
            ? workingDirectoryPath
            : Path.GetFullPath(path, AppContext.BaseDirectory);
    }

    private void StopProcess()
    {
        try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose(); _process = null;
    }

    private void RememberServerMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_serverMessages)
        {
            _serverMessages.Enqueue(message);
            while (_serverMessages.Count > 20) _serverMessages.Dequeue();
        }
    }

    private string GetServerMessageTail()
    {
        lock (_serverMessages) return string.Join(" | ", _serverMessages.TakeLast(3));
    }

    private void SetStatus(PlannerRuntimePhase phase, string message, string? backend = null) =>
        _runtimeStatus = new(phase, message, backend, DateTimeOffset.UtcNow);

    private string FormatBackend() => string.IsNullOrWhiteSpace(_backend)
        ? "backend 未確認"
        : string.IsNullOrWhiteSpace(_device) ? _backend.ToUpperInvariant() : $"{_backend.ToUpperInvariant()} / {_device}";

    public void Dispose() { StopProcess(); _http.Dispose(); _gate.Dispose(); }

    private const string SystemPrompt = """
/no_think
你是謹慎的 AOE2 DE 經濟發展玩家。所有遊戲操作只能使用滑鼠，禁止快捷鍵與鍵盤輸入。panorama 是完整遊戲畫面；command_panel 是左下指令面板；minimap 是右下小地圖。每輪只輸出一個原子工具：Observe、Wait、LeftClick、RightClick 或 Drag。世界目標使用 Panorama normalized 座標；小地圖使用 Minimap 局部 normalized 座標；命令按鈕使用 CommandGrid 的 row 1..3、column 1..5，不得猜全畫面小圖示座標。每個輸入動作都必須用 target 簡述畫面上可見的目標證據。選取村民或建築後，下一輪必須從單位資訊與命令面板確認選取結果；未確認前只能 Observe 或 Wait。若 context 有 previousAction，先設定 previousActionResult；無法確認時必須 Uncertain 且不得提出新輸入。紅色建築預覽不可確認。只管理村民、採集、經濟建築、經濟科技與升時代；禁止軍事與戰鬥。若不確定或畫面矛盾，選 Observe 或 Wait。不要輸出 Markdown，嚴格依 JSON schema 回覆。
""";

    private static readonly object ResponseFormat = new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "agepilot_game_plan",
            strict = true,
            schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new[] { "assessment", "goal", "reason", "confidence", "action", "expectedResult", "recheckAfterMs", "previousActionResult" },
                ["properties"] = new Dictionary<string, object>
                {
                    ["assessment"] = new { type = "string" },
                    ["goal"] = new { type = "string" },
                    ["reason"] = new { type = "string" },
                    ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
                    ["expectedResult"] = new { type = "string" },
                    ["recheckAfterMs"] = new { type = "integer", minimum = 250, maximum = 30000 },
                    ["previousActionResult"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = Enum.GetNames<PreviousActionResult>() },
                    ["action"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new[] { "tool", "space", "target", "x", "y", "endX", "endY", "row", "column" },
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["tool"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = Enum.GetNames<VisualToolKind>(),
                            },
                            ["space"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = Enum.GetNames<VisualCoordinateSpace>() },
                            ["target"] = new { type = "string", maxLength = 80 },
                            ["x"] = new { type = "number", minimum = 0, maximum = 1 },
                            ["y"] = new { type = "number", minimum = 0, maximum = 1 },
                            ["endX"] = new { type = "number", minimum = 0, maximum = 1 },
                            ["endY"] = new { type = "number", minimum = 0, maximum = 1 },
                            ["row"] = new { type = "integer", minimum = 0, maximum = 3 },
                            ["column"] = new { type = "integer", minimum = 0, maximum = 5 },
                        },
                    },
                },
            },
        },
    };

    private sealed record CompletionEnvelope(IReadOnlyList<CompletionChoice> Choices);
    private sealed record CompletionChoice(CompletionMessage Message);
    private sealed record CompletionMessage(string Content);
    private sealed record PlanDto(string Assessment, string Goal, string Reason, double Confidence,
        VisualActionDto Action, string ExpectedResult, int RecheckAfterMs, PreviousActionResult PreviousActionResult);
    private sealed record VisualActionDto(VisualToolKind Tool, VisualCoordinateSpace Space, string? Target,
        double X, double Y, double EndX, double EndY, int Row, int Column);
}
