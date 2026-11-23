using BluePointLilac.Controls;
using Rmemu.Methods;
using System.Windows.Forms;

namespace Rmemu.Controls.Interfaces
{
    interface IBtnShowMenuItem
    {
        ContextMenuStrip ContextMenuStrip { get; set; }
        MenuButton BtnShowMenu { get; set; }
    }

    sealed class MenuButton : PictureButton
    {
        public MenuButton(IBtnShowMenuItem item) : base(AppImage.Setting)
        {
            item.ContextMenuStrip = new ContextMenuStrip();
            ((MyListItem)item).AddCtr(this);
            AccessibleName = AppString.W("MoreActions");
            AccessibleDescription = AppString.W("OpenItemMenu");
            this.Click += (sender, e) =>
            {
                if(item.ContextMenuStrip.Visible) item.ContextMenuStrip.Close();
                else item.ContextMenuStrip.Show(this, 0, Height);
            };
        }
    }
}