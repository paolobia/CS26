namespace VbControls.Abstractions;

/// <summary>
/// Base comune a tutto cio' che vive su un Form, visibile a runtime o no (sezione 2.1 di
/// ARCHITECTURE.md — filosofia "tutto vive nel Form", ispirata a C++Builder/Delphi).
/// Posseduta e scritta dal designer: layout/posizione, proprieta', identita'. Non contiene
/// mai gestori di eventi o logica applicativa: quelli vivono in
/// <see cref="ComponentBehavior{TComponent}"/> o nei metodi generati nel Form.
/// </summary>
public interface IDesignComponent
{
    /// <summary>Identificatore univoco del componente all'interno del form.</summary>
    string Id { get; set; }

    /// <summary>
    /// Posizione (e dimensione) della rappresentazione nel designer: un vero layout per un
    /// <see cref="IVisualComponent"/>, la posizione della sola icona per un
    /// <see cref="INonVisualComponent"/>. Non implica di per se' una resa grafica a runtime.
    /// </summary>
    LayoutBox LayoutBox { get; set; }

    /// <summary>
    /// Proprieta' aggiuntive specifiche del componente (es. Text di un bottone, BaseAddress
    /// di un client HTTP), tenute qui per consentire alla Property Grid (modulo 8) di
    /// enumerarle via reflection senza che IDesignComponent debba conoscere ogni tipo concreto.
    /// </summary>
    IDictionary<string, object?> Properties { get; }
}
