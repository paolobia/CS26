using System;
using System.Net.Http;
using System.Threading.Tasks;
using VbControls.Abstractions;

namespace VbControls;

/// <summary>
/// Aspetto/configurazione di un VbHttpClient (posseduto dal designer). L'HttpClient vero
/// e proprio viene creato pigramente solo quando serve (<see cref="GetStringAsync"/>):
/// l'istanza usata dal designer per la Property Grid e il generatore di codice non apre
/// mai connessioni di rete. Modulo 14: vive come sorgente in Components/.
/// </summary>
[ToolboxComponent("HttpClient", "🌐", "Non-visuali")]
public sealed class VbHttpClientVisual : NonVisualComponentBase
{
    public VbHttpClientVisual() => LayoutBox = new LayoutBox { Width = 32, Height = 32 };

    [VisualProperty("Dati")]
    public string BaseAddress { get; set; } = string.Empty;

    private HttpClient? _client;

    private HttpClient Client => _client ??= new HttpClient
    {
        BaseAddress = string.IsNullOrWhiteSpace(BaseAddress) ? null : new Uri(BaseAddress),
    };

    /// <summary>Esegue una GET e restituisce il corpo della risposta come stringa.</summary>
    public Task<string> GetStringAsync(string requestUri) => Client.GetStringAsync(requestUri);
}
