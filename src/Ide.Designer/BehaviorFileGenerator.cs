namespace Ide.Designer;

/// <summary>
/// Modulo 9: genera lo scheletro dell'handler in <c>{Form}.Behavior.cs</c> al doppio
/// click su un controllo, nel file posseduto dallo sviluppatore (vincolo architetturale
/// n.3). Non riscrive mai il file per intero: se il metodo manca lo crea con un semplice
/// commento TODO (delegando a <see cref="MethodBodyEditor"/>, che usa Roslyn invece di
/// manipolazione di stringhe); se esiste gia' non lo tocca, potrebbe contenere codice
/// dello sviluppatore.
/// </summary>
public static class BehaviorFileGenerator
{
    /// <summary>
    /// Generalizzato oltre il solo "click" (modulo 9): <paramref name="isAsync"/> distingue
    /// uno stub <c>void</c> (es. VbButton.Click, un <c>EventCallback</c>) da uno
    /// <c>async Task</c> (es. VbTimer.Tick, un vero <c>event Func&lt;Task&gt;</c> .NET che
    /// richiede una firma awaitable).
    /// </summary>
    public static string EnsureEventHandler(string pagesDirectory, string formName, string razorNamespace, string methodName, bool isAsync)
    {
        var behaviorPath = Path.Combine(pagesDirectory, $"{formName}.Behavior.cs");

        if (MethodBodyEditor.ReadMethod(behaviorPath, methodName).Exists)
            return behaviorPath; // l'handler c'e' gia': non lo tocchiamo.

        return MethodBodyEditor.WriteMethodBody(
            pagesDirectory, formName, razorNamespace, methodName, isAsync,
            newBody: "// TODO: implementa qui la logica dell'evento.\n");
    }
}
