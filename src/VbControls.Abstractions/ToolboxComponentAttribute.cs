namespace VbControls.Abstractions;

/// <summary>
/// "Manifest" di un componente, come attributo sul codice stesso (nessun file di
/// manifest separato, coerente col vincolo architetturale n.4 - niente DSL intermedio).
/// Letto sia dai componenti built-in sia da quelli caricati come plugin da
/// <c>{ProjectDir}/Components/</c> (sezione 2.2 di ARCHITECTURE.md) per popolare la
/// Toolbox: nome visualizzato, icona/glifo, categoria di raggruppamento.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ToolboxComponentAttribute(string displayName, string icon, string category) : Attribute
{
    public string DisplayName { get; } = displayName;

    public string Icon { get; } = icon;

    public string Category { get; } = category;
}
