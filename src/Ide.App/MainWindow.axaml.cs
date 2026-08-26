using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Ide.Designer;
using VbControls.Abstractions;

namespace Ide.App;

public partial class MainWindow : Window
{
    private const string FormName = "DesignerForm";
    private const string FormNamespace = "BlazorPwaTemplate.Pages";

    private readonly DotnetWatchHost _watchHost = new();
    private readonly ComponentPluginLoader _componentLoader = new();
    private readonly Dictionary<string, Type> _componentTypesByControlType = new();
    private readonly List<PlacedControl> _placedControls = [];
    private readonly Dictionary<string, int> _fieldCounters = new();

    private string? _projectDirectory;
    private bool _designerFormOpened;
    private string _currentPagePath = string.Empty; // "" = home, "designerform" = form generato
    private bool _isRunning;
    private bool _publishInProgress;

    public MainWindow()
    {
        InitializeComponent();

        DesignerWebView.NavigationStarted += (_, e) =>
            Console.WriteLine($"[DesignerWebView] NavigationStarted: {e.Request}");
        DesignerWebView.NavigationCompleted += async (_, e) =>
        {
            Console.WriteLine($"[DesignerWebView] NavigationCompleted: {e.Request}");
            await InjectConsoleForwardingAsync();
        };

        // Modulo 11: abilita i DevTools nativi della WebView (WebKitGTK Inspector su
        // Linux, WebView2 DevTools su Windows) - apribili con click destro > Ispeziona,
        // o F12 dove il motore nativo lo supporta. Nessuna API cross-platform per
        // "aprirli ora" da codice: EnableDevTools sblocca la voce nel menu contestuale
        // nativo della WebView stessa.
        DesignerWebView.EnvironmentRequested += (_, e) => e.EnableDevTools = true;

        // Modulo 11: inoltra console.log/warn/error e gli errori JS non gestiti della
        // pagina reale nel pannello Output, tramite lo stesso canale invokeCSharpAction
        // gia' usato internamente da Avalonia.Controls.WebView per WebMessageReceived.
        DesignerWebView.WebMessageReceived += (_, e) =>
        {
            if (e.Body is null)
                return;

            ConsoleMessage? message = null;
            try
            {
                message = JsonSerializer.Deserialize<ConsoleMessage>(e.Body);
            }
            catch (JsonException)
            {
            }

            Dispatcher.UIThread.Post(() => AppendOutput(
                message is not null ? $"[console.{message.Level}] {message.Text}" : $"[console] {e.Body}"));
        };

        Closed += (_, _) =>
        {
            _watchHost.Dispose();
            _componentLoader.Dispose();
        };
        // Rete di sicurezza: se il processo termina senza passare da una chiusura pulita
        // della finestra (crash, kill del processo), evita comunque di lasciare orfano
        // il processo figlio `dotnet watch`.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _watchHost.Dispose();

        // Come per la Toolbox (modulo 6): ListBoxItem marca il pointer event come Handled
        // per la selezione prima che un gesture recognizer piu' in alto lo riconosca come
        // DoubleTapped, serve handledEventsToo per intercettarlo comunque.
        PlacedControlsList.AddHandler(InputElement.DoubleTappedEvent, OnPlacedControlDoubleTapped, handledEventsToo: true);

        DesignSurface.AddHandler(DragDrop.DragOverEvent, OnDesignSurfaceDragOver);
        DesignSurface.AddHandler(DragDrop.DropEvent, OnDesignSurfaceDrop);

        // Modulo 10: F5 = Run, esce dalla modalita' di design (vincolo architetturale n.5:
        // design mode e run mode sono lo stesso bundle, pilotato da un flag in query string).
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5)
                await RunAsync();
        };

        _ = StartDesignerAsync();
    }

    // Modulo 11: script iniettato ad ogni navigazione per intercettare console.*, errori
    // non gestiti e promise rifiutate, inoltrandoli all'host via invokeCSharpAction (la
    // stessa funzione JS globale che Avalonia.Controls.WebView inietta gia' per il canale
    // WebMessageReceived - non serve definirla noi).
    private const string ConsoleForwardingScript = """
        (function(){
            if (window.__ideConsoleHooked) return 'already-hooked';
            window.__ideConsoleHooked = true;

            function send(level, text) {
                try { invokeCSharpAction(JSON.stringify({ level: level, text: text })); } catch (e) {}
            }

            ['log', 'info', 'warn', 'error'].forEach(function (level) {
                var original = console[level];
                console[level] = function () {
                    var text = Array.prototype.slice.call(arguments).map(function (a) {
                        try { return typeof a === 'object' ? JSON.stringify(a) : String(a); }
                        catch (e) { return String(a); }
                    }).join(' ');
                    send(level, text);
                    original.apply(console, arguments);
                };
            });

            window.addEventListener('error', function (ev) {
                send('error', 'Uncaught: ' + ev.message + ' @ ' + ev.filename + ':' + ev.lineno);
            });
            window.addEventListener('unhandledrejection', function (ev) {
                send('error', 'Unhandled promise rejection: ' + ev.reason);
            });

            return 'hooked';
        })();
        """;

    private async Task InjectConsoleForwardingAsync()
    {
        try
        {
            await DesignerWebView.InvokeScript(ConsoleForwardingScript);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DesignerWebView] Impossibile agganciare la console: {ex.Message}");
        }
    }

    private void OnDevToolsMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        AppendOutput("DevTools abilitati: click destro sulla superficie di design > Ispeziona/Inspect Element (o F12 dove supportato dal motore nativo).");

    private sealed record ConsoleMessage(
        [property: JsonPropertyName("level")] string Level,
        [property: JsonPropertyName("text")] string Text);

    // Modulo 14 (sezione 2.2 di ARCHITECTURE.md): compila via Roslyn i componenti trovati
    // in {ProjectDir}/Components/ e ripopola la Toolbox. Un componente rotto viene
    // segnalato in Output e semplicemente escluso, non blocca l'IDE ne' gli altri.
    private void LoadComponents()
    {
        if (_projectDirectory is null)
            return;

        var componentsDirectory = Path.Combine(_projectDirectory, "Components");
        var errors = _componentLoader.Load(componentsDirectory);
        foreach (var error in errors)
            AppendOutput($"[Components] {error}");

        ToolboxList.Items.Clear();
        _componentTypesByControlType.Clear();

        foreach (var component in _componentLoader.Components.OrderBy(c => c.Category).ThenBy(c => c.DisplayName))
        {
            _componentTypesByControlType[component.ControlType] = component.VisualType;

            var item = new ListBoxItem { Tag = component.ControlType, Content = $"{component.Icon} {component.DisplayName}" };
            item.AddHandler(PointerPressedEvent, OnToolboxItemPointerPressed, handledEventsToo: true);
            ToolboxList.Items.Add(item);
        }

        AppendOutput($"Componenti caricati da Components/: {string.Join(", ", _componentTypesByControlType.Keys)}");
    }

    private void OnReloadComponentsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => LoadComponents();

    // Modulo 6: avvio del drag da un elemento della Toolbox. Nessuna persistenza su file
    // ancora (quella e' il modulo 7, il generatore di codice): qui si trasporta solo il
    // nome del tipo di controllo trascinato, come testo semplice (DataFormat.Text).
    private async void OnToolboxItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string controlType })
            return;

        var item = new DataTransferItem();
        item.Set(DataFormat.Text, controlType);

        var data = new DataTransfer();
        data.Add(item);

        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
    }

    private void OnDesignSurfaceDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.Text) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    // Modulo 7: il drop genera davvero i due file posseduti dal designer
    // (DesignerForm.razor / DesignerForm.razor.designer.cs), a partire dall'istanza
    // reale dell'aspetto (IDesignComponent - visuale o non-visuale, modulo 13) creata
    // qui: e' la stessa istanza su cui la Property Grid (modulo 8) riflette per
    // mostrarne ed editarne le proprieta'.
    private async void OnDesignSurfaceDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() is not { } controlType)
            return;

        if (_projectDirectory is null)
        {
            AppendOutput("Drop ignorato: il progetto Blazor non e' ancora pronto.");
            return;
        }

        var position = e.GetPosition(DesignSurface);
        var fieldName = NextFieldName(controlType);

        // Le dimensioni di default vengono dal costruttore del componente stesso (ogni
        // Visual imposta la propria LayoutBox di default): il designer sa solo dove e'
        // stato rilasciato, non quanto deve essere grande.
        var visual = CreateVisual(controlType);
        visual.LayoutBox.X = Math.Max(0, position.X - visual.LayoutBox.Width / 2);
        visual.LayoutBox.Y = Math.Max(0, position.Y - visual.LayoutBox.Height / 2);

        var placed = new PlacedControl(fieldName, controlType, visual);
        _placedControls.Add(placed);

        PlacedControlsList.Items.Add(fieldName);
        PlacedControlsList.SelectedItem = fieldName; // mostra subito le sue proprieta' nella grid

        AppendOutput($"Aggiunto {fieldName} ({controlType})");

        await RegenerateAndReloadAsync();
    }

    // Modulo 14: il tipo concreto non e' piu' noto a compile-time di Ide.App - viene
    // istanziato per reflection a partire da quanto scoperto in Components/.
    private IDesignComponent CreateVisual(string controlType)
    {
        if (!_componentTypesByControlType.TryGetValue(controlType, out var type))
            throw new NotSupportedException($"Tipo di controllo sconosciuto: {controlType}");

        return (IDesignComponent)Activator.CreateInstance(type)!;
    }

    private string NextFieldName(string controlType)
    {
        var shortName = controlType.StartsWith("Vb", StringComparison.Ordinal) ? controlType[2..] : controlType;
        var count = _fieldCounters.GetValueOrDefault(controlType, 0) + 1;
        _fieldCounters[controlType] = count;
        return $"{shortName}{count}";
    }

    // Stessi tipi che FormCodeGenerator.EmitValue sa serializzare (string/bool/int/double
    // - bool ha pero' il suo editor CheckBox dedicato, quindi qui arrivano solo gli altri).
    private static bool TryConvert(Type targetType, string? text, out object? value)
    {
        if (targetType == typeof(string))
        {
            value = text ?? string.Empty;
            return true;
        }
        if (targetType == typeof(int))
        {
            var ok = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i);
            value = i;
            return ok;
        }
        if (targetType == typeof(double))
        {
            var ok = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
            value = d;
            return ok;
        }

        value = null;
        return false;
    }

    // Modulo 8: Property Grid via reflection. Quando l'utente seleziona un controllo
    // nell'elenco, ricostruisce gli editor per le sue proprieta' [VisualProperty].
    private void OnPlacedControlSelected(object? sender, SelectionChangedEventArgs e)
    {
        PropertyEditorsPanel.Children.Clear();

        if (PlacedControlsList.SelectedItem is not string fieldName)
            return;

        var placed = _placedControls.FirstOrDefault(c => c.FieldName == fieldName);
        if (placed is null)
            return;

        string? currentCategory = null;
        foreach (var property in VisualPropertyReader.GetEditableProperties(placed.Visual))
        {
            var category = property.GetCustomAttributes(typeof(VisualPropertyAttribute), inherit: true)
                .Cast<VisualPropertyAttribute>()
                .First()
                .Category;

            if (category != currentCategory)
            {
                PropertyEditorsPanel.Children.Add(new TextBlock
                {
                    Text = category,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Margin = new Avalonia.Thickness(0, 8, 0, 0),
                });
                currentCategory = category;
            }

            PropertyEditorsPanel.Children.Add(new TextBlock { Text = property.Name });

            var currentValue = property.GetValue(placed.Visual);
            if (property.PropertyType == typeof(bool))
            {
                var checkBox = new CheckBox { IsChecked = currentValue as bool? };
                checkBox.IsCheckedChanged += async (_, _) =>
                {
                    property.SetValue(placed.Visual, checkBox.IsChecked ?? false);
                    await RegenerateAndReloadAsync();
                };
                PropertyEditorsPanel.Children.Add(checkBox);
            }
            else
            {
                var textBox = new TextBox { Text = currentValue?.ToString() ?? string.Empty };
                textBox.LostFocus += async (_, _) =>
                {
                    // Il testo digitato e' sempre una string: per proprieta' non-string
                    // (es. int IntervalMs) va convertita al tipo reale prima di SetValue,
                    // altrimenti ArgumentException non gestita crasha tutto l'IDE (bug
                    // trovato impostando VbTimer.IntervalMs). Un valore non convertibile si
                    // segnala in Output e lascia la proprieta' invariata, non crasha.
                    if (!TryConvert(property.PropertyType, textBox.Text, out var converted))
                    {
                        AppendOutput($"Valore non valido per {property.Name} ({property.PropertyType.Name}): \"{textBox.Text}\"");
                        return;
                    }

                    property.SetValue(placed.Visual, converted);
                    await RegenerateAndReloadAsync();
                };
                PropertyEditorsPanel.Children.Add(textBox);
            }
        }
    }

    // Modulo 9 (generalizzato oltre VbButton, per il modulo "finisci il timer"): doppio
    // click su un controllo -> genera l'handler dell'evento descritto da
    // ComponentEventInfo in {Form}.Behavior.cs (file dello sviluppatore, mai rigenerato
    // per intero) e collega l'evento nel markup/codice rigenerato.
    private async void OnPlacedControlDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (PlacedControlsList.SelectedItem is not string fieldName || _projectDirectory is null)
            return;

        var placed = _placedControls.FirstOrDefault(c => c.FieldName == fieldName);
        if (placed is null)
            return;

        var eventInfo = ComponentEventInfo.For(placed.ControlType);
        if (eventInfo is null)
        {
            AppendOutput($"{fieldName} ({placed.ControlType}): nessun evento gestito dal doppio click in questa fase.");
            return;
        }

        var methodName = $"{fieldName}{eventInfo.MethodSuffix}";
        var pagesDirectory = Path.Combine(_projectDirectory, "Pages");
        var behaviorPath = BehaviorFileGenerator.EnsureEventHandler(pagesDirectory, FormName, FormNamespace, methodName, eventInfo.IsAsync);

        if (!placed.HasEventHandler)
        {
            placed.HasEventHandler = true;
            AppendOutput($"Generato handler {methodName} in {Path.GetFileName(behaviorPath)}");
            await RegenerateAndReloadAsync();
        }
        else
        {
            AppendOutput($"{methodName} esiste gia' in {Path.GetFileName(behaviorPath)}: apri il file per modificarlo.");
        }
    }

    // Un componente (built-in o plugin) con una proprieta' che il generatore non sa
    // ancora serializzare non deve far crashare l'IDE: si segnala in Output e si lascia
    // il form nell'ultimo stato valido generato.
    private async Task RegenerateAndReloadAsync()
    {
        string razorPath, designerCsPath;
        try
        {
            var pagesDirectory = Path.Combine(_projectDirectory!, "Pages");
            (razorPath, designerCsPath) = FormCodeGenerator.Generate(pagesDirectory, FormName, FormNamespace, _placedControls);
        }
        catch (Exception ex)
        {
            AppendOutput($"Errore nella generazione del form: {ex.Message}");
            return;
        }

        AppendOutput($"Generato -> {Path.GetFileName(razorPath)}, {Path.GetFileName(designerCsPath)}");

        await ShowDesignerFormAsync();
    }

    // Dopo il primo drop naviga la WebView sulla pagina generata; dopo i drop successivi
    // la ricarica con Refresh() (dotnet watch ricompila il progetto quando i file
    // cambiano, ma abbiamo disattivato il suo browser-refresh interno, quindi il reload
    // lo pilotiamo qui: riassegnare Source con lo stesso Uri non riavvierebbe la pagina).
    // Attende il segnale di riavvio effettivo di Kestrel invece di un semplice delay: un
    // delay fisso navigava mentre `dotnet watch` stava ancora ricompilando/riavviando,
    // causando una race sui file di build (visto empiricamente durante lo sviluppo).
    private async Task ShowDesignerFormAsync()
    {
        var uri = await _watchHost.WaitForNextRestartAsync(TimeSpan.FromSeconds(20));
        if (uri is null)
        {
            AppendOutput("Timeout in attesa del rebuild di dotnet watch: la pagina potrebbe non essere aggiornata.");
            return;
        }

        _currentPagePath = "designerform";
        _isRunning = false; // un'edit nel designer riporta sempre in modalita' di design

        if (_designerFormOpened)
        {
            // Il server e' stato riavviato da dotnet watch ma l'URL e' identico a prima:
            // riassegnare Source non farebbe nulla (Avalonia salta il PropertyChanged se
            // il valore non cambia), serve un Refresh esplicito.
            DesignerWebView.Refresh();
        }
        else
        {
            DesignerWebView.Source = BuildUri(uri, design: true);
            _designerFormOpened = true;
        }
    }

    // Modulo 10: F5 esce dalla modalita' di design e naviga la stessa pagina senza il
    // flag `?design=true` - stesso bundle, nessuna ricompilazione (vincolo architetturale
    // n.5). "Stop" torna alla modalita' di design.
    private Task RunAsync()
    {
        if (_watchHost.ServerUri is null)
        {
            AppendOutput("Run (F5) ignorato: il server non e' ancora pronto.");
            return Task.CompletedTask;
        }

        _isRunning = true;
        AppendOutput("Run (F5): avvio senza la modalita' di design.");
        DesignerWebView.Source = BuildUri(_watchHost.ServerUri, design: false);
        return Task.CompletedTask;
    }

    private void OnRunMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _ = RunAsync();

    private void OnStopMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_watchHost.ServerUri is null || !_isRunning)
            return;

        _isRunning = false;
        AppendOutput("Stop: torno alla modalita' di design.");
        DesignerWebView.Source = BuildUri(_watchHost.ServerUri, design: true);
    }

    private Uri BuildUri(Uri serverUri, bool design) =>
        new($"{serverUri}{_currentPagePath}{(design ? "?design=true" : string.Empty)}");

    // Modulo 12: dotnet publish -c Release, isolato dall'intermediate directory di
    // dotnet watch (vedi ProjectPublisher) cosi' da poter pubblicare senza fermare la
    // sessione di design in corso. Verifica anche che l'output pubblicato contenga il
    // manifest e il service worker: e' cio' che rende la PWA installabile.
    private async void OnPublishClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_projectDirectory is null)
        {
            AppendOutput("Publish ignorato: il progetto non e' ancora pronto.");
            return;
        }

        if (_publishInProgress)
        {
            AppendOutput("Publish gia' in corso, attendi il completamento.");
            return;
        }

        _publishInProgress = true;
        PublishMenuItem.IsEnabled = false;
        AppendOutput("Publish: avvio 'dotnet publish -c Release' (puo' richiedere qualche minuto)...");

        try
        {
            // Output completo su file (puo' essere enorme per una solution intera in
            // Release): il pannello Output riceve solo il riepilogo e le righe di errore.
            var result = await ProjectPublisher.PublishAsync(_projectDirectory, TimeSpan.FromMinutes(5));

            AppendOutput($"Log completo: {result.LogFilePath}");
            foreach (var errorLine in result.ErrorLines.Take(20))
                AppendOutput($"[publish] {errorLine}");
            if (result.ErrorLines.Count > 20)
                AppendOutput($"... altre {result.ErrorLines.Count - 20} righe con 'error': vedi il log completo.");

            if (result.ExitCode != 0)
            {
                AppendOutput($"Publish fallito (exit code {result.ExitCode}).");
                return;
            }

            var wwwrootPath = Path.Combine(_projectDirectory, ProjectPublisher.PublishOutputRelative, "wwwroot");
            var hasManifest = File.Exists(Path.Combine(wwwrootPath, "manifest.webmanifest"));
            var hasServiceWorker = File.Exists(Path.Combine(wwwrootPath, "service-worker.js"));

            AppendOutput($"Publish completato -> {wwwrootPath}");
            AppendOutput(hasManifest && hasServiceWorker
                ? "PWA installabile: manifest.webmanifest e service-worker.js presenti nell'output."
                : $"Attenzione: manifest={hasManifest}, service worker={hasServiceWorker} - verificare l'output pubblicato.");
        }
        catch (Exception ex)
        {
            AppendOutput($"Errore durante il publish: {ex.Message}");
        }
        finally
        {
            _publishInProgress = false;
            PublishMenuItem.IsEnabled = true;
        }
    }

    private void AppendOutput(string line)
    {
        OutputTextBox.Text = OutputTextBox.Text is { Length: > 0 } existing
            ? $"{existing}{Environment.NewLine}{line}"
            : line;
    }

    // Modulo 5: avvia `dotnet watch` sul progetto Blazor come processo figlio e, quando
    // Kestrel e' pronto, punta la WebView all'URL reale (nessun URL hardcoded a priori).
    private async Task StartDesignerAsync()
    {
        _projectDirectory = Path.Combine(FindRepoRoot(), "templates", "BlazorPwaTemplate");
        LoadComponents(); // solo file locali, non serve attendere che dotnet watch sia pronto

        try
        {
            var uri = await _watchHost.StartAsync(_projectDirectory, "http://localhost:5245");
            await Dispatcher.UIThread.InvokeAsync(() => DesignerWebView.Source = BuildUri(uri, design: true));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DesignerHost] Errore nell'avvio di dotnet watch: {ex}");
        }
    }

    private static string FindRepoRoot()
    {
        // Pacchetto di release (scripts/package-release.sh): templates/ sta gia' accanto
        // all'eseguibile, non c'e' nessun IdeSolution.sln nello zip scaricato dall'utente.
        if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "templates", "BlazorPwaTemplate")))
            return AppContext.BaseDirectory;

        // Sviluppo: l'eseguibile sta in bin/Debug/net8.0/, risali fino alla radice della solution.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IdeSolution.sln")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"Impossibile trovare templates/BlazorPwaTemplate ne' accanto all'eseguibile ne' risalendo da {AppContext.BaseDirectory} fino a IdeSolution.sln.");
    }
}
