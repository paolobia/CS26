using VbControls.Abstractions;

namespace VbControls;

/// <summary>
/// Aspetto di un VbButton (posseduto dal designer). Modulo 14: vive come sorgente in
/// <c>Components/</c> - compilato dal build normale del progetto per il runtime reale,
/// e da <c>ComponentPluginLoader</c> in memoria per il design-time di Ide.App.
/// </summary>
[ToolboxComponent("Button", "🔘", "Standard")]
public sealed class VbButtonVisual : VisualComponentBase
{
    public VbButtonVisual() => LayoutBox = new LayoutBox { Width = 120, Height = 32 };

    [VisualProperty("Aspetto")]
    public string Text { get; set; } = "Button1";
}
