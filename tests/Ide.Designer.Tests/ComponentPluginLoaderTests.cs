using Ide.Designer;
using VbControls.Abstractions;
using Xunit;

namespace Ide.Designer.Tests;

public class ComponentPluginLoaderTests
{
    private interface IEsempioInterfaccia
    {
        string Nome { get; set; }
        int Calcola(int x);
    }

    private sealed class ImplementazioneCompleta : IEsempioInterfaccia
    {
        public string Nome { get; set; } = "";
        public int Calcola(int x) => x;
    }

    private sealed class ImplementazioneSenzaMetodo
    {
        public string Nome { get; set; } = "";
    }

    private sealed class ImplementazioneRitornoSbagliato
    {
        public string Nome { get; set; } = "";
        public string Calcola(int x) => x.ToString();
    }

    [Fact]
    public void Implementazione_completa_non_riporta_problemi()
    {
        var problemi = ComponentPluginLoader.VerificaFirmaComponente(typeof(ImplementazioneCompleta), typeof(IEsempioInterfaccia));

        Assert.Empty(problemi);
    }

    [Fact]
    public void Metodo_mancante_viene_riportato()
    {
        var problemi = ComponentPluginLoader.VerificaFirmaComponente(typeof(ImplementazioneSenzaMetodo), typeof(IEsempioInterfaccia));

        Assert.Contains(problemi, p => p.Contains("Calcola"));
    }

    [Fact]
    public void Tipo_di_ritorno_diverso_viene_riportato()
    {
        var problemi = ComponentPluginLoader.VerificaFirmaComponente(typeof(ImplementazioneRitornoSbagliato), typeof(IEsempioInterfaccia));

        Assert.Contains(problemi, p => p.Contains("Calcola") && p.Contains("ritorna"));
    }

    [Fact]
    public void Componente_reale_valido_non_riporta_problemi_di_firma()
    {
        // VbButtonVisual-like: qui basta verificare un tipo qualunque del progetto che
        // implementa davvero IDesignComponent, per confermare che il controllo non produce
        // falsi positivi sul percorso reale (compilato dal compilatore C#, non da Roslyn
        // in-memory - ma la firma e' la stessa interfaccia).
        var problemi = ComponentPluginLoader.VerificaFirmaComponente(typeof(ComponenteDiProva), typeof(IDesignComponent));

        Assert.Empty(problemi);
    }

    private sealed class ComponenteDiProva : IDesignComponent
    {
        public string Id { get; set; } = "";
        public LayoutBox LayoutBox { get; set; } = new();
        public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
    }

    [Fact]
    public void Componenti_reali_del_template_compilano_senza_errori()
    {
        // Regressione: v0.2.1 ha rimosso VbControls.csproj (che referenziava
        // Microsoft.AspNetCore.Components.Web) senza accorgersi che il suo unico effetto
        // reale era far copiare Microsoft.JSInterop.dll nell'output - senza quel file,
        // VbLocalStorageVisual.cs (che usa IJSRuntime) non compilava, e poiche' tutti i file
        // di Components/ vengono compilati in un'unica CSharpCompilation, l'intera Toolbox
        // restava vuota (non solo il componente rotto). Questo test compila le stesse due
        // cartelle che MainWindow.LoadComponents() passa a Load() (progetto + controlli
        // comuni condivisi) e verifica che TUTTI i controlli comuni carichino.
        var repoRoot = TrovaRepoRoot();
        var componentsDirectory = Path.Combine(repoRoot, "templates", "BlazorPwaTemplate", "Components");
        var commonComponentsDirectory = Path.Combine(repoRoot, "src", "VbControls", "Components");

        using var loader = new ComponentPluginLoader();
        var errori = loader.Load(componentsDirectory, commonComponentsDirectory);

        Assert.Empty(errori);
        Assert.Equal(7, loader.Components.Count);
    }

    private static string TrovaRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IdeSolution.sln")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException($"IdeSolution.sln non trovato risalendo da {AppContext.BaseDirectory}.");
    }
}
