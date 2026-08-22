using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TinyPromptEdit;

public sealed class EditorForm : Form
{
    private readonly TextBoxBase editor;
    private readonly LineNumberPanel lineNumbers;
    private readonly string configPath;
    private readonly System.Windows.Forms.Timer configReloadTimer = new() { Interval = 250 };
    private FileSystemWatcher? configWatcher;
    private string? filePath;
    private bool darkMode;
    private bool showScrollBars = true;
    private bool configuredShowLineNumbers;
    private bool configuredWordWrap = true;
    private int largeFileThresholdMb = 2;
    private Localization localization;

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
        configPath = Path.Combine(AppContext.BaseDirectory, "tiny-prompt-edit.ini");
        var cfg = IniConfig.Load(configPath);
        localization = Localization.Load(AppContext.BaseDirectory,
            cfg.Get("general", "language", "en"));

        int width = cfg.GetInt("window", "width", 800);
        int height = cfg.GetInt("window", "height", 400);
        borderSize = Math.Max(0, cfg.GetInt("window", "border", 5));
        bool borderless = cfg.GetBool("window", "borderless", false);
        bool alwaysOnTop = cfg.GetBool("window", "always_on_top", false);
        double alpha = Math.Clamp(cfg.GetDouble("window", "alpha", 0.92), 0.1, 1.0);

        fontName = cfg.Get("editor", "font", "Consolas");
        fontSize = cfg.GetFloat("editor", "font_size", 11);
        zoomStep = Math.Max(1, cfg.GetInt("editor", "zoom_step", 1));
        minFontSize = Math.Max(1, cfg.GetInt("editor", "min_font_size", 6));
        maxFontSize = Math.Max(minFontSize, cfg.GetInt("editor", "max_font_size", 40));
        zoomModifier = cfg.Get("editor", "zoom_modifier", "Control");
        configuredShowLineNumbers = cfg.GetBool("editor", "show_line_numbers", false);
        showScrollBars = cfg.GetBool("editor", "show_scrollbars", true);
        configuredWordWrap = cfg.GetBool("editor", "word_wrap", true);
        largeFileThresholdMb = Math.Max(0, cfg.GetInt("editor", "large_file_threshold_mb", 2));
        ParseShortcuts(cfg.Get("editor", "close_shortcuts", "Control+X, Escape"));

        editor = IsLargeFile()
            ? new TextBox { Multiline = true, MaxLength = int.MaxValue }
            : new RichTextBox();

        Text = GetWindowTitle();
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(width, height);
        TopMost = alwaysOnTop;
        Opacity = alpha;
        KeyPreview = true;
        FormBorderStyle = borderless ? FormBorderStyle.None : FormBorderStyle.Sizable;
        Padding = new Padding(borderSize);

        PositionWindow(cfg.Get("window", "x", "center"), cfg.Get("window", "y", "center"), width, height);

        darkMode = IsWindowsDarkMode();
        Color bg = darkMode ? Color.FromArgb(30, 30, 30) : Color.White;
        Color fg = darkMode ? Color.FromArgb(221, 221, 221) : Color.Black;
        Color gutter = darkMode ? Color.FromArgb(42, 42, 42) : Color.FromArgb(242, 242, 242);
        Color gutterText = darkMode ? Color.FromArgb(145, 145, 145) : Color.FromArgb(105, 105, 105);
        Color border = darkMode ? Color.FromArgb(102, 102, 102) : Color.FromArgb(119, 119, 119);
        BackColor = border;

        editor.Dock = DockStyle.Fill;
        editor.BorderStyle = BorderStyle.None;
        editor.BackColor = bg;
        editor.ForeColor = fg;
        editor.Font = new Font(fontName, fontSize);
        editor.AcceptsTab = true;
        editor.WordWrap = configuredWordWrap && !IsLargeFile();
        // Keep native scrolling enabled even when only its visual scrollbar is hidden.
        if (editor is RichTextBox richEditor)
        {
            richEditor.ScrollBars = RichTextBoxScrollBars.Vertical;
            richEditor.DetectUrls = false;
        }
        else if (editor is TextBox plainEditor)
        {
            plainEditor.ScrollBars = ScrollBars.Vertical;
        }
        editor.HandleCreated += (_, _) =>
        {
            ApplyEditorNativeTheme();
            ApplyScrollbarVisibility();
        };
        editor.TextChanged += (_, _) =>
        {
            if (editor.IsHandleCreated)
                editor.BeginInvoke((Action)ApplyScrollbarVisibility);
        };

