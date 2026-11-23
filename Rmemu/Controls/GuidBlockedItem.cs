using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Controls.Interfaces;
using Rmemu.Methods;
using System;
using System.Linq;
using System.Windows.Forms;

namespace Rmemu.Controls
{
    class GuidBlockedItem : MyListItem, IBtnShowMenuItem, ITsiWebSearchItem, ITsiFilePathItem, ITsiGuidItem, ITsiRegPathItem
    {
        public GuidBlockedItem(string value)
        {
            InitializeComponents();
            this.Value = value;
            if(GuidEx.TryParse(value, out Guid guid))
            {
                this.Guid = guid;
                this.Image = GuidInfo.GetImage(guid);
                this.ItemFilePath = GuidInfo.GetFilePath(Guid);
            }
            else
            {
                this.Guid = Guid.Empty;
                this.Image = AppImage.SystemFile;
            }
            this.Text = this.ItemText;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Value { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Guid Guid { get; set; }
        public string SearchText => Value;
        public string ValueName => Value;
        public string RegPath
        {
            get
            {
                foreach(string path in GuidBlockedList.BlockedPaths)
                {
                    using(var key = RegistryEx.GetRegistryKey(path))
                    {
                        if(key == null) continue;
                        if(key.GetValueNames().Contains(Value, StringComparer.OrdinalIgnoreCase)) return path;
                    }
                }
                return null;
            }
        }

        public string ItemText
        {
            get
            {
                string text;
                if(GuidEx.TryParse(Value, out Guid guid)) text = GuidInfo.GetText(guid);
                else text = AppString.Message.MalformedGuid;
                text += "\n" + Value;
                return text;
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string ItemFilePath { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MenuButton BtnShowMenu { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public DetailedEditButton BtnDetailedEdit { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public WebSearchMenuItem TsiSearch { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public FileLocationMenuItem TsiFileLocation { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public FilePropertiesMenuItem TsiFileProperties { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public HandleGuidMenuItem TsiHandleGuid { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegLocationMenuItem TsiRegLocation { get; set; }

        readonly ToolStripMenuItem TsiDetails = new ToolStripMenuItem(AppString.Menu.Details);
        readonly ToolStripMenuItem TsiDelete = new ToolStripMenuItem(AppString.Menu.Delete);

        private void InitializeComponents()
        {
            BtnShowMenu = new MenuButton(this);
            BtnDetailedEdit = new DetailedEditButton(this);
            TsiSearch = new WebSearchMenuItem(this);
            TsiFileProperties = new FilePropertiesMenuItem(this);
            TsiFileLocation = new FileLocationMenuItem(this);
            TsiRegLocation = new RegLocationMenuItem(this);
            TsiHandleGuid = new HandleGuidMenuItem(this);

            ContextMenuStrip.Items.AddRange(new ToolStripItem[] {TsiHandleGuid,
                new ToolStripSeparator(), TsiDetails, new ToolStripSeparator(), TsiDelete });
            TsiDetails.DropDownItems.AddRange(new ToolStripItem[] { TsiSearch,
                new ToolStripSeparator(), TsiFileProperties, TsiFileLocation, TsiRegLocation});

            TsiDelete.Click += (sender, e) => DeleteMe();
        }

        public void DeleteMe()
        {
            if(AppMessageBox.Show(AppString.Message.ConfirmDelete, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Array.ForEach(GuidBlockedList.BlockedPaths, path => RegistryEx.DeleteValue(path, this.Value));
            if(!this.Guid.Equals(Guid.Empty)) ExplorerRestarter.Show();
            int index = this.Parent.Controls.GetChildIndex(this);
            index -= (index < this.Parent.Controls.Count - 1) ? 0 : 1;
            this.Parent.Controls[index].Focus();
            this.Dispose();
        }
    }
}