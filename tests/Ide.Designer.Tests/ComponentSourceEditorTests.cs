using Ide.Designer;
using Xunit;

namespace Ide.Designer.Tests;

public class ComponentSourceEditorTests : IDisposable
{
    private readonly string _componentsDirectory = Path.Combine(Path.GetTempPath(), "ComponentSourceEditorTests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_componentsDirectory))
            Directory.Delete(_componentsDirectory, recursive: true);
    }

    private const string SorgenteOriginale = """
        using VbControls.Abstractions;

        namespace VbControls;

        [ToolboxComponent("Button", "🔘", "Standard")]
        public sealed class VbButtonVisual : VisualComponentBase
        {
            public VbButtonVisual() => LayoutBox = new LayoutBox { Width = 120, Height = 32 };

            [VisualProperty("Aspetto")]
            public string Text { get; set; } = "Button1";
        }
        """;

    [Fact]
    public void SaveAs_rinomina_classe_e_costruttore()
    {
        var destinazione = ComponentSourceEditor.SaveAs(
            SorgenteOriginale, _componentsDirectory, "VbButtonVisual", "VbButtonRosso", "Bottone Rosso", "🟥", "Standard");

        Assert.Equal(Path.Combine(_componentsDirectory, "VbButtonRossoVisual.cs"), destinazione);
        var contenuto = File.ReadAllText(destinazione);

        Assert.Contains("public sealed class VbButtonRossoVisual : VisualComponentBase", contenuto);
        Assert.Contains("public VbButtonRossoVisual() => LayoutBox", contenuto);
        Assert.DoesNotContain("VbButtonVisual", contenuto);
    }

    [Fact]
    public void SaveAs_aggiunge_suffisso_Visual_se_mancante()
    {
        var destinazione = ComponentSourceEditor.SaveAs(
            SorgenteOriginale, _componentsDirectory, "VbButtonVisual", "VbButtonRosso", "Bottone Rosso", "🟥", "Standard");

        Assert.EndsWith("VbButtonRossoVisual.cs", destinazione);
    }

    [Fact]
    public void SaveAs_sostituisce_attributo_ToolboxComponent_esistente()
    {
        var destinazione = ComponentSourceEditor.SaveAs(
            SorgenteOriginale, _componentsDirectory, "VbButtonVisual", "VbButtonRosso", "Bottone Rosso", "🟥", "CategoriaCustom");

        var contenuto = File.ReadAllText(destinazione);

        Assert.Contains("""[ToolboxComponent("Bottone Rosso", "🟥", "CategoriaCustom")]""", contenuto);
        Assert.DoesNotContain("Button\", \"🔘", contenuto);
    }

    [Fact]
    public void SaveAs_aggiunge_attributo_ToolboxComponent_se_assente()
    {
        const string senzaAttributo = """
            namespace VbControls;

            public sealed class VbEsempioVisual
            {
                public VbEsempioVisual() { }
            }
            """;

        var destinazione = ComponentSourceEditor.SaveAs(
            senzaAttributo, _componentsDirectory, "VbEsempioVisual", "VbEsempioNuovo", "Esempio Nuovo", "🧩", "Plugin");

        var contenuto = File.ReadAllText(destinazione);

        Assert.Contains("""[ToolboxComponent("Esempio Nuovo", "🧩", "Plugin")]""", contenuto);
        Assert.Contains("class VbEsempioNuovoVisual", contenuto);
    }

    [Fact]
    public void SaveAs_lancia_se_nome_non_e_identificatore_valido()
    {
        Assert.Throws<ArgumentException>(() =>
            ComponentSourceEditor.SaveAs(SorgenteOriginale, _componentsDirectory, "VbButtonVisual", "123 Nome Non Valido", "x", "🧩", "Plugin"));
    }

    [Fact]
    public void SaveAs_lancia_se_file_di_destinazione_esiste_gia()
    {
        Directory.CreateDirectory(_componentsDirectory);
        File.WriteAllText(Path.Combine(_componentsDirectory, "VbButtonRossoVisual.cs"), "// gia' esistente");

        Assert.Throws<InvalidOperationException>(() =>
            ComponentSourceEditor.SaveAs(SorgenteOriginale, _componentsDirectory, "VbButtonVisual", "VbButtonRosso", "x", "🧩", "Plugin"));
    }

    [Fact]
    public void SaveAs_lancia_se_la_classe_originale_non_si_trova()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ComponentSourceEditor.SaveAs(SorgenteOriginale, _componentsDirectory, "NomeCheNonEsiste", "VbNuovo", "x", "🧩", "Plugin"));
    }
}
