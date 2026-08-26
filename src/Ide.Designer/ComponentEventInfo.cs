namespace Ide.Designer;

/// <summary>
/// Modulo 9 (esteso oltre VbButton): descrive, per tipo di controllo, quale evento il
/// doppio click sulla superficie di design deve collegare. Due modi di collegamento
/// esistono perche' i controlli visuali e non-visuali li espongono diversamente:
/// - <c>WireInMarkup = true</c> (es. VbButton): l'evento e' un parametro Blazor del
///   collante .razor (<c>EventCallback</c>), va collegato nel markup generato
///   (<c>&lt;VbButton OnClick="..." /&gt;</c>).
/// - <c>WireInMarkup = false</c> (es. VbTimer): l'evento e' un vero evento .NET
///   sull'istanza "Visual" stessa (<c>event Func&lt;Task&gt;? Tick</c>), va collegato in
///   codice (<c>Timer1.Tick += Timer1_Tick;</c>) in un <c>OnInitialized</c> generato.
/// Questa tabella e' cio' che rende <see cref="FormCodeGenerator"/> e il doppio click in
/// <c>MainWindow</c> generici rispetto al tipo di controllo, invece di avere "VbButton"
/// hardcoded come unico caso gestito.
/// </summary>
public static class ComponentEventInfo
{
    public sealed record Info(string EventName, string MethodSuffix, bool IsAsync, bool WireInMarkup);

    private static readonly Dictionary<string, Info> ByControlType = new()
    {
        ["VbButton"] = new Info(EventName: "OnClick", MethodSuffix: "_Click", IsAsync: false, WireInMarkup: true),
        ["VbTimer"] = new Info(EventName: "Tick", MethodSuffix: "_Tick", IsAsync: true, WireInMarkup: false),
    };

    public static Info? For(string controlType) =>
        ByControlType.TryGetValue(controlType, out var info) ? info : null;
}
