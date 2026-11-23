using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Controls.Interfaces;
using Rmemu.Methods;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using static Rmemu.Methods.ObjectPath;

namespace Rmemu.Controls
{
    class FoldSubItem : MyListItem
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public FoldGroupItem FoldGroupItem { get; set; }

        public void Indent()
        {
            Padding = new Padding(28.DpiZoom(), 0, 0, 0);
        }
    }

    class FoldGroupItem : MyListItem, IBtnShowMenuItem
    {
        public event EventHandler FoldStateChanged;
        private bool isFold;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsFold
        {
            get => isFold;
            set
            {
                if(isFold == value) return;
                isFold = value;
                FoldMe(value);
                FoldStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string GroupPath { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public PathType PathType { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MenuButton BtnShowMenu { get; set; }
        readonly PictureButton btnFold;
        readonly PictureButton btnOpenPath;
        readonly ToolStripMenuItem tsiFoldAll = new ToolStripMenuItem(AppString.Menu.FoldAll);
        readonly ToolStripMenuItem tsiUnfoldAll = new ToolStripMenuItem(AppString.Menu.UnfoldAll);

        public FoldGroupItem(string groupPath, PathType pathType)
        {
            btnFold = new PictureButton(AppImage.Up);
            BtnShowMenu = new MenuButton(this);
            btnOpenPath = new PictureButton(AppImage.Open);

            if(pathType == PathType.File || pathType == PathType.Directory)
            {
                groupPath = Environment.ExpandEnvironmentVariables(groupPath);
            }
            string tip = null;
            Action openPath = null;
            switch(pathType)
            {
                case PathType.File:
                    tip = AppString.Menu.FileLocation;
                    this.Text = Path.GetFileNameWithoutExtension(groupPath);
                    this.Image = ResourceIcon.GetExtensionIcon(groupPath).ToBitmap();
                    openPath = () => ExternalProgram.JumpExplorer(groupPath, AppConfig.OpenMoreExplorer);
                    break;
                case PathType.Directory:
                    tip = AppString.Menu.FileLocation;
                    this.Text = Path.GetFileNameWithoutExtension(groupPath);
                    this.Image = ResourceIcon.GetFolderIcon(groupPath).ToBitmap();
                    openPath = () => ExternalProgram.OpenDirectory(groupPath);
                    break;
                case PathType.Registry:
                    tip = AppString.Menu.RegistryLocation;
                    openPath = () => ExternalProgram.JumpRegEdit(groupPath, null, AppConfig.OpenMoreRegedit);
                    break;
            }
            this.PathType = pathType;
            this.GroupPath = groupPath;
            this.Font = new Font(this.Font, FontStyle.Bold);
            this.AddCtrs(new[] { btnFold, btnOpenPath });
            this.ContextMenuStrip.Items.AddRange(new[] { tsiFoldAll, tsiUnfoldAll });
            this.MouseDown += (sender, e) =>
            {
                if(e.Button == MouseButtons.Left) Fold();
            };
            btnFold.MouseDown += (sender, e) =>
            {
                Fold();
                btnFold.Image = btnFold.BaseImage;
            };
            tsiFoldAll.Click += (sender, e) => FoldAll(true);
            tsiUnfoldAll.Click += (sender, e) => FoldAll(false);
            btnOpenPath.MouseDown += (sender, e) => openPath.Invoke();
            ToolTipBox.SetToolTip(btnOpenPath, tip);
        }

        public void SetVisibleWithSubItemCount()
        {
            foreach(Control ctr in this.Parent.Controls)
            {
                if(ctr is FoldSubItem item && item.FoldGroupItem == this)
                {
                    this.Visible = true;
                    return;
                }
            }
            this.Visible = false;
        }

        private void Fold()
        {
            this.Parent.SuspendLayout();
            this.IsFold = !this.IsFold;
            this.Parent.ResumeLayout();
        }

        private void FoldMe(bool isFold)
        {
            btnFold.BaseImage = isFold ? AppImage.Down : AppImage.Up;
            foreach(Control ctr in this.Parent?.Controls)
            {
                if(ctr is FoldSubItem item && item.FoldGroupItem == this) ctr.Visible = !isFold;
            }
        }

        private void FoldAll(bool isFold)
        {
            this.Parent.SuspendLayout();
            foreach(Control ctr in this.Parent.Controls)
            {
                if(ctr is FoldGroupItem groupItem) groupItem.IsFold = isFold;
            }
            this.Parent.ResumeLayout();
        }
    }
}