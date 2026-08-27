using VbControls.Abstractions;

namespace VbControls;

/// <summary>
/// Implementazione condivisa di <see cref="INonVisualComponent"/> per i componenti
/// non-visuali (VbHttpClient, e in futuro VbTimer, VbLocalStorage, ...). Nessuno
/// StyleModel: a runtime non c'e' nulla da stilare perche' non c'e' nulla da vedere
/// (sezione 2.1 di ARCHITECTURE.md).
/// </summary>
public abstract class NonVisualComponentBase : INonVisualComponent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public LayoutBox LayoutBox { get; set; } = new();

    // Vedi il commento gemello in VisualComponentBase: passthrough su LayoutBox.X/Y in
    // attesa di un vero drag-to-reposition sulla superficie di design.
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

    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
}
