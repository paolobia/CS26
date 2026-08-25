using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Ide.Designer;

/// <summary>
/// Avvia e gestisce, come processo figlio, il `dotnet watch` che serve il progetto Blazor
/// reale dell'utente (vincolo architetturale n.1: il designer non ridisegna i controlli,
/// mostra l'app vera servita localmente). Il ciclo di vita del processo e' legato a
/// quello dell'istanza: <see cref="Dispose"/> lo termina.
/// </summary>
public sealed class DotnetWatchHost : IDisposable
{
    private static readonly Regex ListeningOnRegex = new(@"Now listening on:\s*(https?://\S+)", RegexOptions.Compiled);

    private Process? _process;

    public Uri? ServerUri { get; private set; }

    /// <summary>
    /// Avvia `dotnet watch` sul progetto indicato e attende che Kestrel sia pronto,
    /// restituendone l'URL effettivo.
    /// </summary>
    public async Task<Uri> StartAsync(string projectDirectory, string url, CancellationToken cancellationToken = default)
    {
        if (_process is not null)
            throw new InvalidOperationException("dotnet watch e' gia' stato avviato da questa istanza.");

        var startInfo = new ProcessStartInfo("dotnet", $"watch --urls {url}")
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Il browser-refresh server di `dotnet watch` richiede un certificato HTTPS dev
        // che spesso manca in ambienti non configurati; qui non serve, la pagina la
        // consuma la WebView, non un browser con auto-refresh.
        startInfo.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "1";

        var readyTcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => OnOutputLine(e.Data, readyTcs);
        _process.ErrorDataReceived += (_, e) => OnOutputLine(e.Data, readyTcs);
        _process.Exited += (_, _) => readyTcs.TrySetException(
            new InvalidOperationException($"dotnet watch e' terminato prima di essere pronto (exit code {_process.ExitCode})."));

        if (!_process.Start())
            throw new InvalidOperationException("Impossibile avviare 'dotnet watch'.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await using var registration = cancellationToken.Register(() => readyTcs.TrySetCanceled(cancellationToken));
        ServerUri = await readyTcs.Task.ConfigureAwait(false);
        return ServerUri;
    }

    private void OnOutputLine(string? line, TaskCompletionSource<Uri> readyTcs)
    {
        if (line is null)
            return;

        Console.WriteLine($"[dotnet watch] {line}");

        var match = ListeningOnRegex.Match(line);
        if (match.Success)
            readyTcs.TrySetResult(new Uri(match.Groups[1].Value));
    }

    public void Dispose()
    {
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Il processo e' gia' terminato tra il controllo e la Kill: nulla da fare.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
