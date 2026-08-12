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
            var request = new
            {
                model = "agepilot-local",
                temperature = 0.2,
                max_tokens = 512,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = JsonSerializer.Serialize(ToPromptContext(context), JsonOptions) },
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
            var action = new PlannedAction(dto.NextAction.Intent, Math.Max(50, dto.NextAction.Priority), dto.NextAction.Reason,
                dto.NextAction.Quantity, dto.NextAction.TargetPopulationCap,
                dto.NextAction.TargetFoodWorkers, dto.NextAction.TargetWoodWorkers,
                dto.NextAction.TargetGoldWorkers, dto.NextAction.TargetStoneWorkers,
                dto.NextAction.TargetResourceAmount, dto.NextAction.RecheckSeconds,
                dto.NextAction.SuccessCondition, [], []);
            action = NormalizeQuantities(action, context.State, context.Map);
            var plan = new GamePlan(Guid.NewGuid().ToString("N"), now, now.AddSeconds(60),
                dto.Strategy, dto.CurrentGoal, dto.Reason, dto.Confidence,
                [], [], [action]);
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
        var runtime = Resolve(_settings.LlamaRuntimePath);
        if (!File.Exists(model))
        {
            SetStatus(PlannerRuntimePhase.NotConfigured, $"找不到模型：{model}");
            throw new FileNotFoundException("找不到本機 LLM 模型", model);
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
                    Arguments = $"-m \"{model}\" --host 127.0.0.1 --port {_settings.LlmPort} -c {_settings.LlmContextSize} --n-gpu-layers {_settings.LlmGpuLayers} --cache-ram 0 --alias agepilot-local --jinja -np 1 --device {device}",
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
你是 AOE2 DE 的冷靜本機策略規劃器。只根據提供的結構化觀測，為未來 1 至 2 分鐘規劃經濟、住房、採集、升封建/城堡與地圖策略。未知資料不得猜測。不要輸出思考過程。嚴格依指定 JSON schema 輸出策略、目標、原因、信心與一個最高優先度下一步。下一步必須包含具體數量、人口上限目標、食物/木材/黃金/石頭的村民目標配置、資源存量檢查點、重新評估秒數與可觀察的完成條件；不適用的數值填 0。資源人數是目標配置，不得宣稱是目前實測人數。不得輸出座標、按鍵、軍事命令或 Markdown。
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
                ["required"] = new[] { "strategy", "currentGoal", "reason", "confidence", "nextAction" },
                ["properties"] = new Dictionary<string, object>
                {
                    ["strategy"] = new { type = "string" },
                    ["currentGoal"] = new { type = "string" },
                    ["reason"] = new { type = "string" },
                    ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
                    ["nextAction"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new[] { "intent", "priority", "reason", "quantity", "targetPopulationCap",
                            "targetFoodWorkers", "targetWoodWorkers", "targetGoldWorkers", "targetStoneWorkers",
                            "targetResourceAmount", "recheckSeconds", "successCondition" },
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["intent"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = Enum.GetNames<PlannedActionKind>(),
                            },
                            ["priority"] = new { type = "integer", minimum = 0, maximum = 100 },
                            ["reason"] = new { type = "string" },
                            ["quantity"] = new { type = "integer", minimum = 0, maximum = 20 },
                            ["targetPopulationCap"] = new { type = "integer", minimum = 0, maximum = 500 },
                            ["targetFoodWorkers"] = new { type = "integer", minimum = 0, maximum = 200 },
                            ["targetWoodWorkers"] = new { type = "integer", minimum = 0, maximum = 200 },
                            ["targetGoldWorkers"] = new { type = "integer", minimum = 0, maximum = 200 },
                            ["targetStoneWorkers"] = new { type = "integer", minimum = 0, maximum = 200 },
                            ["targetResourceAmount"] = new { type = "integer", minimum = 0, maximum = 100000 },
                            ["recheckSeconds"] = new { type = "integer", minimum = 5, maximum = 120 },
                            ["successCondition"] = new { type = "string" },
                        },
                    },
                },
            },
        },
    };

    private sealed record CompletionEnvelope(IReadOnlyList<CompletionChoice> Choices);
    private sealed record CompletionChoice(CompletionMessage Message);
    private sealed record CompletionMessage(string Content);
    private sealed record PlanDto(string Strategy, string CurrentGoal, string Reason, double Confidence, NextActionDto NextAction);
    private sealed record NextActionDto(
        PlannedActionKind Intent,
        int Priority,
        string Reason,
        int Quantity,
        int TargetPopulationCap,
        int TargetFoodWorkers,
        int TargetWoodWorkers,
        int TargetGoldWorkers,
        int TargetStoneWorkers,
        int TargetResourceAmount,
        int RecheckSeconds,
        string SuccessCondition);
}
