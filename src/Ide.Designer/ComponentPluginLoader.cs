using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VbControls.Abstractions;

namespace Ide.Designer;

/// <summary>
/// Un componente scoperto in <c>{ProjectDir}/Components/</c>: nome del tag usato nel
/// markup generato (per convenzione, il nome della classe "Visual" senza il suffisso
/// "Visual" - es. <c>VbButtonVisual</c> -> <c>VbButton</c>), il tipo CLR concreto, e i
/// metadati per la Toolbox letti da <see cref="ToolboxComponentAttribute"/>.
/// </summary>
public sealed record DiscoveredComponent(string ControlType, Type VisualType, string DisplayName, string Icon, string Category);

/// <summary>
/// Modulo 14 (sezione 2.2 di ARCHITECTURE.md): compila in memoria, via Roslyn, i file
/// <c>*.cs</c> di <c>{ProjectDir}/Components/</c> - gli stessi file che il build normale
/// del progetto Blazor compila per il runtime reale - e li riflette per popolare Toolbox,
/// Property Grid e generatore di codice nel processo desktop di Ide.App.
///
/// Non compila mai i <c>.razor</c>: quelli servono solo al runtime reale (dotnet watch
/// li ricompila gia' da solo, essendo dentro l'albero del progetto), Ide.App non
/// renderizza mai Blazor (vincolo architetturale n.1).
///
/// Usa un <see cref="AssemblyLoadContext"/> collezionabile che delega sempre al contesto
/// di default per la risoluzione dei riferimenti: cosi' i tipi come <see cref="IDesignComponent"/>
/// nell'assembly dinamico sono la STESSA identita' di tipo gia' caricata in Ide.App (non
/// una copia separata), altrimenti IsAssignableFrom/i cast fallirebbero silenziosamente.
/// </summary>
public sealed class ComponentPluginLoader : IDisposable
{
    private sealed class ComponentsLoadContext() : AssemblyLoadContext("Ide.Components", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => null; // delega sempre al default ALC
    }

    private ComponentsLoadContext? _context;

    public IReadOnlyList<DiscoveredComponent> Components { get; private set; } = [];

    /// <summary>
    /// Ricompila da zero i componenti in <paramref name="componentsDirectory"/> (se
    /// esisteva un caricamento precedente, lo scarica prima - supporta "Reload
    /// Components" senza riavviare l'IDE). Un file che non compila viene segnalato negli
    /// errori restituiti e i suoi tipi vengono semplicemente esclusi: un plugin rotto non
    /// deve bloccare l'avvio dell'IDE ne' gli altri componenti gia' funzionanti.
    /// </summary>
    public IReadOnlyList<string> Load(string componentsDirectory)
    {
        Unload();

        if (!Directory.Exists(componentsDirectory))
            return [];

        // VbControls(.Abstractions) potrebbe non essere ancora stato caricato nel processo
        // se nulla nel codice di Ide.App ne ha ancora toccato un tipo direttamente: senza
        // questo, i componenti compilati qui sotto (che estendono VisualComponentBase
        // ecc.) non troverebbero quei tipi fra i riferimenti disponibili.
        EnsureAssemblyLoaded("VbControls.Abstractions.dll");
        EnsureAssemblyLoaded("VbControls.dll");

        var sourceFiles = Directory.GetFiles(componentsDirectory, "*.cs", SearchOption.AllDirectories);
        if (sourceFiles.Length == 0)
            return [];

        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();

        // Riferimenti: l'intera shared framework (System.Net.Http, System.Runtime, ...) -
        // un plugin puo' usare liberamente BCL che Ide.App non ha ancora mai caricato da
        // sola - unita agli assembly gia' caricati nel processo (VbControls,
        // VbControls.Abstractions, ...), che e' cio' che rende possibile la condivisione
        // di identita' di tipo con ComponentsLoadContext.Load sopra.
        var referencePaths = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var references = new List<MetadataReference>();
        foreach (var path in referencePaths)
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (IOException)
            {
                // Alcuni file nella cartella del runtime non sono assembly .NET validi
                // (o non sono leggibili): li si salta, non e' un errore fatale.
            }
            catch (BadImageFormatException)
            {
            }
        }

        var compilation = CSharpCompilation.Create(
            $"Ide.Components.Dynamic.{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var result = compilation.Emit(peStream);

        if (!result.Success)
        {
            return result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToArray();
        }

        peStream.Seek(0, SeekOrigin.Begin);
        _context = new ComponentsLoadContext();
        var assembly = _context.LoadFromStream(peStream);

        Components = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDesignComponent).IsAssignableFrom(t))
            .Select(ToDiscoveredComponent)
            .ToList();

        return [];
    }

    private static void EnsureAssemblyLoaded(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(path))
            Assembly.LoadFrom(path);
    }

    private static DiscoveredComponent ToDiscoveredComponent(Type type)
    {
        var controlType = type.Name.EndsWith("Visual", StringComparison.Ordinal)
            ? type.Name[..^"Visual".Length]
            : type.Name;

        var attribute = type.GetCustomAttribute<ToolboxComponentAttribute>();

        return new DiscoveredComponent(
            controlType,
            type,
            attribute?.DisplayName ?? controlType,
            attribute?.Icon ?? "🧩",
            attribute?.Category ?? "Plugin");
    }

    public void Unload()
    {
        Components = [];

        if (_context is null)
            return;

        _context.Unload();
        _context = null;
    }

    public void Dispose() => Unload();
}
