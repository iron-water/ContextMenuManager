using System;
using BluePointLilac.Methods;
using Rmemu.Methods;
using System.Drawing;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    public class PictureButton : PictureBox
    {
        private static readonly Font iconFont = new Font("Segoe MDL2 Assets", 12F, FontStyle.Regular, GraphicsUnit.Point);
        private Image baseImage;
        private Image disabledImage;
        private string glyph;
        private bool hovered;

        public PictureButton(Image image)
        {
            SizeMode = PictureBoxSizeMode.Zoom;
            Size = new Size(28, 28).DpiZoom();
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            BaseImage = image;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Image BaseImage
        {
            get => baseImage;
            set
            {
                Image = null;
                disabledImage?.Dispose();
                disabledImage = null;
                baseImage = value;
                glyph = GetGlyph(value);
                if(glyph == null && value != null) disabledImage = ToolStripRenderer.CreateDisabledImage(value);
                ApplyTheme();
            }
        }

        private string GetGlyph(Image image)
        {
            if(image == null) return null;
            string name;
            string symbol;
            if(ReferenceEquals(image, AppImage.Setting)) { name = AppString.W("MoreActions"); symbol = "\uE712"; }
            else if(ReferenceEquals(image, AppImage.Open)) { name = AppString.W("OpenLocation"); symbol = "\uE8DA"; }
            else if(ReferenceEquals(image, AppImage.AddNewItem)) { name = AppString.W("AddItem"); symbol = "\uE710"; }
            else if(ReferenceEquals(image, AppImage.AddExisting)) { name = AppString.W("AddFromPublic"); symbol = "\uE8F1"; }
            else if(ReferenceEquals(image, AppImage.Enhance)) { name = AppString.W("AddEnhance"); symbol = "\uE734"; }
            else if(ReferenceEquals(image, AppImage.SubItems)) { name = AppString.W("EditDetails"); symbol = "\uE70F"; }
            else if(ReferenceEquals(image, AppImage.AddSeparator)) { name = AppString.W("AddSeparator"); symbol = "\uE738"; }
            else if(ReferenceEquals(image, AppImage.Select)) { name = AppString.W("PickTarget"); symbol = "\uE8B7"; }
            else if(ReferenceEquals(image, AppImage.Jump)) { name = AppString.W("GoToMenu"); symbol = "\uE8A7"; }
            else if(ReferenceEquals(image, AppImage.NewFolder)) { name = AppString.W("NewWinXGroup"); symbol = "\uE8F4"; }
            else if(ReferenceEquals(image, AppImage.Delete)) { name = AppString.W("Delete"); symbol = "\uE74D"; }
            else if(ReferenceEquals(image, AppImage.Up)) { name = AppString.W("MoveUp"); symbol = "\uE70E"; }
            else if(ReferenceEquals(image, AppImage.Down)) { name = AppString.W("MoveDown"); symbol = "\uE70D"; }
            else if(ReferenceEquals(image, AppImage.CheckUpdate) || ReferenceEquals(image, AppImage.Refresh)) { name = AppString.W("CheckUpdate"); symbol = "\uE72C"; }
            else if(ReferenceEquals(image, AppImage.DownLoad)) { name = AppString.W("Download"); symbol = "\uE896"; }
            else if(ReferenceEquals(image, AppImage.Translate)) { name = AppString.W("Translate"); symbol = "\uE8C1"; }
            else if(ReferenceEquals(image, AppImage.RestartExplorer)) { name = AppString.W("RestartExplorer"); symbol = "\uE777"; }
            else return null;
            AccessibleName = name;
            return symbol;
        }

        internal void ApplyTheme()
        {
            Image = glyph == null ? Enabled ? baseImage : disabledImage : null;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); ApplyTheme(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if(e.Button != MouseButtons.Left || !Enabled) return;
            Focus();
            base.OnMouseDown(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                Activate();
                e.Handled = e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
        private void Activate()
        {
            if(!Enabled) return;
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
            OnClick(EventArgs.Empty);
        }
        protected override AccessibleObject CreateAccessibilityInstance() => new ActionAccessibleObject(this);
        private sealed class ActionAccessibleObject : ControlAccessibleObject
        {
            private readonly PictureButton owner;
            public ActionAccessibleObject(PictureButton owner) : base(owner) { this.owner = owner; }
            public override AccessibleRole Role => AccessibleRole.PushButton;
            public override string DefaultAction => AppString.W("Run");
            public override void DoDefaultAction() => owner.Activate();
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            bool highlighted = Enabled && (hovered || Focused);
            if(highlighted)
                using(Brush brush = new SolidBrush(FluentTheme.Hover)) e.Graphics.FillRectangle(brush, ClientRectangle);
            base.OnPaint(e);
            if(glyph != null)
                TextRenderer.DrawText(e.Graphics, glyph, iconFont, ClientRectangle,
                    !Enabled ? FluentTheme.Muted : highlighted && SystemInformation.HighContrast ? SystemColors.HighlightText : FluentTheme.Foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if(Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(1, 1, Width - 3, Height - 3), FluentTheme.Foreground, BackColor);
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                Image = null;
                disabledImage?.Dispose();
                disabledImage = null;
            }
            base.Dispose(disposing);
        }
    }
}