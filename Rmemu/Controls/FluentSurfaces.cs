using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using BluePointLilac.Controls;
using BluePointLilac.Methods;

namespace Rmemu.Controls
{
    internal static class FluentIcons
    {
        internal static readonly Font Font = new Font("Segoe Fluent Icons", 12F);
        internal static void Draw(Graphics graphics, string glyph, Rectangle bounds, Color color)
        {
            if(!string.IsNullOrEmpty(glyph)) TextRenderer.DrawText(graphics, glyph, Font, bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class FluentButton : Button
    {
        private bool hovered;
        private bool pressed;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Primary { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Quiet { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string Glyph { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool IsNavigation { get; set; }

        public FluentButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        /// <summary>Width needed to show the glyph and the whole label, never below <paramref name="minimum"/>.</summary>
        internal int FitWidth(int minimum)
        {
            if(string.IsNullOrEmpty(Text)) return minimum;
            int icon = string.IsNullOrEmpty(Glyph) ? 0 : 26.DpiZoom();
            // Same flags as OnPaint (which keeps DrawText's default padding), plus room for the ellipsis rounding.
            int text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine).Width;
            return Math.Max(minimum, 12.DpiZoom() * 2 + icon + text + 10.DpiZoom());
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if(AutoFit && !IsNavigation) Width = FitWidth(Width); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if(AutoFit && !IsNavigation) Width = FitWidth(Width); }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool AutoFit { get; set; } = true;
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color parent = Parent?.BackColor ?? FluentTheme.Background;
            g.Clear(parent);
            Rectangle bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            Color fill = Primary && Enabled ? FluentTheme.Accent : Selected || hovered && Enabled ? FluentTheme.Hover : Quiet ? parent : FluentTheme.Surface;
            bool highlighted = SystemInformation.HighContrast && Enabled && (Primary || Selected || hovered);
            Color textColor = highlighted ? SystemColors.HighlightText : !Enabled ? FluentTheme.Muted : Primary ? (FluentTheme.IsDark ? Color.FromArgb(20, 20, 20) : Color.White) : FluentTheme.Foreground;
            if(Primary && Enabled && pressed) fill = ControlPaint.Dark(fill, .08F);
            using(GraphicsPath path = FluentCard.Rounded(bounds, 5.DpiZoom()))
            {
                using(var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if(!Quiet && !Primary && !IsNavigation)
                    using(var pen = new Pen(FluentTheme.Border)) g.DrawPath(pen, path);
                if(Focused && ShowFocusCues)
                    using(var pen = new Pen(FluentTheme.Accent, 1.5F)) g.DrawPath(pen, path);
            }
            int inset = 12.DpiZoom();
            if(IsNavigation && Selected)
                using(var brush = new SolidBrush(FluentTheme.Accent)) g.FillRectangle(brush, 1, Height / 2 - 8.DpiZoom(), 3.DpiZoom(), 16.DpiZoom());
            bool hasText = !string.IsNullOrEmpty(Text);
            int iconWidth = string.IsNullOrEmpty(Glyph) ? 0 : 26.DpiZoom();
            if(iconWidth > 0)
                FluentIcons.Draw(g, Glyph, hasText ? new Rectangle(inset, 0, iconWidth, Height) : ClientRectangle, textColor);
            Rectangle text = new Rectangle(inset + iconWidth, 0, Math.Max(1, Width - inset * 2 - iconWidth), Height);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            flags |= IsNavigation || TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            if(hasText) TextRenderer.DrawText(g, Text, Font, text, textColor, flags);
        }
    }

    internal sealed class FluentSelect : Control
    {
        private int selectedIndex = -1;
        private bool hovered;
        private bool expanded;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public List<object> Items { get; } = new List<object>();
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                if(value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException(nameof(value));
                if(selectedIndex == value) return;
                selectedIndex = value;
                Invalidate();
                if(IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler SelectedIndexChanged;

        public FluentSelect()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ComboBox;
            Height = 34.DpiZoom();
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); OpenMenu(); }
        private void OpenMenu()
        {
            if(!Enabled || Items.Count == 0) return;
            expanded = true;
            if(IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            var menu = new ContextMenuStrip { ShowImageMargin = false, MinimumSize = new Size(Width, 0), MaximumSize = new Size(Math.Max(Width, 300.DpiZoom()), 500.DpiZoom()) };
            for(int i = 0; i < Items.Count; i++)
            {
                int index = i;
                var entry = new ToolStripMenuItem(Items[i].ToString()) { Checked = i == SelectedIndex };
                entry.Click += (s, e) => SelectedIndex = index;
                menu.Items.Add(entry);
            }
            menu.Closed += (s, e) =>
            {
                expanded = false;
                if(!IsDisposed && IsHandleCreated) { AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); BeginInvoke(new Action(menu.Dispose)); }
                else menu.Dispose();
            };
            FluentTheme.Apply(menu);
            menu.Show(this, new Point(0, Height + 3));
        }
        protected override bool IsInputKey(Keys keyData)
            => (keyData & Keys.KeyCode) == Keys.Up || (keyData & Keys.KeyCode) == Keys.Down || base.IsInputKey(keyData);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.Alt && e.KeyCode == Keys.Down) OpenMenu();
            else if(e.KeyCode == Keys.Down && Items.Count > 0) SelectedIndex = Math.Min(Items.Count - 1, SelectedIndex + 1);
            else if(e.KeyCode == Keys.Up && Items.Count > 0) SelectedIndex = Math.Max(0, SelectedIndex - 1);
            else { base.OnKeyDown(e); return; }
            e.Handled = e.SuppressKeyPress = true;
        }
        protected override AccessibleObject CreateAccessibilityInstance() => new SelectAccessibleObject(this);
        private sealed class SelectAccessibleObject : ControlAccessibleObject
        {
            private readonly FluentSelect owner;
            public SelectAccessibleObject(FluentSelect owner) : base(owner) { this.owner = owner; }
            public override string Value
            {
                get => owner.SelectedIndex < 0 ? "" : owner.Items[owner.SelectedIndex].ToString();
                set
                {
                    int index = owner.Items.FindIndex(item => string.Equals(item.ToString(), value, StringComparison.CurrentCultureIgnoreCase));
                    if(index < 0) throw new ArgumentException(AppString.W("OptionMissing"), nameof(value));
                    owner.SelectedIndex = index;
                }
            }
            public override AccessibleStates State => base.State | (owner.expanded ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
            public override string DefaultAction => AppString.W("Expand");
            public override void DoDefaultAction() { if(owner.Enabled) owner.OpenMenu(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? FluentTheme.Background);
            using(GraphicsPath path = FluentCard.Rounded(new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3)), 5.DpiZoom()))
            {
                using(var brush = new SolidBrush(hovered && Enabled ? FluentTheme.Hover : FluentTheme.Surface)) e.Graphics.FillPath(brush, path);
                using(var pen = new Pen(Focused ? FluentTheme.Accent : FluentTheme.Border)) e.Graphics.DrawPath(pen, path);
            }
            string value = SelectedIndex >= 0 ? Items[SelectedIndex].ToString() : AppString.W("SelectLocation");
            Color color = Enabled && hovered && SystemInformation.HighContrast ? SystemColors.HighlightText : Enabled ? FluentTheme.Foreground : FluentTheme.Muted;
            TextRenderer.DrawText(e.Graphics, value, Font, new Rectangle(12.DpiZoom(), 0, Math.Max(1, Width - 44.DpiZoom()), Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            FluentIcons.Draw(e.Graphics, "\uE70D", new Rectangle(Width - 30.DpiZoom(), 0, 24.DpiZoom(), Height), color);
        }
    }

    internal sealed class FluentSearchBox : UserControl
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public TextBox Editor { get; }
        public FluentSearchBox()
        {
            DoubleBuffered = true;
            Height = 34.DpiZoom();
            Editor = new TextBox { BorderStyle = BorderStyle.None, PlaceholderText = AppString.W("SearchPlaceholder"), AccessibleName = AppString.W("SearchList") };
            Controls.Add(Editor);
            Editor.GotFocus += (s, e) => Invalidate();
            Editor.LostFocus += (s, e) => Invalidate();
            Editor.FontChanged += (s, e) => PerformLayout();
            Click += (s, e) => Editor.Focus();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if(Editor == null) return;
            Editor.SetBounds(36.DpiZoom(), Math.Max(4, (Height - Editor.PreferredHeight) / 2), Math.Max(1, Width - 48.DpiZoom()), Editor.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? FluentTheme.Background);
            using(GraphicsPath path = FluentCard.Rounded(new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3)), 5.DpiZoom()))
            {
                using(var brush = new SolidBrush(FluentTheme.Surface)) e.Graphics.FillPath(brush, path);
                using(var pen = new Pen(Editor.Focused ? FluentTheme.Accent : FluentTheme.Border)) e.Graphics.DrawPath(pen, path);
            }
            FluentIcons.Draw(e.Graphics, "\uE721", new Rectangle(8.DpiZoom(), 0, 24.DpiZoom(), Height), FluentTheme.Muted);
        }
    }

    internal class FluentCard : Panel
    {
        public FluentCard() { DoubleBuffered = true; }
        // Children inherit the card surface rather than the page background behind its rounded corners.
        public override Color BackColor { get => FluentTheme.Surface; set { } }
        internal static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent?.BackColor ?? FluentTheme.Background);
            using(GraphicsPath path = Rounded(new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3)), 7.DpiZoom()))
            {
                using(var brush = new SolidBrush(FluentTheme.Surface)) e.Graphics.FillPath(brush, path);
                using(var pen = new Pen(FluentTheme.Border)) e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    internal sealed class FluentTaskTile : Button
    {
        private bool hovered;
        private readonly Font titleFont = new Font(FluentTheme.BodyFont.FontFamily, 10.5F, FontStyle.Bold);
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string Description { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public string Glyph { get; set; }
        public FluentTaskTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? FluentTheme.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using(GraphicsPath path = FluentCard.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 6.DpiZoom()))
            {
                using(var brush = new SolidBrush(hovered ? FluentTheme.Hover : FluentTheme.Surface)) e.Graphics.FillPath(brush, path);
                using(var pen = new Pen(Focused ? FluentTheme.Accent : FluentTheme.Border)) e.Graphics.DrawPath(pen, path);
            }
            bool highlighted = hovered && SystemInformation.HighContrast;
            Color titleColor = highlighted ? SystemColors.HighlightText : FluentTheme.Foreground;
            Color detailColor = highlighted ? SystemColors.HighlightText : FluentTheme.Muted;
            FluentIcons.Draw(e.Graphics, Glyph, new Rectangle(16.DpiZoom(), 0, 28.DpiZoom(), Height), highlighted ? SystemColors.HighlightText : FluentTheme.Accent);
            int x = 58.DpiZoom(), width = Math.Max(1, Width - 90.DpiZoom());
            TextRenderer.DrawText(e.Graphics, Text, titleFont, new Rectangle(x, 16.DpiZoom(), width, 25.DpiZoom()), titleColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, Description, Font, new Rectangle(x, 43.DpiZoom(), width, 22.DpiZoom()), detailColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            FluentIcons.Draw(e.Graphics, "\uE76C", new Rectangle(Width - 32.DpiZoom(), 0, 22.DpiZoom(), Height), detailColor);
        }
        protected override void Dispose(bool disposing) { if(disposing) titleFont.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class MenuIllustration : Control
    {
        public MenuIllustration() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(FluentTheme.Surface);
            int left = 24.DpiZoom(), top = 16.DpiZoom(), width = Math.Min(Width - 44.DpiZoom(), 174.DpiZoom());
            if(width <= 0) return;
            using(var pen = new Pen(FluentTheme.Border))
            using(var brush = new SolidBrush(FluentTheme.Background))
            using(var frame = FluentCard.Rounded(new Rectangle(left, top, width, 110.DpiZoom()), 8.DpiZoom()))
            {
                g.FillPath(brush, frame);
                g.DrawPath(pen, frame);
            }
            for(int i = 0; i < 3; i++)
            {
                int y = top + (14 + i * 29).DpiZoom();
                if(i == 0)
                    using(var brush = new SolidBrush(FluentTheme.Hover)) g.FillRectangle(brush, left + 6.DpiZoom(), y - 4.DpiZoom(), width - 12.DpiZoom(), 26.DpiZoom());
                FluentIcons.Draw(g, i == 0 ? "\uE700" : i == 1 ? "\uE8C8" : "\uE713", new Rectangle(left + 12.DpiZoom(), y, 20.DpiZoom(), 18.DpiZoom()), i == 0 ? FluentTheme.Accent : FluentTheme.Muted);
                using(var pen = new Pen(i == 0 ? FluentTheme.Accent : FluentTheme.Border, 3.DpiZoom())) g.DrawLine(pen, left + 44.DpiZoom(), y + 9.DpiZoom(), left + (i == 2 ? 105 : 125).DpiZoom(), y + 9.DpiZoom());
            }
        }
    }
}
