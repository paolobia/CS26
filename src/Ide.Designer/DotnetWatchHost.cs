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
    // "Now listening on:" non ricompare MAI in questo caso.
    private static readonly Regex HotReloadCompletedRegex =
        new(@"Hot reload of changes (succeeded|failed)\.|No hot reload changes to apply\.", RegexOptions.Compiled);

    // Debounce: FormCodeGenerator scrive .razor e .razor.designer.cs in rapida successione,
    // ma dotnet watch a volte li tratta come UN solo evento di modifica (un ciclo di hot
    // reload) e a volte come DUE separati (osservato nei log reali: il primo spesso produce
    // "No hot reload changes to apply." da solo, il secondo, poco dopo, "succeeded.") -
    // avanzare la generazione al primo segnale rischierebbe di dire "pronto" prima che il
    // secondo file sia stato davvero applicato. Si aspetta un breve intervallo di silenzio
    // dopo l'ultimo segnale rilevante prima di considerare il ciclo di build concluso.
    private static readonly TimeSpan SignalDebounce = TimeSpan.FromMilliseconds(700);

    private Process? _process;
    private CancellationTokenSource? _debounceCts;
    private TaskCompletionSource? _pendingGenerationTcs;

    public Uri? ServerUri { get; private set; }

    /// <summary>
    /// Contatore incrementato ogni volta che dotnet watch ha finito di elaborare una
    /// modifica (riavvio completo o ciclo di Hot Reload), SEMPRE - anche se in quel momento
    /// nessuno sta chiamando <see cref="WaitForBuildSettledAsync"/>. Essenziale per la
    /// sincronizzazione manuale (bottone "Aggiorna"): con file scritti immediatamente ma
    /// sincronizzazione posticipata a quando l'utente preme il bottone, il segnale di
    /// completamento di dotnet watch arriva quasi sempre PRIMA che qualcuno inizi ad
    /// aspettare - un design "un solo in ascolto alla volta" perderebbe quel segnale per
    /// sempre, causando un timeout garantito ad ogni pressione del bottone (bug reale
    /// osservato: introdotto proprio dalla combinazione fra la sync manuale e un fix
    /// precedente che ignorava i segnali quando nessuno era in attesa).
    /// </summary>
    public int Generation { get; private set; }

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
    /// Attende che dotnet watch abbia elaborato almeno un cambiamento successivo a
    /// <paramref name="sinceGeneration"/> (tipicamente <see cref="Generation"/> letto
    /// subito prima di scrivere i file che si vuole veder riflessi). Se il completamento e'
    /// gia' avvenuto PRIMA della chiamata (perche' l'utente ha aspettato prima di premere
    /// "Aggiorna"), ritorna true immediatamente, senza aspettare un nuovo segnale che
    /// potrebbe non arrivare mai. Ritorna false se il timeout scade senza nessun
    /// avanzamento (es. perche' la build e' fallita in un modo che non produce ne' un
    /// riavvio ne' un ciclo di hot reload).
    /// </summary>
    public async Task<bool> WaitForBuildSettledAsync(int sinceGeneration, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (Generation > sinceGeneration)
            return true;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingGenerationTcs = tcs;

        // Ricontrolla dopo aver agganciato il TCS: un avanzamento avvenuto esattamente fra
        // il check sopra e questa riga non deve andare perso.
        if (Generation > sinceGeneration)
        {
            _pendingGenerationTcs = null;
            return true;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        await using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            await tcs.Task.ConfigureAwait(false);
            return true;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        finally
        {
            _pendingGenerationTcs = null;
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

            // TrySetResult ritorna true solo la prima volta (il TCS di StartAsync non era
            // ancora completato): e' l'avvio iniziale, non un vero riavvio dovuto a una
            // modifica. Va avanzato SINCRONO, senza debounce: altrimenti un chiamante che
            // legge Generation subito dopo che StartAsync() e' tornato (prima che il
            // debounce di 700ms scada) lo scambierebbe per il completamento di una
            // modifica fatta molto dopo - visto accadere davvero in un test manuale
            // (Generation avanzava ~700ms dopo l'avvio, indipendentemente da qualunque
            // modifica reale, facendo tornare "sincronizzato" un piazzamento appena fatto).
            if (readyTcs.TrySetResult(uri))
                Generation++;
            else
                ScheduleGenerationAdvance(); // un riavvio vero, successivo - questo si' va debounced
            return;
        }

        if (HotReloadCompletedRegex.IsMatch(line))
            ScheduleGenerationAdvance();
    }

    // (Ri)pianifica l'avanzamento di Generation dopo un breve intervallo di silenzio: ogni
    // nuovo segnale rilevante annulla e riavvia l'attesa, cosi' due file scritti quasi
    // insieme (.razor + .razor.designer.cs) ma elaborati da dotnet watch come eventi
    // separati non avanzano la generazione dopo il primo, troppo presto. A differenza di
    // una versione precedente di questo codice, avanza SEMPRE (non solo se qualcuno sta
    // aspettando in quel momento) - vedi il commento su Generation per il perche'.
    private void ScheduleGenerationAdvance()
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = AdvanceGenerationAfterDebounceAsync(cts.Token);
    }

    private async Task AdvanceGenerationAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SignalDebounce, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return; // superato da un segnale piu' recente, questa pianificazione e' obsoleta
        }

        Generation++;
        _pendingGenerationTcs?.TrySetResult();
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
