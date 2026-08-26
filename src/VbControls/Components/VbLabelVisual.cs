using VbControls.Abstractions;

namespace VbControls;

/// <summary>Aspetto di un VbLabel (posseduto dal designer). Modulo 14: vive in Components/.</summary>
[ToolboxComponent("Label", "🏷️", "Standard")]
public sealed class VbLabelVisual : VisualComponentBase
{
    public VbLabelVisual() => LayoutBox = new LayoutBox { Width = 150, Height = 24 };

    [VisualProperty("Aspetto")]
    public string Text { get; set; } = "Label1";
}
