using VbControls.Abstractions;

namespace VbControls;

/// <summary>Aspetto di un VbButton (posseduto dal designer).</summary>
public sealed class VbButtonVisual : VisualComponentBase
{
    [VisualProperty("Aspetto")]
    public string Text { get; set; } = "Button1";
}
