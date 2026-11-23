using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Methods;
using System;
using System.Windows.Forms;

namespace Rmemu.Controls
{
    sealed class ExplorerRestarter : MyListItem
    {
        public ExplorerRestarter()
        {
            this.Visible = false;
            this.DoubleBuffered = false;
            this.Dock = DockStyle.Bottom;
            this.Image = AppImage.Explorer;
            this.Text = AppString.W("ExplorerRestartNeeded");
            ToolTipBox.SetToolTip(BtnRestart, AppString.W("ExplorerRestartTip"));
            this.AddCtr(BtnRestart);
            this.CanMoveForm();
            ShowHandler += () => this.Visible = true;
            HideHandler += () => this.Visible = false;
            BtnRestart.MouseDown += (sender, e) =>
            {
                if(e.Button != MouseButtons.Left) return;
                if(MessageBox.Show(this, AppString.W("ExplorerRestartWarn"), AppString.W("ApplyMenuChanges"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                ExternalProgram.RestartExplorer();
                this.Visible = false;
            };
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public new bool Visible
        {
            get => base.Visible;
            set
            {
                base.Visible = value;
            }
        }

        private readonly PictureButton BtnRestart = new PictureButton(AppImage.RestartExplorer);

        private static Action ShowHandler;
        private static Action HideHandler;

        public static new void Show() => ShowHandler?.Invoke();
        public static new void Hide() => HideHandler?.Invoke();
    }
}