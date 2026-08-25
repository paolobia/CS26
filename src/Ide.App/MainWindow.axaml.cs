using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Ide.Designer;

namespace Ide.App;

public partial class MainWindow : Window
{
    private const string FormName = "DesignerForm";
    private const string FormNamespace = "BlazorPwaTemplate.Pages";

    private static readonly IReadOnlyDictionary<string, (double Width, double Height)> DefaultSizeByControlType =
        new Dictionary<string, (double, double)>
        {
            ["VbButton"] = (120, 32),
            ["VbLabel"] = (150, 24),
            ["VbTextBox"] = (200, 28),
        };

    private readonly DotnetWatchHost _watchHost = new();
    private readonly List<PlacedControl> _placedControls = [];
    private readonly Dictionary<string, int> _fieldCounters = new();

    private string? _projectDirectory;
    private bool _designerFormOpened;

    public MainWindow()
    {
        InitializeComponent();

        DesignerWebView.NavigationStarted += (_, e) =>
            Console.WriteLine($"[DesignerWebView] NavigationStarted: {e.Request}");
        DesignerWebView.NavigationCompleted += (_, e) =>
            Console.WriteLine($"[DesignerWebView] NavigationCompleted: {e.Request}");

        Closed += (_, _) => _watchHost.Dispose();
        // Rete di sicurezza: se il processo termina senza passare da una chiusura pulita
        // della finestra (crash, kill del processo), evita comunque di lasciare orfano
        // il processo figlio `dotnet watch`.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _watchHost.Dispose();

        DesignSurface.AddHandler(DragDrop.DragOverEvent, OnDesignSurfaceDragOver);
        DesignSurface.AddHandler(DragDrop.DropEvent, OnDesignSurfaceDrop);

        // ListBoxItem consuma il PointerPressed per la selezione (Handled = true) prima
        // che un handler XAML ordinario venga invocato: serve handledEventsToo per
        // intercettarlo comunque e avviare il drag.
        foreach (var item in new[] { ToolboxButtonItem, ToolboxLabelItem, ToolboxTextBoxItem })
            item.AddHandler(PointerPressedEvent, OnToolboxItemPointerPressed, handledEventsToo: true);

        _ = StartDesignerAsync();
    }

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

    // Modulo 7: il drop non si limita piu' a loggare, genera davvero i due file
    // posseduti dal designer (DesignerForm.razor / DesignerForm.razor.designer.cs).
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
        var (width, height) = DefaultSizeByControlType[controlType];
        var fieldName = NextFieldName(controlType);

        _placedControls.Add(new PlacedControl(
            fieldName, controlType,
            X: Math.Max(0, position.X - width / 2),
            Y: Math.Max(0, position.Y - height / 2),
            Width: width, Height: height));

        var pagesDirectory = Path.Combine(_projectDirectory, "Pages");
        var (razorPath, designerCsPath) = FormCodeGenerator.Generate(pagesDirectory, FormName, FormNamespace, _placedControls);

        AppendOutput($"Generato {fieldName} ({controlType}) -> {Path.GetFileName(razorPath)}, {Path.GetFileName(designerCsPath)}");

        await ShowDesignerFormAsync();
    }

    private string NextFieldName(string controlType)
    {
        var shortName = controlType.StartsWith("Vb", StringComparison.Ordinal) ? controlType[2..] : controlType;
        var count = _fieldCounters.GetValueOrDefault(controlType, 0) + 1;
        _fieldCounters[controlType] = count;
        return $"{shortName}{count}";
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

        if (_designerFormOpened)
        {
            // Il server e' stato riavviato da dotnet watch ma l'URL e' identico a prima:
            // riassegnare Source non farebbe nulla (Avalonia salta il PropertyChanged se
            // il valore non cambia), serve un Refresh esplicito.
            DesignerWebView.Refresh();
        }
        else
        {
            DesignerWebView.Source = new Uri($"{uri}designerform");
            _designerFormOpened = true;
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

        try
        {
            var uri = await _watchHost.StartAsync(_projectDirectory, "http://localhost:5245");
            await Dispatcher.UIThread.InvokeAsync(() => DesignerWebView.Source = uri);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DesignerHost] Errore nell'avvio di dotnet watch: {ex}");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IdeSolution.sln")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException(
                $"Impossibile trovare IdeSolution.sln risalendo da {AppContext.BaseDirectory}.");
    }
}