        lineNumbers = new LineNumberPanel(editor)
        {
            Dock = DockStyle.Left,
            BackColor = gutter,
            ForeColor = gutterText,
            Font = editor.Font,
            Visible = configuredShowLineNumbers && !IsLargeFile()
        };

        Controls.Add(editor);
        Controls.Add(lineNumbers);
        editor.ContextMenuStrip = CreateContextMenu();
        ApplyWindowsTheme();

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            try
            {
                editor.Text = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, localization.Get("Could not open file"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        Shown += (_, _) =>
        {
            ApplyTitleBarTheme();
            ApplyScrollbarVisibility();
            lineNumbers.RefreshNumbers();
            editor.Focus();
            editor.SelectionStart = editor.TextLength;
        };

        MouseDown += BorderMouseDown;
        MouseMove += BorderMouseMove;
        MouseUp += BorderMouseUp;
        KeyDown += EditorForm_KeyDown;
        editor.MouseWheel += Editor_MouseWheel;
        FormClosing += (_, _) => Save(false);
        FormClosed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            configWatcher?.Dispose();
            configReloadTimer.Dispose();
        };
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        configReloadTimer.Tick += (_, _) =>
        {
            configReloadTimer.Stop();
            ReloadConfiguration();
        };
        StartConfigWatcher();
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.Color or UserPreferenceCategory.General or
            UserPreferenceCategory.VisualStyle))
            return;

        if (IsDisposed || Disposing || !IsHandleCreated)
            return;

        BeginInvoke((Action)ApplyWindowsTheme);
    }

    private void ApplyWindowsTheme()
    {
        darkMode = IsWindowsDarkMode();

        Color background = darkMode ? Color.FromArgb(30, 30, 30) : Color.White;
        Color foreground = darkMode ? Color.FromArgb(221, 221, 221) : Color.Black;
        Color border = darkMode ? Color.FromArgb(102, 102, 102) : Color.FromArgb(119, 119, 119);
        Color gutter = darkMode ? Color.FromArgb(42, 42, 42) : Color.FromArgb(242, 242, 242);
        Color gutterText = darkMode ? Color.FromArgb(145, 145, 145) : Color.FromArgb(105, 105, 105);

        BackColor = border;
        editor.BackColor = background;
        editor.ForeColor = foreground;
        lineNumbers.BackColor = gutter;
        lineNumbers.ForeColor = gutterText;

        if (editor.ContextMenuStrip is { } menu)
        {
            menu.BackColor = darkMode ? Color.FromArgb(37, 37, 38) : SystemColors.Menu;
            menu.ForeColor = darkMode ? Color.FromArgb(241, 241, 241) : SystemColors.MenuText;
            menu.Renderer = darkMode
                ? new ToolStripProfessionalRenderer(new DarkMenuColorTable())
                : new ToolStripProfessionalRenderer();

            foreach (ToolStripItem item in menu.Items)
            {
                item.BackColor = menu.BackColor;
                item.ForeColor = menu.ForeColor;
            }
        }

        ApplyTitleBarTheme();
        ApplyEditorNativeTheme();
        Invalidate(true);
    }

    private void ApplyTitleBarTheme()
    {
        if (!IsHandleCreated || !OperatingSystem.IsWindows())
            return;

        int enabled = darkMode ? 1 : 0;
        const int immersiveDarkMode = 20;
        const int immersiveDarkModeBefore20H1 = 19;

        if (DwmSetWindowAttribute(Handle, immersiveDarkMode, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, immersiveDarkModeBefore20H1, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    private void ApplyEditorNativeTheme()
    {
        if (!editor.IsHandleCreated || !OperatingSystem.IsWindows())
            return;

        SetWindowTheme(editor.Handle, darkMode ? "DarkMode_Explorer" : "Explorer", null);
        editor.Invalidate(true);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(
        IntPtr windowHandle,
        string? subApplicationName,
        string? subIdList);

    private void ApplyScrollbarVisibility()
    {
        if (!editor.IsHandleCreated)
            return;

        const int verticalScrollBar = 1;
        ShowScrollBar(editor.Handle, verticalScrollBar, showScrollBars);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowScrollBar(IntPtr windowHandle, int scrollBar, bool show);

    private void StartConfigWatcher()
    {
        string? directory = Path.GetDirectoryName(configPath);
        string fileName = Path.GetFileName(configPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            return;

        configWatcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                           NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        configWatcher.Changed += (_, _) => ScheduleConfigReload();
        configWatcher.Created += (_, _) => ScheduleConfigReload();
        configWatcher.Renamed += (_, _) => ScheduleConfigReload();
    }

    private void ScheduleConfigReload()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
            return;

        try
        {
            BeginInvoke(() =>
            {
                configReloadTimer.Stop();
                configReloadTimer.Start();
            });
        }
        catch (InvalidOperationException)
        {
            // The form is closing while the watcher is reporting a change.
        }
    }

    private void ReloadConfiguration()
    {
        IniConfig cfg;
        try
        {
            cfg = IniConfig.Load(configPath);
        }
        catch (IOException)
        {
            // A program may still be replacing the file. Try again shortly.
            configReloadTimer.Start();
            return;
        }

        localization = Localization.Load(AppContext.BaseDirectory,
            cfg.Get("general", "language", "en"));
        Text = GetWindowTitle();
        editor.ContextMenuStrip?.Dispose();
        editor.ContextMenuStrip = CreateContextMenu();
        ApplyWindowsTheme();

        int width = Math.Max(100, cfg.GetInt("window", "width", ClientSize.Width));
        int height = Math.Max(80, cfg.GetInt("window", "height", ClientSize.Height));
        borderSize = Math.Max(0, cfg.GetInt("window", "border", borderSize));
        FormBorderStyle = cfg.GetBool("window", "borderless", FormBorderStyle == FormBorderStyle.None)
            ? FormBorderStyle.None
            : FormBorderStyle.Sizable;
        Padding = new Padding(borderSize);
        ClientSize = new Size(width, height);
        TopMost = cfg.GetBool("window", "always_on_top", TopMost);
        Opacity = Math.Clamp(cfg.GetDouble("window", "alpha", Opacity), 0.1, 1.0);
        PositionWindow(cfg.Get("window", "x", Location.X.ToString()),
            cfg.Get("window", "y", Location.Y.ToString()), width, height);

        string newFontName = cfg.Get("editor", "font", fontName);
        float newFontSize = cfg.GetFloat("editor", "font_size", fontSize);
        zoomStep = Math.Max(1, cfg.GetInt("editor", "zoom_step", zoomStep));
        minFontSize = Math.Max(1, cfg.GetInt("editor", "min_font_size", minFontSize));
        maxFontSize = Math.Max(minFontSize, cfg.GetInt("editor", "max_font_size", maxFontSize));
        newFontSize = Math.Clamp(newFontSize, minFontSize, maxFontSize);
        zoomModifier = cfg.Get("editor", "zoom_modifier", zoomModifier);
        configuredWordWrap = cfg.GetBool("editor", "word_wrap", configuredWordWrap);
        largeFileThresholdMb = Math.Max(0,
            cfg.GetInt("editor", "large_file_threshold_mb", largeFileThresholdMb));
        editor.WordWrap = configuredWordWrap && !IsLargeFile();

        try
        {
            var newFont = new Font(newFontName, newFontSize);
            editor.Font = newFont;
            lineNumbers.Font = newFont;
            fontName = newFontName;
            fontSize = newFontSize;
        }
        catch (ArgumentException)
        {
            // Keep the current font if the configured font cannot be created.
        }

        configuredShowLineNumbers = cfg.GetBool("editor", "show_line_numbers",
            configuredShowLineNumbers);
        lineNumbers.Visible = configuredShowLineNumbers && !IsLargeFile();
        showScrollBars = cfg.GetBool("editor", "show_scrollbars", showScrollBars);
        ApplyScrollbarVisibility();
        lineNumbers.RefreshNumbers();

        closeShortcuts.Clear();
        ParseShortcuts(cfg.Get("editor", "close_shortcuts", "Control+X, Escape"));
        editor.Focus();
    }

    private bool IsLargeFile()
    {
        if (largeFileThresholdMb <= 0 || string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            long thresholdBytes = largeFileThresholdMb * 1024L * 1024L;
            return new FileInfo(filePath).Length >= thresholdBytes;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(localization.Get("Search..."), null, (_, _) => ShowSearch());
        menu.Items.Add(localization.Get("Save"), null, (_, _) => Save(true));
        menu.Items.Add(localization.Get("Open with..."), null, (_, _) => OpenWith());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(localization.Get("Settings..."), null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(localization.Get("Close"), null, (_, _) => SaveAndClose());
        return menu;
    }

    private string GetWindowTitle() => string.IsNullOrWhiteSpace(filePath)
        ? "Tiny Prompt Edit"
        : $"{Path.GetFileName(filePath)} — Tiny Prompt Edit";

    private void ShowSearch()
    {
        var search = new Form
        {
            Text = localization.Get("Search"),
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(390, 76),
            ShowInTaskbar = false,
            TopMost = TopMost
        };

        var input = new TextBox { Left = 10, Top = 10, Width = 275 };
        var next = new Button { Text = localization.Get("Next"), Left = 294, Top = 8, Width = 86 };
        var matchCase = new CheckBox { Text = localization.Get("Match case"), Left = 10, Top = 43, AutoSize = true };

        void FindNext()
        {
            if (input.TextLength == 0)
                return;

            StringComparison comparison = matchCase.Checked
                ? StringComparison.CurrentCulture
                : StringComparison.CurrentCultureIgnoreCase;
            int start = editor.SelectionStart + editor.SelectionLength;
            string content = editor.Text;
            int found = content.IndexOf(input.Text, start, comparison);
            if (found < 0 && start > 0)
                found = content.IndexOf(input.Text, 0, comparison);

            if (found >= 0)
            {
                editor.Select(found, input.TextLength);
                editor.ScrollToCaret();
            }
            else
            {
                System.Media.SystemSounds.Beep.Play();
            }
        }

        next.Click += (_, _) => FindNext();
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                FindNext();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                search.Close();
            }
        };

        search.Controls.AddRange(new Control[] { input, next, matchCase });
        search.AcceptButton = next;
        search.Show(this);
        input.Focus();
    }

    private bool Save(bool choosePathWhenMissing)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            if (!choosePathWhenMissing)
                return true;

            using var dialog = new SaveFileDialog
            {
                Title = localization.Get("Save"),
                FileName = localization.Get("New file.txt"),
                Filter = localization.Get("Text files (*.txt)|*.txt|All files (*.*)|*.*")
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return false;

            filePath = dialog.FileName;
            Text = GetWindowTitle();
        }

        try
        {
            File.WriteAllText(filePath, editor.Text);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, localization.Get("Could not save"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void OpenWith()
    {
        if (!Save(true) || string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true, Verb = "openas" });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, localization.Get("Could not show Open with"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSettings()
    {
        try
        {
            var startInfo = new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true };
            startInfo.ArgumentList.Add(configPath);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, localization.Get("Could not open settings"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PositionWindow(string xCfg, string yCfg, int width, int height)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int x = xCfg.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? area.Left + (area.Width - width) / 2
            : int.TryParse(xCfg, out int xv) ? xv : area.Left + (area.Width - width) / 2;
        int y = yCfg.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? area.Top + (area.Height - height) / 2
            : int.TryParse(yCfg, out int yv) ? yv : area.Top + (area.Height - height) / 2;
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

    private void SaveAndClose()
    {
        if (Save(false))
            Close();
    }

    private void EditorForm_KeyDown(object? sender, KeyEventArgs e)
    {
        Keys pressed = e.KeyCode;
        if (e.Control) pressed |= Keys.Control;
        if (e.Shift) pressed |= Keys.Shift;
        if (e.Alt) pressed |= Keys.Alt;

        if (pressed == (Keys.Control | Keys.F))
        {
            e.SuppressKeyPress = true;
            ShowSearch();
        }
        else if (pressed == (Keys.Control | Keys.S))
        {
            e.SuppressKeyPress = true;
            Save(true);
        }
        else if (closeShortcuts.Any(k => k == pressed))
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

        float newSize = Math.Clamp(fontSize + (e.Delta > 0 ? zoomStep : -zoomStep), minFontSize, maxFontSize);
        if (Math.Abs(newSize - fontSize) < 0.01f)
            return;

        fontSize = newSize;
        editor.Font = new Font(fontName, fontSize);
        lineNumbers.Font = editor.Font;
        lineNumbers.RefreshNumbers();
    }

    private void ParseShortcuts(string raw)
    {
        foreach (string item in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Keys keys = Keys.None;
            foreach (string part in item.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "control":
                    case "ctrl": keys |= Keys.Control; break;
                    case "shift": keys |= Keys.Shift; break;
                    case "alt": keys |= Keys.Alt; break;
                    case "escape":
                    case "esc": keys |= Keys.Escape; break;
                    default:
                        if (Enum.TryParse<Keys>(part, true, out var parsed)) keys |= parsed;
                        break;
                }
            }
            if (keys != Keys.None)
                closeShortcuts.Add(keys);
        }
    }

    private bool IsOnBorder(Point p) => borderSize > 0 &&
        (p.X < borderSize || p.Y < borderSize || p.X >= ClientSize.Width - borderSize || p.Y >= ClientSize.Height - borderSize);

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
        Location = new Point(dragStartWindow.X + now.X - dragStartCursor.X,
            dragStartWindow.Y + now.Y - dragStartCursor.Y);
    }

    private void BorderMouseUp(object? sender, MouseEventArgs e) => dragging = false;

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        private static readonly Color MenuBackground = Color.FromArgb(37, 37, 38);
        private static readonly Color Selection = Color.FromArgb(62, 62, 64);
        private static readonly Color Border = Color.FromArgb(81, 81, 81);

        public override Color ToolStripDropDownBackground => MenuBackground;
        public override Color ImageMarginGradientBegin => MenuBackground;
        public override Color ImageMarginGradientMiddle => MenuBackground;
        public override Color ImageMarginGradientEnd => MenuBackground;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => Selection;
        public override Color MenuItemSelectedGradientBegin => Selection;
        public override Color MenuItemSelectedGradientEnd => Selection;
        public override Color MenuItemPressedGradientBegin => Selection;
        public override Color MenuItemPressedGradientMiddle => Selection;
        public override Color MenuItemPressedGradientEnd => Selection;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => MenuBackground;
    }

    private sealed class LineNumberPanel : Control
    {
        private readonly TextBoxBase editor;

        public LineNumberPanel(TextBoxBase editor)
        {
            this.editor = editor;
            DoubleBuffered = true;
            Width = 42;
            editor.TextChanged += (_, _) => RefreshNumbers();
            if (editor is RichTextBox richEditor)
            {
                richEditor.SelectionChanged += (_, _) => RefreshIfVisible();
                richEditor.VScroll += (_, _) => RefreshIfVisible();
            }
            editor.Resize += (_, _) => RefreshIfVisible();
        }

        private void RefreshIfVisible()
        {
            if (Visible)
                Invalidate();
        }

        public void RefreshNumbers()
        {
            if (!Visible)
                return;

            int digits = GetLineCount().ToString().Length;
            Width = Math.Max(36, TextRenderer.MeasureText(new string('0', digits), Font).Width + 14);
            Invalidate();
        }

        private int GetLineCount() => editor.TextLength == 0
            ? 1
            : editor.GetLineFromCharIndex(editor.TextLength) + 1;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int firstChar = editor.GetCharIndexFromPosition(new Point(1, 1));
            int firstLine = Math.Max(0, editor.GetLineFromCharIndex(firstChar));
            int lastVisibleChar = editor.GetCharIndexFromPosition(
                new Point(Math.Max(1, editor.ClientSize.Width - 2),
                    Math.Max(1, editor.ClientSize.Height - 2)));
            int lastVisibleLine = editor.GetLineFromCharIndex(lastVisibleChar) + 1;

            for (int line = firstLine; line <= lastVisibleLine; line++)
            {
                int charIndex = editor.GetFirstCharIndexFromLine(line);
                if (charIndex < 0)
                    break;
                int y = editor.GetPositionFromCharIndex(charIndex).Y;
                if (y < -Font.Height || y > Height)
                    continue;

                TextRenderer.DrawText(e.Graphics, (line + 1).ToString(), Font,
                    new Rectangle(0, y, Width - 7, Font.Height + 3), ForeColor,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding);
            }
        }
    }
}
