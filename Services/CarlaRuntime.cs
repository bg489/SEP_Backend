using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using AutoScenarioX.AuthApi.Contracts;

namespace AutoScenarioX.AuthApi.Services;

public sealed record CarlaReadiness(bool Ready, bool Connected, string Message);
public sealed record CarlaExecutionResult(int? ExitCode, bool TimedOut, int DurationSeconds, string Log, bool? Passed = null,
    string? ResultJson = null, List<FailureEvidenceResponse>? Evidence = null, string? Error = null);

public sealed class CarlaRuntime(IConfiguration configuration, ILogger<CarlaRuntime> logger)
{
    private const string LowQuality = "Low";

    public string CarlaRoot => Environment.GetEnvironmentVariable("CARLA_ROOT") ??
        configuration["Carla:RootPath"] ??
        @"C:\Users\admin\Downloads\CARLA_0.9.16";

    public string ScenarioRunnerRoot(CarlaIntegrationResponse config)
    {
        if (File.Exists(Path.Combine(config.ScenarioRunnerPath, "scenario_runner.py"))) return config.ScenarioRunnerPath;
        var bundledRunner = Path.Combine(CarlaRoot, "ScenarioRunner");
        return File.Exists(Path.Combine(bundledRunner, "scenario_runner.py")) ? bundledRunner : config.ScenarioRunnerPath;
    }

    public string ScenarioRunnerScript(CarlaIntegrationResponse config) => Path.Combine(ScenarioRunnerRoot(config), "scenario_runner.py");

