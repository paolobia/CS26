using VbControls.Abstractions;

namespace VbControls;

/// <summary>Aspetto di un VbTextArea (testo multilinea, posseduto dal designer).</summary>
[ToolboxComponent("TextArea", "📝", "Standard")]
public sealed class VbTextAreaVisual : VisualComponentBase
{
    public VbTextAreaVisual() => LayoutBox = new LayoutBox { Width = 250, Height = 100 };

    [VisualProperty("Dati")]
    public string Text { get; set; } = string.Empty;

    [VisualProperty("Aspetto")]
    public string? Placeholder { get; set; }

    [VisualProperty("Dati")]
    public bool ReadOnly { get; set; }
}
