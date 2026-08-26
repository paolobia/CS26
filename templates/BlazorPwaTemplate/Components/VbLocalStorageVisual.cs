using System.Threading.Tasks;
using Microsoft.JSInterop;
using VbControls.Abstractions;

namespace VbControls;

/// <summary>
/// Componente non-visuale generico per persistere dati nel <c>localStorage</c> del browser.
/// "Generico" nel senso VB6/COM: le chiavi/valori sono stringhe (il chiamante serializza se
/// gli serve altro), cosi' il componente non deve conoscere alcun tipo applicativo.
/// </summary>
[ToolboxComponent("LocalStorage", "💾", "Non-visuali")]
public sealed class VbLocalStorageVisual : NonVisualComponentBase
{
    public VbLocalStorageVisual() => LayoutBox = new LayoutBox { Width = 32, Height = 32 };

    /// <summary>Assegnato dal collante .razor in OnInitialized: qui, non nel designer, e' disponibile un vero browser.</summary>
    public IJSRuntime? JsRuntime { get; set; }

    public ValueTask SetItemAsync(string key, string value)
        => JsRuntime is null ? ValueTask.CompletedTask : JsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);

    public ValueTask<string?> GetItemAsync(string key)
        => JsRuntime is null ? ValueTask.FromResult<string?>(null) : JsRuntime.InvokeAsync<string?>("localStorage.getItem", key);

    public ValueTask RemoveItemAsync(string key)
        => JsRuntime is null ? ValueTask.CompletedTask : JsRuntime.InvokeVoidAsync("localStorage.removeItem", key);
}
