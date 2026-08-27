namespace Ide.Designer;

/// <summary>
/// Modulo 9 (esteso oltre VbButton, poi generalizzato a "tutti gli eventi di un tipo", non
/// piu' uno solo): descrive, per tipo di controllo, gli eventi disponibili. Due modi di
/// collegamento esistono perche' i controlli visuali e non-visuali li espongono
/// diversamente:
/// - <c>WireInMarkup = true</c> (es. VbButton): l'evento e' un parametro Blazor del
///   collante .razor (<c>EventCallback</c>), va collegato nel markup generato
///   (<c>&lt;VbButton OnClick="..." /&gt;</c>).
/// - <c>WireInMarkup = false</c> (es. VbTimer): l'evento e' un vero evento .NET
///   sull'istanza "Visual" stessa (<c>event Func&lt;Task&gt;? Tick</c>), va collegato in
///   codice (<c>Timer1.Tick += Timer1_Tick;</c>) in un <c>OnInitialized</c> generato.
/// Questa tabella e' cio' che rende <see cref="FormCodeGenerator"/> e la property grid in
/// <c>MainWindow</c> generici rispetto al tipo di controllo, invece di avere "VbButton"
/// hardcoded come unico caso gestito.
///
/// Nota: solo i componenti che espongono davvero un evento nel proprio .razor/.cs sono
/// registrati qui (VbButton, VbTextBox, VbTextArea, VbTimer). VbLabel/VbHttpClient/
/// VbLocalStorage non hanno ancora eventi propri: aggiungerne di nuovi (es. OnFocus,
/// OnConnect) richiede prima estendere il componente stesso in VbControls/Components/,
/// e' fuori scope qui - questa tabella si limita a descrivere cio' che gia' esiste.
/// </summary>
public static class ComponentEventInfo
{
    public sealed record Info(string EventName, string MethodSuffix, bool IsAsync, bool WireInMarkup);

    private static readonly Dictionary<string, IReadOnlyList<Info>> ByControlType = new()
    {
        ["VbButton"] = [new Info(EventName: "OnClick", MethodSuffix: "_Click", IsAsync: false, WireInMarkup: true)],
        ["VbTimer"] = [new Info(EventName: "Tick", MethodSuffix: "_Tick", IsAsync: true, WireInMarkup: false)],
        // Il .razor di VbTextBox/VbTextArea ha gia' il parametro EventCallback<string>
        // ValueChanged: mancava solo la registrazione qui.
        ["VbTextBox"] = [new Info(EventName: "ValueChanged", MethodSuffix: "_ValueChanged", IsAsync: false, WireInMarkup: true)],
        ["VbTextArea"] = [new Info(EventName: "ValueChanged", MethodSuffix: "_ValueChanged", IsAsync: false, WireInMarkup: true)],
    };

    /// <summary>Compatibilita' con i chiamanti che vogliono un solo evento "principale" (il primo registrato).</summary>
    public static Info? For(string controlType) => ForAll(controlType).FirstOrDefault();

    public static IReadOnlyList<Info> ForAll(string controlType) =>
        ByControlType.TryGetValue(controlType, out var list) ? list : [];
}
