using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
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

        _ = StartDesignerAsync();
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
