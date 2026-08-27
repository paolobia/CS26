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
    /// Nomi logici (es. "OnClick", <see cref="SpecialMethodNames.Constructor"/>) dei metodi
    /// che hanno gia' del codice non-vuoto in <c>{Form}.Behavior.cs</c>. Dice a
    /// <see cref="FormCodeGenerator"/> quali eventi collegare nel markup/OnInitialized e
    /// quali Costruttori/Distruttori chiamare - sostituisce il vecchio singolo
    /// <c>HasEventHandler</c> (un solo evento per tipo non basta piu' con la property grid
    /// che elenca tutti gli eventi disponibili).
    /// </summary>
    public HashSet<string> WiredMethods { get; } = [];
}