    public string PythonExecutable(CarlaIntegrationResponse config)
    {
        var configured = configuration["ScenarioRunner:PythonExecutable"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var localVenv = Path.Combine(ScenarioRunnerRoot(config), ".venv", "Scripts", "python.exe");
        return File.Exists(localVenv) ? localVenv : "python";
    }

    public string? CarlaExecutable()
    {
        var packaged = Path.Combine(CarlaRoot, "CarlaUE4.exe");
        if (File.Exists(packaged)) return packaged;
        var shipping = Path.Combine(CarlaRoot, "CarlaUE4", "Binaries", "Win64", "CarlaUE4-Win64-Shipping.exe");
        return File.Exists(shipping) ? shipping : null;
    }

    public async Task<CarlaReadiness> CheckReadinessAsync(CarlaIntegrationResponse config, CancellationToken cancellationToken)
    {
        if (CarlaExecutable() is null)
            return new(false, false, $"CARLA executable was not found under '{CarlaRoot}'.");
        var script = ScenarioRunnerScript(config);
        var runnerRoot = ScenarioRunnerRoot(config);
        if (!File.Exists(script))
            return new(false, false, $"ScenarioRunner script was not found at '{script}'.");
        var python = PythonExecutable(config);
        if (Path.IsPathFullyQualified(python) && !File.Exists(python))
            return new(false, false, $"Configured Python executable was not found at '{python}'.");

        var version = await RunProcessAsync(python, [script, "--version"], runnerRoot,
            TimeSpan.FromSeconds(20), cancellationToken, includeRuntimeEnvironment: true);
        if (version.ExitCode != 0)
            return new(false, false, "ScenarioRunner could not start. Check its Python dependencies and CARLA 0.9.16 client package. " + Tail(version.Log));

        var connected = await IsPortOpenAsync(config.Host, config.RpcPort, TimeSpan.FromSeconds(2), cancellationToken);
        return new(true, connected, connected
            ? "CARLA 0.9.16 and ScenarioRunner are ready; the CARLA RPC port is connected."
            : "CARLA 0.9.16 and ScenarioRunner are ready; CARLA will start in Low quality when a run is claimed.");
    }

    public async Task<CarlaExecutionResult> RunScenarioAsync(
        CarlaIntegrationResponse config,
        string runId,
        string fileName,
        string source,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var readiness = await CheckReadinessAsync(config, cancellationToken);
        if (!readiness.Ready) return new(1, false, 0, readiness.Message);
        cancellationToken.ThrowIfCancellationRequested();
        var log = new List<string> { $"[{DateTimeOffset.UtcNow:O}] CARLA quality: {LowQuality}" };
        var startup = await EnsureCarlaStartedAsync(config, readiness.Connected, cancellationToken);
        log.AddRange(startup.Log);
        if (!startup.Connected) return new(startup.ExitCode ?? 1, false, 0, string.Join(Environment.NewLine, log), false);

        var runDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoScenarioX", "Runs", SafeName(runId));
        Directory.CreateDirectory(runDirectory);
        var scenarioPath = Path.Combine(runDirectory, Path.GetFileName(fileName));
        await File.WriteAllTextAsync(scenarioPath, source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var runner = ScenarioRunnerScript(config);
        var python = PythonExecutable(config);
        var arguments = new List<string>
        {
            runner,
            "--host", config.Host,
            "--port", config.RpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--timeout", "15",
            "--openscenario", scenarioPath,
            "--output",
            "--file",
            "--json",
            "--outputDir", runDirectory
        };
        var started = Stopwatch.StartNew();
        var result = await RunProcessAsync(python, arguments, runDirectory,
            TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 86400)), cancellationToken, includeRuntimeEnvironment: true);
        started.Stop();
        log.Add($"[{DateTimeOffset.UtcNow:O}] ScenarioRunner finished (exit={result.ExitCode?.ToString() ?? "timeout"}, duration={Math.Round(started.Elapsed.TotalSeconds)}s).");
        log.Add(result.Log);
        var passed = false;
        string? reportJson = null;
        string? error = "ScenarioRunner did not produce a safety-criteria report.";
        var evidence = new List<FailureEvidenceResponse>();
        foreach (var reportPath in Directory.EnumerateFiles(runDirectory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var json = await File.ReadAllTextAsync(reportPath, cancellationToken);
                using var report = JsonDocument.Parse(json);
                if (!report.RootElement.TryGetProperty("criteria", out var criteriaNode) ||
                    criteriaNode.ValueKind != JsonValueKind.Array ||
                    !report.RootElement.TryGetProperty("success", out var successNode)) continue;
                var criteria = criteriaNode.EnumerateArray().Where(item => item.GetProperty("name").GetString() != "Duration").ToList();
                reportJson = json;
                var failed = criteria.Where(item => !item.GetProperty("optional").GetBoolean() && !item.GetProperty("success").GetBoolean()).ToList();
                passed = !result.TimedOut && result.ExitCode == 0 && successNode.GetBoolean() && criteria.Count > 0 && failed.Count == 0;
                error = passed ? null : criteria.Count == 0
                    ? "ScenarioRunner executed without any safety criteria. Define criteria_ conditions in the imported source."
                    : "ScenarioRunner reported failed or incomplete safety criteria. See simulation-result.json for measured values.";
                evidence = failed.Select(item => new FailureEvidenceResponse(
                    DateTimeOffset.UtcNow.ToString("O"), item.GetProperty("name").GetString() ?? "ScenarioRunner criterion",
                    item.GetProperty("actual").ToString(), item.GetProperty("expected").ToString(),
                    "Measured by ScenarioRunner in the saved OpenSCENARIO source.")).ToList();
                break;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                logger.LogWarning(exception, "Could not read ScenarioRunner result {ReportPath}", reportPath);
            }
        }
        return new(result.ExitCode, result.TimedOut, (int)Math.Round(started.Elapsed.TotalSeconds), string.Join(Environment.NewLine, log), passed,
            reportJson, evidence, error);
    }

