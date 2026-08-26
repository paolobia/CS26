using VbControls.Abstractions;

namespace VbControls;

/// <summary>
/// Componente di prova per il modulo 14: dimostra che un utente puo' scrivere un
/// componente non-visuale del tutto nuovo senza toccare ne' ricompilare l'IDE, mettendolo
/// semplicemente in Components/.
/// </summary>
[ToolboxComponent("Timer", "⏱️", "Non-visuali")]
public sealed class VbTimerVisual : NonVisualComponentBase
{
    public VbTimerVisual() => LayoutBox = new LayoutBox { Width = 32, Height = 32 };

    [VisualProperty("Dati")]
    public int IntervalMs { get; set; } = 1000;
}
