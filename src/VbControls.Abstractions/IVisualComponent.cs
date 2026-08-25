namespace VbControls.Abstractions;

/// <summary>
/// Aspetto/layout di un controllo, posseduto e scritto dal designer (vincolo architetturale
/// n.2: separazione netta aspetto/logica per composizione, non ereditarieta').
/// Non contiene mai gestori di eventi o logica applicativa: quelli vivono in <see cref="ComponentBehavior{TComponent}"/>.
/// </summary>
public interface IVisualComponent
{
    /// <summary>Identificatore univoco del controllo all'interno del form.</summary>
    string Id { get; set; }

    /// <summary>Posizione e dimensione sulla superficie di design.</summary>
    LayoutBox LayoutBox { get; set; }

    /// <summary>Stile visuale (colori, font, visibilita').</summary>
    StyleModel StyleModel { get; set; }

    /// <summary>
    /// Proprieta' aggiuntive specifiche del controllo (es. Text di un bottone), tenute qui
    /// per consentire alla Property Grid (modulo 8) di enumerarle via reflection senza che
    /// IVisualComponent debba conoscere ogni tipo di controllo concreto.
    /// </summary>
    IDictionary<string, object?> Properties { get; }
}
