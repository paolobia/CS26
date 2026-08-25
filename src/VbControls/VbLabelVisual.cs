using VbControls.Abstractions;

namespace VbControls;

/// <summary>Aspetto di un VbLabel (posseduto dal designer).</summary>
public sealed class VbLabelVisual : VisualComponentBase
{
    [VisualProperty("Aspetto")]
    public string Text { get; set; } = "Label1";
}
