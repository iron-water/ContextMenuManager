using BluePointLilac.Methods;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    public sealed class MyStatusBar : Panel
    {
        public static readonly string DefaultText = $"Ver: {Application.ProductVersion}    {Application.CompanyName}";

        public MyStatusBar()
        {
            this.Text = DefaultText;
            this.Height = 30.DpiZoom();
            this.Dock = DockStyle.Bottom;
            this.Font = SystemFonts.StatusFont;
            this.BackColor = FluentTheme.Surface;
            this.ForeColor = FluentTheme.Muted;
        }

        [Browsable(true), EditorBrowsable(EditorBrowsableState.Always)]
        public override string Text { get => base.Text; set => base.Text = value; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle bounds = new Rectangle(12.DpiZoom(), 0, Math.Max(0, ClientSize.Width - 24.DpiZoom()), ClientSize.Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using(Pen pen = new Pen(FluentTheme.Border)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); this.Refresh();
        }
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e); this.Refresh();
        }
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e); this.Refresh();
        }
        protected override void OnForeColorChanged(EventArgs e)
        {
            base.OnForeColorChanged(e); this.Refresh();
        }
        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e); this.Refresh();
        }
    }
}