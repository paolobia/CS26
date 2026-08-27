using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ide.Designer;
using VbControls.Abstractions;

namespace Ide.App;

public partial class MainWindow : Window
{
    private const string FormName = "DesignerForm";
    private const string FormNamespace = "BlazorPwaTemplate.Pages";

    // Deve corrispondere alla dimensione della griglia di sfondo generata in
    // FormCodeGenerator.GenerateDesignerCs (GridBackgroundStyle), altrimenti i controlli
    // non cadrebbero visivamente sulle linee.
    private const double GridSize = 25;

    private static double SnapToGrid(double value) => Math.Round(value / GridSize) * GridSize;

    private readonly BlazorAppHost _appHost = new();
    private readonly ComponentPluginLoader _componentLoader = new();
    private readonly Dictionary<string, Type> _componentTypesByControlType = new();
    private readonly Dictionary<string, DiscoveredComponent> _discoveredComponentsByControlType = new();
    private readonly List<PlacedControl> _placedControls = [];
    private readonly Dictionary<string, int> _fieldCounters = new();

    private string? _projectDirectory;
    private bool _designerFormOpened;
    private string _currentPagePath = string.Empty; // "" = home, "designerform" = form generato
    private bool _isRunning;
    private bool _publishInProgress;
    private bool _syncInProgress;
    private bool _pendingSync;
    private ScrollViewer? _outputScrollViewer;
    private bool _outputAutoScroll = true;

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
        // Su Linux, Avalonia.Controls.WebView usa di default l'adapter GTK a finestra
        // nativa: una finestra nativa embedded disegna sempre sopra qualunque contenuto
        // Avalonia gestito che le si sovrapponga, indipendentemente dall'ordine Z
        // dichiarato ("airspace problem") - ne soffrono sia il BusyOverlay sopra la
        // WebView sia il popup a tendina del menu quando si estende sopra la sua area.
        // ExperimentalOffscreen forza l'adapter compositato invece di quello a finestra,
        // risolvendo il problema alla radice. E' marcata "Experimental" dal pacchetto
        // stesso: se risultasse instabile su un desktop reale, rimuovere questo blocco if.
        DesignerWebView.EnvironmentRequested += (_, e) =>
        {
            e.EnableDevTools = true;
            if (e is Avalonia.Platform.GtkWebViewEnvironmentRequestedEventArgs gtk)
                gtk.ExperimentalOffscreen = true;
        };

        // Modulo 11 + ponte click (vedi ClickForwardingScript sotto): un solo canale
        // WebMessageReceived porta sia i log della console inoltrata sia i click per il
        // piazzamento, distinti dal campo "kind" nel JSON.
        DesignerWebView.WebMessageReceived += (_, e) =>
        {
            DebugLog($"WebMessageReceived: body={e.Body ?? "(null)"}");

            if (e.Body is null)
                return;

            try
            {
                using var doc = JsonDocument.Parse(e.Body);
                if (doc.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() == "click")
                {
                    var x = doc.RootElement.GetProperty("x").GetDouble();
                    var y = doc.RootElement.GetProperty("y").GetDouble();
                    DebugLog($"WebMessageReceived: riconosciuto come click, x={x}, y={y}, _armedControlType={_armedControlType ?? "(null)"}");
                    Dispatcher.UIThread.Post(() => OnDesignSurfaceClickFromWebView(new Point(x, y)));
                    return;
                }
            }
            catch (JsonException ex)
            {
                DebugLog($"WebMessageReceived: JSON non valido ({ex.Message})");
            }

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
            _appHost.Dispose();
            _componentLoader.Dispose();
        };
        // Rete di sicurezza: se il processo termina senza passare da una chiusura pulita
        // della finestra (crash, kill del processo), evita comunque di lasciare orfano
        // il processo figlio del server di file statici.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _appHost.Dispose();

        // Come per la Toolbox (modulo 6): ListBoxItem marca il pointer event come Handled
        // per la selezione prima che un gesture recognizer piu' in alto lo riconosca come
        // DoubleTapped, serve handledEventsToo per intercettarlo comunque.
        PlacedControlsList.AddHandler(InputElement.DoubleTappedEvent, OnPlacedControlDoubleTapped, handledEventsToo: true);

        // Modulo 16: doppio click su un componente della Toolbox -> editor del sorgente.
        // Il primo click di norma arma comunque il piazzamento (OnToolboxSelectionChanged):
        // nessun conflitto reale, l'armamento resta finche' non si clicca sulla superficie.
        ToolboxList.AddHandler(InputElement.DoubleTappedEvent, OnToolboxComponentDoubleTapped, handledEventsToo: true);

        // handledEventsToo: la WebView dentro DesignSurface intercetta gli eventi pointer
        // per l'interazione con la pagina e li marca come gestiti - senza questo il click
        // sulla superficie non arriverebbe mai qui (stesso motivo per cui il vecchio
        // drag&drop nativo non funzionava in modo affidabile).
        DesignSurface.AddHandler(InputElement.PointerPressedEvent, OnDesignSurfacePointerPressed, handledEventsToo: true);

