using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Controls.Interfaces;
using Rmemu.Methods;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Rmemu.Controls
{
    class ShellItem : MyListItem, IChkVisibleItem, IBtnShowMenuItem, ITsiTextItem, ITsiCommandItem, IProtectOpenItem,
        ITsiIconItem, ITsiWebSearchItem, ITsiFilePathItem, ITsiRegPathItem, ITsiRegDeleteItem, ITsiRegExportItem
    {
        /// <summary>Shell公共引用子菜单注册表项路径</summary>
        public const string CommandStorePath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell";

        private const string OpenInNewWindowPath = @"HKEY_CLASSES_ROOT\Folder\shell\opennewwindow";

        /// <summary>Shell类型菜单特殊注册表项名默认名称</summary>
        /// <remarks>字符串资源在windows.storage.dll里面</remarks>
        private static readonly Dictionary<string, int> DefaultNameIndexs
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "open", 8496 }, { "edit", 8516 }, { "print", 8497 }, { "find", 8503 },
                { "play", 8498 }, { "runas", 8505 }, { "explore", 8502 }, { "preview", 8499 }
            };

        /// <summary>菜单项目在菜单中出现的位置</summary>
        enum Positions { Default, Top, Bottom }

        public ShellItem(string regPath)
        {
            InitializeComponents();
            this.RegPath = regPath;
        }

        private string regPath;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string RegPath
        {
            get => regPath;
            set
            {
                regPath = value;
                this.Text = this.ItemText;
                this.Image = this.ItemIcon.ToBitmap();
                if(!HasIcon) this.Image = Image.ToTransparent();
                BtnSubItems.Visible = IsMultiItem;
            }
        }

        public string ValueName => null;
        public string SearchText => Text;
        private string CommandPath => $@"{RegPath}\command";
        public string KeyName => RegistryEx.GetKeyName(RegPath);
        protected virtual bool IsSubItem => false;
        private bool IsOpenItem => KeyName.ToLower() == "open";
        public string ItemFilePath => GuidInfo.GetFilePath(Guid) ?? ObjectPath.ExtractFilePath(ItemCommand);
        private bool HasIcon => !IconLocation.IsNullOrWhiteSpace() || HasLUAShield;

        private bool IsMultiItem
        {
            get
            {
                object value = Registry.GetValue(RegPath, "SubCommands", null);
                if(value != null) return true;
                value = Registry.GetValue(RegPath, "ExtendedSubCommandsKey", null);
                if(!string.IsNullOrEmpty(value?.ToString())) return true;
                return false;
            }
        }

        private bool OnlyInExplorer
        {
            get => Registry.GetValue(RegPath, "OnlyInBrowserWindow", null) != null;
            set
            {
                if(value)
                {
                    if(!TryProtectOpenItem()) return;
                    Registry.SetValue(RegPath, "OnlyInBrowserWindow", "");
                }
                else RegistryEx.DeleteValue(RegPath, "OnlyInBrowserWindow");
            }
        }

        private bool OnlyWithShift
        {
            get => Registry.GetValue(RegPath, "Extended", null) != null;
            set
            {
                if(value)
                {
                    if(!TryProtectOpenItem()) return;
                    Registry.SetValue(RegPath, "Extended", "");
                }
                else RegistryEx.DeleteValue(RegPath, "Extended");
            }
        }

        private bool NoWorkingDirectory
        {
            get => Registry.GetValue(RegPath, "NoWorkingDirectory", null) != null;
            set
            {
                if(!TryProtectOpenItem()) return;
                if(value) Registry.SetValue(RegPath, "NoWorkingDirectory", "");
                else RegistryEx.DeleteValue(RegPath, "NoWorkingDirectory");
            }
        }

        private bool NeverDefault
        {
            get => Registry.GetValue(RegPath, "NeverDefault", null) != null;
            set
            {
                if(!TryProtectOpenItem()) return;
                if(value) Registry.SetValue(RegPath, "NeverDefault", "");
                else RegistryEx.DeleteValue(RegPath, "NeverDefault");
            }
        }

        private bool ShowAsDisabledIfHidden
        {
            get => Registry.GetValue(RegPath, "ShowAsDisabledIfHidden", null) != null;
            set
            {
                if(!TryProtectOpenItem()) return;
                if(value) Registry.SetValue(RegPath, "ShowAsDisabledIfHidden", "");
                else RegistryEx.DeleteValue(RegPath, "ShowAsDisabledIfHidden");
                if(value && !ItemVisible && !ChangeHistory.TryChangeVisibility(this, false, out _, out string error, true))
                    throw new IOException(error);
            }
        }

        private Positions ItemPosition
        {
            get
            {
                string value = Registry.GetValue(RegPath, "Position", null)?.ToString()?.ToLower();
                switch(value)
                {
                    case "top":
                        return Positions.Top;
                    case "bottom":
                        return Positions.Bottom;
                    default:
                        return Positions.Default;
                }
            }
            set
            {
                switch(value)
                {
                    case Positions.Top:
                        Registry.SetValue(RegPath, "Position", "top");
                        break;
                    case Positions.Bottom:
                        Registry.SetValue(RegPath, "Position", "bottom");
                        break;
                    case Positions.Default:
                        RegistryEx.DeleteValue(RegPath, "Position");
                        break;
                }
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ItemVisible
        {
            get
            {
                using(RegistryKey key = RegistryEx.GetRegistryKey(RegPath))
                {
                    if(key == null) throw new IOException(AppString.W("MenuKeyDeleted"));
                    if(WinOsVersion.Current >= WinOsVersion.Win10_1703 && Convert.ToInt32(key.GetValue("HideBasedOnVelocityId", 0)) == 0x639bc8) return false;
                    if(!IsSubItem)
                    {
                        if(key.GetValue("LegacyDisable", null) != null || key.GetValue("ProgrammaticAccessOnly", null) != null) return false;
                        if(WinOsVersion.Current > WinOsVersion.Vista && (Convert.ToInt32(key.GetValue("CommandFlags", 0)) & 8) != 0) return false;
                    }
                    return true;
                }
            }
            set
            {
                if(!ChangeHistory.TryChangeVisibility(this, value, out _, out string error))
                    throw new IOException(error);
            }
        }

        internal RegistryHistoryNode CreateVisibilitySnapshot(RegistryHistoryNode original, bool visible)
        {
            RegistryHistoryNode result = ChangeHistory.CloneNode(original);
            void Remove(string name) => result.Values.RemoveAll(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            void Set(string name, RegistryValueKind kind, string data)
            {
                Remove(name);
                result.Values.Add(new RegistryHistoryValue { Name = name, Kind = kind, Data = new[] { data } });
            }
            void RemoveLegacyValues()
            {
                Remove("LegacyDisable");
                Remove("ProgrammaticAccessOnly");
                RegistryHistoryValue flags = result.Values.Find(value => value.Name.Equals("CommandFlags", StringComparison.OrdinalIgnoreCase));
                if(WinOsVersion.Current > WinOsVersion.Vista && flags != null && (Convert.ToInt32(flags.GetData()) & 8) != 0)
                {
                    int remaining = Convert.ToInt32(flags.GetData()) & ~8;
                    if(remaining == 0) Remove("CommandFlags");
                    else Set("CommandFlags", RegistryValueKind.DWord, remaining.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            if(visible)
            {
                Remove("HideBasedOnVelocityId");
                RemoveLegacyValues();
            }
            else
            {
                if(WinOsVersion.Current >= WinOsVersion.Win10_1703)
                    Set("HideBasedOnVelocityId", RegistryValueKind.DWord, (0x639bc8).ToString(System.Globalization.CultureInfo.InvariantCulture));
                else if(IsSubItem) throw new InvalidOperationException(AppString.Message.CannotHideSubItem);
                if(!IsSubItem)
                {
                    if(!RegPath.Equals(OpenInNewWindowPath, StringComparison.OrdinalIgnoreCase))
                        Set("LegacyDisable", RegistryValueKind.String, "");
                    Set("ProgrammaticAccessOnly", RegistryValueKind.String, "");
                }
                if(result.Values.Exists(value => value.Name.Equals("ShowAsDisabledIfHidden", StringComparison.OrdinalIgnoreCase))) RemoveLegacyValues();
            }
            result.Values.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
            return result;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string ItemText
        {
            get
            {
                string name;
                //菜单名称优先级别：MUIVerb > 默认值 > 特殊键值名 > 项名
                List<string> valueNames = new List<string> { "MUIVerb" };
                if(!IsMultiItem) valueNames.Add("");//多级母菜单不支持使用默认值作为名称
                foreach(string valueName in valueNames)
                {
                    name = Registry.GetValue(RegPath, valueName, null)?.ToString();
                    name = ResourceString.GetDirectString(name);
                    if(!string.IsNullOrEmpty(name)) return name;
                }
                if(DefaultNameIndexs.TryGetValue(KeyName, out int index))
                {
                    name = $"@windows.storage.dll,-{index}";
                    name = ResourceString.GetDirectString(name);
                    if(!string.IsNullOrEmpty(name)) return name;
                }
                return KeyName;
            }
            set
            {
                //MUIVerb长度不可超过80,超过80系统会隐藏该菜单项目
                if(ResourceString.GetDirectString(value).Length >= 80)
                {
                    AppMessageBox.Show(AppString.Message.TextLengthCannotExceed80);
                }
                else
                {
                    Registry.SetValue(RegPath, "MUIVerb", value);
                    this.Text = ResourceString.GetDirectString(value);
                }
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string ItemCommand
        {
            get
            {
                if(IsMultiItem) return null;
                else return Registry.GetValue(CommandPath, "", null)?.ToString();
            }
            set
            {
                if(!TryProtectOpenItem()) return;
                Registry.SetValue(CommandPath, "", value);
                if(!this.HasIcon) this.Image = this.ItemIcon.ToBitmap().ToTransparent();
            }
        }

        private bool HasLUAShield
        {
            get => Registry.GetValue(RegPath, "HasLUAShield", null) != null;
            set
            {
                if(value) Registry.SetValue(RegPath, "HasLUAShield", "");
                else RegistryEx.DeleteValue(RegPath, "HasLUAShield");
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string IconLocation
        {
            get => Registry.GetValue(RegPath, "Icon", null)?.ToString();
            set
            {
                if(value != null) Registry.SetValue(RegPath, "Icon", value);
                else RegistryEx.DeleteValue(RegPath, "Icon");
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string IconPath { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int IconIndex { get; set; }
        public Icon ItemIcon
        {
            get
            {
                //菜单图标优先级别：Icon > HasLUAShield
                //只要有Icon键值，不论数据是否为空，HasLUAShield键值就不起作用
                Icon icon;
                string iconPath;
                int iconIndex;
                if(IconLocation != null)
                {
                    icon = ResourceIcon.GetIcon(IconLocation, out iconPath, out iconIndex);
                    if(icon == null && Path.GetExtension(iconPath)?.ToLower() == ".exe")//文件不存在，或为没有图标的exe文件
                        icon = ResourceIcon.GetIcon(iconPath = "imageres.dll", iconIndex = -15);//不含图标的默认exe图标
                }
                else if(HasLUAShield)
                    icon = ResourceIcon.GetIcon(iconPath = "imageres.dll", iconIndex = -78);//管理员小盾牌图标
                else icon = ResourceIcon.GetIcon(iconPath = ItemFilePath, iconIndex = 0);//文件第一个图标
                if(icon == null) icon = ResourceIcon.GetExtensionIcon(iconPath = ItemFilePath)//文件类型图标
                        ?? ResourceIcon.GetIcon(iconPath = "imageres.dll", iconIndex = -2);//图标资源不存在，白纸图标
                IconPath = iconPath;
                IconIndex = iconIndex;
                return icon;
            }
        }

        private Guid Guid
        {
            get
            {
                Dictionary<string, string> keyValues = new Dictionary<string, string>
                {
                    { CommandPath , "DelegateExecute" },
                    { $@"{RegPath}\DropTarget" , "CLSID" },
                    { RegPath , "ExplorerCommandHandler" },
                };
                foreach(var item in keyValues)
                {
                    string value = Registry.GetValue(item.Key, item.Value, null)?.ToString();
                    if(GuidEx.TryParse(value, out Guid guid)) return guid;
                }
                return Guid.Empty;
            }
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public VisibleCheckBox ChkVisible { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MenuButton BtnShowMenu { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ChangeTextMenuItem TsiChangeText { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ChangeIconMenuItem TsiChangeIcon { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ChangeCommandMenuItem TsiChangeCommand { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public WebSearchMenuItem TsiSearch { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public FilePropertiesMenuItem TsiFileProperties { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public FileLocationMenuItem TsiFileLocation { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegLocationMenuItem TsiRegLocation { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public DeleteMeMenuItem TsiDeleteMe { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegExportMenuItem TsiRegExport { get; set; }

        protected readonly PictureButton BtnSubItems = new PictureButton(AppImage.SubItems);
        protected readonly ToolStripMenuItem TsiOtherAttributes = new ToolStripMenuItem(AppString.Menu.OtherAttributes);
        readonly ToolStripMenuItem TsiItemIcon = new ToolStripMenuItem(AppString.Menu.ItemIcon);
        readonly ToolStripMenuItem TsiDeleteIcon = new ToolStripMenuItem(AppString.Menu.DeleteIcon);
        readonly ToolStripMenuItem TsiShieldIcon = new ToolStripMenuItem(AppString.Menu.ShieldIcon);
        readonly ToolStripMenuItem TsiPosition = new ToolStripMenuItem(AppString.Menu.ItemPosition);
        readonly ToolStripMenuItem TsiDefault = new ToolStripMenuItem(AppString.Menu.SetDefault);
        readonly ToolStripMenuItem TsiSetTop = new ToolStripMenuItem(AppString.Menu.SetTop);
        readonly ToolStripMenuItem TsiSetBottom = new ToolStripMenuItem(AppString.Menu.SetBottom);
        readonly ToolStripMenuItem TsiOnlyWithShift = new ToolStripMenuItem(AppString.Menu.OnlyWithShift);
        readonly ToolStripMenuItem TsiOnlyInExplorer = new ToolStripMenuItem(AppString.Menu.OnlyInExplorer);
        readonly ToolStripMenuItem TsiNoWorkDir = new ToolStripMenuItem(AppString.Menu.NoWorkingDirectory);
        readonly ToolStripMenuItem TsiNeverDefault = new ToolStripMenuItem(AppString.Menu.NeverDefault);
        readonly ToolStripMenuItem TsiDetails = new ToolStripMenuItem(AppString.Menu.Details);
        readonly ToolStripMenuItem TsiShowAsDisabled = new ToolStripMenuItem(AppString.Menu.ShowAsDisabledIfHidden);
        readonly ToolStripMenuItem TsiClsidLocation = new ToolStripMenuItem(AppString.Menu.ClsidLocation);

        private void InitializeComponents()
        {
            BtnShowMenu = new MenuButton(this);
            ChkVisible = new VisibleCheckBox(this);
            TsiChangeText = new ChangeTextMenuItem(this);
            TsiChangeCommand = new ChangeCommandMenuItem(this);
            TsiChangeIcon = new ChangeIconMenuItem(this);
            TsiSearch = new WebSearchMenuItem(this);
            TsiFileLocation = new FileLocationMenuItem(this);
            TsiFileProperties = new FilePropertiesMenuItem(this);
            TsiRegLocation = new RegLocationMenuItem(this);
            TsiRegExport = new RegExportMenuItem(this);
            TsiDeleteMe = new DeleteMeMenuItem(this);

            ContextMenuStrip.Items.AddRange(new ToolStripItem[] { TsiChangeText, new ToolStripSeparator(), TsiItemIcon,
                TsiPosition, TsiOtherAttributes, new ToolStripSeparator(), TsiDetails, new ToolStripSeparator(), TsiDeleteMe});

            TsiItemIcon.DropDownItems.AddRange(new ToolStripItem[] { TsiChangeIcon, TsiDeleteIcon, TsiShieldIcon });

            TsiPosition.DropDownItems.AddRange(new ToolStripItem[] { TsiDefault, TsiSetTop, TsiSetBottom });

            TsiOtherAttributes.DropDownItems.AddRange(new ToolStripItem[] { TsiOnlyWithShift, TsiOnlyInExplorer,
                TsiNoWorkDir, TsiNeverDefault, TsiShowAsDisabled });

            TsiDetails.DropDownItems.AddRange(new ToolStripItem[] { TsiSearch, new ToolStripSeparator(),
                TsiChangeCommand, TsiFileProperties, TsiFileLocation, TsiRegLocation, TsiRegExport, TsiClsidLocation});

            TsiDeleteIcon.Click += (sender, e) => DeleteIcon();
            TsiSetTop.Click += (sender, e) => this.ItemPosition = Positions.Top;
            TsiSetBottom.Click += (sender, e) => this.ItemPosition = Positions.Bottom;
            TsiDefault.Click += (sender, e) => this.ItemPosition = Positions.Default;
            TsiOnlyInExplorer.Click += (sender, e) => this.OnlyInExplorer = !TsiOnlyInExplorer.Checked;
            TsiOnlyWithShift.Click += (sender, e) => this.OnlyWithShift = !TsiOnlyWithShift.Checked;
            TsiNoWorkDir.Click += (sender, e) => this.NoWorkingDirectory = !TsiNoWorkDir.Checked;
            TsiNeverDefault.Click += (sender, e) => this.NeverDefault = !TsiNeverDefault.Checked;
            TsiShowAsDisabled.Click += (sender, e) => this.ShowAsDisabledIfHidden = !TsiShowAsDisabled.Checked;
            TsiClsidLocation.Click += (sender, e) => ExternalProgram.JumpRegEdit(GuidInfo.GetClsidPath(Guid), null, AppConfig.OpenMoreRegedit);
            ChkVisible.PreCheckChanging += () => !ChkVisible.Checked || TryProtectOpenItem();
            ContextMenuStrip.Opening += (sender, e) => RefreshMenuItem();
            BtnSubItems.MouseDown += (sender, e) => ShowSubItems();
            TsiShieldIcon.Click += (sender, e) => UseShieldIcon();
            ToolTipBox.SetToolTip(BtnSubItems, AppString.Tip.EditSubItems);
            this.AddCtr(BtnSubItems);
        }

        private void DeleteIcon()
        {
            this.IconLocation = null;
            this.HasLUAShield = false;
            this.Image = this.Image.ToTransparent();
        }

        private void UseShieldIcon()
        {
            bool flag = this.HasLUAShield = TsiShieldIcon.Checked = !TsiShieldIcon.Checked;
            if(IconLocation == null)
            {
                if(flag)
                {
                    this.Image = AppImage.Shield;
                    this.IconPath = "imageres.dll";
                    this.IconIndex = -78;
                }
                else
                {
                    this.Image = this.Image.ToTransparent();
                }
            }
        }

        private void RefreshMenuItem()
        {
            TsiOnlyWithShift.Visible = !IsSubItem;
            TsiDeleteMe.Enabled = !(IsOpenItem && AppConfig.ProtectOpenItem);
            TsiNoWorkDir.Checked = this.NoWorkingDirectory;
            TsiShowAsDisabled.Visible = WinOsVersion.Current >= WinOsVersion.Win10_1703;
            TsiShowAsDisabled.Checked = this.ShowAsDisabledIfHidden;
            TsiChangeCommand.Visible = !IsMultiItem && Guid.Equals(Guid.Empty);
            TsiClsidLocation.Visible = GuidInfo.GetClsidPath(Guid) != null;
            if(!this.IsSubItem) TsiOnlyWithShift.Checked = this.OnlyWithShift;

            if(WinOsVersion.Current >= WinOsVersion.Vista)
            {
                TsiItemIcon.Visible = true;
                TsiPosition.Visible = !IsSubItem;
                TsiOnlyInExplorer.Visible = !IsSubItem;
                TsiNeverDefault.Visible = !IsSubItem;
                if(this.HasIcon)
                {
                    TsiChangeIcon.Text = AppString.Menu.ChangeIcon;
                    TsiDeleteIcon.Visible = true;
                }
                else
                {
                    TsiChangeIcon.Text = AppString.Menu.AddIcon;
                    TsiDeleteIcon.Visible = false;
                }
                TsiShieldIcon.Checked = HasLUAShield;

                if(!IsSubItem)
                {
                    TsiOnlyInExplorer.Checked = this.OnlyInExplorer;
                    TsiNeverDefault.Checked = this.NeverDefault;
                    TsiDefault.Checked = TsiSetTop.Checked = TsiSetBottom.Checked = false;
                    switch(this.ItemPosition)
                    {
                        case Positions.Default:
                            TsiDefault.Checked = true;
                            break;
                        case Positions.Top:
                            TsiSetTop.Checked = true;
                            break;
                        case Positions.Bottom:
                            TsiSetBottom.Checked = true;
                            break;
                    }
                }
            }
            else
            {
                TsiItemIcon.Visible = false;
                TsiPosition.Visible = false;
                TsiOnlyInExplorer.Visible = false;
                TsiNeverDefault.Visible = false;
            }
        }

        private void ShowSubItems()
        {
            if(WinOsVersion.Current == WinOsVersion.Vista)
            {
                AppMessageBox.Show(AppString.Message.VistaUnsupportedMulti);
                return;
            }
            using(ShellSubMenuDialog dlg = new ShellSubMenuDialog())
            {
                dlg.Text = AppString.Dialog.EditSubItems.Replace("%s", this.Text);
                dlg.Icon = ResourceIcon.GetIcon(IconPath, IconIndex);
                dlg.ParentPath = this.RegPath;
                dlg.ShowDialog();
            }
        }

        public bool TryProtectOpenItem()
        {
            if(!IsOpenItem) return true;
            if(!AppConfig.ProtectOpenItem) return true;
            return AppMessageBox.Show(AppString.Message.PromptIsOpenItem, MessageBoxButtons.YesNo) == DialogResult.Yes;
        }

        public virtual void DeleteMe()
        {
            RegistryEx.DeleteKeyTree(this.RegPath, true);
        }
    }
}