using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ide.App;

/// <summary>
/// Modale per editare il corpo di un singolo metodo (evento, Costruttore o Distruttore) di
/// un controllo piazzato - editor di solo testo per ora, niente evidenziazione sintattica
/// (rimandata a un giro futuro con AvaloniaEdit). Aperta con
/// <c>ShowDialog&lt;string?&gt;(owner)</c>: Salva chiude restituendo il testo digitato,
/// Annulla chiude restituendo null (nessuna modifica sul chiamante).
/// </summary>
public partial class MethodEditorWindow : Window
{
    public MethodEditorWindow()
    {
        InitializeComponent();
    }

    public MethodEditorWindow(string title, string initialBody) : this()
    {
        Title = $"Modifica {title}";
        HeaderText.Text = title;
        CodeTextBox.Text = initialBody;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e) => Close(CodeTextBox.Text ?? string.Empty);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
