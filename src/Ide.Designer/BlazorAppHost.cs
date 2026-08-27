using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ide.Designer;

/// <summary>
/// Avvia e gestisce il progetto Blazor reale dell'utente (vincolo architetturale n.1: il
/// designer non ridisegna i controlli, mostra l'app vera servita localmente).
///
/// Sostituisce <c>DotnetWatchHost</c> (basato su <c>dotnet watch</c>), abbandonato dopo due
/// bug reali riscontrati in produzione:
/// 1) `dotnet watch` con Hot Reload abilitato termina con una NullReferenceException in
///    BlazorWebAssemblyDeltaApplier.WaitForProcessRunningAsync quando il browser-refresh e'
///    soppresso (necessario qui: non c'e' un browser esterno, solo la WebView) - l'applier
///    aspetta una connessione dal browser che non arrivera' mai.
/// 2) Disattivando l'Hot Reload (`--no-hot-reload`) per aggirare il problema sopra, OGNI
///    modifica forza un riavvio completo del processo figlio, che va spesso in race sui file
///    compilati (il processo precedente non ha ancora rilasciato l'handle) - osservato
///    empiricamente, riproducibile su modifiche successive alla prima.
///
/// Il nostro modello di sincronizzazione e' manuale (bottone "Aggiorna"): sappiamo sempre
/// con precisione il momento esatto in cui serve una rebuild, quindi non serve un file-watcher
/// generico. Questa classe lancia invece una build esplicita, one-shot, e serve l'output
/// gia' generato con un server di file statici avviato una sola volta e mai piu' riavviato -
/// non potendo mai andare in race con se stesso.
///
/// Il server usato e' <c>blazor-devserver.dll</c>, fornito dal pacchetto NuGet
/// <c>Microsoft.AspNetCore.Components.WebAssembly.DevServer</c> - gia' una dipendenza
/// esistente del progetto Blazor WASM del template, e letteralmente lo stesso eseguibile che
/// `dotnet watch` lanciava come proprio processo figlio.
/// </summary>
public sealed class BlazorAppHost : IDisposable
{
    private static readonly Regex ListeningOnRegex = new(@"Now listening on:\s*(https?://\S+)", RegexOptions.Compiled);

    private string? _projectDirectory;
    private string? _csprojPath;
    private Process? _devServerProcess;

    public Uri? ServerUri { get; private set; }

    /// <summary>
    /// Compila il progetto una volta (in caso di errore, l'IDE non deve avviarsi su una build
    /// rotta) e avvia il server di file statici persistente sull'output generato.
    /// </summary>
    public async Task<Uri> StartAsync(
        string projectDirectory,
        string url,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        if (_devServerProcess is not null)
            throw new InvalidOperationException("Il server e' gia' stato avviato da questa istanza.");

        _projectDirectory = projectDirectory;
        _csprojPath = Directory.GetFiles(projectDirectory, "*.csproj").Single();

        var buildOk = await RunDotnetBuildAsync(onOutputLine, cancellationToken).ConfigureAwait(false);
        if (!buildOk)
            throw new InvalidOperationException("La build iniziale del progetto Blazor e' fallita: vedi Output per i dettagli.");

        var devServerDll = ResolveDevServerDllPath();
        var appDll = Path.Combine(projectDirectory, "bin", "Debug", "net8.0", Path.GetFileNameWithoutExtension(_csprojPath) + ".dll");

        var startInfo = new ProcessStartInfo("dotnet", $"\"{devServerDll}\" --applicationpath \"{appDll}\" --urls {url}")
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var readyTcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);

        _devServerProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _devServerProcess.OutputDataReceived += (_, e) => OnServerOutputLine(e.Data, onOutputLine, readyTcs);
        _devServerProcess.ErrorDataReceived += (_, e) => OnServerOutputLine(e.Data, onOutputLine, readyTcs);
        _devServerProcess.Exited += (_, _) => readyTcs.TrySetException(
            new InvalidOperationException($"Il server di file statici e' terminato prima di essere pronto (exit code {_devServerProcess.ExitCode})."));

        if (!_devServerProcess.Start())
            throw new InvalidOperationException("Impossibile avviare il server di file statici.");

        _devServerProcess.BeginOutputReadLine();
        _devServerProcess.BeginErrorReadLine();

