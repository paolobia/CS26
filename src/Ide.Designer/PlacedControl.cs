using VbControls.Abstractions;

namespace Ide.Designer;

/// <summary>
/// Un controllo posizionato sulla superficie di design: nome del campo generato, tipo
/// Blazor (es. "VbButton" o, dal modulo 13, "VbHttpClient") e l'istanza reale
/// dell'aspetto (<see cref="IDesignComponent"/> - visuale o non-visuale).
/// Tenere l'istanza vera (non un DTO con i soli valori) e' cio' che permette alla
/// Property Grid (modulo 8) di riflettere sulle stesse proprieta' che il generatore di
/// codice (modulo 7) scrive su file.
/// </summary>
public sealed record PlacedControl(string FieldName, string ControlType, IDesignComponent Visual)
{
    /// <summary>
    /// Modulo 9 (generalizzato oltre VbButton): true se il doppio click ha gia' generato
    /// l'handler dell'evento descritto da <see cref="ComponentEventInfo.For"/> per questo
    /// controllo (es. <c>Click</c> per VbButton, <c>Tick</c> per VbTimer) in
    /// <c>{Form}.Behavior.cs</c>. Dice a <see cref="FormCodeGenerator"/> se collegare
    /// l'evento nel markup generato o in codice.
    /// </summary>
    public bool HasEventHandler { get; set; }
}
