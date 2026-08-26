namespace Ide.Designer;

/// <summary>
/// Modulo 9: genera lo scheletro dell'handler in <c>{Form}.Behavior.cs</c> al doppio
/// click su un controllo, nel file posseduto dallo sviluppatore (vincolo architetturale
/// n.3). A differenza di <see cref="FormCodeGenerator"/>, questa classe non rigenera mai
/// il file per intero: lo crea solo se manca, e vi aggiunge solo il metodo mancante,
/// lasciando intatto tutto cio' che lo sviluppatore ci ha scritto sopra.
/// </summary>
public static class BehaviorFileGenerator
{
    /// <summary>
    /// Generalizzato oltre il solo "click" (modulo 9): <paramref name="isAsync"/> distingue
    /// uno stub <c>void</c> (es. VbButton.Click, un <c>EventCallback</c>) da uno
    /// <c>async Task</c> (es. VbTimer.Tick, un vero <c>event Func&lt;Task&gt;</c> .NET che
    /// richiede una firma awaitable).
    /// </summary>
    public static string EnsureEventHandler(string pagesDirectory, string formName, string razorNamespace, string methodName, bool isAsync)
    {
        Directory.CreateDirectory(pagesDirectory);
        var path = Path.Combine(pagesDirectory, $"{formName}.Behavior.cs");

        if (!File.Exists(path))
        {
            File.WriteAllText(path,
                "using System.Threading.Tasks;\n\n" +
                $"namespace {razorNamespace};\n\n" +
                $"public partial class {formName}\n" +
                "{\n" +
                "}\n");
        }

        var content = File.ReadAllText(path);
        if (content.Contains($" {methodName}(", StringComparison.Ordinal))
            return path; // l'handler c'e' gia': non lo tocchiamo, potrebbe contenere codice dello sviluppatore.

        // Un file creato prima che esistesse un evento async (es. da una IDE piu' vecchia)
        // potrebbe non avere ancora questo using: aggiungerlo qui evita un errore di
        // compilazione sorprendente al primo handler async richiesto su quel form.
        if (isAsync && !content.Contains("using System.Threading.Tasks;", StringComparison.Ordinal))
            content = "using System.Threading.Tasks;\n" + content;

        var closingBraceIndex = content.LastIndexOf('}');
        if (closingBraceIndex < 0)
            throw new InvalidOperationException($"{path} non sembra contenere una classe valida: correggerlo a mano prima di continuare.");

        var signature = isAsync ? $"private async Task {methodName}()" : $"private void {methodName}()";
        var handlerStub =
            $"    // TODO: implementa qui la logica dell'evento.\n" +
            $"    {signature}\n" +
            "    {\n" +
            "    }\n\n";

        content = content[..closingBraceIndex] + handlerStub + content[closingBraceIndex..];
        File.WriteAllText(path, content);

        return path;
    }
}
