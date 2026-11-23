using BluePointLilac.Methods;
using Microsoft.Win32;
using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    /// <summary>Shared application palette. Call Initialize on the UI thread before creating forms.</summary>
    public sealed class FluentTheme
    {
        private FluentTheme() { }
        private static readonly FluentTheme instance = new FluentTheme();
        private static readonly ConditionalWeakTable<Control, Binding> bindings = new ConditionalWeakTable<Control, Binding>();
        private static readonly List<WeakReference<Control>> roots = new List<WeakReference<Control>>();
        private static readonly Font uiFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly ToolStripRenderer menuRenderer = new FluentMenuRenderer();
        private static bool initialized;
        private static int refreshPending;
        private static string mode = "System";
        private static string ConfigPath => Path.Combine(AppConfig.ConfigDir, "Theme.ini");

        public static string Mode => mode;
        public static bool IsDark { get; private set; }
        public static Font BodyFont => uiFont;
        public static Color Background { get; private set; } = Color.FromArgb(243, 243, 243);
        public static Color Surface { get; private set; } = Color.FromArgb(255, 255, 255);
        public static Color Foreground { get; private set; } = Color.FromArgb(32, 32, 32);
        public static Color Muted { get; private set; } = Color.FromArgb(96, 96, 96);
        public static Color Accent { get; private set; } = Color.FromArgb(0, 103, 192);
        public static Color Border { get; private set; } = Color.FromArgb(229, 229, 229);
        public static Color Hover { get; private set; } = Color.FromArgb(246, 246, 246);
        public static event EventHandler Changed;

        public static FluentTheme Initialize()
        {
            if(initialized) return instance;
            string savedMode = new IniReader(ConfigPath).GetValue("Appearance", "Mode");
            mode = NormalizeMode(savedMode);
            initialized = true;
            UpdatePalette();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Application.Idle += OnApplicationIdle;
            Application.ApplicationExit += OnApplicationExit;
            return instance;
        }

        public static void SetMode(string value)
        {
            Initialize();
            string next = NormalizeMode(value);
            new IniWriter(ConfigPath).SetValue("Appearance", "Mode", next);
            if(mode == next) return;
            mode = next;
            RefreshTheme();
        }

        private static string NormalizeMode(string value)
        {
            if(string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase)) return "Dark";
            if(string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase)) return "Light";
            return "System";
        }

        private static void UpdatePalette()
        {
            bool systemDark = false;
            if(mode == "System")
            {
                using(RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    systemDark = key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            IsDark = !SystemInformation.HighContrast && (mode == "Dark" || mode == "System" && systemDark);
            if(SystemInformation.HighContrast)
            {
                Background = SystemColors.Window;
                Surface = SystemColors.Window;
                Foreground = SystemColors.WindowText;
                Muted = SystemColors.GrayText;
                Accent = SystemColors.Highlight;
                Border = SystemColors.WindowText;
                Hover = SystemColors.Highlight;
                return;
            }
            Background = IsDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
            Surface = IsDark ? Color.FromArgb(44, 44, 44) : Color.White;
            Foreground = IsDark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(32, 32, 32);
            Muted = IsDark ? Color.FromArgb(190, 190, 190) : Color.FromArgb(96, 96, 96);
            Accent = IsDark ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 103, 192);
            Border = IsDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(229, 229, 229);
            Hover = IsDark ? Color.FromArgb(51, 51, 51) : Color.FromArgb(246, 246, 246);
        }

        // SystemEvents may run on a worker thread. Palette and control updates remain on the UI thread.
        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
            => Interlocked.Exchange(ref refreshPending, 1);

        private static void OnApplicationIdle(object sender, EventArgs e)
        {
            if(Interlocked.Exchange(ref refreshPending, 0) != 0) RefreshTheme();
            foreach(Form form in Application.OpenForms)
                if(!bindings.TryGetValue(form, out _)) Apply(form);
        }

        private static void OnApplicationExit(object sender, EventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            Application.Idle -= OnApplicationIdle;
            Application.ApplicationExit -= OnApplicationExit;
            roots.Clear();
            Changed = null;
            initialized = false;
        }

        private static void RefreshTheme()
        {
            UpdatePalette();
            for(int i = roots.Count - 1; i >= 0; i--)
            {
                if(!roots[i].TryGetTarget(out Control root) || root.IsDisposed) roots.RemoveAt(i);
                else ApplyTree(root);
            }
            Changed?.Invoke(instance, EventArgs.Empty);
        }

        public static void Apply(Control root)
        {
            if(root == null || root.IsDisposed) return;
            Initialize();
            Binding binding = Bind(root);
            if(!binding.IsRoot)
            {
                binding.IsRoot = true;
                roots.Add(new WeakReference<Control>(root));
            }
            ApplyTree(root);
        }

        private sealed class Binding
        {
            public bool IsRoot;
            public bool FontApplied;
        }

        private static Binding Bind(Control control)
        {
            return bindings.GetValue(control, c =>
            {
                c.ControlAdded += OnControlAdded;
                c.ContextMenuStripChanged += OnContextMenuChanged;
                c.HandleCreated += OnHandleCreated;
                c.EnabledChanged += OnControlEnabledChanged;
                c.Disposed += OnControlDisposed;
                if(c is ToolStrip strip) strip.ItemAdded += OnMenuItemAdded;
                return new Binding();
            });
        }

        private static void OnControlAdded(object sender, ControlEventArgs e) => ApplyTree(e.Control);
        private static void OnControlEnabledChanged(object sender, EventArgs e)
        {
            Control control = (Control)sender;
            control.ForeColor = control.Enabled ? Foreground : Muted;
            if(control is MyListItem item) item.ApplyTheme();
            if(control is MyCheckBox checkBox) checkBox.ApplyTheme();
            if(control is PictureButton pictureButton) pictureButton.ApplyTheme();
            control.Invalidate();
        }
        private static void OnContextMenuChanged(object sender, EventArgs e)
        {
            if(((Control)sender).ContextMenuStrip is ContextMenuStrip menu) ApplyTree(menu);
        }
        private static void OnHandleCreated(object sender, EventArgs e)
        {
            Control control = (Control)sender;
            ApplyNativeTheme(control);
            // Constructors may set colors after ControlAdded has run.
            ApplyTree(control);
        }
        private static void OnControlDisposed(object sender, EventArgs e)
        {
            Control control = (Control)sender;
            for(int i = roots.Count - 1; i >= 0; i--)
                if(!roots[i].TryGetTarget(out Control root) || root == control) roots.RemoveAt(i);
            control.ControlAdded -= OnControlAdded;
            control.ContextMenuStripChanged -= OnContextMenuChanged;
            control.HandleCreated -= OnHandleCreated;
            control.EnabledChanged -= OnControlEnabledChanged;
            control.Disposed -= OnControlDisposed;
            if(control is ToolStrip strip) strip.ItemAdded -= OnMenuItemAdded;
            bindings.Remove(control);
        }

        private static void ApplyTree(Control control)
        {
            if(control == null || control.IsDisposed) return;
            Binding binding = Bind(control);
            if(!binding.FontApplied)
            {
                // Replace only legacy system defaults; preserve chosen sizes, weights and families.
                if(control.Font.Equals(Control.DefaultFont) || control.Font.Equals(SystemFonts.MenuFont)
                    || control.Font.Equals(SystemFonts.MessageBoxFont))
                    control.Font = uiFont;
                binding.FontApplied = true;
            }
            bool surface = control is MyListItem || control is TextBoxBase || control is ComboBox
                || control is ListBox || control is ListView || control is TreeView || control is ToolStrip;
            control.BackColor = control is Form || control is MyListBox || control is MyList || control is MySideBar
                ? Background : surface ? Surface : control.Parent?.BackColor ?? Background;
            control.ForeColor = control.Enabled ? Foreground : Muted;
            if(control is Label label && (label.Name == "Separator" || label.Dock == DockStyle.Right && label.Width == 1))
                label.BackColor = Border;
            if(control is TextBoxBase textBox) textBox.BackColor = Surface;
            if(control is LinkLabel link)
            {
                link.LinkColor = Accent;
                link.ActiveLinkColor = Accent;
                link.VisitedLinkColor = Accent;
            }
            if(control is Button button)
            {
                button.FlatStyle = SystemInformation.HighContrast ? FlatStyle.System : FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Hover;
                button.FlatAppearance.MouseDownBackColor = Hover;
                button.UseVisualStyleBackColor = SystemInformation.HighContrast;
                if(button.GetType() == typeof(Button) && !SystemInformation.HighContrast)
                    button.BackColor = Surface;
            }
            if(control is CheckBox check) check.UseVisualStyleBackColor = false;
            if(control is RadioButton radio) radio.UseVisualStyleBackColor = false;
            if(control is ComboBox combo && combo.DrawMode == DrawMode.Normal && combo.DropDownStyle == ComboBoxStyle.DropDownList)
            {
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.DrawItem += DrawComboItem;
            }
            if(control is ToolStrip strip) ApplyMenu(strip);
            if(control.ContextMenuStrip != null) ApplyTree(control.ContextMenuStrip);
            foreach(Control child in control.Controls) ApplyTree(child);
            if(control is MySideBar sideBar) sideBar.ApplyTheme();
            if(control is MyCheckBox checkBox) checkBox.ApplyTheme();
            if(control is PictureButton pictureButton) pictureButton.ApplyTheme();
            if(control is MyToolBarButton toolbarButton) toolbarButton.ApplyTheme();
            if(control is MyListItem item) item.ApplyTheme();
            if(control is MyStatusBar) control.ForeColor = Muted;
            ApplyNativeTheme(control);
            control.Invalidate();
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = selected ? Hover : Surface;
            Color foreground = !combo.Enabled ? Muted : selected && SystemInformation.HighContrast ? SystemColors.HighlightText : Foreground;
            using(Brush brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            string text = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, Rectangle.Inflate(e.Bounds, -4, 0), foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }

        private static void OnMenuItemAdded(object sender, ToolStripItemEventArgs e) => ApplyMenuItem(e.Item);
        private static void ApplyMenu(ToolStrip menu)
        {
            menu.Renderer = menuRenderer;
            menu.BackColor = Surface;
            menu.ForeColor = Foreground;
            menu.Font = uiFont;
            foreach(ToolStripItem item in menu.Items) ApplyMenuItem(item);
        }
        private static void ApplyMenuItem(ToolStripItem item)
        {
            item.BackColor = Surface;
            item.ForeColor = Foreground;
            if(item is ToolStripDropDownItem dropDown)
            {
                dropDown.DropDownOpening -= OnDropDownOpening;
                dropDown.DropDownOpening += OnDropDownOpening;
                if(dropDown.HasDropDownItems) ApplyTree(dropDown.DropDown);
            }
            if(item is ToolStripControlHost host) ApplyTree(host.Control);
        }
        private static void OnDropDownOpening(object sender, EventArgs e)
            => ApplyTree(((ToolStripDropDownItem)sender).DropDown);

        private static void ApplyNativeTheme(Control control)
        {
            if(!control.IsHandleCreated) return;
            if(control is Form)
            {
                int dark = IsDark ? 1 : 0;
                if(DwmSetWindowAttribute(control.Handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(control.Handle, 19, ref dark, sizeof(int));
                int corner = SystemInformation.HighContrast ? 1 : 2;
                DwmSetWindowAttribute(control.Handle, 33, ref corner, sizeof(int));
            }
            if(control is ComboBox)
            {
                // The combo-box class only picks up dark borders and arrows from the CFD theme.
                SetWindowTheme(control.Handle, IsDark ? "DarkMode_CFD" : "Explorer", null);
                return;
            }
            if(control is TextBoxBase || control is ListBox || control is ListView || control is TreeView
                || control is ComboBox || control is ScrollableControl || control is ButtonBase)
                SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        private sealed class FluentMenuRenderer : ToolStripProfessionalRenderer
        {
            public FluentMenuRenderer() : base(new FluentColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = !e.Item.Enabled ? Muted : SystemInformation.HighContrast && e.Item.Selected
                    ? SystemColors.HighlightText : Foreground;
                base.OnRenderItemText(e);
            }
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = !e.Item.Enabled ? Muted : SystemInformation.HighContrast && e.Item.Selected
                    ? SystemColors.HighlightText : Foreground;
                base.OnRenderArrow(e);
            }
        }
        private sealed class FluentColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Surface;
            public override Color ImageMarginGradientBegin => Surface;
            public override Color ImageMarginGradientMiddle => Surface;
            public override Color ImageMarginGradientEnd => Surface;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Hover;
            public override Color MenuItemPressedGradientMiddle => Hover;
            public override Color MenuItemPressedGradientEnd => Hover;
            public override Color MenuItemBorder => Border;
            public override Color MenuBorder => Border;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Surface;
            public override Color CheckBackground => Hover;
            public override Color CheckSelectedBackground => Hover;
            public override Color CheckPressedBackground => Hover;
            public override Color ToolStripBorder => Border;
        }
    }
}
