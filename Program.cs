namespace TinyPromptEdit;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Sans argument : fenêtre vide.
        // Avec argument : ouvre le fichier et le réécrit à la fermeture.
        string? filePath = args.Length > 0 ? args[0] : null;

        Application.Run(new EditorForm(filePath));
    }
}
