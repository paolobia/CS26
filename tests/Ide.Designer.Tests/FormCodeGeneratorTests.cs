using Ide.Designer;
using VbControls;
using Xunit;

namespace Ide.Designer.Tests;

public class FormCodeGeneratorTests : IDisposable
{
    private readonly string _pagesDirectory = Path.Combine(Path.GetTempPath(), "FormCodeGeneratorTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_pagesDirectory))
            Directory.Delete(_pagesDirectory, recursive: true);
    }

    [Fact]
    public void Evento_WireInMarkup_compare_nel_razor_solo_se_wired()
    {
        var control = new PlacedControl("Button1", "VbButton", new VbButtonVisual());

        var (razorPath, _) = FormCodeGenerator.Generate(_pagesDirectory, "TestForm", "Test.Pages", [control]);
        var razorSenzaWiring = File.ReadAllText(razorPath);
        Assert.DoesNotContain("OnClick=", razorSenzaWiring);

        control.WiredMethods.Add("OnClick");
        FormCodeGenerator.Generate(_pagesDirectory, "TestForm", "Test.Pages", [control]);
        var razorConWiring = File.ReadAllText(razorPath);
        Assert.Contains("OnClick=\"Button1_Click\"", razorConWiring);
    }

    [Fact]
    public void Costruttore_cablato_genera_chiamata_in_OnInitialized()
    {
        var control = new PlacedControl("Button1", "VbButton", new VbButtonVisual());
        control.WiredMethods.Add(SpecialMethodNames.Constructor);

        var (_, designerCsPath) = FormCodeGenerator.Generate(_pagesDirectory, "TestForm", "Test.Pages", [control]);
        var content = File.ReadAllText(designerCsPath);

        Assert.Contains("protected override void OnInitialized()", content);
        Assert.Contains("Button1_Constructor();", content);
    }

    [Fact]
    public void Nessun_costruttore_o_evento_code_behind_non_genera_OnInitialized()
    {
        var control = new PlacedControl("Button1", "VbButton", new VbButtonVisual());

        var (_, designerCsPath) = FormCodeGenerator.Generate(_pagesDirectory, "TestForm", "Test.Pages", [control]);
        var content = File.ReadAllText(designerCsPath);

        Assert.DoesNotContain("OnInitialized", content);
    }

    [Fact]
    public void Distruttore_cablato_genera_chiamata_in_Dispose_e_classe_implementa_IDisposable()
    {
        var control = new PlacedControl("Button1", "VbButton", new VbButtonVisual());
        control.WiredMethods.Add(SpecialMethodNames.Destructor);

        var (_, designerCsPath) = FormCodeGenerator.Generate(_pagesDirectory, "TestForm", "Test.Pages", [control]);
        var content = File.ReadAllText(designerCsPath);

        Assert.Contains("public partial class TestForm : IDisposable", content);
        Assert.Contains("public void Dispose()", content);
        Assert.Contains("Button1_Destructor();", content);
    }

    [Fact]
    public void Nessun_distruttore_cablato_genera_comunque_Dispose_vuoto()
    {
        var control = new PlacedControl("Button1", "VbButton", new VbButtonVisual());

        var (_, designerCsPath) = FormCodeGenerator.Generate(_pagesDirectory, "TestForm", "Test.Pages", [control]);
        var content = File.ReadAllText(designerCsPath);

        // IDisposable sempre implementato (stabile), anche senza nessun Distruttore cablato.
        Assert.Contains("public partial class TestForm : IDisposable", content);
        Assert.Contains("public void Dispose()", content);
        Assert.DoesNotContain("_Destructor();", content);
    }

    [Fact]
    public void Evento_code_behind_VbTimer_Tick_genera_wiring_in_OnInitialized()
    {
        var control = new PlacedControl("Timer1", "VbTimer", new VbTimerVisual());
        control.WiredMethods.Add("Tick");

        var (_, designerCsPath) = FormCodeGenerator.Generate(_pagesDirectory, "TestForm", "Test.Pages", [control]);
        var content = File.ReadAllText(designerCsPath);

        Assert.Contains("Timer1.Tick += Timer1_Tick;", content);
    }
}