    public async Task<CarlaExecutionResult> RunConfiguredScenarioAsync(
        CarlaIntegrationResponse config,
        string runId,
        ScenarioResponse scenario,
        ScenarioRunResponse run,
        IReadOnlyList<OracleRuleResponse> oracleRules,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var readiness = await CheckReadinessAsync(config, cancellationToken);
        if (!readiness.Ready) return new(1, false, 0, readiness.Message, false);
        var log = new List<string> { $"[{DateTimeOffset.UtcNow:O}] CARLA quality: {LowQuality}; native actor execution." };
        var startup = await EnsureCarlaStartedAsync(config, readiness.Connected, cancellationToken);
        log.AddRange(startup.Log);
        if (!startup.Connected) return new(startup.ExitCode ?? 1, false, 0, string.Join(Environment.NewLine, log), false);

        var runDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoScenarioX", "Runs", SafeName(runId));
        Directory.CreateDirectory(runDirectory);
        var configPath = Path.Combine(runDirectory, "scenario-config.json");
        var configJson = JsonSerializer.Serialize(new
        {
            scenario,
            randomSeed = run.RandomSeed ?? 42,
            agent = run.Agent ?? "Autopilot",
            oracleRules
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(configPath, configJson, cancellationToken);

        var script = Path.Combine(AppContext.BaseDirectory, "scripts", "run_configured_scenario.py");
        if (!File.Exists(script))
            script = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "run_configured_scenario.py");
        if (!File.Exists(script)) return new(1, false, 0, $"Configured CARLA actor runner was not found at '{script}'.", false);
        var python = PythonExecutable(config);
        var stopFile = Path.Combine(runDirectory, "stop.requested");
        if (File.Exists(stopFile)) File.Delete(stopFile);
        var arguments = new List<string>
        {
            script,
            "--config", configPath,
            "--stop-file", stopFile,
            "--host", config.Host,
            "--port", config.RpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var configuredDuration = Math.Clamp(scenario.ExecutionDurationSeconds ?? 30, 5, 600);
        var processTimeout = Math.Clamp(Math.Min(timeoutSeconds, configuredDuration + 60), 1, 86400);
        var started = Stopwatch.StartNew();
        (int? ExitCode, bool TimedOut, string Log) result;
        (int? ExitCode, bool TimedOut, string Log) recovery = (1, false, "Recovery did not run.");
        try
        {
            result = await RunProcessAsync(python, arguments, runDirectory,
                TimeSpan.FromSeconds(processTimeout), cancellationToken, includeRuntimeEnvironment: true,
                runtimeRoot: ScenarioRunnerRoot(config), cooperativeStopFile: stopFile);
        }
        finally
        {
            // The caller's token may already be cancelled. Recovery must still
            // release this run's recorded actors and restore the shared world.
            try
            {
                recovery = await RunProcessAsync(python,
                    [script, "--cleanup", "--config", configPath, "--host", config.Host,
                        "--port", config.RpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                    runDirectory, TimeSpan.FromSeconds(30), CancellationToken.None,
                    includeRuntimeEnvironment: true, runtimeRoot: ScenarioRunnerRoot(config));
            }
            catch (Exception exception)
            {
                // Preserve a cancellation or process exception from the primary
                // run while retaining a useful recovery failure in the server log.
                recovery = (1, false, exception.ToString());
                logger.LogError(exception, "CARLA resource recovery failed for run {RunId}", runId);
            }
            log.Add($"[{DateTimeOffset.UtcNow:O}] CARLA resource recovery (exit={recovery.ExitCode?.ToString() ?? "timeout"}).");
            log.Add(recovery.Log);
            logger.LogInformation("CARLA resource recovery for run {RunId}: exit={ExitCode}, timeout={TimedOut}. {RecoveryLog}",
                runId, recovery.ExitCode, recovery.TimedOut, recovery.Log);
        }
        started.Stop();
        log.Add($"[{DateTimeOffset.UtcNow:O}] CARLA actor simulation finished (exit={result.ExitCode?.ToString() ?? "timeout"}, duration={Math.Round(started.Elapsed.TotalSeconds)}s).");
        log.Add(result.Log);
        var marker = result.Log.Split('\n').LastOrDefault(line => line.StartsWith("AUTOSCENARIOX_RESULT:", StringComparison.Ordinal));
        var passed = false;
        var resultJson = marker?["AUTOSCENARIOX_RESULT:".Length..].Trim();
        List<FailureEvidenceResponse>? evidence = null;
        string? error = null;
        if (resultJson is not null)
        {
            try
            {
                using var parsed = JsonDocument.Parse(resultJson);
                passed = !result.TimedOut && result.ExitCode == 0 && parsed.RootElement.GetProperty("outcome").GetString() == "PASS";
                if (!passed && parsed.RootElement.TryGetProperty("reason", out var reason)) error = reason.GetString();
                if (parsed.RootElement.TryGetProperty("evidence", out var evidenceNode))
                    evidence = evidenceNode.Deserialize<List<FailureEvidenceResponse>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
            {
                passed = false;
                error = "The CARLA runner returned an invalid execution result.";
            }
        }
        if (recovery.TimedOut || recovery.ExitCode != 0)
        {
            passed = false;
            error = "CARLA resource cleanup could not be completed. Review the recovery output in scenario-run.log before retrying.";
        }
        return new(result.ExitCode, result.TimedOut, (int)Math.Round(started.Elapsed.TotalSeconds), string.Join(Environment.NewLine, log), passed,
            resultJson, evidence, error);
    }

    private async Task<(bool Connected, int? ExitCode, List<string> Log)> EnsureCarlaStartedAsync(
        CarlaIntegrationResponse config,
        bool alreadyConnected,
        CancellationToken cancellationToken)
    {
        var log = new List<string>();
        if (alreadyConnected) return (true, null, log);
        var carla = CarlaExecutable();
        if (carla is null) return (false, 1, ["CARLA executable was not found."]);
        var serverInfo = new ProcessStartInfo(carla)
        {
            WorkingDirectory = CarlaRoot,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        serverInfo.ArgumentList.Add("-quality-level=Low");
        serverInfo.ArgumentList.Add("-carla-rpc-port=" + config.RpcPort);
        serverInfo.ArgumentList.Add("-carla-streaming-port=" + config.StreamingPort);
        serverInfo.ArgumentList.Add("-windowed");
        serverInfo.ArgumentList.Add("-ResX=800");
        serverInfo.ArgumentList.Add("-ResY=600");
        serverInfo.ArgumentList.Add("-nosound");
        using var server = Process.Start(serverInfo);
        if (server is null) return (false, 1, ["Could not start CarlaUE4.exe."]);
        log.Add($"[{DateTimeOffset.UtcNow:O}] Started CARLA PID {server.Id} with -quality-level=Low.");
        for (var attempt = 0; attempt < 180 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            if (server.HasExited)
                return (false, server.ExitCode, [.. log, "CARLA exited before opening its RPC port."]);
            if (await IsPortOpenAsync(config.Host, config.RpcPort, TimeSpan.FromSeconds(1), cancellationToken))
                return (true, null, log);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        return (false, 1, [.. log, "Timed out waiting for the CARLA RPC port."]);
    }

    private async Task<(int? ExitCode, bool TimedOut, string Log)> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool includeRuntimeEnvironment,
        string? runtimeRoot = null,
        string? cooperativeStopFile = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var argumentList = arguments.ToList();
        foreach (var argument in argumentList) startInfo.ArgumentList.Add(argument);
        if (includeRuntimeEnvironment)
        {
            startInfo.Environment["CARLA_ROOT"] = CarlaRoot;
            var runnerRoot = runtimeRoot ?? (argumentList.Count > 0 ? Path.GetDirectoryName(argumentList[0]) ?? workingDirectory : workingDirectory);
            startInfo.Environment["SCENARIO_RUNNER_ROOT"] = runnerRoot;
            startInfo.Environment.TryGetValue("PYTHONPATH", out var inheritedPythonPath);
            var pythonPaths = new[]
            {
                Path.Combine(CarlaRoot, "PythonAPI", "carla"),
                Path.Combine(CarlaRoot, "PythonAPI"),
                runnerRoot,
                inheritedPythonPath
            }.Where(path => !string.IsNullOrWhiteSpace(path));
            startInfo.Environment["PYTHONPATH"] = string.Join(Path.PathSeparator, pythonPaths);
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(startInfo);
            if (process is null) return (1, false, "Failed to start the configured process.");
            // Keep draining pipes while the child performs cooperative cleanup,
            // even when the request that launched it has been cancelled.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await StopProcessAsync(process, cooperativeStopFile);
                return (null, true, (await stdout) + Environment.NewLine + (await stderr));
            }
            catch (OperationCanceledException)
            {
                await StopProcessAsync(process, cooperativeStopFile);
                // Observe both pipe readers before propagating the original
                // cancellation, rather than leaving unobserved async work.
                await Task.WhenAll(stdout, stderr);
                throw;
            }
            return (process.ExitCode, false, (await stdout) + Environment.NewLine + (await stderr));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogError(exception, "Failed to start CARLA/ScenarioRunner process {Executable}", executable);
            return (1, false, exception.Message);
        }
    }

    private async Task StopProcessAsync(Process process, string? cooperativeStopFile)
    {
        if (process.HasExited) return;
        if (!string.IsNullOrWhiteSpace(cooperativeStopFile))
        {
            try
            {
                await File.WriteAllTextAsync(cooperativeStopFile, DateTimeOffset.UtcNow.ToString("O"), CancellationToken.None);
                using var gracePeriod = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    await process.WaitForExitAsync(gracePeriod.Token);
                    return;
                }
                catch (OperationCanceledException) when (gracePeriod.IsCancellationRequested)
                {
                    logger.LogWarning("CARLA runner PID {ProcessId} did not exit within the cooperative stop period; terminating it before resource recovery.", process.Id);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not request a cooperative CARLA runner stop using {StopFile}", cooperativeStopFile);
            }
        }
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* The child exited between the wait and kill. */ }
        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await client.ConnectAsync(host, port, timeoutSource.Token);
            return client.Connected;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or ArgumentException)
        {
            return false;
        }
    }

    private static string SafeName(string value) => string.Concat(value.Select(character =>
        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static string Tail(string value) => value.Length <= 1200 ? value : value[^1200..];
}
