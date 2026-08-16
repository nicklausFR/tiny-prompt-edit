using Microsoft.Win32;

namespace TinyPromptEdit;

public sealed class EditorForm : Form
{
    private readonly RichTextBox editor = new();
    private readonly string? filePath;

    private int borderSize;
    private string fontName = "Consolas";
    private float fontSize = 11;
    private int zoomStep = 1;
    private int minFontSize = 6;
    private int maxFontSize = 40;
    private string zoomModifier = "Control";
    private readonly List<Keys> closeShortcuts = new();

    private bool dragging;
    private Point dragStartCursor;
    private Point dragStartWindow;

    public EditorForm(string? filePath)
    {
        this.filePath = filePath;

        var cfg = IniConfig.Load(Path.Combine(AppContext.BaseDirectory, "tiny-prompt-edit.ini"));

        int width = cfg.GetInt("window", "width", 800);
        int height = cfg.GetInt("window", "height", 400);

        borderSize = Math.Max(0, cfg.GetInt("window", "border", 5));
        bool borderless = cfg.GetBool("window", "borderless", true);
        bool alwaysOnTop = cfg.GetBool("window", "always_on_top", false);
        double alpha = Math.Clamp(cfg.GetDouble("window", "alpha", 0.92), 0.1, 1.0);

        fontName = cfg.Get("editor", "font", "Consolas");
        fontSize = cfg.GetFloat("editor", "font_size", 11);
        zoomStep = Math.Max(1, cfg.GetInt("editor", "zoom_step", 1));
        minFontSize = Math.Max(1, cfg.GetInt("editor", "min_font_size", 6));
        maxFontSize = Math.Max(minFontSize, cfg.GetInt("editor", "max_font_size", 40));
        zoomModifier = cfg.Get("editor", "zoom_modifier", "Control");

        ParseShortcuts(cfg.Get("editor", "close_shortcuts", "Control+X, Escape"));

        Text = "Tiny Prompt Edit";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(width, height);
        TopMost = alwaysOnTop;
        Opacity = alpha;
        KeyPreview = true;
        FormBorderStyle = borderless ? FormBorderStyle.None : FormBorderStyle.Sizable;
        Padding = new Padding(borderSize);

        PositionWindow(
            cfg.Get("window", "x", "center"),
            cfg.Get("window", "y", "center"),
            width,
            height
        );

        bool dark = IsWindowsDarkMode();

        Color bg = dark ? Color.FromArgb(30, 30, 30) : Color.White;
        Color fg = dark ? Color.FromArgb(221, 221, 221) : Color.Black;
        Color border = dark ? Color.FromArgb(102, 102, 102) : Color.FromArgb(119, 119, 119);

        BackColor = border;

        editor.Dock = DockStyle.Fill;
        editor.BorderStyle = BorderStyle.None;
        editor.BackColor = bg;
        editor.ForeColor = fg;
        editor.Font = new Font(fontName, fontSize);
        editor.AcceptsTab = true;
        editor.WordWrap = true;
        editor.DetectUrls = false;

        Controls.Add(editor);

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            try
            {
                editor.Text = File.ReadAllText(filePath);
            }
            catch
            {
                editor.Text = "";
            }
        }

        Shown += (_, _) =>
        {
            editor.Focus();
            editor.SelectionStart = editor.TextLength;
        };

        MouseDown += BorderMouseDown;
        MouseMove += BorderMouseMove;
        MouseUp += BorderMouseUp;

        KeyDown += EditorForm_KeyDown;
        editor.MouseWheel += Editor_MouseWheel;

        FormClosing += (_, _) => Save();
    }

    private void PositionWindow(string xCfg, string yCfg, int width, int height)
    {
        var area = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, 1920, 1080);

        int x = xCfg.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? area.Left + (area.Width - width) / 2
            : int.TryParse(xCfg, out int xv)
                ? xv
                : area.Left + (area.Width - width) / 2;

        int y = yCfg.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? area.Top + (area.Height - height) / 2
            : int.TryParse(yCfg, out int yv)
                ? yv
                : area.Top + (area.Height - height) / 2;

        Location = new Point(x, y);
    }

    private static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            File.WriteAllText(filePath, editor.Text);
        }
        catch
        {
        }
    }

    private void SaveAndClose()
    {
        Save();
        Close();
    }

    private void EditorForm_KeyDown(object? sender, KeyEventArgs e)
    {
        Keys pressed = e.KeyCode;
        if (e.Control) pressed |= Keys.Control;
        if (e.Shift) pressed |= Keys.Shift;
        if (e.Alt) pressed |= Keys.Alt;

        if (closeShortcuts.Any(k => k == pressed))
        {
            e.SuppressKeyPress = true;
            SaveAndClose();
        }
    }

    private void Editor_MouseWheel(object? sender, MouseEventArgs e)
    {
        bool modifierOk = zoomModifier.ToLowerInvariant() switch
        {
            "control" or "ctrl" => ModifierKeys.HasFlag(Keys.Control),
            "shift" => ModifierKeys.HasFlag(Keys.Shift),
            "alt" => ModifierKeys.HasFlag(Keys.Alt),
            "none" => ModifierKeys == Keys.None,
            _ => ModifierKeys.HasFlag(Keys.Control)
        };

        if (!modifierOk)
            return;

        float newSize = fontSize + (e.Delta > 0 ? zoomStep : -zoomStep);
        newSize = Math.Clamp(newSize, minFontSize, maxFontSize);

        if (Math.Abs(newSize - fontSize) < 0.01f)
            return;

        fontSize = newSize;
        editor.Font = new Font(fontName, fontSize);
    }

    private void ParseShortcuts(string raw)
    {
        foreach (string item in raw.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Keys keys = Keys.None;

            foreach (string part in item.Split(
                '+',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "control":
                    case "ctrl":
                        keys |= Keys.Control;
                        break;

                    case "shift":
                        keys |= Keys.Shift;
                        break;

                    case "alt":
                        keys |= Keys.Alt;
                        break;

                    case "escape":
                    case "esc":
                        keys |= Keys.Escape;
                        break;

                    default:
                        if (Enum.TryParse<Keys>(part, true, out var parsed))
                            keys |= parsed;
                        break;
                }
            }

            if (keys != Keys.None)
                closeShortcuts.Add(keys);
        }
    }

    private bool IsOnBorder(Point p)
    {
        if (borderSize <= 0)
            return false;

        return p.X < borderSize ||
               p.Y < borderSize ||
               p.X >= ClientSize.Width - borderSize ||
               p.Y >= ClientSize.Height - borderSize;
    }

    private void BorderMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !IsOnBorder(e.Location))
            return;

        dragging = true;
        dragStartCursor = Cursor.Position;
        dragStartWindow = Location;
    }

    private void BorderMouseMove(object? sender, MouseEventArgs e)
    {
        if (!dragging)
            return;

        Point now = Cursor.Position;

        Location = new Point(
            dragStartWindow.X + now.X - dragStartCursor.X,
            dragStartWindow.Y + now.Y - dragStartCursor.Y
        );
    }

    private void BorderMouseUp(object? sender, MouseEventArgs e)
    {
        dragging = false;
    }
}
