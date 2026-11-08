using System.Diagnostics;
using System.Text;

namespace IntegrationTests;

/// <summary>
/// A child <c>dotnet</c> process (API or Gateway) with its stdout/stderr captured, so a startup failure
/// surfaces as readable output rather than a silent connection refusal (project_e2e_aspire_fixture_gotchas).
/// </summary>
internal sealed class ChildProcess : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();

    private ChildProcess(Process process) => _process = process;

    /// <summary>True if the child process has exited.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Captured stdout + stderr output from the child process, thread-safe.</summary>
    public string Output
    {
        get { lock (_output) return _output.ToString(); }
    }

    /// <summary>
    /// Starts a child <c>dotnet</c> process, passing the given environment variables and capturing all output.
    /// </summary>
    /// <param name="dllPath">Path to the .NET assembly to run (e.g., API.dll or UserOrg.Gateway.dll).</param>
    /// <param name="environment">Environment variables to pass to the child process.</param>
    /// <returns>A ChildProcess handle; use Output to retrieve stdout/stderr, HasExited to poll for completion, Dispose to kill the process.</returns>
    public static ChildProcess Start(string dllPath, IReadOnlyDictionary<string, string> environment)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dllPath}\"",
                WorkingDirectory = Path.GetDirectoryName(dllPath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var (key, value) in environment)
            process.StartInfo.Environment[key] = value;

        var child = new ChildProcess(process);
        process.OutputDataReceived += (_, e) => child.Append(e.Data);
        process.ErrorDataReceived += (_, e) => child.Append(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return child;
    }

    private void Append(string? line)
    {
        if (line is null)
            return;
        lock (_output)
            _output.AppendLine(line);
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                // Teardown deletes directories this child holds open, so the kill has to have landed
                // before it runs rather than merely have been requested.
                _process.WaitForExit(TimeSpan.FromSeconds(10));
            }
        }
        catch (InvalidOperationException)
        {
            // Process already gone — nothing to kill.
        }

        _process.Dispose();
    }
}
