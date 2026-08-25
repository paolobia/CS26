using System;
using Avalonia.Controls;

namespace Ide.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DesignerWebView.NavigationStarted += (_, e) =>
            Console.WriteLine($"[DesignerWebView] NavigationStarted: {e.Request}");
        DesignerWebView.NavigationCompleted += (_, e) =>
            Console.WriteLine($"[DesignerWebView] NavigationCompleted: {e.Request}");

        // Task 0.2: la WebView carica il progetto Blazor reale servito da `dotnet watch`
        // (in produzione l'URL/porta sara' determinato dinamicamente all'avvio di dotnet watch).
        DesignerWebView.Source = new Uri("http://localhost:5245/");
    }
}