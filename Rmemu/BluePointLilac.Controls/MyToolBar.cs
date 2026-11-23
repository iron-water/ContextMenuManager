using BluePointLilac.Methods;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    public sealed class MyToolBar : FlowLayoutPanel
    {
        public MyToolBar()
        {
            this.Height = 80.DpiZoom();
            this.Dock = DockStyle.Top;
            this.DoubleBuffered = true;
            this.BackColor = FluentTheme.Surface;
        }

        private MyToolBarButton selectedButton;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MyToolBarButton SelectedButton
        {
            get => selectedButton;
            set
            {
                if(selectedButton == value) return;
                if(selectedButton != null)
                {
                    selectedButton.Opacity = 0;
                    selectedButton.Cursor = Cursors.Hand;
                }
                selectedButton = value;
                if(selectedButton != null)
                {
                    selectedButton.Opacity = 0.4F;
                    selectedButton.Cursor = Cursors.Default;
                }
                SelectedButtonChanged?.Invoke(this, null);
            }
        }

        public event EventHandler SelectedButtonChanged;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get
            {
                if(SelectedButton == null) return -1;
                else return Controls.GetChildIndex(SelectedButton);
            }
            set
            {
                if(value < 0 || value >= this.Controls.Count) SelectedButton = null;
                else SelectedButton = (MyToolBarButton)Controls[value];
            }
        }

        public void AddButton(MyToolBarButton button)
        {
            this.SuspendLayout();
            button.Parent = this;
            button.Margin = new Padding(12, 4, 0, 0).DpiZoom();
            button.MouseDown += (sender, e) =>
            {
                if(e.Button == MouseButtons.Left && button.CanBeSelected) SelectedButton = button;
            };
            button.MouseEnter += (sender, e) =>
            {
                if(button != SelectedButton) button.Opacity = 0.2F;
            };
            button.MouseLeave += (sender, e) =>
            {
                if(button != SelectedButton) button.Opacity = 0;
            };
            this.ResumeLayout();
        }

        public void AddButtons(MyToolBarButton[] buttons)
        {
            int maxWidth = 72.DpiZoom();
            Array.ForEach(buttons, button => maxWidth = Math.Max(maxWidth, TextRenderer.MeasureText(button.Text, button.Font).Width));
            Array.ForEach(buttons, button => { button.Width = maxWidth; AddButton(button); });
        }
    }

    public sealed class MyToolBarButton : Panel
    {
        public MyToolBarButton(Image image, string text)
        {
            this.SuspendLayout();
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.Selectable, true);
            this.TabStop = true;
            this.AccessibleRole = AccessibleRole.PushButton;
            this.Cursor = Cursors.Hand;
            this.Size = new Size(72, 72).DpiZoom();
            this.Controls.AddRange(new Control[] { picImage, lblText });
            lblText.Resize += (sender, e) => this.OnResize(null);
            picImage.Top = 6.DpiZoom();
            lblText.Top = 52.DpiZoom();
            lblText.SetEnabled(false);
            this.Image = image;
            this.Text = text;
            this.ResumeLayout();
        }

        readonly PictureBox picImage = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.StretchImage,
            Size = new Size(40, 40).DpiZoom(),
            BackColor = Color.Transparent,
            Enabled = false
        };

        readonly Label lblText = new Label
        {
            BackColor = Color.Transparent,
            Font = SystemFonts.MenuFont,
            ForeColor = FluentTheme.Foreground,
            AutoSize = true,
        };

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Image Image
        {
            get => picImage.Image;
            set => picImage.Image = value;
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public new string Text
        {
            get => lblText.Text;
            set { lblText.Text = value; AccessibleName = value; }
        }
        private float opacity;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float Opacity
        {
            get => opacity;
            set { opacity = Math.Max(0, Math.Min(1, value)); ApplyTheme(); }
        }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool CanBeSelected { get; set; } = true;

        internal void ApplyTheme()
        {
            BackColor = opacity > 0 ? FluentTheme.Hover : FluentTheme.Surface;
            lblText.BackColor = picImage.BackColor = Color.Transparent;
            lblText.ForeColor = opacity > 0 && SystemInformation.HighContrast ? SystemColors.HighlightText : FluentTheme.Foreground;
            Invalidate();
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                OnClick(EventArgs.Empty);
                e.Handled = e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if(opacity >= 0.4F)
            {
                using(Brush brush = new SolidBrush(FluentTheme.Accent))
                    e.Graphics.FillRectangle(brush, 12, Height - 3, Math.Max(1, Width - 24), 3);
            }
            if(Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(2, 2, Width - 5, Height - 5), FluentTheme.Foreground, BackColor);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            lblText.Left = (this.Width - lblText.Width) / 2;
            picImage.Left = (this.Width - picImage.Width) / 2;
        }
    }
}