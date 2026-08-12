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
            SetStatus(PlannerRuntimePhase.Planning, "LLM 正在產生戰局計畫", _backend);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_settings.LlmPlanningTimeoutSeconds));
            var started = Stopwatch.StartNew();
            var request = new
            {
                model = "agepilot-local",
                temperature = 0.2,
                max_tokens = 256,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = JsonSerializer.Serialize(ToPromptContext(context), JsonOptions) },
                },
                response_format = new { type = "json_object" },
            };
            using var response = await _http.PostAsJsonAsync("v1/chat/completions", request, JsonOptions, timeout.Token);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<CompletionEnvelope>(JsonOptions, timeout.Token)
                ?? throw new InvalidDataException("模型回覆為空");
            var content = envelope.Choices.FirstOrDefault()?.Message.Content;
            var dto = JsonSerializer.Deserialize<PlanDto>(content ?? "", JsonOptions)
                ?? throw new InvalidDataException("模型未回傳有效 JSON 計畫");
            var now = DateTimeOffset.UtcNow;
            var plan = new GamePlan(Guid.NewGuid().ToString("N"), now, now.AddSeconds(60),
                dto.Strategy, dto.CurrentGoal, dto.Reason, dto.Confidence,
                dto.Assumptions ?? [], dto.MissingInformation ?? [], dto.Actions ?? []);
            var validated = GamePlanValidator.Validate(plan, now);
            _logger?.Write("planning.completed", new { backend = _backend, latencyMs = started.ElapsedMilliseconds, validated = validated.Success, validated.Error });
            SetStatus(validated.Success ? PlannerRuntimePhase.Ready : PlannerRuntimePhase.Error,
                validated.Success ? "LLM 已就緒" : $"計畫格式驗證失敗：{validated.Error}", _backend);
            return validated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger?.Write("planning.failure", new { backend = _backend, type = ex.GetType().Name, ex.Message });
            var tail = GetServerMessageTail();
            var message = string.IsNullOrWhiteSpace(tail) ? ex.Message : $"{ex.Message}；llama-server: {tail}";
            SetStatus(PlannerRuntimePhase.Error, message, _backend);
            return new(null, message);
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && await IsHealthyAsync(cancellationToken))
        {
            SetStatus(PlannerRuntimePhase.Ready, "LLM 已就緒", _backend);
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
                _process = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    Arguments = $"-m \"{model}\" --host 127.0.0.1 --port {_settings.LlmPort} -c {_settings.LlmContextSize} --n-gpu-layers {_settings.LlmGpuLayers} --cache-ram 0 --alias agepilot-local --jinja -np 1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (_process is null) throw new InvalidOperationException("無法啟動 llama-server");
                _process.OutputDataReceived += (_, e) => RememberServerMessage(e.Data);
                _process.ErrorDataReceived += (_, e) => RememberServerMessage(e.Data);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _backend = backend;
                SetStatus(PlannerRuntimePhase.LoadingModel, $"正在以 {backend.ToUpperInvariant()} 載入模型", backend);
                var deadline = DateTimeOffset.UtcNow.AddSeconds(180);
                while (DateTimeOffset.UtcNow < deadline && _process is { HasExited: false })
                {
                    if (await IsHealthyAsync(cancellationToken))
                    {
                        SetStatus(PlannerRuntimePhase.Ready, $"LLM 已就緒（{backend.ToUpperInvariant()}）", backend);
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

    public void Dispose() { StopProcess(); _http.Dispose(); _gate.Dispose(); }

    private const string SystemPrompt = """
/no_think
你是 AOE2 DE 的冷靜本機策略規劃器。只根據提供的結構化觀測，為未來 1 至 2 分鐘規劃經濟、住房、採集、升封建/城堡與地圖策略。未知資料不得猜測。不要輸出思考過程。輸出單一 JSON object，欄位為 strategy,currentGoal,reason,confidence,assumptions,missingInformation,actions。assumptions、missingInformation、actions、preconditions、completionConditions 必須都是 JSON arrays，沒有項目就輸出 []。actions 每項欄位為 intent,priority,reason,preconditions,completionConditions；intent 只能是 QueueVillager,BuildHouse,GatherFood,GatherWood,GatherGold,AdvanceFeudal,AdvanceCastle,DevelopWaterEconomy,Scout,Wait,Reobserve。條件 field 只能是 food,wood,gold,stone,population,populationCap,age,mapArchetype,waterRatio；operator 只能是 eq,ne,gt,gte,lt,lte,confirmed。不得輸出座標、按鍵、軍事命令或 Markdown。
""";

    private sealed record CompletionEnvelope(IReadOnlyList<CompletionChoice> Choices);
    private sealed record CompletionChoice(CompletionMessage Message);
    private sealed record CompletionMessage(string Content);
    private sealed record PlanDto(string Strategy, string CurrentGoal, string Reason, double Confidence,
        IReadOnlyList<string>? Assumptions, IReadOnlyList<string>? MissingInformation, IReadOnlyList<PlannedAction>? Actions);
}
