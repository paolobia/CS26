using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Ide.Designer;

namespace Ide.App;

public partial class MainWindow : Window
{
    private readonly DotnetWatchHost _watchHost = new();

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

    private void OnDesignSurfaceDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetText() is not { } controlType)
            return;

        var position = e.GetPosition(DesignSurface);
        AppendOutput($"Drop: {controlType} a ({position.X:0}, {position.Y:0})");
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
        var projectDirectory = Path.Combine(FindRepoRoot(), "templates", "BlazorPwaTemplate");

        try
        {
            var uri = await _watchHost.StartAsync(projectDirectory, "http://localhost:5245");
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
