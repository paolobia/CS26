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

    private const string VisualSorgenteOriginale = """
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

    private const string RazorSorgenteOriginale = """
        @namespace VbControls

        <button style="@Style">@Visual.Text</button>

        @code {
            [Parameter, EditorRequired]
            public VbButtonVisual Visual { get; set; } = null!;
        }
        """;

    [Fact]
    public void SaveAs_rinomina_classe_e_costruttore_nel_cs()
    {
        var saved = ComponentSourceEditor.SaveAs(
            VisualSorgenteOriginale, RazorSorgenteOriginale, _componentsDirectory,
            oldControlType: "VbButton", oldTypeName: "VbButtonVisual",
            "VbButtonRosso", "Bottone Rosso", "🟥", "Standard");

        Assert.Equal(Path.Combine(_componentsDirectory, "VbButtonRossoVisual.cs"), saved.VisualFilePath);
        var contenuto = File.ReadAllText(saved.VisualFilePath);

        Assert.Contains("public sealed class VbButtonRossoVisual : VisualComponentBase", contenuto);
        Assert.Contains("public VbButtonRossoVisual() => LayoutBox", contenuto);
        Assert.DoesNotContain("VbButtonVisual", contenuto);
    }

    [Fact]
    public void SaveAs_rinomina_il_riferimento_al_tipo_Visual_nel_razor()
    {
        var saved = ComponentSourceEditor.SaveAs(
            VisualSorgenteOriginale, RazorSorgenteOriginale, _componentsDirectory,
            oldControlType: "VbButton", oldTypeName: "VbButtonVisual",
            "VbButtonRosso", "Bottone Rosso", "🟥", "Standard");

        Assert.Equal(Path.Combine(_componentsDirectory, "VbButtonRosso.razor"), saved.RazorFilePath);
        var contenuto = File.ReadAllText(saved.RazorFilePath);

        Assert.Contains("public VbButtonRossoVisual Visual", contenuto);
        Assert.DoesNotContain("VbButtonVisual", contenuto);
    }

    [Fact]
    public void SaveAs_aggiunge_suffisso_Visual_se_mancante()
    {
        var saved = ComponentSourceEditor.SaveAs(
            VisualSorgenteOriginale, RazorSorgenteOriginale, _componentsDirectory,
            oldControlType: "VbButton", oldTypeName: "VbButtonVisual",
            "VbButtonRosso", "Bottone Rosso", "🟥", "Standard");

        Assert.EndsWith("VbButtonRossoVisual.cs", saved.VisualFilePath);
        Assert.EndsWith("VbButtonRosso.razor", saved.RazorFilePath);
    }

    [Fact]
    public void SaveAs_sostituisce_attributo_ToolboxComponent_esistente()
    {
        var saved = ComponentSourceEditor.SaveAs(
            VisualSorgenteOriginale, RazorSorgenteOriginale, _componentsDirectory,
            oldControlType: "VbButton", oldTypeName: "VbButtonVisual",
            "VbButtonRosso", "Bottone Rosso", "🟥", "CategoriaCustom");

        var contenuto = File.ReadAllText(saved.VisualFilePath);

        Assert.Contains("""[ToolboxComponent("Bottone Rosso", "🟥", "CategoriaCustom")]""", contenuto);
        Assert.DoesNotContain("Button\", \"🔘", contenuto);
    }

    [Fact]
    public void SaveAs_aggiunge_attributo_ToolboxComponent_se_assente()
    {
        const string visualSenzaAttributo = """
            namespace VbControls;

            public sealed class VbEsempioVisual
            {
                public VbEsempioVisual() { }
            }
            """;
        const string razorEsempio = """
            @namespace VbControls
            @code {
                [Parameter, EditorRequired]
                public VbEsempioVisual Visual { get; set; } = null!;
            }
            """;

        var saved = ComponentSourceEditor.SaveAs(
            visualSenzaAttributo, razorEsempio, _componentsDirectory,
            oldControlType: "VbEsempio", oldTypeName: "VbEsempioVisual",
            "VbEsempioNuovo", "Esempio Nuovo", "🧩", "Plugin");

        var contenuto = File.ReadAllText(saved.VisualFilePath);

        Assert.Contains("""[ToolboxComponent("Esempio Nuovo", "🧩", "Plugin")]""", contenuto);
        Assert.Contains("class VbEsempioNuovoVisual", contenuto);
    }

    [Fact]
    public void SaveAs_lancia_se_nome_non_e_identificatore_valido()
    {
        Assert.Throws<ArgumentException>(() =>
            ComponentSourceEditor.SaveAs(
                VisualSorgenteOriginale, RazorSorgenteOriginale, _componentsDirectory,
                "VbButton", "VbButtonVisual", "123 Nome Non Valido", "x", "🧩", "Plugin"));
    }

    [Fact]
    public void SaveAs_lancia_se_file_di_destinazione_esiste_gia()
    {
        Directory.CreateDirectory(_componentsDirectory);
        File.WriteAllText(Path.Combine(_componentsDirectory, "VbButtonRossoVisual.cs"), "// gia' esistente");

        Assert.Throws<InvalidOperationException>(() =>
            ComponentSourceEditor.SaveAs(
                VisualSorgenteOriginale, RazorSorgenteOriginale, _componentsDirectory,
                "VbButton", "VbButtonVisual", "VbButtonRosso", "x", "🧩", "Plugin"));
    }

    [Fact]
    public void SaveAs_lancia_se_la_classe_originale_non_si_trova_nel_cs()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ComponentSourceEditor.SaveAs(
                VisualSorgenteOriginale, RazorSorgenteOriginale, _componentsDirectory,
                "VbButton", "NomeCheNonEsiste", "VbNuovo", "x", "🧩", "Plugin"));
    }

    [Fact]
    public void SaveAs_lancia_se_il_razor_non_referenzia_il_tipo_originale()
    {
        const string razorScollegato = """
            @namespace VbControls
            <span>@Visual.Text</span>
            @code {
                public VbEsempioVisual Visual { get; set; } = null!;
            }
            """;

        Assert.Throws<InvalidOperationException>(() =>
            ComponentSourceEditor.SaveAs(
                VisualSorgenteOriginale, razorScollegato, _componentsDirectory,
                "VbButton", "VbButtonVisual", "VbButtonRosso", "x", "🧩", "Plugin"));
    }
}
