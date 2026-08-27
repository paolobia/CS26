using Ide.Designer;
using VbControls;
using VbControls.Abstractions;
using Xunit;

namespace Ide.Designer.Tests;

public class ProjectStateStoreTests : IDisposable
{
    private readonly string _projectDirectory = Path.Combine(Path.GetTempPath(), "ProjectStateStoreTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_projectDirectory))
            Directory.Delete(_projectDirectory, recursive: true);
    }

    private static IDesignComponent CreateVisual(string controlType) => controlType switch
    {
        "VbButton" => new VbButtonVisual(),
        "VbTimer" => new VbTimerVisual(),
        _ => throw new NotSupportedException(controlType),
    };

    [Fact]
    public void Load_su_progetto_mai_salvato_ritorna_null()
    {
        Directory.CreateDirectory(_projectDirectory);
        Assert.Null(ProjectStateStore.Load(_projectDirectory));
    }

    [Fact]
    public void Save_poi_Load_preserva_metadata_e_numero_controlli()
    {
        var button = new PlacedControl("Button1", "VbButton", new VbButtonVisual());
        var metadata = new ProjectMetadata("MioProgetto", "La mia app", "Descrizione di prova");

        ProjectStateStore.Save(_projectDirectory, metadata, [button]);
        var loaded = ProjectStateStore.Load(_projectDirectory);

        Assert.NotNull(loaded);
        Assert.Equal(metadata, loaded!.Metadata);
        Assert.Single(loaded.Controls);
    }

    [Fact]
    public void ToState_poi_FromState_preserva_LayoutBox_e_proprieta()
    {
        var button = new PlacedControl("Button1", "VbButton", new VbButtonVisual());
        button.Visual.LayoutBox.X = 40;
        button.Visual.LayoutBox.Y = 65;
        ((VbButtonVisual)button.Visual).Text = "Cliccami";

        var state = ProjectStateStore.ToState(button);
        var restored = ProjectStateStore.FromState(state, CreateVisual);

        Assert.Equal("Button1", restored.FieldName);
        Assert.Equal("VbButton", restored.ControlType);
        Assert.Equal(40, restored.Visual.LayoutBox.X);
        Assert.Equal(65, restored.Visual.LayoutBox.Y);
        Assert.Equal("Cliccami", ((VbButtonVisual)restored.Visual).Text);
    }

    [Fact]
    public void ToState_poi_FromState_preserva_WiredMethods()
    {
        var button = new PlacedControl("Button1", "VbButton", new VbButtonVisual());
        button.WiredMethods.Add("OnClick");
        button.WiredMethods.Add(SpecialMethodNames.Constructor);

        var restored = ProjectStateStore.FromState(ProjectStateStore.ToState(button), CreateVisual);

        Assert.Contains("OnClick", restored.WiredMethods);
        Assert.Contains(SpecialMethodNames.Constructor, restored.WiredMethods);
    }

    [Fact]
    public void FromState_con_proprieta_intera_ne_preserva_il_tipo()
    {
        var timer = new PlacedControl("Timer1", "VbTimer", new VbTimerVisual());
        ((VbTimerVisual)timer.Visual).IntervalMs = 2500;

        var restored = ProjectStateStore.FromState(ProjectStateStore.ToState(timer), CreateVisual);

        Assert.Equal(2500, ((VbTimerVisual)restored.Visual).IntervalMs);
    }
}
