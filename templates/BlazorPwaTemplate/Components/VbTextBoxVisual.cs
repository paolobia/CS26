using VbControls.Abstractions;

namespace VbControls;

/// <summary>Aspetto di un VbTextBox (posseduto dal designer). Modulo 14: vive in Components/.</summary>
[ToolboxComponent("TextBox", "✏️", "Standard")]
public sealed class VbTextBoxVisual : VisualComponentBase
{
    public VbTextBoxVisual() => LayoutBox = new LayoutBox { Width = 200, Height = 28 };

    [VisualProperty("Dati")]
    public string Text { get; set; } = string.Empty;

    [VisualProperty("Aspetto")]
    public string? Placeholder { get; set; }
}
