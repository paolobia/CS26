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

    // Confermato empiricamente (log reali dell'utente): per una modifica a .razor/.razor.cs
    // gia' tracciati, dotnet watch applica l'Hot Reload invece di un riavvio completo -
    // "Now listening on:" non ricompare MAI in questo caso. WaitForNextRestartAsync
    // aspettava solo quella riga, quindi andava sempre in timeout (20s) dopo ogni
    // piazzamento/modifica di proprieta', lasciando la WebView sulla pagina vecchia.
    private static readonly Regex HotReloadCompletedRegex =
        new(@"Hot reload of changes (succeeded|failed)\.|No hot reload changes to apply\.", RegexOptions.Compiled);

    // Debounce: FormCodeGenerator scrive .razor e .razor.designer.cs in rapida successione,
    // ma dotnet watch a volte li tratta come UN solo evento di modifica (un ciclo di hot
    // reload) e a volte come DUE separati (osservato nei log reali: il primo spesso produce
    // "No hot reload changes to apply." da solo, il secondo, poco dopo, "succeeded.") -
    // completare al primo segnale rischierebbe di dire "pronto" prima che il secondo file
    // sia stato davvero applicato. Si aspetta un breve intervallo di silenzio dopo l'ultimo
    // segnale rilevante prima di considerare il ciclo di build concluso.
    private static readonly TimeSpan SignalDebounce = TimeSpan.FromMilliseconds(700);

    private Process? _process;
    private TaskCompletionSource<Uri>? _pendingRestartTcs;
    private CancellationTokenSource? _debounceCts;

    public Uri? ServerUri { get; private set; }

    /// <summary>
    /// Avvia `dotnet watch` sul progetto indicato e attende che Kestrel sia pronto,
    /// restituendone l'URL effettivo.
    /// </summary>
    public async Task<Uri> StartAsync(string projectDirectory, string url, CancellationToken cancellationToken = default)
    {
        if (_process is not null)
            throw new InvalidOperationException("dotnet watch e' gia' stato avviato da questa istanza.");

        // --non-interactive evita che `dotnet watch` chieda conferma (Yes/No/Always/Never)
        // prima di riavviare quando modifica un file gia' tracciato senza poter usare il
        // suo browser-refresh (che qui e' disattivato): senza questa opzione il processo
        // resta bloccato in attesa di un input su stdin che non arrivera' mai.
        var startInfo = new ProcessStartInfo("dotnet", $"watch --non-interactive --urls {url}")
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

    /// <summary>
    /// Attende che dotnet watch abbia finito di elaborare l'ultima modifica ai file -
    /// tramite un riavvio completo (nuova "Now listening on:") oppure un ciclo di Hot
    /// Reload (che non riavvia il processo: <see cref="ServerUri"/> resta invariato). Utile
    /// dopo aver scritto file che il generatore di codice sa che forzeranno una
    /// ricompilazione. Se il timeout scade (es. perche' la build e' fallita in un modo che
    /// non produce nessuno dei due segnali), restituisce null: il chiamante decide come
    /// procedere.
    /// </summary>
    public async Task<Uri?> WaitForNextRestartAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRestartTcs = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        await using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        finally
        {
            _pendingRestartTcs = null;
        }
    }

    private void OnOutputLine(string? line, TaskCompletionSource<Uri> readyTcs)
    {
        if (line is null)
            return;

        Console.WriteLine($"[dotnet watch] {line}");

        var match = ListeningOnRegex.Match(line);
        if (match.Success)
        {
            var uri = new Uri(match.Groups[1].Value);
            ServerUri = uri;
            readyTcs.TrySetResult(uri);
            ScheduleRestartSignal();
            return;
        }

        if (HotReloadCompletedRegex.IsMatch(line))
            ScheduleRestartSignal();
    }

    // (Ri)pianifica il completamento di WaitForNextRestartAsync dopo un breve intervallo di
    // silenzio: ogni nuovo segnale rilevante annulla e riavvia l'attesa, cosi' due file
    // scritti quasi insieme (.razor + .razor.designer.cs) ma elaborati da dotnet watch come
    // eventi separati non completano l'attesa dopo il primo, troppo presto.
    // Pianifica solo se qualcuno sta davvero aspettando (_pendingRestartTcs non nullo):
    // altrimenti il debounce del "Now listening on" iniziale (StartAsync, quando nessuno
    // sta ancora chiamando WaitForNextRestartAsync) potrebbe restare in sospeso e
    // completare per sbaglio una WaitForNextRestartAsync successiva e scorrelata, appena
    // questa viene chiamata - visto accadere davvero in un test manuale.
    private void ScheduleRestartSignal()
    {
        if (_pendingRestartTcs is null)
            return;

        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = SignalAfterDebounceAsync(cts.Token);
    }

    private async Task SignalAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SignalDebounce, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return; // superato da un segnale piu' recente, questa pianificazione e' obsoleta
        }

        if (ServerUri is { } uri)
            _pendingRestartTcs?.TrySetResult(uri);
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
