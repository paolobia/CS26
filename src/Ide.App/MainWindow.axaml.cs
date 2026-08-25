using System;
using System.IO;
using Avalonia.Controls;

namespace Ide.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var testPagePath = Path.Combine(AppContext.BaseDirectory, "DesignerTestPage", "index.html");
        Console.WriteLine($"[WebViewTest] Loading {testPagePath}, exists={File.Exists(testPagePath)}");

        DesignerWebView.NavigationStarted += (_, e) =>
            Console.WriteLine($"[WebViewTest] NavigationStarted: {e.Request}");
        DesignerWebView.NavigationCompleted += (_, e) =>
            Console.WriteLine($"[WebViewTest] NavigationCompleted: request={e.Request}");

        DesignerWebView.Source = new Uri(testPagePath);
    }
}