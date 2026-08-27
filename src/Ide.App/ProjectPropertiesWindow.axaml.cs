using Avalonia.Controls;
using Avalonia.Interactivity;
using Ide.Designer;

namespace Ide.App;

/// <summary>
/// Modulo 17: proprieta' di progetto (Nome, Titolo app, Descrizione), persistite in
/// <c>project.ide.json</c> insieme allo stato dei controlli piazzati (vedi
/// <see cref="ProjectStateStore"/>). Riusata anche da "Nuovo Progetto" per raccogliere gli
/// stessi dati alla creazione, invece di un prompt di solo testo separato.
/// </summary>
public partial class ProjectPropertiesWindow : Window
{
    public ProjectPropertiesWindow()
    {
        InitializeComponent();
    }

    public ProjectPropertiesWindow(ProjectMetadata current) : this()
    {
        ProjectNameBox.Text = current.ProjectName;
        AppTitleBox.Text = current.AppTitle;
        DescriptionBox.Text = current.AppDescription;
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        var name = ProjectNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return; // niente nome, niente salvataggio - si resta sul modale per correggere

        Close(new ProjectMetadata(name, AppTitleBox.Text?.Trim() ?? string.Empty, DescriptionBox.Text?.Trim() ?? string.Empty));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