        await using var registration = cancellationToken.Register(() => readyTcs.TrySetCanceled(cancellationToken));
        ServerUri = await readyTcs.Task.ConfigureAwait(false);
        return ServerUri;
    }

    /// <summary>
    /// Rebuild one-shot: nessun contatore, nessun debounce, nessun riavvio del server - il
    /// server statico persistente legge sempre da disco ad ogni richiesta, quindi una build
    /// riuscita e' immediatamente visibile al prossimo refresh della pagina.
    /// </summary>
    public Task<bool> RebuildAsync(Action<string> onOutputLine, CancellationToken cancellationToken = default) =>
        RunDotnetBuildAsync(onOutputLine, cancellationToken);

    // Timeout con kill come rete di sicurezza (stessa lezione di ProjectPublisher, modulo
    // 12, dove pero' la causa esatta non era stata isolata). Qui e' stato isolato con
    // certezza maggiore in questa sessione: un piccolo harness standalone (fuori da
    // Ide.App) che lancia lo stesso identico `dotnet build` rileva l'uscita del processo
    // in ~2.7s senza alcun problema (sia con WaitForExitAsync sia con polling di
    // HasExited - provati entrambi, stesso esito); i file di output (blazor.boot.json,
    // ecc.) vengono scritti entro pochi secondi anche quando lanciato DENTRO Ide.App - ma
    // li' NESSUNA delle due strategie si accorge mai dell'uscita, per il timeout intero.
    // Ipotesi piu' probabile: GTK (backend Linux di Avalonia.Controls.WebView, visibile nei
    // warning "Gdk-WARNING" nei log) intercetta la notifica di terminazione del processo
    // figlio (SIGCHLD/waitpid) prima che .NET possa osservarla - un conflitto noto quando
    // si mescolano toolkit GTK e la gestione processi di .NET su Linux, e coerente col
    // fatto che ANCHE una lettura diretta e sincrona di HasExited fallisca, non solo
    // l'attesa asincrona. Spiegherebbe anche perche' DotnetWatchHost non ha mai sofferto di
    // questo (non aspettava mai l'uscita di `dotnet watch`, solo il suo output in
    // streaming) e perche' ProjectPublisher ha lo stesso sintomo per `dotnet publish`. Su
    // Windows (WebView2, nessun GTK, attesa di uscita processo via handle nativo, non
    // SIGCHLD) questo specifico conflitto non dovrebbe presentarsi. Il timeout e' comunque
    // tenuto breve (non i 3 minuti usati da ProjectPublisher per un publish Release, qui
    // una build Debug che funziona normalmente impiega pochi secondi) per non penalizzare
    // l'utente reale nel caso, improbabile ma non escluso, che si presenti anche li'.
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromSeconds(60);

    private async Task<bool> RunDotnetBuildAsync(Action<string> onOutputLine, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(_csprojPath!);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine($"[dotnet build] {e.Data}"); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine($"[dotnet build] {e.Data}"); };

        if (!process.Start())
            throw new InvalidOperationException("Impossibile avviare 'dotnet build'.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(BuildTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            onOutputLine($"[dotnet build] Timeout dopo {BuildTimeout.TotalSeconds:0}s, processo terminato.");
            return false;
        }

        return process.ExitCode == 0;
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
            // Il processo e' gia' terminato tra il controllo e la Kill: nulla da fare.
        }
    }

    // Il percorso assoluto di blazor-devserver.dll dipende dalla cartella pacchetti NuGet
    // dell'utente (personalizzabile via NuGet.config/NUGET_PACKAGES): va risolto leggendo
    // project.assets.json (generato dal restore implicito di `dotnet build` sopra), non
    // assunto fisso - stesso meccanismo con cui dotnet watch/dotnet run lo risolvono
    // internamente.
    private string ResolveDevServerDllPath()
    {
        var assetsPath = Path.Combine(_projectDirectory!, "obj", "project.assets.json");
        using var stream = File.OpenRead(assetsPath);
        using var doc = JsonDocument.Parse(stream);

        var packagesPath = doc.RootElement.GetProperty("project").GetProperty("restore").GetProperty("packagesPath").GetString()!;

        var libraries = doc.RootElement.GetProperty("libraries");
        var devServerEntry = libraries.EnumerateObject()
            .FirstOrDefault(p => p.Name.StartsWith("Microsoft.AspNetCore.Components.WebAssembly.DevServer/", StringComparison.OrdinalIgnoreCase));

        if (devServerEntry.Value.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException(
                "Microsoft.AspNetCore.Components.WebAssembly.DevServer non trovato in project.assets.json: " +
                "e' un riferimento del progetto Blazor WASM del template, dovrebbe essere sempre presente dopo il restore.");

        var relativePackagePath = devServerEntry.Value.GetProperty("path").GetString()!;
        return Path.Combine(packagesPath, relativePackagePath, "tools", "blazor-devserver.dll");
    }

    private void OnServerOutputLine(string? line, Action<string> onOutputLine, TaskCompletionSource<Uri> readyTcs)
    {
        if (line is null)
            return;

        onOutputLine($"[server] {line}");

        var match = ListeningOnRegex.Match(line);
        if (match.Success)
            readyTcs.TrySetResult(new Uri(match.Groups[1].Value));
    }

    public void Dispose()
    {
        if (_devServerProcess is null)
            return;

        try
        {
            if (!_devServerProcess.HasExited)
                _devServerProcess.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Il processo e' gia' terminato tra il controllo e la Kill: nulla da fare.
        }
        finally
        {
            _devServerProcess.Dispose();
            _devServerProcess = null;
        }
    }
}