        // Auto-scroll del pannello Output: si aggancia allo ScrollViewer interno del
        // TextBox solo dopo che il template e' stato applicato (non e' disponibile prima).
        OutputTextBox.AttachedToVisualTree += (_, _) =>
        {
            _outputScrollViewer = OutputTextBox.FindDescendantOfType<ScrollViewer>();
            if (_outputScrollViewer is not null)
                _outputScrollViewer.PropertyChanged += OnOutputScrollChanged;
        };

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

    // Ponte per il piazzamento: su Windows, WebView2 e' una vera finestra nativa che
    // intercetta i click a livello di sistema operativo PRIMA che arrivino ad Avalonia -
    // OnDesignSurfacePointerPressed non riceve mai l'evento (stesso identico problema, mai
    // risolto, del vecchio drag&drop nativo che mostrava sempre il cursore "vietato" anche
    // dopo aver provato a disattivare IsHitTestVisible sulla WebView). Un listener JS
    // dentro la pagina stessa non ha questo problema: riceve il click e lo rimanda
    // all'host via lo stesso canale invokeCSharpAction gia' usato per i log della console.
    // Le coordinate sono relative a #ide-design-surface (generato da FormCodeGenerator),
    // le stesse usate da LayoutBox.X/Y - non serve nessuna conversione.
    // Il vecchio OnDesignSurfacePointerPressed (Avalonia) resta attivo in parallelo: se
    // dovesse ricevere l'evento per primo su una piattaforma dove i click NON sono
    // intercettati dalla WebView, consuma _armedControlType e questo messaggio arriva
    // comunque ma non fa nulla (armedControlType gia' null) - nessun doppio piazzamento.
    // BUG REALE trovato (log di debug beta6): #ide-design-surface esiste solo dentro
    // {FormName}.razor, generato da RegenerateFiles() - che a sua volta viene chiamato solo
    // DENTRO PlaceControl, cioe' solo DOPO un piazzamento gia' avvenuto. La primissima volta
    // (pagina Home vuota, nessun controllo mai piazzato) quel div non esiste ancora: il
    // vecchio codice faceva `if (!el) return;` e abbandonava il click in silenzio - un
    // vicolo cieco totale su Windows, dove il percorso Avalonia nativo non riceve mai
    // l'evento (vedi sopra): il primo controllo in assoluto non era MAI piazzabile.
    // Fallback su document.body quando il div non c'e' ancora: la pagina iniziale e' vuota
    // e occupa l'intero body, quindi le coordinate coincidono comunque con quelle che
    // avrebbe #ide-design-surface una volta generato.
    private const string ClickForwardingScript = """
        (function(){
            if (window.__ideClickHooked) return 'already-hooked';
            window.__ideClickHooked = true;

            document.addEventListener('click', function (ev) {
                var el = document.getElementById('ide-design-surface') || document.body;
                var rect = el.getBoundingClientRect();
                try
                {
                    invokeCSharpAction(JSON.stringify({
                        kind: 'click',
                        x: ev.clientX - rect.left,
                        y: ev.clientY - rect.top,
                    }));
                }
                catch (e) {}
            }, true);

            return 'hooked';
        })();
        """;

