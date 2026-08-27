using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ide.App;

public enum ComponentEditorAction
{
    Save,
    SaveAs,
}

public sealed record SaveComponentAsRequest(string ComponentName, string DisplayName, string Icon, string Category);

public sealed record ComponentEditorResult(ComponentEditorAction Action, string SourceCode, SaveComponentAsRequest? SaveAsRequest);

/// <summary>
/// Modulo 16: modale aperto dal doppio click su un componente della Toolbox - mostra il
/// sorgente completo del file che lo definisce (comune o di progetto, il percorso e' sempre
/// visibile nell'header cosi' l'utente sa cosa sta modificando). "Salva" sovrascrive lo
/// stesso file; "Salva come nuovo componente..." apre <see cref="SaveComponentAsWindow"/> per
/// raccogliere nome/etichetta/icona/categoria del nuovo componente, che verra' scritto SEMPRE
/// in Components/ del progetto corrente (mai nella cartella comune), anche se il sorgente di
/// partenza era un componente comune.
/// Nessuna evidenziazione sintattica per ora (stessa scelta gia' fatta per
/// <see cref="MethodEditorWindow"/>): un errore si scopre al prossimo "Ricompila Componenti".
/// </summary>
public partial class ComponentEditorWindow : Window
{
    private readonly string _currentDisplayName = string.Empty;
    private readonly string _currentIcon = string.Empty;
    private readonly string _currentCategory = string.Empty;

    public ComponentEditorWindow()
    {
        InitializeComponent();
    }

    public ComponentEditorWindow(string filePath, string initialSource, string currentDisplayName, string currentIcon, string currentCategory)
        : this()
    {
        _currentDisplayName = currentDisplayName;
        _currentIcon = currentIcon;
        _currentCategory = currentCategory;

        Title = $"Modifica componente - {filePath}";
        HeaderText.Text = filePath;
        CodeTextBox.Text = initialSource;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e) =>
        Close(new ComponentEditorResult(ComponentEditorAction.Save, CodeTextBox.Text ?? string.Empty, null));

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        var request = await new SaveComponentAsWindow(_currentDisplayName, _currentIcon, _currentCategory).ShowDialog<SaveComponentAsRequest?>(this);
        if (request is null)
            return; // annullato dal sotto-modale: si resta sull'editor, nessuna modifica persa

        Close(new ComponentEditorResult(ComponentEditorAction.SaveAs, CodeTextBox.Text ?? string.Empty, request));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
