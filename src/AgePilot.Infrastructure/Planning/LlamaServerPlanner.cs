using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgePilot.Core.Automation;
using AgePilot.Core;
using AgePilot.Core.Configuration;
using AgePilot.Core.Planning;
using AgePilot.Infrastructure.Diagnostics;

namespace AgePilot.Infrastructure.Planning;

public sealed record PlannerRequestTelemetry(
    string RequestId,
    string PresetId,
    int PresetRevision,
    string ContractId,
    int ContractRevision,
    PlanUpdateScope Scope,
    int MaxCompletionTokens,
    string? Backend,
    string? Device,
    string MmprojOffloadStatus,
    string? MainModelLayerEvidence,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    string PerImageTokenMeasurementMethod,
    IReadOnlyDictionary<string, int>? ImagePromptTokens,
    double? PromptMilliseconds,
    double? PredictedMilliseconds,
    long PanelHashMilliseconds,
    long CropMilliseconds,
    long ResizeMilliseconds,
    long JpegEncodeMilliseconds,
    long RequestSerializeMilliseconds,
    long HttpRoundtripMilliseconds,
    long EndToEndMilliseconds,
    bool Accepted,
    IReadOnlyList<VisualImageTelemetry> Images);

public sealed record VisualImageTelemetry(
    string Name, int Width, int Height, int JpegBytes, string? InclusionReason,
    long CropMilliseconds, long ResizeMilliseconds, long JpegEncodeMilliseconds);

