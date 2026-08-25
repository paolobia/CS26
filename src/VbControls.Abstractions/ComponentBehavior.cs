namespace VbControls.Abstractions;

/// <summary>
/// Eventi e logica applicativa di un controllo, scritta e posseduta dallo sviluppatore
/// (vincolo architetturale n.2). Vive nel file <c>*.Behavior.cs</c> e non viene mai
/// toccata dal designer, che invece scrive/rigenera <see cref="IVisualComponent"/>.
/// </summary>
/// <typeparam name="TComponent">Il tipo del componente Blazor a cui questo behavior si aggancia.</typeparam>
public abstract class ComponentBehavior<TComponent>
    where TComponent : class
{
    protected ComponentBehavior(TComponent component)
    {
        Component = component;
    }

    protected TComponent Component { get; }

    /// <summary>Eseguito all'inizializzazione del componente (equivalente a Form_Load in VB6).</summary>
    public virtual Task OnInitAsync() => Task.CompletedTask;

    /// <summary>Eseguito al click del controllo (es. doppio click nel designer genera l'override, modulo 9).</summary>
    public virtual Task OnClickAsync() => Task.CompletedTask;
}
