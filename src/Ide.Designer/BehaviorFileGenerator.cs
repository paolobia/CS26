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
    public static string EnsureClickHandler(string pagesDirectory, string formName, string razorNamespace, string methodName)
    {
        Directory.CreateDirectory(pagesDirectory);
        var path = Path.Combine(pagesDirectory, $"{formName}.Behavior.cs");

        if (!File.Exists(path))
        {
            File.WriteAllText(path,
                $"namespace {razorNamespace};\n\n" +
                $"public partial class {formName}\n" +
                "{\n" +
                "}\n");
        }

        var content = File.ReadAllText(path);
        if (content.Contains($" {methodName}(", StringComparison.Ordinal))
            return path; // l'handler c'e' gia': non lo tocchiamo, potrebbe contenere codice dello sviluppatore.

        var closingBraceIndex = content.LastIndexOf('}');
        if (closingBraceIndex < 0)
            throw new InvalidOperationException($"{path} non sembra contenere una classe valida: correggerlo a mano prima di continuare.");

        var handlerStub =
            $"    // TODO: implementa la logica del click qui.\n" +
            $"    private void {methodName}()\n" +
            "    {\n" +
            "    }\n\n";

        content = content[..closingBraceIndex] + handlerStub + content[closingBraceIndex..];
        File.WriteAllText(path, content);

        return path;
    }
}
