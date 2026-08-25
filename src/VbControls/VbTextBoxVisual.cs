using VbControls.Abstractions;

namespace VbControls;

/// <summary>Aspetto di un VbTextBox (posseduto dal designer).</summary>
public sealed class VbTextBoxVisual : VisualComponentBase
{
    [VisualProperty("Dati")]
    public string Text { get; set; } = string.Empty;

    [VisualProperty("Aspetto")]
    public string? Placeholder { get; set; }
}