    private async Task InjectConsoleForwardingAsync()
    {
        try
        {
            var consoleResult = await DesignerWebView.InvokeScript(ConsoleForwardingScript);
            DebugLog($"InjectConsoleForwardingAsync: ConsoleForwardingScript -> {consoleResult}");
            var clickResult = await DesignerWebView.InvokeScript(ClickForwardingScript);
            DebugLog($"InjectConsoleForwardingAsync: ClickForwardingScript -> {clickResult}");
        }
        catch (Exception ex)
        {
            DebugLog($"InjectConsoleForwardingAsync: ECCEZIONE durante l'iniezione: {ex}");
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
    // I controlli comuni (VbButton, VbLabel, ...) vivono in src/VbControls/Components/,
    // condivisi fra tutti i progetti: vanno compilati insieme a quelli del progetto aperto,
    // cosi' sono sempre disponibili anche se il progetto non li duplica localmente.
    private void LoadComponents()
    {
        if (_projectDirectory is null)
            return;

        var componentsDirectory = Path.Combine(_projectDirectory, "Components");
        var commonComponentsDirectory = Path.Combine(FindRepoRoot(), "src", "VbControls", "Components");
        var errors = _componentLoader.Load(componentsDirectory, commonComponentsDirectory);
        foreach (var error in errors)
            AppendOutput($"[Components] {error}");

        ToolboxList.Items.Clear();
        _componentTypesByControlType.Clear();
        _discoveredComponentsByControlType.Clear();

        foreach (var component in _componentLoader.Components.OrderBy(c => c.Category).ThenBy(c => c.DisplayName))
        {
            _componentTypesByControlType[component.ControlType] = component.VisualType;
            _discoveredComponentsByControlType[component.ControlType] = component;

            var item = new ListBoxItem { Tag = component.ControlType, Content = $"{component.Icon} {component.DisplayName}" };
            ToolboxList.Items.Add(item);
        }

        AppendOutput($"Componenti caricati da Components/: {string.Join(", ", _componentTypesByControlType.Keys)}");
    }

    private void OnReloadComponentsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => LoadComponents();

    // Punto unico di sincronizzazione con la WebView: scrivere i file (RegenerateFiles) non
    // aspetta piu' il rebuild ne' aggiorna la WebView da solo, tocca a questo bottone.
    private async void OnForceRefreshClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DebugLog($"OnForceRefreshClicked: _syncInProgress={_syncInProgress}, _pendingSync={_pendingSync}");
        if (_syncInProgress)
        {
            AppendOutput("Sincronizzazione gia' in corso, attendi il completamento.");
            return;
        }

        _syncInProgress = true;
        // Disabilitato (non solo colorato): senza questo, click ripetuti per impazienza
        // durante un'attesa lunga producevano solo spam di "gia' in corso" in Output senza
        // nessun segnale visivo che il bottone stesse davvero facendo qualcosa - stesso
        // pattern gia' usato per PublishMenuItem durante il publish.
        ForceRefreshButton.IsEnabled = false;
        BusyOverlay.IsVisible = true;
        DebugLog("OnForceRefreshClicked: BusyOverlay.IsVisible = true");
        AppendOutput("Sincronizzazione...");
        try
        {
            // Se WaitForBuildSettledAsync (dentro ShowDesignerFormAsync) trova Generation
            // gia' avanzata, ritorna vero senza mai sospendere davvero: l'intero metodo
            // completerebbe nello stesso tick UI in cui e' iniziato, senza che Avalonia
            // abbia mai avuto occasione di disegnare un frame con l'overlay visibile (regressione
            // notata dall'utente dopo il fix delle race di Generation, che ha reso il percorso
            // "gia' sincronizzato" molto piu' frequente). Un giro forzato sul dispatcher a
            // priorita' di rendering garantisce almeno un frame visibile prima di procedere.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            DesignerWebView.Refresh();
            if (await ShowDesignerFormAsync())
            {
                _pendingSync = false;
                UpdateSyncButtonState();
            }
        }
        finally
        {
            _syncInProgress = false;
            ForceRefreshButton.IsEnabled = true;
            BusyOverlay.IsVisible = false;
            DebugLog("OnForceRefreshClicked: BusyOverlay.IsVisible = false");
        }
    }

    // Confermato empiricamente (anche dall'utente): ne' la WebView ne' la property grid si
    // ridisegnano da sole dopo un aggiornamento - serve un resize manuale della finestra.
    // Colpisce entrambe (la WebView e un pannello Avalonia nativo separato), quindi non e'
    // un problema del solo controllo WebView ma piu' probabilmente del compositor/driver
    // grafico di questo ambiente: InvalidateMeasure/Arrange/Visual mirati non bastano.
    // Si imita l'identico effetto di un resize reale (che si sa gia' funzionare) invece di
    // inseguire invalidazioni puntuali: allarga la finestra di 1px e la riporta indietro
    // un istante dopo, dando ad Avalonia il tempo di completare il ciclo di
    // misura/arrangiamento/repaint per la dimensione intermedia.
    private void InvalidateDesignerWebViewDisplay()
    {
        var originalWidth = Width;
        Width = originalWidth + 1;
        Dispatcher.UIThread.Post(() => Width = originalWidth, DispatcherPriority.Background);
    }

    // Rimuove il controllo selezionato e rigenera il form: FormCodeGenerator.Generate
    // riscrive sempre da zero a partire dall'intera lista _placedControls, quindi togliere
    // l'elemento prima di rigenerare basta. Se il controllo aveva gia' un handler evento in
    // {Form}.Behavior.cs (file mai riscritto automaticamente), lo stub resta orfano: comportamento
    // accettato, analogo a VB6/Delphi.
    private void OnDeletePlacedControlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (PlacedControlsList.SelectedItem is not string fieldName)
            return;

        _placedControls.RemoveAll(c => c.FieldName == fieldName);
        PlacedControlsList.Items.Remove(fieldName);
        PropertiesGrid.Children.Clear();
        MethodsGrid.Children.Clear();

        AppendOutput($"Rimosso {fieldName}");

        RegenerateFiles();
    }

    // Riordino dei controlli piazzati (richiesta esplicita dell'utente): l'ordine incide
    // sull'ordine dei tag nel .razor generato (FormCodeGenerator.GenerateRazor itera
    // _placedControls nell'ordine della lista) e quindi sullo z-order visivo di controlli
    // sovrapposti. _placedControls e PlacedControlsList.Items devono restare sincronizzati -
    // stesso indice, stesso ordine - altrimenti la selezione per FieldName smetterebbe di
    // corrispondere al controllo giusto.
    private void OnMoveControlUpClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveSelectedControl(-1);

    private void OnMoveControlDownClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveSelectedControl(1);

