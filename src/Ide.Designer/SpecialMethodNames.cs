namespace Ide.Designer;

/// <summary>
/// Costruttore/Distruttore sono metodi universali, sempre disponibili per qualunque
/// controllo piazzato indipendentemente dal tipo (a differenza degli eventi in
/// <see cref="ComponentEventInfo"/>, specifici del tipo) - per questo vivono qui, non li'.
/// Mappano su OnInitialized/IDisposable.Dispose del Form Blazor generato
/// (<see cref="FormCodeGenerator"/>), l'unico posto dove un singolo campo del Form ha un
/// aggancio al ciclo di vita.
/// </summary>
public static class SpecialMethodNames
{
    public const string Constructor = "Costruttore";
    public const string Destructor = "Distruttore";

    public static string ConstructorMethodName(string fieldName) => $"{fieldName}_Constructor";
    public static string DestructorMethodName(string fieldName) => $"{fieldName}_Destructor";
}
