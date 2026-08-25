namespace Ide.Designer;

/// <summary>
/// Un controllo posizionato sulla superficie di design, cosi' come lo conosce il
/// generatore di codice: nome del campo, tipo e layout. Non contiene logica.
/// </summary>
public sealed record PlacedControl(string FieldName, string ControlType, double X, double Y, double Width, double Height);
