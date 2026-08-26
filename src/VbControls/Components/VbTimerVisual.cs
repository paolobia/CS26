using System;
using System.Threading.Tasks;
using VbControls.Abstractions;

namespace VbControls;

/// <summary>
/// Timer non-visuale: solleva <see cref="Tick"/> ogni <see cref="IntervalMs"/> millisecondi
/// mentre l'app gira (mai a design-time - vedi VbTimer.razor). Il collante .razor possiede il
/// vero <c>System.Threading.Timer</c> e marshalla ogni scadenza sul thread del renderer, cosi'
/// l'handler generato in Behavior.cs puo' toccare la UI (es. StateHasChanged) senza pensarci.
/// </summary>
[ToolboxComponent("Timer", "⏱️", "Non-visuali")]
public sealed class VbTimerVisual : NonVisualComponentBase
{
    public VbTimerVisual() => LayoutBox = new LayoutBox { Width = 32, Height = 32 };

    [VisualProperty("Dati")]
    public int IntervalMs { get; set; } = 1000;

    [VisualProperty("Dati")]
    public bool Enabled { get; set; } = true;

    public event Func<Task>? Tick;

    internal Task RaiseTickAsync() => Tick is null ? Task.CompletedTask : Tick.Invoke();
}