public sealed class LlamaServerPlanner : IStrategicPlanner, IPlannerRuntimeStatusSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
    private AppSettings _settings;
    private readonly LocalJsonLineLogger? _logger;
    private HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private string? _backend;
    private string? _device;
    private OffloadEvidence _lastOffloadEvidence = new("RequestedUnverified", null);
    private bool _startupAttempted;
    private bool _restartRequired;
    private readonly Queue<string> _serverMessages = new();
    private PlannerRuntimeStatus _runtimeStatus = PlannerRuntimeStatus.NotConfigured();

    public PlannerRuntimeStatus RuntimeStatus => _runtimeStatus;
    public string? LastRawResponse { get; private set; }
    public PlannerRequestTelemetry? LastTelemetry { get; private set; }

    public LlamaServerPlanner(AppSettings settings, LocalJsonLineLogger? logger = null)
    {
        _settings = settings;
        _logger = logger;
        _http = CreateHttpClient(settings.LlmPort);
    }

    public async Task<PlanningResult> PlanAsync(SituationContext context, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var serializeMilliseconds = 0L;
        var httpMilliseconds = 0L;
        Stopwatch? httpClock = null;
        CompletionEnvelope? receivedEnvelope = null;
        var requestStarted = Stopwatch.StartNew();
        var contract = GamePlanContractCatalog.Get(_settings.GamePlanContractId);
        var effectiveScope = context.PreviousPlan is null ? PlanUpdateScope.Major : context.AllowedUpdateScope;
        var maxCompletionTokens = contract.CompletionBudgets[effectiveScope].HardCap;
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
            var serializeClock = Stopwatch.StartNew();
            var userContent = new List<object>();
            if (context.Visual is { } visual)
            {
                userContent.AddRange(visual.Images.Select(image => (object)new
                {
                    type = "image_url",
                    image_url = new { url = $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Data)}" },
                }));
            }
            userContent.Add(new { type = "text", text = JsonSerializer.Serialize(ToPromptContext(context, effectiveScope, contract.IsCompact), JsonOptions) });
            var request = new
            {
                model = "agepilot-local",
                temperature = 0.2,
                seed = _settings.LlmSeed,
                max_tokens = maxCompletionTokens,
                messages = new object[]
                {
                    new { role = "system", content = contract.IsCompact ? CompactSystemPrompt : SystemPrompt },
                    new { role = "user", content = userContent.ToArray() },
                },
                response_format = contract.IsCompact
                    ? BuildCompactResponseFormat(effectiveScope, AllowedActions(context.State))
                    : BuildResponseFormat(effectiveScope, AllowedActions(context.State)),
            };
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            serializeClock.Stop();
            serializeMilliseconds = serializeClock.ElapsedMilliseconds;
            using var contentBody = new ByteArrayContent(requestBytes);
            contentBody.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            httpClock = Stopwatch.StartNew();
            using var response = await _http.PostAsync("v1/chat/completions", contentBody, timeout.Token);
            response.EnsureSuccessStatusCode();
            receivedEnvelope = await response.Content.ReadFromJsonAsync<CompletionEnvelope>(JsonOptions, timeout.Token)
                ?? throw new InvalidDataException("模型回覆為空");
            httpClock.Stop();
            httpMilliseconds = httpClock.ElapsedMilliseconds;
            var content = receivedEnvelope.Choices.FirstOrDefault()?.Message.Content;
            LastRawResponse = content;
            var now = DateTimeOffset.UtcNow;
            var validated = contract.IsCompact
                ? ParseCompactPlan(content, context, effectiveScope, now)
                : ParseLegacyPlan(content, context, effectiveScope, now);
            requestStarted.Stop();
            LastTelemetry = CreateTelemetry(requestId, context.Visual, receivedEnvelope, contract, effectiveScope,
                maxCompletionTokens, serializeMilliseconds, httpMilliseconds, requestStarted.ElapsedMilliseconds, validated.Success);
            _logger?.Write("planning.completed", new
            {
                requestId,
                backend = _backend,
                latencyMs = requestStarted.ElapsedMilliseconds,
                scope = effectiveScope.ToString(),
                contractId = contract.Id,
                maxTokens = maxCompletionTokens,
                telemetry = LastTelemetry,
                validated = validated.Success,
                validated.Error,
            });
            SetStatus(validated.Success ? PlannerRuntimePhase.Ready : PlannerRuntimePhase.Error,
                validated.Success ? $"LLM 已就緒（{FormatBackend()}）" : $"計畫格式驗證失敗：{validated.Error}", FormatBackend());
            return validated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (httpClock is { IsRunning: true })
            {
                httpClock.Stop();
                httpMilliseconds = httpClock.ElapsedMilliseconds;
            }
            requestStarted.Stop();
            LastTelemetry = CreateTelemetry(requestId, context.Visual, receivedEnvelope, contract, effectiveScope,
                maxCompletionTokens, serializeMilliseconds, httpMilliseconds, requestStarted.ElapsedMilliseconds, accepted: false);
            _logger?.Write("planning.failure", new { requestId, backend = _backend, type = ex.GetType().Name, ex.Message, telemetry = LastTelemetry });
            var tail = GetServerMessageTail();
            var message = string.IsNullOrWhiteSpace(tail) ? ex.Message : $"{ex.Message}；llama-server: {tail}";
            SetStatus(PlannerRuntimePhase.Error, message, FormatBackend());
            return new(null, message);
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (!_settings.EnableLocalPlanning)
        {
            SetStatus(PlannerRuntimePhase.NotConfigured, "本機規劃已停用");
            return;
        }
        if (_restartRequired)
            throw new InvalidOperationException("llama-server 已停止；請在 Dashboard 按「重新啟動 LLM」");
        if (_process is { HasExited: false })
        {
            if (await IsHealthyAsync(cancellationToken))
            {
                SetStatus(PlannerRuntimePhase.Ready, $"LLM 已就緒（{FormatBackend()}）", FormatBackend());
                return;
            }
            MarkRestartRequired("llama-server 無回應；請在 Dashboard 按「重新啟動 LLM」");
            throw new InvalidOperationException(_runtimeStatus.Message);
        }
        if (_startupAttempted)
        {
            MarkRestartRequired("llama-server 已意外結束；請在 Dashboard 按「重新啟動 LLM」");
            throw new InvalidOperationException(_runtimeStatus.Message);
        }
        _startupAttempted = true;
        var model = Resolve(_settings.LlmModelPath);
        var mmproj = Resolve(_settings.VisionProjectorPath);
        var runtime = Resolve(_settings.LlamaRuntimePath);
        var pipelinePreset = VlmPipelinePresetCatalog.Get(_settings.VlmPipelinePresetId);
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
        _logger?.Write("llm.server.starting", new { _settings.LlmBackend, _settings.LlmPort });
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
                    Arguments = $"-m \"{model}\" --mmproj \"{mmproj}\" --mmproj-offload --image-min-tokens {pipelinePreset.ImageMinTokens} --image-max-tokens {pipelinePreset.ImageMaxTokens} --host 127.0.0.1 --port {_settings.LlmPort} -c {_settings.LlmContextSize} --n-gpu-layers {_settings.LlmGpuLayers} --cache-ram 0 --alias agepilot-local --jinja --perf -np 1 --device {device}",
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
                        var offloadEvidence = GetOffloadEvidence();
                        if (offloadEvidence.Mmproj == "Contradictory")
                            throw new InvalidOperationException("llama-server 回報 mmproj 使用 CPU；拒絕未預期的 projector fallback");
                        SetStatus(PlannerRuntimePhase.Ready, $"LLM 已就緒（{FormatBackend()}）", FormatBackend());
                        _lastOffloadEvidence = offloadEvidence;
                        _logger?.Write("llm.server.ready", new
                        {
                            backend = _backend,
                            device = _device,
                            processId = _process.Id,
                            presetId = pipelinePreset.Id,
                            imageMinTokens = pipelinePreset.ImageMinTokens,
                            imageMaxTokens = pipelinePreset.ImageMaxTokens,
                            offload = offloadEvidence,
                        });
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
        _restartRequired = true;
        SetStatus(PlannerRuntimePhase.Error, failure);
        _logger?.Write("llm.server.failed", new { failure });
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try { await EnsureStartedAsync(cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetStatus(PlannerRuntimePhase.Error, ex.Message, _backend);
            }
            return RuntimeStatus;
        }
        finally { _gate.Release(); }
    }

    public async Task<PlannerRuntimeStatus> RestartAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _logger?.Write("llm.server.restart_requested", new { settings.LlmBackend, settings.LlmPort });
            StopProcess();
            if (_settings.LlmPort != settings.LlmPort)
            {
                _http.Dispose();
                _http = CreateHttpClient(settings.LlmPort);
            }
            _settings = settings;
            _backend = null;
            _device = null;
            _lastOffloadEvidence = new("RequestedUnverified", null);
            _startupAttempted = false;
            _restartRequired = false;
            LastRawResponse = null;
            LastTelemetry = null;
            lock (_serverMessages) _serverMessages.Clear();
            SetStatus(PlannerRuntimePhase.Starting, "正在重新啟動 llama-server");
            try { await EnsureStartedAsync(cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetStatus(PlannerRuntimePhase.Error, ex.Message, _backend);
            }
            return RuntimeStatus;
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try { using var response = await _http.GetAsync("health", cancellationToken); return response.StatusCode == HttpStatusCode.OK; }
        catch (HttpRequestException) { return false; }
    }

    private static object ToPromptContext(SituationContext context, PlanUpdateScope scope, bool compact) => new
    {
        capturedAt = context.CapturedAt,
        directive = context.Directive is null ? null : new
        {
            context.Directive.Strategy,
            targetAge = context.Directive.TargetAge.ToString(),
        },
        allowedUpdateScope = scope.ToString(),
        outputFields = OutputFields(scope, compact),
        frozenDecisions = new
        {
            major = scope < PlanUpdateScope.Major ? context.PreviousPlan?.MajorDecision : null,
            medium = scope < PlanUpdateScope.Medium ? context.PreviousPlan?.MediumDecision : null,
        },
        state = new
        {
            age = context.State.Age?.ToString(),
            food = Value(context.State.Food), wood = Value(context.State.Wood), gold = Value(context.State.Gold), stone = Value(context.State.Stone),
            population = Value(context.State.Population), populationCap = Value(context.State.PopulationCap),
        },
        allowedActions = AllowedActions(context.State).Select(kind => kind.ToString()).ToArray(),
        history = context.History,
        map = context.Map?.IsUsable == true ? context.Map : null,
        previousPlan = context.PreviousPlan is null ? null : new
        {
            context.PreviousPlan.Revision,
            context.PreviousPlan.Strategy,
            context.PreviousPlan.CurrentGoal,
            context.PreviousPlan.MajorDecision,
            context.PreviousPlan.MediumDecision,
            context.PreviousPlan.MinorDecision,
        },
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

    private PlannerRequestTelemetry CreateTelemetry(
        string requestId,
        VisualObservation? visual,
        CompletionEnvelope? envelope,
        GamePlanContract contract,
        PlanUpdateScope scope,
        int maxCompletionTokens,
        long serializeMilliseconds,
        long httpMilliseconds,
        long endToEndMilliseconds,
        bool accepted)
    {
        var preset = VlmPipelinePresetCatalog.Get(_settings.VlmPipelinePresetId);
        var composition = visual?.Telemetry;
        var images = visual?.Images.Select(image => new VisualImageTelemetry(
            image.Name, image.Width, image.Height, image.Data.Length, image.InclusionReason,
            image.CropMilliseconds, image.ResizeMilliseconds, image.JpegEncodeMilliseconds)).ToArray()
            ?? [];
        return new PlannerRequestTelemetry(requestId, composition?.PresetId ?? preset.Id,
            composition?.PresetRevision ?? preset.Revision, contract.Id, contract.Revision, scope, maxCompletionTokens,
            _backend, _device, _lastOffloadEvidence.Mmproj, _lastOffloadEvidence.MainModelLayerEvidence,
            envelope?.Usage?.PromptTokens, envelope?.Usage?.CompletionTokens, envelope?.Usage?.TotalTokens,
            "Unavailable", null,
            envelope?.Timings?.PromptMilliseconds, envelope?.Timings?.PredictedMilliseconds,
            composition?.PanelHashMilliseconds ?? 0, composition?.CropMilliseconds ?? 0,
            composition?.ResizeMilliseconds ?? 0, composition?.JpegEncodeMilliseconds ?? 0,
            serializeMilliseconds, httpMilliseconds, endToEndMilliseconds, accepted, images);
    }

    private static string[] OutputFields(PlanUpdateScope scope, bool compact = false)
    {
        var fields = new List<string> { compact ? "minor" : "minorDecision" };
        if (scope >= PlanUpdateScope.Medium) fields.Add(compact ? "medium" : "mediumDecision");
        if (scope >= PlanUpdateScope.Major) fields.Add(compact ? "major" : "majorDecision");
        return fields.ToArray();
    }

    private static PlanningResult ParseCompactPlan(string? content, SituationContext context,
        PlanUpdateScope scope, DateTimeOffset now)
    {
        var response = JsonSerializer.Deserialize<CompactGamePlanResponse>(content ?? "", JsonOptions)
            ?? throw new InvalidDataException("模型未回傳有效 Compact GamePlan JSON");
        if (!AllowedActions(context.State).Contains(response.Action))
            return new(null, $"Compact GamePlan 動作不在本輪 allowlist：{response.Action}");
        return CompactGamePlanAdapter.Adapt(response, context, scope, now);
    }

    private static PlanningResult ParseLegacyPlan(string? content, SituationContext context,
        PlanUpdateScope scope, DateTimeOffset now)
    {
        var dto = JsonSerializer.Deserialize<PlanDto>(content ?? "", JsonOptions)
            ?? throw new InvalidDataException("模型未回傳有效 JSON 計畫");
        if (dto.Action is null) throw new InvalidDataException("模型未回傳動作");
        if (dto.MinorDecision is null) throw new InvalidDataException("模型未回傳 Minor 判斷");
        var gameAction = new GameAction(dto.Action.Kind, dto.Action.Reason ?? "", Math.Max(1, dto.Action.Quantity));
        var decision = new VisualPlayerDecision(dto.Assessment, dto.Goal, dto.Reason, gameAction,
            dto.ExpectedResult, dto.RecheckAfterMs, dto.Confidence);
        var (major, medium, minor) = AssembleDecisions(
            dto.MajorDecision is { } majorDto ? ToDecision(majorDto, DecisionLevel.Major) : null,
            dto.MediumDecision is { } mediumDto ? ToDecision(mediumDto, DecisionLevel.Medium) : null,
            ToDecision(dto.MinorDecision, DecisionLevel.Minor), context.PreviousPlan, scope);
        var plan = new GamePlan(Guid.NewGuid().ToString("N"), now, now.AddSeconds(60),
            context.Directive?.Strategy ?? "穩定發展經濟並升時代", minor.Objective, dto.Reason, dto.Confidence,
            VisualDecision: decision, MajorDecision: major, MediumDecision: medium, MinorDecision: minor,
            RequestedUpdateScope: dto.RequestedUpdateScope);
        return GamePlanValidator.Validate(plan, now);
    }

    private static DecisionNode ToDecision(DecisionDto dto, DecisionLevel level) => new(
        dto.NodeId, level, dto.Objective, dto.Reason, dto.Evidence,
        dto.CompletionCondition, dto.FailureCondition, dto.Status);

    public static (DecisionNode Major, DecisionNode Medium, DecisionNode Minor) AssembleDecisions(
        DecisionNode? major, DecisionNode? medium, DecisionNode? minor, GamePlan? previous, PlanUpdateScope scope)
    {
        if (minor is null) throw new InvalidDataException("模型未回傳 Minor 判斷");
        var resolvedMedium = scope >= PlanUpdateScope.Medium
            ? medium ?? throw new InvalidDataException("模型未回傳 Medium 判斷")
            : previous?.MediumDecision ?? throw new InvalidOperationException("缺少前一份 Medium 判斷可供沿用");
        var resolvedMajor = scope >= PlanUpdateScope.Major
            ? major ?? throw new InvalidDataException("模型未回傳 Major 判斷")
            : previous?.MajorDecision ?? throw new InvalidOperationException("缺少前一份 Major 判斷可供沿用");
        return (resolvedMajor, resolvedMedium, minor);
    }

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

    private void MarkRestartRequired(string message)
    {
        _restartRequired = true;
        SetStatus(PlannerRuntimePhase.Error, message, FormatBackend());
        _logger?.Write("llm.server.crashed", new { message, backend = _backend, device = _device });
    }

    private static HttpClient CreateHttpClient(int port) =>
        new() { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

    private void RememberServerMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_serverMessages)
        {
            _serverMessages.Enqueue(message);
            while (_serverMessages.Count > 200) _serverMessages.Dequeue();
        }
    }

    private OffloadEvidence GetOffloadEvidence()
    {
        string[] messages;
        lock (_serverMessages) messages = _serverMessages.ToArray();
        var projectorLines = messages.Where(line =>
            line.Contains("mmproj", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("projector", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("vision", StringComparison.OrdinalIgnoreCase)).ToArray();
        var contradictory = projectorLines.Any(line =>
            line.Contains("CPU", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("GPU", StringComparison.OrdinalIgnoreCase));
        var confirmed = projectorLines.Any(line =>
            line.Contains("ROCm", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Vulkan", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("GPU", StringComparison.OrdinalIgnoreCase));
        var layerLine = messages.LastOrDefault(line =>
            line.Contains("offload", StringComparison.OrdinalIgnoreCase) &&
            line.Contains("layer", StringComparison.OrdinalIgnoreCase));
        return new OffloadEvidence(
            contradictory ? "Contradictory" : confirmed ? "Confirmed" : "RequestedUnverified",
            SanitizeServerEvidence(layerLine));
    }

    private static string? SanitizeServerEvidence(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var modelIndex = line.IndexOf(".gguf", StringComparison.OrdinalIgnoreCase);
        return modelIndex < 0 ? line.Trim() : "GGUF path omitted; " + line[(modelIndex + 5)..].Trim();
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

    public void Dispose() { StopProcess(); _http.Dispose(); }

    private const string SystemPrompt = """
/no_think
你是謹慎的 AOE2 DE 經濟發展玩家。玩家的 directive 是不可改寫的長期策略與最終目標時代。你必須維護 Major、Medium、Minor 三層判斷：Major 是目前升時代階段，Medium 是達成它的方法（例如生產村民、避免卡人口、依可見證據選擇食物來源），Minor 是現在要完成的具體小目標。context.outputFields 列出這一輪你能輸出哪些層級（一定包含 minorDecision，可能包含 mediumDecision、majorDecision）；沒有列在 outputFields 裡的層級，系統會直接沿用 context.frozenDecisions 裡的內容，你的回覆裡不會有、也不能有那個欄位，不需要嘗試重寫它。若你認為 frozenDecisions 裡的層級已經不成立，把 requestedUpdateScope 設成需要的層級，下一輪系統才會把它加進 outputFields 讓你重建；這一輪仍然只能照 outputFields 輸出。正常情況 requestedUpdateScope 等於 allowedUpdateScope。nodeId 只能用英文字母、數字、-、_，長度 1-80，且不可以跟 frozenDecisions 裡任何 nodeId 相同。每個節點都要寫明畫面證據、完成條件與失敗條件。

你不操作滑鼠也不按鍵，不需要也不可以提供任何座標。你每輪只做一件事：從下列具名動作中選一個，系統會用寫死且測試過的程序去執行它。
- Observe：畫面資訊不足，需要重新觀察。
- Wait：情勢正確但還不到動作時機。
- QueueVillager：人口未滿且食物至少 50 時，在城鎮中心生產一名村民。
- BuildHouse：人口剩餘空間不超過 2 且木材至少 25 時，選閒置村民建造房屋。
- AdvanceAge、GatherFood、GatherWood、GatherGold：本階段尚未開放，選了會被系統擋下，這一輪等於空轉。

當人口剩餘空間不超過 2 時優先 BuildHouse；否則食物足夠時選 QueueVillager。不要用 Observe 或 Wait 取代一個前置條件已滿足的動作。

資源不足、OCR 讀值不可靠或動作尚未開放時，系統會擋下該動作，因此請依 context 提供的資源數值判斷是否負擔得起再選。

expectedResult 必須且只能描述這個動作在數秒內就能從 HUD 數值驗證的直接後果，例如「食物減少 50」「時代欄位改變」。不可以寫「升上城堡時代」「人口增加 10」這種需要數十秒到數分鐘、跨多個動作才會發生的策略結果——系統是用 HUD 數值變化來確認動作是否被遊戲接受的。

前一動作的結果由系統依 OCR 自行判定，不會問你，你也不需要回報。visual.imageOrder 是本輪實際附帶的影像：battlefield 是純世界視野，minimap 是右下小地圖，command_panel 是條件式左下指令面板；legacy preset 可能改以 panorama 表示完整遊戲畫面。沒有列出的影像代表本輪未提供，不得假裝看見。只管理村民、採集、經濟建築、經濟科技與升時代；禁止軍事與戰鬥。若不確定或畫面矛盾，選 Observe 或 Wait。你只能選 context.allowedActions 與 JSON schema 同時允許的動作。所有文字欄位都要精簡，assessment 不超過 300 字。不要輸出 Markdown，嚴格依 JSON schema 回覆。
""";

    private const string CompactSystemPrompt = """
/no_think
你是謹慎的 AOE2 DE 經濟發展玩家。玩家 directive 與目標時代不可改寫。維護 Major、Medium、Minor 三層決策語意，但只輸出 JSON schema 指定的 enum，不輸出自然語言、座標、證據或結果描述。

context.outputFields 是本輪可更新的層級；未列出的父層會由系統沿用。若 frozen parent 已失效，raise 設為需要重建的更高層；否則 raise=None。Major 表示長期發展階段，Medium 表示達成方法，Minor 表示當下小目標。

每輪只從 context.allowedActions 選一個 action。人口剩餘空間不超過 2 且木材足夠時選 BuildHouse；否則食物足夠且人口安全時選 QueueVillager。關鍵 OCR 不可用或矛盾時只能 Observe 或 Wait。未開放、資源不足或不在 allowlist 的動作不得選擇。

reason、minor、medium、major 必須選最貼近 context 的 schema enum。recheckMs 介於 250 與 30000；對 Observe/Wait 表示重新觀察等待時間，其他動作仍填安全的短期重查時間。不要輸出 Markdown 或 schema 以外欄位。
""";

    private static object DecisionNodeSchema() => new Dictionary<string, object>
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new[] { "nodeId", "objective", "reason", "evidence", "completionCondition", "failureCondition", "status" },
        ["properties"] = new Dictionary<string, object>
        {
            ["nodeId"] = new { type = "string", maxLength = 80, pattern = "^[A-Za-z0-9_-]{1,80}$" },
            ["objective"] = new { type = "string", maxLength = 200 },
            ["reason"] = new { type = "string", maxLength = 300 },
            ["evidence"] = new { type = "string", maxLength = 300 },
            ["completionCondition"] = new { type = "string", maxLength = 300 },
            ["failureCondition"] = new { type = "string", maxLength = 300 },
            ["status"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = Enum.GetNames<DecisionStatus>() },
        },
    };

    /// <summary>
    /// 依 ADR 0015，模型只選具名動作，不輸出任何座標。x/y/row/column/space 已全部移除——
    /// 2026-08-13 的實機日誌顯示模型產出的座標是幻覺（0.05,0.1 連點 4 次且點在不可選取的樹上）。
    /// 定位改由 GameActionRegistry 的程序負責。
    /// </summary>
    private static object ActionSchema(IReadOnlyList<GameActionKind> allowedActions) => new Dictionary<string, object>
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new[] { "kind", "reason", "quantity" },
        ["properties"] = new Dictionary<string, object>
        {
            ["kind"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = allowedActions.Select(kind => kind.ToString()).ToArray() },
            ["reason"] = new { type = "string", maxLength = 200 },
            ["quantity"] = new { type = "integer", minimum = 1, maximum = 10 },
        },
    };

    public static object BuildResponseFormat(PlanUpdateScope scope) =>
        BuildResponseFormat(scope, Enum.GetValues<GameActionKind>());

    public static object BuildResponseFormat(PlanUpdateScope scope, IReadOnlyList<GameActionKind> allowedActions)
    {
        // action 排在最前面：strict json_schema 會依 properties 順序生成，先決定動作再寫論述，
        // 避免舊版那種「寫完數百字才擠出決定」的行為。
        // previousActionResult 已移除——改由 ActionOutcomeVerifier 依 OCR 判定，不再詢問模型。
        // 所有自由文字欄位都有 maxLength：舊版無上限導致回覆撐爆 max_tokens=1024，
        // 在 4735／5021 byte 處被截斷，2026-08-13 當天造成 4 次 planning.failure。
        var required = new List<string>
        {
            "action", "expectedResult", "recheckAfterMs", "confidence",
            "goal", "reason", "assessment", "requestedUpdateScope", "minorDecision",
        };
        var properties = new Dictionary<string, object>
        {
            ["action"] = ActionSchema(allowedActions),
            ["expectedResult"] = new { type = "string", maxLength = 150 },
            ["recheckAfterMs"] = new { type = "integer", minimum = 250, maximum = 30000 },
            ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
            ["goal"] = new { type = "string", maxLength = 120 },
            ["reason"] = new { type = "string", maxLength = 300 },
            ["assessment"] = new { type = "string", maxLength = 300 },
            ["requestedUpdateScope"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = Enum.GetNames<PlanUpdateScope>() },
            ["minorDecision"] = DecisionNodeSchema(),
        };
        if (scope >= PlanUpdateScope.Medium) { required.Add("mediumDecision"); properties["mediumDecision"] = DecisionNodeSchema(); }
        if (scope >= PlanUpdateScope.Major) { required.Add("majorDecision"); properties["majorDecision"] = DecisionNodeSchema(); }

        return new
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
                    ["required"] = required.ToArray(),
                    ["properties"] = properties,
                },
            },
        };
    }

    public static object BuildCompactResponseFormat(PlanUpdateScope scope) =>
        BuildCompactResponseFormat(scope, Enum.GetValues<GameActionKind>());

    public static object BuildCompactResponseFormat(PlanUpdateScope scope, IReadOnlyList<GameActionKind> allowedActions)
    {
        var required = new List<string> { "action", "confidence", "recheckMs", "reason", "raise", "minor" };
        var properties = new Dictionary<string, object>
        {
            ["action"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = allowedActions.Select(item => item.ToString()).ToArray() },
            ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
            ["recheckMs"] = new { type = "integer", minimum = 250, maximum = 30000 },
            ["reason"] = EnumSchema<PlanReasonCode>(),
            ["raise"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["enum"] = scope switch
                {
                    PlanUpdateScope.Minor => new[] { "None", "Medium", "Major" },
                    PlanUpdateScope.Medium => new[] { "None", "Major" },
                    _ => new[] { "None" },
                },
            },
            ["minor"] = EnumSchema<MinorPlanIntent>(),
        };
        if (scope >= PlanUpdateScope.Medium) { required.Add("medium"); properties["medium"] = EnumSchema<MediumPlanIntent>(); }
        if (scope >= PlanUpdateScope.Major) { required.Add("major"); properties["major"] = EnumSchema<MajorPlanIntent>(); }
        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "agepilot_compact_game_plan_v2",
                strict = true,
                schema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = required.ToArray(),
                    ["properties"] = properties,
                },
            },
        };
    }

    private static Dictionary<string, object> EnumSchema<T>() where T : struct, Enum => new()
    {
        ["type"] = "string",
        ["enum"] = Enum.GetNames<T>(),
    };

    public static IReadOnlyList<GameActionKind> AllowedActions(GameState state)
    {
        var allowed = new List<GameActionKind> { GameActionKind.Observe, GameActionKind.Wait };
        if (state.Population?.IsUsable != true || state.PopulationCap?.IsUsable != true) return allowed;

        var remaining = state.PopulationCap.Value.GetValueOrDefault() - state.Population.Value.GetValueOrDefault();
        if (remaining <= 2)
        {
            if (state.Wood?.IsUsable == true && state.Wood.Value.GetValueOrDefault() >= 25)
                allowed.Add(GameActionKind.BuildHouse);
            return allowed;
        }

        if (state.Food?.IsUsable == true && state.Food.Value.GetValueOrDefault() >= 50)
            allowed.Add(GameActionKind.QueueVillager);
        return allowed;
    }

    private sealed record CompletionEnvelope(
        IReadOnlyList<CompletionChoice> Choices,
        CompletionUsage? Usage = null,
        CompletionTimings? Timings = null);
    private sealed record CompletionChoice(CompletionMessage Message);
    private sealed record CompletionMessage(string Content);
    private sealed record OffloadEvidence(string Mmproj, string? MainModelLayerEvidence);
    private sealed record CompletionUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);
    private sealed record CompletionTimings(
        [property: JsonPropertyName("prompt_ms")] double? PromptMilliseconds,
        [property: JsonPropertyName("predicted_ms")] double? PredictedMilliseconds);
    private sealed record PlanDto(string Assessment, string Goal, string Reason, double Confidence, PlanUpdateScope RequestedUpdateScope,
        DecisionDto? MajorDecision, DecisionDto? MediumDecision, DecisionDto? MinorDecision,
        GameActionDto? Action, string ExpectedResult, int RecheckAfterMs);
    private sealed record DecisionDto(string NodeId, string Objective, string Reason, string Evidence,
        string CompletionCondition, string FailureCondition, DecisionStatus Status);
    private sealed record GameActionDto(GameActionKind Kind, string? Reason, int Quantity);
}
