using System.Diagnostics;

namespace Ide.Designer;

/// <summary>
/// Modulo 12: <c>dotnet publish -c Release</c> del progetto Blazor WASM PWA.
///
/// Due lezioni imparate testando questo modulo:
/// - **Non forzare `-p:BaseIntermediateOutputPath`**: un tentativo iniziale lo puntava
///   fuori da `obj/` per isolarlo da `dotnet watch` (modulo 5), ma rompe la risoluzione
///   NuGet del target `net8.0/browser-wasm` nell'assets file (`NETSDK1047`), anche
///   eseguendo un `dotnet restore` esplicito con lo stesso path prima del publish. La
///   directory intermedia di default (`obj/Release/net8.0/`) e' gia' separata da quella
///   di `dotnet watch` (`obj/Debug/net8.0/`) per via della configurazione diversa: nessun
///   isolamento aggiuntivo e' necessario, solo l'output finale (`-o`) va spostato in una
///   posizione comoda per l'utente.
/// - L'output completo del processo non va mai spinto riga per riga nella UI: per una
///   solution intera in Release il volume e' enorme e un pannello Output che riscrive
///   l'intero testo ad ogni riga (O(n) per riga) puo' congelare l'IDE per minuti. L'output
///   completo va su file di log; solo un riepilogo/le righe di errore vanno in Output.
/// - **Un timeout con kill non e' opzionale, e' la vera rete di sicurezza**: nel sandbox
///   di sviluppo, lo stesso `dotnet publish -c Release` che da riga di comando termina in
///   ~15-20s, quando lanciato come figlio di `Ide.App` a volte resta "zombie" (confermato
///   con `ps`: terminato ma non reaped) senza che `Process.WaitForExitAsync` se ne
///   accorga, appendendo l'attesa indefinitamente - ne' `-p:UseSharedCompilation=false`
///   ne' `/nodeReuse:false` (provati entrambi) risolvono la causa. Non essendo stato
///   possibile isolare la causa esatta (probabile particolarita' del wrapper `dotnet`
///   di questo ambiente sandbox/snap), il timeout con `Kill(entireProcessTree: true)`
///   e' cio' che garantisce che l'IDE si riprenda comunque: verificato che al timeout
///   il comando Publish torna disponibile e l'IDE resta pienamente utilizzabile.
/// </summary>
public static class ProjectPublisher
{
    public const string PublishOutputRelative = "bin/PwaPublish";

    public sealed record Result(int ExitCode, string LogFilePath, IReadOnlyList<string> ErrorLines);

    public static async Task<Result> PublishAsync(
        string projectDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var outputDirectory = Path.Combine(projectDirectory, PublishOutputRelative);
        var logFilePath = Path.Combine(projectDirectory, "bin", "publish.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDirectory);
        // Evita che il processo resti legato ai server di build persistenti (Roslyn
        // VBCSCompiler / nodi MSBuild riutilizzabili): osservato empiricamente che senza
        // questo il processo puo' terminare (zombie riscontrato via `ps`) senza che
        // Process.WaitForExitAsync se ne accorga, appendendo l'IDE indefinitamente.
        startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
        startInfo.ArgumentList.Add("/nodeReuse:false");
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        var writeLock = new object();
        var errorLines = new List<string>();
        await using var logWriter = new StreamWriter(logFilePath, append: false) { AutoFlush = true };

        void OnLine(string? line)
        {
            if (line is null)
                return;

            lock (writeLock)
            {
                logWriter.WriteLine(line);
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                    errorLines.Add(line);
            }
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        if (!process.Start())
            throw new InvalidOperationException("Impossibile avviare 'dotnet publish'.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"'dotnet publish' non e' terminato entro {timeout.TotalSeconds:0}s ed e' stato interrotto. Log: {logFilePath}");
        }

        return new Result(process.ExitCode, logFilePath, errorLines);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
