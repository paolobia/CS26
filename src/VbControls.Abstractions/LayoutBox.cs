namespace VbControls.Abstractions;

/// <summary>
/// Posizione e dimensione di un controllo sulla superficie di design.
/// Parte dell'aspetto (<see cref="IVisualComponent"/>), posseduta dal designer.
/// </summary>
public sealed class LayoutBox
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}