    private void MoveSelectedControl(int direction)
    {
        if (PlacedControlsList.SelectedItem is not string fieldName)
            return;

        var index = _placedControls.FindIndex(c => c.FieldName == fieldName);
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _placedControls.Count)
            return; // gia' primo/ultimo, o nessuna selezione valida: no-op silenzioso

        (_placedControls[index], _placedControls[newIndex]) = (_placedControls[newIndex], _placedControls[index]);
        PlacedControlsList.Items.RemoveAt(index);
        PlacedControlsList.Items.Insert(newIndex, fieldName);
        PlacedControlsList.SelectedItem = fieldName; // l'indice e' cambiato, il valore no

        RegenerateFiles();
    }

    // Sostituisce il drag&drop nativo (DragDrop.DoDragDropAsync), rivelatosi inaffidabile
    // su desktop reale (cursore sempre "vietato", causa mai isolata con certezza nonostante
    // due tentativi di fix mirati): un click su un elemento della Toolbox lo "arma", il
    // click successivo sulla superficie di design lo piazza li'. Piu' semplice, niente API
    // nativa di drag&drop coinvolta.
    private string? _armedControlType;

    private void OnToolboxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _armedControlType = (ToolboxList.SelectedItem as ListBoxItem)?.Tag as string;
        DebugLog($"OnToolboxSelectionChanged: SelectedItem={ToolboxList.SelectedItem?.GetType().Name ?? "(null)"}, _armedControlType={_armedControlType ?? "(null)"}");
    }

    // Modulo 16: doppio click su un componente della Toolbox -> editor della COPPIA di file
    // che lo definisce insieme (comune o di progetto): il componente Blazor .razor
    // (rendering/comportamento) e la classe Visual .cs (dati/proprieta'). I due file sono
    // trattati come un'unita' indivisibile - mai letti/scritti/duplicati separatamente -
    // per evitare esattamente il bug reale trovato in sessione (una classe Visual duplicata
    // senza il suo .razor produce un componente che Blazor non sa renderizzare). Disponibile
    // subito in Toolbox dopo il salvataggio, senza un passo di "Ricompila Componenti" separato.
    private async void OnToolboxComponentDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ToolboxList.SelectedItem is not ListBoxItem { Tag: string controlType })
            return;

        if (!_discoveredComponentsByControlType.TryGetValue(controlType, out var component))
            return;

        string razorSource, visualSource;
        try
        {
            razorSource = await File.ReadAllTextAsync(component.RazorFilePath);
            visualSource = await File.ReadAllTextAsync(component.SourceFilePath);
        }
        catch (IOException ex)
        {
            AppendOutput($"Impossibile leggere i file di {controlType}: {ex.Message}");
            return;
        }

        // Ciclo "riprova": un errore di ComponentSourceEditor.SaveAs (nome duplicato,
        // identificatore non valido, classe non trovata) non deve far perdere il testo
        // gia' editato - si riapre lo stesso editor con lo stesso contenuto finche' l'utente
        // non salva con successo o annulla.
        while (true)
        {
            var result = await new ComponentEditorWindow(
                component.RazorFilePath, razorSource, component.SourceFilePath, visualSource,
                component.DisplayName, component.Icon, component.Category)
                .ShowDialog<ComponentEditorResult?>(this);

            if (result is null)
                return; // annullato

            razorSource = result.RazorSourceCode;
            visualSource = result.VisualSourceCode;

            if (result.Action == ComponentEditorAction.Save)
            {
                await File.WriteAllTextAsync(component.RazorFilePath, razorSource);
                await File.WriteAllTextAsync(component.SourceFilePath, visualSource);
                AppendOutput($"Salvati {component.RazorFilePath} e {component.SourceFilePath}");
                LoadComponents();
                return;
            }

            var request = result.SaveAsRequest!;
            try
            {
                var componentsDirectory = Path.Combine(_projectDirectory!, "Components");
                var saved = ComponentSourceEditor.SaveAs(
                    visualSource, razorSource, componentsDirectory,
                    oldControlType: controlType, oldTypeName: component.VisualType.Name,
                    request.ComponentName, request.DisplayName, request.Icon, request.Category);
                AppendOutput($"Nuovo componente salvato in {saved.RazorFilePath} e {saved.VisualFilePath}");
                LoadComponents();
                return;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                AppendOutput($"Salvataggio come nuovo componente fallito: {ex.Message}");
                // torna in cima al ciclo: riapre l'editor con lo stesso testo per riprovare
            }
        }
    }

    private void OnDesignSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var position = e.GetPosition(DesignSurface);
        DebugLog($"OnDesignSurfacePointerPressed (Avalonia): position=({position.X},{position.Y}), _armedControlType={_armedControlType ?? "(null)"}");
        TryPlaceArmedControlAt(position);
    }

    // Chiamato dal ponte JS (ClickForwardingScript) quando il click nativo non arriva ad
    // Avalonia (WebView2 su Windows) - vedi commento su ClickForwardingScript.
    private void OnDesignSurfaceClickFromWebView(Point position)
    {
        DebugLog($"OnDesignSurfaceClickFromWebView (ponte JS): position=({position.X},{position.Y}), _armedControlType={_armedControlType ?? "(null)"}");
        TryPlaceArmedControlAt(position);
    }

    private void TryPlaceArmedControlAt(Point position)
    {
        if (_armedControlType is not { } controlType)
        {
            DebugLog("TryPlaceArmedControlAt: nessun controllo armato, ignoro il click.");
            return;
        }

        DebugLog($"TryPlaceArmedControlAt: piazzo controlType={controlType} a ({position.X},{position.Y})");

        // Un solo piazzamento per click sulla Toolbox (coerente con quanto descritto
        // dall'utente): per piazzarne un altro bisogna riselezionarlo.
        _armedControlType = null;
        ToolboxList.SelectedItem = null;

        PlaceControl(controlType, position);
    }

    // Modulo 7: genera davvero i due file posseduti dal designer (DesignerForm.razor /
    // DesignerForm.razor.designer.cs), a partire dall'istanza reale dell'aspetto
    // (IDesignComponent - visuale o non-visuale, modulo 13) creata qui: e' la stessa
    // istanza su cui la Property Grid (modulo 8) riflette per mostrarne ed editarne le
    // proprieta'.
    private void PlaceControl(string controlType, Point position)
    {
        DebugLog($"PlaceControl: controlType={controlType}, position=({position.X},{position.Y}), _projectDirectory={_projectDirectory ?? "(null)"}");

        if (_projectDirectory is null)
        {
            AppendOutput("Piazzamento ignorato: il progetto Blazor non e' ancora pronto.");
            return;
        }

        var fieldName = NextFieldName(controlType);

        // Le dimensioni di default vengono dal costruttore del componente stesso (ogni
        // Visual imposta la propria LayoutBox di default): il designer sa solo dove e'
        // stato cliccato, non quanto deve essere grande.
        var visual = CreateVisual(controlType);
        visual.LayoutBox.X = SnapToGrid(Math.Max(0, position.X - visual.LayoutBox.Width / 2));
        visual.LayoutBox.Y = SnapToGrid(Math.Max(0, position.Y - visual.LayoutBox.Height / 2));

        var placed = new PlacedControl(fieldName, controlType, visual);
        _placedControls.Add(placed);

        PlacedControlsList.Items.Add(fieldName);
        PlacedControlsList.SelectedItem = fieldName; // mostra subito le sue proprieta' nella grid

        AppendOutput($"Aggiunto {fieldName} ({controlType})");
        DebugLog($"PlaceControl: creato {fieldName}, LayoutBox=({visual.LayoutBox.X},{visual.LayoutBox.Y},{visual.LayoutBox.Width}x{visual.LayoutBox.Height})");

        RegenerateFiles();
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
        if (PlacedControlsList.SelectedItem is not string fieldName)
        {
            PropertiesGrid.Children.Clear();
            MethodsGrid.Children.Clear();
            return;
        }

        var placed = _placedControls.FirstOrDefault(c => c.FieldName == fieldName);
        if (placed is null)
        {
            PropertiesGrid.Children.Clear();
            MethodsGrid.Children.Clear();
            return;
        }

        RefreshPropertyGrid(placed);
    }

    // Fattorizzato fuori da OnPlacedControlSelected per poter aggiornare la grid (in
    // particolare le preview "<N righe>"/"<vuoto>" della sezione Metodi) anche dopo un
    // salvataggio dal modale (OpenMethodEditorAsync), non solo al cambio di selezione.
    // Divisa in due Grid distinti (PropertiesGrid/MethodsGrid, richiesta esplicita
    // dell'utente) invece di un unico Grid con un separatore manuale in mezzo.
    private void RefreshPropertyGrid(PlacedControl placed)
    {
        RefreshProperties(placed);
        RefreshMethods(placed);

        // Selezionare un controllo gia' piazzato non passa da RegenerateFiles/OnForceRefreshClicked
        // (che fanno gia' il nudge di ridisegno per altri casi): senza questo, la grid appena
        // popolata non si vedrebbe finche' non succede qualcos'altro che forzi un repaint.
        InvalidateDesignerWebViewDisplay();
    }

    private void RefreshProperties(PlacedControl placed)
    {
        PropertiesGrid.Children.Clear();
        PropertiesGrid.RowDefinitions.Clear();
        var row = 0;

        // "Nome" sempre in cima, presa da FieldName (non da [VisualProperty] come le altre
        // proprieta') e sola lettura: rinominare un controllo gia' piazzato richiederebbe
        // un refactoring coordinato di tutti i file generati + il codice sviluppatore che
        // referenzia il vecchio nome - fuori scope per ora.
        PropertiesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var nameLabel = new TextBlock
        {
            Text = "Nome",
            Margin = new Avalonia.Thickness(0, 0, 8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Grid.SetRow(nameLabel, row);
        Grid.SetColumn(nameLabel, 0);
        PropertiesGrid.Children.Add(nameLabel);

        var nameValue = new TextBox { Text = placed.FieldName, IsReadOnly = true, Opacity = 0.7 };
        Grid.SetRow(nameValue, row);
        Grid.SetColumn(nameValue, 1);
        PropertiesGrid.Children.Add(nameValue);
        row++;

        // Griglia piatta, senza raggruppamenti per categoria (richiesta esplicita
        // dell'utente: stile Visual Basic 6 - una property grid VB6 non separa le proprieta'
        // in sezioni "Aspetto"/"Dati"/ecc., e' un unico elenco). L'ordine resta quello di
        // dichiarazione delle proprieta' [VisualProperty] sulla classe Visual.
        foreach (var property in VisualPropertyReader.GetEditableProperties(placed.Visual))
        {
            PropertiesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBlock
            {
                Text = property.Name,
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            PropertiesGrid.Children.Add(label);

            var currentValue = property.GetValue(placed.Visual);
            Control editor;
            if (property.PropertyType == typeof(bool))
            {
                var checkBox = new CheckBox { IsChecked = currentValue as bool? };
                checkBox.IsCheckedChanged += (_, _) =>
                {
                    property.SetValue(placed.Visual, checkBox.IsChecked ?? false);
                    RegenerateFiles();
                };
                editor = checkBox;
            }
            else
            {
                var textBox = new TextBox { Text = currentValue?.ToString() ?? string.Empty };
                textBox.LostFocus += (_, _) =>
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
                    RegenerateFiles();
                };
                editor = textBox;
            }

            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            PropertiesGrid.Children.Add(editor);
            row++;
        }
    }

    private void RefreshMethods(PlacedControl placed)
    {
        MethodsGrid.Children.Clear();
        MethodsGrid.RowDefinitions.Clear();
        var row = 0;

        var pagesDirectory = Path.Combine(_projectDirectory!, "Pages");
        var behaviorPath = Path.Combine(pagesDirectory, $"{FormName}.Behavior.cs");

        // Costruttore/Distruttore sono universali (ogni controllo li ha, indipendentemente
        // dal tipo), poi seguono gli eventi specifici del tipo (nessuno, per i componenti
        // che ancora non ne espongono, es. VbLabel).
        var methodRows = new List<(string DisplayName, string ActualMethodName, bool IsAsync)>
        {
            (SpecialMethodNames.Constructor, SpecialMethodNames.ConstructorMethodName(placed.FieldName), false),
            (SpecialMethodNames.Destructor, SpecialMethodNames.DestructorMethodName(placed.FieldName), false),
        };
        methodRows.AddRange(ComponentEventInfo.ForAll(placed.ControlType)
            .Select(info => (info.EventName, $"{placed.FieldName}{info.MethodSuffix}", info.IsAsync)));

        foreach (var (displayName, actualMethodName, isAsync) in methodRows)
        {
            MethodsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var methodLabel = new TextBlock
            {
                Text = displayName,
                Margin = new Avalonia.Thickness(0, 0, 8, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetRow(methodLabel, row);
            Grid.SetColumn(methodLabel, 0);
            MethodsGrid.Children.Add(methodLabel);

            var methodInfo = MethodBodyEditor.ReadMethod(behaviorPath, actualMethodName);
            var previewBlock = new TextBlock
            {
                Text = methodInfo.Exists ? $"<{methodInfo.LineCount} righe>" : "<vuoto>",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetRow(previewBlock, row);
            Grid.SetColumn(previewBlock, 1);
            MethodsGrid.Children.Add(previewBlock);

            // TextBlock espone gia' l'evento routed DoubleTapped, non serve il pattern
            // AddHandler(..., handledEventsToo: true) usato per PlacedControlsList (quello
            // serviva perche' il ListBoxItem sottostante marcava l'evento come Handled).
            methodLabel.DoubleTapped += async (_, _) => await OpenMethodEditorAsync(placed, displayName, actualMethodName, isAsync);
            previewBlock.DoubleTapped += async (_, _) => await OpenMethodEditorAsync(placed, displayName, actualMethodName, isAsync);

            row++;
        }
    }

    // Apre il modale di editing per un metodo (evento del tipo, Costruttore o Distruttore):
    // legge il corpo attuale, e se l'utente non annulla lo scrive (MethodBodyEditor),
    // aggiorna WiredMethods (svuotare il corpo "scollega" l'evento) e rigenera i file -
    // niente refresh automatico della WebView, coerente col bottone "Aggiorna" manuale.
    private async Task OpenMethodEditorAsync(PlacedControl placed, string displayName, string actualMethodName, bool isAsync)
    {
        var pagesDirectory = Path.Combine(_projectDirectory!, "Pages");
        var behaviorPath = Path.Combine(pagesDirectory, $"{FormName}.Behavior.cs");
        var current = MethodBodyEditor.ReadMethod(behaviorPath, actualMethodName);

        var dialog = new MethodEditorWindow($"{placed.FieldName}.{displayName}", current.Body);
        var result = await dialog.ShowDialog<string?>(this);

        if (result is null)
            return; // Annulla: nessuna modifica.

        MethodBodyEditor.WriteMethodBody(pagesDirectory, FormName, FormNamespace, actualMethodName, isAsync, result);

        var hasCode = !string.IsNullOrWhiteSpace(result);
        if (hasCode)
            placed.WiredMethods.Add(displayName);
        else
            placed.WiredMethods.Remove(displayName);

        AppendOutput($"Salvato {actualMethodName} in {FormName}.Behavior.cs ({(hasCode ? $"{MethodBodyEditor.CountBodyLines(result)} righe" : "vuoto")})");

        RegenerateFiles();
        RefreshPropertyGrid(placed);
    }

    // Modulo 9 (generalizzato oltre VbButton, per il modulo "finisci il timer"): doppio
    // click su un controllo -> genera l'handler dell'evento descritto da
    // ComponentEventInfo in {Form}.Behavior.cs (file dello sviluppatore, mai rigenerato
    // per intero) e collega l'evento nel markup/codice rigenerato.
    private void OnPlacedControlDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (PlacedControlsList.SelectedItem is not string fieldName || _projectDirectory is null)
            return;

        var placed = _placedControls.FirstOrDefault(c => c.FieldName == fieldName);
        if (placed is null)
            return;

        // Scorciatoia verso il modale per l'evento "principale" del tipo (il primo/unico
        // registrato in ComponentEventInfo): la property grid sotto elenca TUTTI gli eventi
        // con doppio click -> modale, questo resta solo un accesso rapido per il caso
        // comune "un controllo, un evento" senza dover scorrere fino alla sezione Metodi.
        var eventInfo = ComponentEventInfo.For(placed.ControlType);
        if (eventInfo is null)
        {
            AppendOutput($"{fieldName} ({placed.ControlType}): nessun evento gestito dal doppio click in questa fase.");
            return;
        }

        _ = OpenMethodEditorAsync(placed, eventInfo.EventName, $"{fieldName}{eventInfo.MethodSuffix}", eventInfo.IsAsync);
    }

    // Scrive solo i file (veloce, sincrono) - non aspetta la rebuild ne' aggiorna la WebView:
    // con piu' modifiche ravvicinate, aspettare/ricaricare a ogni
    // singola modifica era lento e bloccava il flusso di lavoro. La sincronizzazione vera
    // e propria (attesa rebuild + refresh WebView) e' ora solo manuale, via il bottone
    // "Aggiorna" (OnForceRefreshClicked) - il bottone diventa rosso finche' non la premi.
    // Un componente (built-in o plugin) con una proprieta' che il generatore non sa ancora
    // serializzare non deve far crashare l'IDE: si segnala in Output e si lascia il form
    // nell'ultimo stato valido generato.
    private void RegenerateFiles()
    {
        DebugLog($"RegenerateFiles: controlli={_placedControls.Count}");

        string razorPath, designerCsPath;
        try
        {
            var pagesDirectory = Path.Combine(_projectDirectory!, "Pages");
            (razorPath, designerCsPath) = FormCodeGenerator.Generate(pagesDirectory, FormName, FormNamespace, _placedControls);
        }
        catch (Exception ex)
        {
            DebugLog($"RegenerateFiles: ECCEZIONE nella generazione: {ex}");
            AppendOutput($"Errore nella generazione del form: {ex.Message}");
            return;
        }

        AppendOutput($"Generato -> {Path.GetFileName(razorPath)}, {Path.GetFileName(designerCsPath)}");
        DebugLog($"RegenerateFiles: file scritti con successo ({razorPath}, {designerCsPath})");

        _pendingSync = true;
        UpdateSyncButtonState();
    }

    private void UpdateSyncButtonState()
    {
        if (_pendingSync)
            ForceRefreshButton.Background = Brushes.OrangeRed;
        else
            ForceRefreshButton.ClearValue(Button.BackgroundProperty); // torna al colore di tema di default
    }

    // Dopo il primo piazzamento naviga la WebView sulla pagina generata; dopo i successivi
    // la ricarica con Refresh() (il server di file statici e' persistente, non viene mai
    // riavviato - riassegnare Source con lo stesso Uri non ricaricherebbe la pagina).
    // Aspetta prima l'esito reale della build (BlazorAppHost.RebuildAsync: nessun riavvio
    // di processo, nessuna race - vedi commento su BlazorAppHost) invece di indovinare lo
    // stato da un output streaming come faceva il vecchio DotnetWatchHost.
    // Ritorna false se la build fallisce: il chiamante (OnForceRefreshClicked) usa questo per
    // NON azzerare _pendingSync/il bottone "Aggiorna" rosso - non siamo davvero sincronizzati.
    private async Task<bool> ShowDesignerFormAsync()
    {
        DebugLog("ShowDesignerFormAsync: avvio rebuild...");

        var buildOk = await _appHost.RebuildAsync(line => Dispatcher.UIThread.Post(() => AppendOutput(line)));
        DebugLog($"ShowDesignerFormAsync: RebuildAsync -> buildOk={buildOk}");
        if (!buildOk)
        {
            AppendOutput("Build fallita: la pagina potrebbe non essere aggiornata (vedi errori sopra).");
            return false;
        }

        var uri = _appHost.ServerUri!; // impostato da StartAsync, sempre non-null a questo punto
        DebugLog($"ShowDesignerFormAsync: navigo verso {uri}");

        _currentPagePath = "designerform";
        _isRunning = false; // un'edit nel designer riporta sempre in modalita' di design

        if (_designerFormOpened)
        {
            // Il server statico legge sempre da disco, ma la WebView potrebbe avere gia'
            // in cache la vecchia pagina/asset: riassegnare Source con lo stesso Uri non
            // farebbe nulla (Avalonia salta il PropertyChanged se il valore non cambia),
            // serve un Refresh esplicito.
            DesignerWebView.Refresh();
        }
        else
        {
            DesignerWebView.Source = BuildUri(uri, design: true);
            _designerFormOpened = true;
        }

        InvalidateDesignerWebViewDisplay();
        return true;
    }

    // Modulo 10: F5 esce dalla modalita' di design e naviga la stessa pagina senza il
    // flag `?design=true` - stesso bundle, nessuna ricompilazione (vincolo architetturale
    // n.5). "Stop" torna alla modalita' di design.
    private Task RunAsync()
    {
        if (_appHost.ServerUri is null)
        {
            AppendOutput("Run (F5) ignorato: il server non e' ancora pronto.");
            return Task.CompletedTask;
        }

        _isRunning = true;
        AppendOutput("Run (F5): avvio senza la modalita' di design.");
        DesignerWebView.Source = BuildUri(_appHost.ServerUri, design: false);
        InvalidateDesignerWebViewDisplay();
        return Task.CompletedTask;
    }

    private void OnRunMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _ = RunAsync();

    private void OnStopMenuClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_appHost.ServerUri is null || !_isRunning)
            return;

        _isRunning = false;
        AppendOutput("Stop: torno alla modalita' di design.");
        DesignerWebView.Source = BuildUri(_appHost.ServerUri, design: true);
        InvalidateDesignerWebViewDisplay();
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

    // Traccia di debug per l'intera catena click-to-place (selezione Toolbox -> click,
    // via Avalonia o via il ponte JS -> piazzamento -> sincronizzazione): visibile sul
    // terminale da cui parte l'IDE (dotnet run / Ide.App.exe da riga di comando), non nel
    // pannello Output. Serve a capire ESATTAMENTE dove si interrompe la catena su una
    // macchina reale, invece di continuare a ipotizzare - vanno letti in ordine con
    // timestamp per capire quali passaggi avvengono e quali no.
    private static void DebugLog(string message) =>
        Console.WriteLine($"[DEBUG {DateTime.Now:HH:mm:ss.fff}] {message}");

    private void AppendOutput(string line)
    {
        OutputTextBox.Text = OutputTextBox.Text is { Length: > 0 } existing
            ? $"{existing}{Environment.NewLine}{line}"
            : line;

        if (_outputAutoScroll)
        {
            // Il nuovo Extent non e' ancora disponibile subito dopo aver cambiato Text
            // nello stesso frame: serve rimandare lo scroll dopo che il layout si aggiorna.
            Dispatcher.UIThread.Post(() => _outputScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }
    }

    // Aggiorna _outputAutoScroll in base a dove si trova l'utente nel log: si disattiva
    // se scrolla via dal fondo, si riattiva da solo se torna in fondo - comportamento
    // standard di un log viewer.
    private void OnOutputScrollChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ScrollViewer.OffsetProperty || _outputScrollViewer is not { } scrollViewer)
            return;

        var distanceFromBottom = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
        _outputAutoScroll = distanceFromBottom <= 2;
    }

    private void OnClearOutputClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => OutputTextBox.Text = string.Empty;

    // Modulo 5 (rivisto: BlazorAppHost sostituisce dotnet watch): compila il progetto Blazor
    // e avvia il server di file statici come processo figlio persistente, poi punta la
    // WebView all'URL reale (nessun URL hardcoded a priori).
    private async Task StartDesignerAsync()
    {
        _projectDirectory = Path.Combine(FindRepoRoot(), "templates", "BlazorPwaTemplate");
        LoadComponents(); // solo file locali, non serve attendere che il server sia pronto

        BusyOverlay.IsVisible = true;
        DebugLog("StartDesignerAsync: BusyOverlay.IsVisible = true, build iniziale + avvio server...");
        try
        {
            var uri = await _appHost.StartAsync(_projectDirectory, "http://localhost:5245",
                line => Dispatcher.UIThread.Post(() => AppendOutput(line)));
            DebugLog($"StartDesignerAsync: server pronto su {uri}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DesignerWebView.Source = BuildUri(uri, design: true);
                InvalidateDesignerWebViewDisplay();
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DesignerHost] Errore nell'avvio del designer: {ex}");
            Dispatcher.UIThread.Post(() => AppendOutput($"Errore nell'avvio del designer: {ex.Message}"));
        }
        finally
        {
            BusyOverlay.IsVisible = false;
            DebugLog("StartDesignerAsync: BusyOverlay.IsVisible = false");
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
