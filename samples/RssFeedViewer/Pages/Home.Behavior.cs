using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RssFeedViewer.Pages;

public partial class Home
{
    // Modulo 15: visualizzatore RSS. TextBox1 contiene l'URL del feed (indicato
    // dall'utente), TextBox2 e TextArea1 mostrano rispettivamente titolo e contenuto
    // dell'ultimo articolo, Label1 riporta lo stato dell'ultimo aggiornamento.
    //
    // NOTA IMPORTANTE (limite reale del browser, non un bug di questo codice): la
    // maggior parte dei server RSS non imposta le intestazioni CORS necessarie per essere
    // letti da un HttpClient dentro Blazor WASM, che gira nel browser ed e' soggetto alle
    // stesse regole di un qualunque fetch() JavaScript. Un feed che funziona da riga di
    // comando o da un server puo' quindi fallire qui con un errore di rete generico: non
    // c'e' modo di aggirarlo lato client, serve un feed che imposti
    // 'Access-Control-Allow-Origin' o un proxy CORS server-side.
    private void Button1_Click() => _ = RefreshFeedAsync();

    private async Task Timer1_Tick() => await RefreshFeedAsync();

    private async Task RefreshFeedAsync()
    {
        var url = TextBox1.Text;
        if (string.IsNullOrWhiteSpace(url))
        {
            Label1.Text = "Inserisci un URL nella text box.";
            StateHasChanged();
            return;
        }

        try
        {
            var xml = await HttpClient1.GetStringAsync(url);
            var (title, content) = ParseLatestItem(xml);
            TextBox2.Text = title;
            TextArea1.Text = content;
            Label1.Text = $"Aggiornato alle {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            Label1.Text = $"Errore durante il caricamento: {ex.Message}";
        }

        StateHasChanged();
    }

    // Riconosce sia RSS 2.0 (<rss><channel><item>) sia Atom (<feed><entry>), prendendo il
    // primo elemento incontrato (i feed elencano gli articoli dal piu' recente al piu'
    // vecchio) - sufficiente per un "semplice esempio", non un parser RSS completo.
    private static (string Title, string Content) ParseLatestItem(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        if (root is null)
            return ("(feed vuoto)", "");

        var item = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "item");
        if (item is not null)
        {
            var title = item.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "";
            var body = item.Elements().FirstOrDefault(e => e.Name.LocalName == "description")?.Value ?? "";
            return (title.Trim(), body.Trim());
        }

        var entry = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "entry");
        if (entry is not null)
        {
            var title = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "";
            var body = entry.Elements().FirstOrDefault(e => e.Name.LocalName is "summary" or "content")?.Value ?? "";
            return (title.Trim(), body.Trim());
        }

        return ("(formato feed non riconosciuto)", "");
    }
}
