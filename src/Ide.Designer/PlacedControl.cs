using VbControls.Abstractions;

namespace Ide.Designer;

/// <summary>
/// Un controllo posizionato sulla superficie di design: nome del campo generato, tipo
/// Blazor (es. "VbButton") e l'istanza reale dell'aspetto (<see cref="IVisualComponent"/>).
/// Tenere l'istanza vera (non un DTO con i soli valori) e' cio' che permette alla
/// Property Grid (modulo 8) di riflettere sulle stesse proprieta' che il generatore di
/// codice (modulo 7) scrive su file.
/// </summary>
public sealed record PlacedControl(string FieldName, string ControlType, IVisualComponent Visual);
