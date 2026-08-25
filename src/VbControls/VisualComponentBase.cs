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

    public StyleModel StyleModel { get; set; } = new();

    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
}
