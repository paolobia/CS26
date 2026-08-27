using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ide.App;

/// <summary>
/// Sotto-modale di <see cref="ComponentEditorWindow"/>: raccoglie nome/etichetta/icona/
/// categoria del nuovo componente. Nessun picker grafico per l'icona (stesso principio delle
/// icone gia' esistenti nel codice: una semplice stringa emoji digitata a mano). Il nome
/// visualizzato/categoria sono precompilati con i valori del componente di partenza come
/// default sensato; il nome del componente va sempre scelto dall'utente (non ha un default
/// ovvio: deve essere un identificatore univoco).
/// </summary>
public partial class SaveComponentAsWindow : Window
{
    public SaveComponentAsWindow()
    {
        InitializeComponent();
    }

    public SaveComponentAsWindow(string currentDisplayName, string currentIcon, string currentCategory) : this()
    {
        DisplayNameBox.Text = currentDisplayName;
        IconBox.Text = currentIcon;
        CategoryBox.Text = currentCategory;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        var componentName = ComponentNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(componentName))
            return; // niente nome, niente salvataggio - l'utente resta sul modale per correggere

        Close(new SaveComponentAsRequest(
            componentName,
            DisplayNameBox.Text?.Trim() is { Length: > 0 } displayName ? displayName : componentName,
            IconBox.Text?.Trim() is { Length: > 0 } icon ? icon : "🧩",
            CategoryBox.Text?.Trim() is { Length: > 0 } category ? category : "Plugin"));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
