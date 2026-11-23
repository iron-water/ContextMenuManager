using BluePointLilac.Methods;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BluePointLilac.Controls
{
    public class SelectDialog : CommonDialog
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Title { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Selected { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string[] Items { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool CanEdit { get; set; }

        public override void Reset() { }

        protected override bool RunDialog(IntPtr hwndOwner)
        {
            using(SelectForm frm = new SelectForm())
            {
                frm.Text = this.Title;
                frm.CanEdit = this.CanEdit;
                frm.Items = this.Items;
                if(this.Selected != null) frm.Selected = this.Selected;
                else frm.SelectedIndex = this.SelectedIndex;
                Form owner = Control.FromHandle(hwndOwner)?.FindForm();
                if(owner != null) frm.TopMost = owner.TopMost;
                bool flag = frm.ShowDialog(owner) == DialogResult.OK;
                if(flag)
                {
                    this.Selected = frm.Selected;
                    this.SelectedIndex = frm.SelectedIndex;
                }
                return flag;
            }
        }

        sealed class SelectForm : Form
        {
            public SelectForm()
            {
                this.SuspendLayout();
                this.AcceptButton = btnOK;
                this.CancelButton = btnCancel;
                this.Font = FluentTheme.BodyFont;
                this.AutoScaleMode = AutoScaleMode.Dpi;
                this.ShowIcon = this.ShowInTaskbar = false;
                this.MaximizeBox = this.MinimizeBox = false;
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.StartPosition = FormStartPosition.CenterParent;
                this.InitializeComponents();
                this.ResumeLayout();
            }

            protected override void OnLoad(EventArgs e)
            {
                FluentTheme.Apply(this);
                base.OnLoad(e);
            }

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string Selected
            {
                get => cmbItems.Text;
                set => cmbItems.Text = value;
            }

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string[] Items
            {
                get
                {
                    string[] value = new string[cmbItems.Items.Count];
                    cmbItems.Items.CopyTo(value, 0);
                    return value;
                }
                set
                {
                    cmbItems.Items.Clear();
                    if(value != null) cmbItems.Items.AddRange(value);
                }
            }

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public bool CanEdit
            {
                get => cmbItems.DropDownStyle == ComboBoxStyle.DropDown;
                set => cmbItems.DropDownStyle = value ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
            }

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int SelectedIndex
            {
                get => cmbItems.SelectedIndex;
                set => cmbItems.SelectedIndex = value >= 0 && value < cmbItems.Items.Count ? value : -1;
            }

            readonly Button btnOK = new Button
            {
                DialogResult = DialogResult.OK,
                Text = ResourceString.OK,
                AutoSize = true
            };
            readonly Button btnCancel = new Button
            {
                DialogResult = DialogResult.Cancel,
                Text = ResourceString.Cancel,
                AutoSize = true
            };
            readonly ComboBox cmbItems = new ComboBox
            {
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                DropDownHeight = 294.DpiZoom(),
                MaxDropDownItems = 14,
                Dock = DockStyle.Top,
                TabIndex = 0
            };

            private void InitializeComponents()
            {
                int inset = 20.DpiZoom();
                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, Padding = new Padding(inset), ColumnCount = 1, RowCount = 2
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var actions = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
                    FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 20.DpiZoom(), 0, 0)
                };
                foreach(Button button in new[] { btnCancel, btnOK })
                {
                    button.MinimumSize = new Size(88, 30).DpiZoom();
                    button.Padding = new Padding(12, 3, 12, 3).DpiZoom();
                    button.Margin = new Padding(8.DpiZoom(), 0, 0, 0);
                    actions.Controls.Add(button);
                }
                btnOK.TabIndex = 0;
                btnCancel.TabIndex = 1;
                cmbItems.Margin = Padding.Empty;
                layout.Controls.Add(cmbItems, 0, 0);
                layout.Controls.Add(actions, 0, 1);
                Controls.Add(layout);
                ClientSize = new Size(440, 142).DpiZoom();
                cmbItems.AutosizeDropDownWidth();
            }
        }
    }
}