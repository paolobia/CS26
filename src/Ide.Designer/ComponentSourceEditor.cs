using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ide.Designer;

/// <summary>
/// Modulo 16: rinomina un componente per "Salva come nuovo componente..." dal doppio click
/// sulla Toolbox. Un componente e' la COPPIA indivisibile classe Visual (.cs) + componente
/// Blazor (.razor) che la renderizza: <see cref="SaveAs"/> crea SEMPRE entrambi i nuovi file
/// insieme, mai uno senza l'altro (bug reale trovato in sessione: un "pippo" con solo la
/// classe Visual duplicata produce un tag che Blazor non sa renderizzare). Un solo tipo per
/// file - stessa convenzione gia' rispettata da tutti i componenti esistenti in
/// <c>Components/</c>.
/// Il salvataggio "in place" (stesso nome, stessi file) non passa da qui: e' un semplice
/// <c>File.WriteAllText</c> su entrambi i file fatto direttamente dal chiamante.
/// </summary>
public static class ComponentSourceEditor
{
    public sealed record SaveAsResult(string VisualFilePath, string RazorFilePath);

    public static SaveAsResult SaveAs(
        string visualSourceCode,
        string razorSourceCode,
        string componentsDirectory,
        string oldControlType,
        string oldTypeName,
        string newComponentName,
        string displayName,
        string icon,
        string category)
    {
        var newTypeName = newComponentName.EndsWith("Visual", StringComparison.Ordinal)
            ? newComponentName
            : newComponentName + "Visual";
        var newControlType = newTypeName[..^"Visual".Length];

        if (!SyntaxFacts.IsValidIdentifier(newTypeName))
            throw new ArgumentException($"'{newComponentName}' non e' un nome di componente valido.", nameof(newComponentName));

        var visualDestinationPath = Path.Combine(componentsDirectory, $"{newTypeName}.cs");
        var razorDestinationPath = Path.Combine(componentsDirectory, $"{newControlType}.razor");
        if (File.Exists(visualDestinationPath) || File.Exists(razorDestinationPath))
            throw new InvalidOperationException($"Esiste gia' un componente chiamato '{newControlType}' in {componentsDirectory}.");

        var tree = CSharpSyntaxTree.ParseText(visualSourceCode);
        var root = tree.GetCompilationUnitRoot();

        var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => c.Identifier.Text == oldTypeName)
            .ToList();

        if (classDeclarations.Count != 1)
            throw new InvalidOperationException(
                $"Attesa esattamente una classe '{oldTypeName}' nel sorgente .cs, trovate {classDeclarations.Count}.");

        var classDeclaration = classDeclarations[0];

        var newRoot = root.ReplaceNode(classDeclaration, RenameClass(classDeclaration, oldTypeName, newTypeName, displayName, icon, category));

        // Il .razor non e' C# puro (mescola markup HTML e blocchi @code): niente Roslyn qui,
        // una sostituzione testuale sui confini di parola basta - oldTypeName e' un
        // identificatore specifico (es. "VbButtonVisual"), improbabile che comparia per
        // caso come sottostringa di qualcos'altro nel file.
        var newRazorSource = System.Text.RegularExpressions.Regex.Replace(
            razorSourceCode, $@"\b{System.Text.RegularExpressions.Regex.Escape(oldTypeName)}\b", newTypeName);

        if (newRazorSource == razorSourceCode)
            throw new InvalidOperationException(
                $"Il sorgente .razor non contiene nessun riferimento a '{oldTypeName}': verifica che sia davvero il file del componente '{oldControlType}'.");

        Directory.CreateDirectory(componentsDirectory);
        File.WriteAllText(visualDestinationPath, newRoot.ToFullString());
        File.WriteAllText(razorDestinationPath, newRazorSource);
        return new SaveAsResult(visualDestinationPath, razorDestinationPath);
    }

    private static ClassDeclarationSyntax RenameClass(
        ClassDeclarationSyntax classDeclaration,
        string oldTypeName,
        string newTypeName,
        string displayName,
        string icon,
        string category)
    {
        // Rinomina la dichiarazione della classe e ogni costruttore con lo stesso nome (in
        // C# un costruttore deve sempre chiamarsi come la sua classe).
        var renamed = classDeclaration.ReplaceToken(
            classDeclaration.Identifier,
            SyntaxFactory.Identifier(newTypeName).WithTriviaFrom(classDeclaration.Identifier));

        renamed = renamed.ReplaceNodes(
            renamed.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Where(c => c.Identifier.Text == oldTypeName),
            (ctor, _) => ctor.ReplaceToken(ctor.Identifier, SyntaxFactory.Identifier(newTypeName).WithTriviaFrom(ctor.Identifier)));

        return ReplaceToolboxComponentAttribute(renamed, displayName, icon, category);
    }

    private static ClassDeclarationSyntax ReplaceToolboxComponentAttribute(
        ClassDeclarationSyntax classDeclaration, string displayName, string icon, string category)
    {
        // Costruito riparsando un frammento di testo (invece di comporre a mano i nodi
        // AttributeArgumentSyntax/SeparatedList) per ottenere la stessa formattazione con
        // spazio dopo la virgola gia' in uso in tutti i componenti esistenti
        // (es. VbButtonVisual.cs: `[ToolboxComponent("Button", "🔘", "Standard")]`).
        var argumentList = SyntaxFactory.ParseAttributeArgumentList(
            $"({EmitStringLiteral(displayName)}, {EmitStringLiteral(icon)}, {EmitStringLiteral(category)})");
        var newAttribute = SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("ToolboxComponent"), argumentList);

        var existingList = classDeclaration.AttributeLists
            .FirstOrDefault(list => list.Attributes.Any(a => a.Name.ToString() is "ToolboxComponent" or "ToolboxComponentAttribute"));

        if (existingList is null)
        {
            var newList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(newAttribute))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
            return classDeclaration.WithAttributeLists(classDeclaration.AttributeLists.Insert(0, newList));
        }

        var existingAttribute = existingList.Attributes.First(a => a.Name.ToString() is "ToolboxComponent" or "ToolboxComponentAttribute");
        var replacedAttribute = newAttribute.WithTriviaFrom(existingAttribute);
        var replacedList = existingList.WithAttributes(existingList.Attributes.Replace(existingAttribute, replacedAttribute));

        return classDeclaration.WithAttributeLists(classDeclaration.AttributeLists.Replace(existingList, replacedList));
    }

    // Passa dal letterale C# vero e proprio (via SyntaxFactory.Literal, che gestisce
    // correttamente l'escaping) invece di costruire la stringa a mano con Replace("\"", "\\\""):
    // stesso principio gia' seguito da FormCodeGenerator.EmitValue.
    private static string EmitStringLiteral(string value) => SyntaxFactory.Literal(value).ToFullString();
}
