using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Ide.Designer;

/// <summary>
/// Legge/scrive/conta righe del corpo di un metodo per nome in <c>{Form}.Behavior.cs</c>,
/// via Roslyn invece di manipolazione di stringhe - serve sia alla property grid (preview
/// "&lt;N righe&gt;"/"&lt;vuoto&gt;"), sia al modale di editing (leggere il corpo attuale,
/// scrivere quello nuovo). Sostituisce SOLO il testo dello span del corpo del metodo
/// (<c>SourceText.WithChanges</c>), mai l'intero file: lascia intatte formattazione,
/// commenti e tutto il resto scritto a mano dallo sviluppatore altrove nel file - stesso
/// vincolo gia' rispettato dalla vecchia logica in <see cref="BehaviorFileGenerator"/>.
/// </summary>
public static class MethodBodyEditor
{
    public sealed record MethodInfo(bool Exists, string Body, int LineCount);

    public static MethodInfo ReadMethod(string behaviorFilePath, string methodName)
    {
        if (!File.Exists(behaviorFilePath))
            return new MethodInfo(Exists: false, Body: string.Empty, LineCount: 0);

        var text = File.ReadAllText(behaviorFilePath);
        var method = FindMethod(text, methodName);
        if (method?.Body is not { } block)
            return new MethodInfo(Exists: false, Body: string.Empty, LineCount: 0);

        var body = ExtractBodyText(text, block);
        return new MethodInfo(Exists: true, Body: body, LineCount: CountBodyLines(body));
    }

    /// <summary>Righe non vuote del corpo - 0 per un corpo vuoto o solo whitespace.</summary>
    public static int CountBodyLines(string body) =>
        body.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));

    /// <summary>
    /// Scrive <paramref name="newBody"/> come corpo di <paramref name="methodName"/>: crea
    /// il file/il metodo se mancano (stesso schema di stub di
    /// <see cref="BehaviorFileGenerator"/>), altrimenti sostituisce solo il testo fra le
    /// graffe del metodo esistente.
    /// </summary>
    public static string WriteMethodBody(
        string pagesDirectory, string formName, string razorNamespace,
        string methodName, bool isAsync, string newBody)
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

        var text = File.ReadAllText(path);
        if (isAsync && !text.Contains("using System.Threading.Tasks;", StringComparison.Ordinal))
            text = "using System.Threading.Tasks;\n" + text;

        var method = FindMethod(text, methodName);
        var indentedBody = IndentBody(newBody);

        string newText;
        if (method?.Body is { } block)
        {
            // Sostituisce solo lo span fra le graffe esistenti, non l'intero file/metodo:
            // firma, attributi, commenti sopra restano byte-per-byte quelli scritti oggi.
            var innerSpan = TextSpan.FromBounds(block.OpenBraceToken.Span.End, block.CloseBraceToken.Span.Start);
            newText = SourceText.From(text).WithChanges(new TextChange(innerSpan, "\n" + indentedBody + "    ")).ToString();
        }
        else
        {
            var classNode = FindClass(text, formName)
                ?? throw new InvalidOperationException($"{path} non sembra contenere la classe {formName}: correggerlo a mano prima di continuare.");

            var signature = isAsync ? $"private async Task {methodName}()" : $"private void {methodName}()";
            var newMethodText = $"    {signature}\n    {{\n{indentedBody}    }}\n\n";

            var insertAt = classNode.CloseBraceToken.SpanStart;
            newText = text[..insertAt] + newMethodText + text[insertAt..];
        }

        File.WriteAllText(path, newText);
        return path;
    }

    private static string IndentBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var lines = body.Replace("\r\n", "\n").Split('\n');
        return string.Concat(lines
            .Where(line => line.Length > 0)
            .Select(line => $"        {line.TrimEnd()}\n"));
    }

    private static string ExtractBodyText(string sourceText, BlockSyntax block)
    {
        var innerSpan = TextSpan.FromBounds(block.OpenBraceToken.Span.End, block.CloseBraceToken.Span.Start);
        var raw = sourceText.Substring(innerSpan.Start, innerSpan.Length);
        // Rimuove l'indentazione fissa aggiunta da IndentBody, per mostrare nel modale
        // testo pulito invece che con 8 spazi davanti a ogni riga.
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines
                .Select(l => l.Length >= 8 && l[..8].All(c => c == ' ') ? l[8..] : l.TrimStart())
                .SkipWhile(string.IsNullOrWhiteSpace))
            .TrimEnd('\n', ' ');
    }

    private static MethodDeclarationSyntax? FindMethod(string text, string methodName) =>
        CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);

    private static ClassDeclarationSyntax? FindClass(string text, string className) =>
        CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className);
}
