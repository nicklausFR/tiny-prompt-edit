namespace TinyPromptEdit;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var cfg = IniConfig.Load(Path.Combine(AppContext.BaseDirectory, "tiny-prompt-edit.ini"));
        var localization = Localization.Load(AppContext.BaseDirectory,
            cfg.Get("general", "language", "en"));

        string? filePath;

        if (args.Length > 0 && args[0].Equals("--new", StringComparison.OrdinalIgnoreCase))
        {
            string directory = args.Length > 1 && Directory.Exists(args[1])
                ? args[1]
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            using var dialog = new SaveFileDialog
            {
                Title = localization.Get("New file name"),
                InitialDirectory = directory,
                FileName = localization.Get("New file.txt"),
                Filter = localization.Get("Text files (*.txt)|*.txt|All files (*.*)|*.*")
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            filePath = dialog.FileName;
        }
        else
        {
            filePath = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
        }

        Application.Run(new EditorForm(filePath));
    }
}
