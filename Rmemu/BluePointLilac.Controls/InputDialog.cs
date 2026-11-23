using BluePointLilac.Methods;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    public sealed class InputDialog : CommonDialog
    {
        /// <summary>输入对话框标题</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Title { get; set; } = Application.ProductName;
        /// <summary>输入对话框文本框文本</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Text { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Size Size { get; set; }

        public override void Reset() { }

        protected override bool RunDialog(IntPtr hwndOwner)
        {
            using(InputBox frm = new InputBox())
            {
                frm.Text = Title;
                frm.InputedText = this.Text;
                if(!this.Size.IsEmpty) frm.Size = this.Size;
                Form owner = Control.FromHandle(hwndOwner)?.FindForm();
                if(owner != null) frm.TopMost = owner.TopMost;
                bool flag = frm.ShowDialog(owner) == DialogResult.OK;
                this.Text = flag ? frm.InputedText : null;
                return flag;
            }
        }

        sealed class InputBox : Form
        {
            public InputBox()
            {
                this.AcceptButton = btnOK;
                this.CancelButton = btnCancel;
                this.Font = FluentTheme.BodyFont;
                this.SizeGripStyle = SizeGripStyle.Hide;
                this.StartPosition = FormStartPosition.CenterParent;
                this.MaximizeBox = MinimizeBox = ShowIcon = ShowInTaskbar = false;
                this.AutoScaleMode = AutoScaleMode.Dpi;
                txtInput.CanResizeFont();
                InitializeComponents();
            }

            protected override void OnLoad(EventArgs e)
            {
                FluentTheme.Apply(this);
                base.OnLoad(e);
            }

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string InputedText
            {
                get => txtInput.Text;
                set => txtInput.Text = value;
            }

            readonly TextBox txtInput = new TextBox
            {
                ScrollBars = ScrollBars.Vertical,
                Multiline = true,
                AcceptsReturn = true,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                TabIndex = 0
            };
            readonly Button btnOK = new Button
            {
                TabIndex = 1,
                DialogResult = DialogResult.OK,
                Text = ResourceString.OK,
                AutoSize = true
            };
            readonly Button btnCancel = new Button
            {
                TabIndex = 2,
                DialogResult = DialogResult.Cancel,
                Text = ResourceString.Cancel,
                AutoSize = true
            };

            private void InitializeComponents()
            {
                SuspendLayout();
                int inset = 20.DpiZoom();
                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, Padding = new Padding(inset), ColumnCount = 1, RowCount = 2
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var actions = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
                    FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 16.DpiZoom(), 0, 0)
                };
                foreach(Button button in new[] { btnCancel, btnOK })
                {
                    button.MinimumSize = new Size(88, 30).DpiZoom();
                    button.Padding = new Padding(12, 3, 12, 3).DpiZoom();
                    button.Margin = new Padding(8.DpiZoom(), 0, 0, 0);
                    actions.Controls.Add(button);
                }
                txtInput.Margin = Padding.Empty;
                layout.Controls.Add(txtInput, 0, 0);
                layout.Controls.Add(actions, 0, 1);
                Controls.Add(layout);
                ClientSize = new Size(440, 230).DpiZoom();
                MinimumSize = Size;
                ResumeLayout();
            }
        }
    }
}