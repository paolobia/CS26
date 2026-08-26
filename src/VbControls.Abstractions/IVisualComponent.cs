namespace VbControls.Abstractions;

/// <summary>
/// Aspetto/layout di un controllo che produce una resa grafica a runtime, posseduto e
/// scritto dal designer (vincolo architetturale n.2). Per i componenti che sul Form non
/// producono nulla a runtime, vedi <see cref="INonVisualComponent"/> (sezione 2.1).
/// </summary>
public interface IVisualComponent : IDesignComponent
{
    /// <summary>Stile visuale a runtime (colori, font, visibilita').</summary>
    StyleModel StyleModel { get; set; }
}
