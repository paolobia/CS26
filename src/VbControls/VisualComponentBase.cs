using VbControls.Abstractions;

namespace VbControls;

/// <summary>
/// Implementazione condivisa di <see cref="IVisualComponent"/> per i controlli base
/// (VbButton, VbLabel, VbTextBox, ...). Contiene solo aspetto/layout: nessuna logica
/// applicativa (vincolo architetturale n.2).
/// </summary>
public abstract class VisualComponentBase : IVisualComponent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public LayoutBox LayoutBox { get; set; } = new();

    // Passthrough su LayoutBox.X/Y: non esiste ancora un vero drag-to-reposition sulla
    // superficie di design (il click dentro la NativeWebView non e' intercettabile da
    // Avalonia), quindi finche' non si costruisce un ponte JS la Property Grid resta
    // l'unico modo per spostare un controllo gia' piazzato.
    [VisualProperty("Layout")]
    public double X
    {
        get => LayoutBox.X;
        set => LayoutBox.X = value;
    }

    [VisualProperty("Layout")]
    public double Y
    {
        get => LayoutBox.Y;
        set => LayoutBox.Y = value;
    }

    public StyleModel StyleModel { get; set; } = new();

    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
}
