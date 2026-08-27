using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit.Highlighting;

namespace Ide.App;

public enum ComponentEditorAction
{
    Save,
    SaveAs,
}

public sealed record SaveComponentAsRequest(string ComponentName, string DisplayName, string Icon, string Category);

public sealed record ComponentEditorResult(
    ComponentEditorAction Action, string RazorSourceCode, string VisualSourceCode, SaveComponentAsRequest? SaveAsRequest);

/// <summary>
/// Modulo 16: modale aperto dal doppio click su un componente della Toolbox - mostra il
/// sorgente completo della COPPIA di file che definisce il componente (comune o di progetto,
/// il percorso e' sempre visibile nell'header): il file `.razor` (rendering/comportamento -
/// quello che decide come si disegna e come reagisce agli eventi) e il file `.cs` della
/// classe Visual (dati/proprieta' - quello gia' esposto anche dalla Property Grid una volta
/// piazzato il controllo, per questo e' la seconda tab, non la prima).
///
/// La coppia e' trattata come un'unita' indivisibile ("piano A", scelto esplicitamente
/// dall'utente rispetto a fonderli in un unico file .razor - vedi discussione in sessione):
/// "Salva" sovrascrive ENTRAMBI i file agli stessi percorsi; "Salva come nuovo
/// componente..." (tramite <see cref="SaveComponentAsWindow"/> + <c>ComponentSourceEditor</c>)
/// crea SEMPRE entrambi i nuovi file insieme, mai uno senza l'altro - e' esattamente il bug
/// reale trovato in questa sessione (un "pippo" con solo la classe Visual duplicata, senza un
/// componente Blazor a renderizzarlo, quindi inutilizzabile) che questo vincolo elimina.
///
/// Evidenziazione sintattica: C# per la tab .cs (definizione integrata di AvaloniaEdit),
/// "Razor" (custom, <see cref="RazorHighlighting"/>) per la tab .razor.
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

    public ComponentEditorWindow(
        string razorFilePath, string razorSource, string visualFilePath, string visualSource,
        string currentDisplayName, string currentIcon, string currentCategory)
        : this()
    {
        _currentDisplayName = currentDisplayName;
        _currentIcon = currentIcon;
        _currentCategory = currentCategory;

        Title = $"Modifica componente - {razorFilePath}";
        HeaderText.Text = $"{razorFilePath}\n{visualFilePath}";

        RazorHighlighting.EnsureRegistered();
        RazorEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Razor");
        VisualEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");

        RazorEditor.Text = razorSource;
        VisualEditor.Text = visualSource;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e) =>
        Close(new ComponentEditorResult(ComponentEditorAction.Save, RazorEditor.Text, VisualEditor.Text, null));

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        var request = await new SaveComponentAsWindow(_currentDisplayName, _currentIcon, _currentCategory).ShowDialog<SaveComponentAsRequest?>(this);
        if (request is null)
            return; // annullato dal sotto-modale: si resta sull'editor, nessuna modifica persa

        Close(new ComponentEditorResult(ComponentEditorAction.SaveAs, RazorEditor.Text, VisualEditor.Text, request));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
