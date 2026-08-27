using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ide.Designer;

/// <summary>
/// Modulo 16: rinomina un componente (classe + costruttore + attributo
/// <see cref="VbControls.Abstractions.ToolboxComponentAttribute"/>) per "Salva come nuovo
/// componente..." dal doppio click sulla Toolbox. Un solo tipo per file - stessa convenzione
/// gia' rispettata da tutti i componenti esistenti in <c>Components/</c>.
/// Il salvataggio "in place" (stesso nome, stesso file) non passa da qui: e' un semplice
/// <c>File.WriteAllText</c> fatto direttamente dal chiamante.
/// </summary>
public static class ComponentSourceEditor
{
    public static string SaveAs(
        string sourceCode,
        string componentsDirectory,
        string oldTypeName,
        string newComponentName,
        string displayName,
        string icon,
        string category)
    {
        var newTypeName = newComponentName.EndsWith("Visual", StringComparison.Ordinal)
            ? newComponentName
            : newComponentName + "Visual";

        if (!SyntaxFacts.IsValidIdentifier(newTypeName))
            throw new ArgumentException($"'{newComponentName}' non e' un nome di componente valido.", nameof(newComponentName));

        var destinationPath = Path.Combine(componentsDirectory, $"{newTypeName}.cs");
        if (File.Exists(destinationPath))
            throw new InvalidOperationException($"Esiste gia' un componente chiamato '{newTypeName}' ({destinationPath}).");

        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => c.Identifier.Text == oldTypeName)
            .ToList();

        if (classDeclarations.Count != 1)
            throw new InvalidOperationException(
                $"Attesa esattamente una classe '{oldTypeName}' nel sorgente, trovate {classDeclarations.Count}.");

        var classDeclaration = classDeclarations[0];

        var newRoot = root.ReplaceNode(classDeclaration, RenameClass(classDeclaration, oldTypeName, newTypeName, displayName, icon, category));

        Directory.CreateDirectory(componentsDirectory);
        File.WriteAllText(destinationPath, newRoot.ToFullString());
        return destinationPath;
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
