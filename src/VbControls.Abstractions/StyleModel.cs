namespace VbControls.Abstractions;

/// <summary>
/// Stile visuale di un controllo (colori, font, visibilita').
/// Parte dell'aspetto (<see cref="IVisualComponent"/>), posseduta dal designer.
/// </summary>
public sealed class StyleModel
{
    public string? BackgroundColor { get; set; }

    public string? ForegroundColor { get; set; }

    public string? FontFamily { get; set; }

    public double? FontSize { get; set; }

    public bool Visible { get; set; } = true;

    public bool Enabled { get; set; } = true;
}
