using System.Diagnostics;
using System.Net.Sockets;
using AutoScenarioX.AuthApi.Contracts;

namespace AutoScenarioX.AuthApi.Services;

public sealed record CarlaReadiness(bool Ready, bool Connected, string Message);
public sealed record CarlaExecutionResult(int? ExitCode, bool TimedOut, int DurationSeconds, string Log);

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
        if (!readiness.Connected)
        {
            var carla = CarlaExecutable()!;
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
            if (server is null) return new(1, false, 0, "Could not start CarlaUE4.exe.");
            log.Add($"[{DateTimeOffset.UtcNow:O}] Started CARLA PID {server.Id} with -quality-level=Low.");
            var connected = false;
            for (var attempt = 0; attempt < 180 && !cancellationToken.IsCancellationRequested; attempt++)
            {
                if (server.HasExited)
                    return new(server.ExitCode, false, 0, string.Join(Environment.NewLine, log.Append("CARLA exited before opening its RPC port.")));
                if (await IsPortOpenAsync(config.Host, config.RpcPort, TimeSpan.FromSeconds(1), cancellationToken))
                {
                    connected = true;
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            if (!connected) return new(1, false, 0, string.Join(Environment.NewLine, log.Append("Timed out waiting for the CARLA RPC port.")));
        }

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
            "--file"
        };
        var started = Stopwatch.StartNew();
        var result = await RunProcessAsync(python, arguments, runDirectory,
            TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 86400)), cancellationToken, includeRuntimeEnvironment: true);
        started.Stop();
        log.Add($"[{DateTimeOffset.UtcNow:O}] ScenarioRunner finished (exit={result.ExitCode?.ToString() ?? "timeout"}, duration={Math.Round(started.Elapsed.TotalSeconds)}s).");
        log.Add(result.Log);
        return new(result.ExitCode, result.TimedOut, (int)Math.Round(started.Elapsed.TotalSeconds), string.Join(Environment.NewLine, log));
    }

    private async Task<(int? ExitCode, bool TimedOut, string Log)> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool includeRuntimeEnvironment)
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
            var runnerRoot = argumentList.Count > 0 ? Path.GetDirectoryName(argumentList[0]) ?? workingDirectory : workingDirectory;
            startInfo.Environment["SCENARIO_RUNNER_ROOT"] = runnerRoot;
            var pythonPaths = new[]
            {
                Path.Combine(CarlaRoot, "PythonAPI", "carla"),
                Path.Combine(CarlaRoot, "PythonAPI"),
                runnerRoot,
                startInfo.Environment["PYTHONPATH"]
            }.Where(path => !string.IsNullOrWhiteSpace(path));
            startInfo.Environment["PYTHONPATH"] = string.Join(Path.PathSeparator, pythonPaths);
        }
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return (1, false, "Failed to start the configured process.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                return (null, true, (await stdout) + Environment.NewLine + (await stderr));
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
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
