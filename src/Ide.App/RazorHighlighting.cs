using System.IO;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace Ide.App;

/// <summary>
/// Registra un'evidenziazione sintattica "Razor" per l'editor sorgente dei componenti
/// (doppio click sulla Toolbox). AvaloniaEdit non ne include una propria (i file .razor
/// mescolano markup HTML e C#, un caso non coperto dalle definizioni XSHD integrate), ma
/// include gia' "ASP/XHTML" per lo scenario molto simile di ASP.NET classico (HTML +
/// blocchi <% %> di C#) - la stessa tecnica (una definizione XSHD che importa i ruleset
/// gia' registrati "HTML/" e "C#/" invece di reimplementarli) funziona identica per Razor,
/// con "@code { ... }" al posto di "&lt;% %&gt;". Verificato empiricamente in un harness
/// isolato prima di scriverlo qui: la definizione carica senza errori e produce sezioni
/// colorate sia per il markup HTML sia per il C# dentro @code.
/// </summary>
public static class RazorHighlighting
{
    private const string XshdDefinition = """
        <?xml version="1.0"?>
        <SyntaxDefinition name="Razor" extensions=".razor" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="RazorDirective" foreground="#C586C0" fontWeight="bold" />
          <RuleSet ignoreCase="false">
            <!-- Blocco @code { ... }: evidenziato come C# vero e proprio (RuleSet C#/ gia'
                 registrato da AvaloniaEdit stesso per i file .cs). L'euristica end="^\}" -
                 una graffa da sola a inizio riga - e' la stessa convenzione di indentazione
                 gia' usata da tutti i componenti esistenti in Components/. -->
            <Span ruleSet="RazorCSharp" multiline="true">
              <Begin>@code\s*\{</Begin>
              <End>^\}</End>
            </Span>
            <!-- Direttive (@page, @namespace, @inject, ...) ed espressioni inline
                 (@Visual.Text, @onclick, ...): stesso colore, nessuna distinzione fine fra
                 le due (non serve un vero parser Razor per dare comunque un riscontro
                 visivo utile). -->
            <Rule color="RazorDirective">@\w+(\.\w+)*</Rule>
            <Import ruleSet="HTML/" />
          </RuleSet>
          <RuleSet name="RazorCSharp">
            <Import ruleSet="C#/" />
          </RuleSet>
        </SyntaxDefinition>
        """;

    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
            return;

        using var stringReader = new StringReader(XshdDefinition);
        using var xmlReader = XmlReader.Create(stringReader);
        var xshd = HighlightingLoader.LoadXshd(xmlReader);
        var definition = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting("Razor", [".razor"], definition);

        _registered = true;
    }
}
