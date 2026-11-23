using BluePointLilac.Methods;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    public sealed class ReadOnlyTextBox : TextBox
    {
        public ReadOnlyTextBox()
        {
            this.Font = FluentTheme.BodyFont;
            this.BorderStyle = BorderStyle.FixedSingle;
            this.HideSelection = false;
            this.ReadOnly = true;
            this.Multiline = true;
            this.ShortcutsEnabled = true;
            this.BackColor = FluentTheme.Surface;
            this.ForeColor = FluentTheme.Foreground;
        }

    }

    public sealed class ReadOnlyRichTextBox : RichTextBox
    {
        public ReadOnlyRichTextBox()
        {
            this.Font = FluentTheme.BodyFont;
            this.HideSelection = false;
            this.ReadOnly = true;
            this.Dock = DockStyle.Fill;
            this.BackColor = FluentTheme.Surface;
            this.BorderStyle = BorderStyle.None;
            this.ForeColor = FluentTheme.Foreground;
        }


        protected override void OnLinkClicked(LinkClickedEventArgs e)
        {
            base.OnLinkClicked(e);
            ExternalProgram.OpenWebUrl(e.LinkText);
        }
    }
}