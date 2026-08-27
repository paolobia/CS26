using System.Text.Json;
using System.Text.Json.Serialization;
using VbControls.Abstractions;

namespace Ide.Designer;

/// <summary>
/// Modulo 17: stato del design (quali controlli sono piazzati sul Form, con quali
/// proprieta') persistito su disco come JSON, separato dai file generati (.razor/
/// .razor.designer.cs) - quei file restano puramente derivati/rigenerabili (vincolo gia' in
/// vigore: mai letti indietro), questo file invece e' la vera fonte di verita' del design,
/// letta all'apertura del progetto per ricostruire <see cref="PlacedControl"/> senza dover
/// fare reverse-parsing del Blazor generato.
/// </summary>
public sealed record PlacedControlState(
    string FieldName,
    string ControlType,
    double X,
    double Y,
    double Width,
    double Height,
    Dictionary<string, string?> Properties,
    HashSet<string> WiredMethods);

public sealed record ProjectMetadata(string ProjectName, string AppTitle, string AppDescription);

public sealed record ProjectState(ProjectMetadata Metadata, List<PlacedControlState> Controls);

public static class ProjectStateStore
{
    public const string FileName = "project.ide.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Save(string projectDirectory, ProjectMetadata metadata, IReadOnlyList<PlacedControl> controls)
    {
        Directory.CreateDirectory(projectDirectory);
        var state = new ProjectState(metadata, controls.Select(ToState).ToList());
        File.WriteAllText(Path.Combine(projectDirectory, FileName), JsonSerializer.Serialize(state, JsonOptions));
    }

    public static ProjectState? Load(string projectDirectory)
    {
        var path = Path.Combine(projectDirectory, FileName);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<ProjectState>(File.ReadAllText(path));
    }

    // Usata anche dallo snapshot di Undo/Redo (stessa forma serializzabile, senza toccare
    // il file su disco - vedi UndoRedoManager).
    public static PlacedControlState ToState(PlacedControl control)
    {
        var properties = VisualPropertyReader.GetEditableProperties(control.Visual)
            .ToDictionary(p => p.Name, p => p.GetValue(control.Visual)?.ToString());

        return new PlacedControlState(
            control.FieldName, control.ControlType,
            control.Visual.LayoutBox.X, control.Visual.LayoutBox.Y, control.Visual.LayoutBox.Width, control.Visual.LayoutBox.Height,
            properties, [.. control.WiredMethods]);
    }

    // Ricostruisce un PlacedControl da uno stato salvato: createVisual e' passato dal
    // chiamante (MainWindow.CreateVisual) perche' solo li' si conosce la mappa
    // ControlType -> Type concreto scoperta da ComponentPluginLoader.
    public static PlacedControl FromState(PlacedControlState state, Func<string, IDesignComponent> createVisual)
    {
        var visual = createVisual(state.ControlType);
        visual.LayoutBox.X = state.X;
        visual.LayoutBox.Y = state.Y;
        visual.LayoutBox.Width = state.Width;
        visual.LayoutBox.Height = state.Height;

        foreach (var property in VisualPropertyReader.GetEditableProperties(visual))
        {
            if (!state.Properties.TryGetValue(property.Name, out var rawValue))
                continue;

            if (TryConvert(property.PropertyType, rawValue, out var converted))
                property.SetValue(visual, converted);
        }

        var control = new PlacedControl(state.FieldName, state.ControlType, visual);
        foreach (var method in state.WiredMethods)
            control.WiredMethods.Add(method);

        return control;
    }

    // Stessa conversione tollerante gia' usata da MainWindow per gli editor della property
    // grid (bool/int/double/string) - duplicata qui (non estratta in una utility condivisa)
    // perche' e' l'unico altro punto che ne ha bisogno, per ora.
    private static bool TryConvert(Type targetType, string? rawValue, out object? value)
    {
        try
        {
            if (targetType == typeof(string)) { value = rawValue ?? string.Empty; return true; }
            if (targetType == typeof(bool)) { value = bool.Parse(rawValue!); return true; }
            if (targetType == typeof(int)) { value = int.Parse(rawValue!); return true; }
            if (targetType == typeof(double)) { value = double.Parse(rawValue!); return true; }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException or OverflowException)
        {
        }

        value = null;
        return false;
    }
}
