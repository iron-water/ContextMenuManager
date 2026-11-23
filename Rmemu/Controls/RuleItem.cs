using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Controls.Interfaces;
using Rmemu.Methods;
using Microsoft.Win32;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace Rmemu.Controls
{
    class RuleItem : FoldSubItem, IBtnShowMenuItem, ITsiWebSearchItem
    {
        public RuleItem(ItemInfo info)
        {
            this.Text = info.Text;
            this.Image = info.Image;
            this.RestartExplorer = info.RestartExplorer;
            BtnShowMenu = new MenuButton(this);
            TsiSearch = new WebSearchMenuItem(this);
            this.ContextMenuStrip?.Dispose();
            this.ContextMenuStrip = new ContextMenuStrip();
            this.ContextMenuStrip.Items.Add(TsiSearch);
        }

        protected void BindNumberEditor(NumericUpDown editor, Func<int> read, Action<int> write, int defaultValue)
        {
            bool synchronizing = false;
            void Synchronize()
            {
                synchronizing = true;
                try
                {
                    editor.Value = read();
                    editor.ForeColor = editor.Value == defaultValue ? Color.FromArgb(0, 138, 217) : FluentTheme.Foreground;
                    editor.Enabled = true;
                }
                catch(Exception ex)
                {
                    editor.Enabled = false;
                    editor.AccessibleDescription = AppString.W("ReadValueFailed", ex.Message);
                    ToolTipBox.SetToolTip(editor, editor.AccessibleDescription);
                }
                finally { synchronizing = false; }
            }
            // Binding may clamp an out-of-range stored value for display, but never persists it.
            Synchronize();
            editor.ValueChanged += (sender, e) =>
            {
                if(synchronizing) return;
                try { write((int)editor.Value); }
                catch(Exception ex) { AppMessageBox.Show(AppString.W("SaveSettingFailed") + ex.Message); }
                finally { Synchronize(); }
            };
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public WebSearchMenuItem TsiSearch { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MenuButton BtnShowMenu { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool RestartExplorer { get; set; }

        public string SearchText
        {
            get
            {
                if(this.FoldGroupItem == null) return this.Text;
                else return $"{FoldGroupItem.Text} {this.Text}";
            }
        }
    }

    public struct ItemInfo
    {
        public string Text { get; set; }
        public Image Image { get; set; }
        public string Tip { get; set; }
        public bool RestartExplorer { get; set; }
    }

    sealed class VisibleRegRuleItem : RuleItem, IChkVisibleItem, ITsiRegPathItem
    {
        public struct RegRule
        {
            public string RegPath { get; set; }
            public string ValueName { get; set; }
            public RegistryValueKind ValueKind { get; set; }
            public object TurnOnValue { get; set; }
            public object TurnOffValue { get; set; }
            public RegRule(string regPath, string valueName, object turnOnValue,
                object turnOffValue, RegistryValueKind valueKind = RegistryValueKind.DWord)
            {
                this.RegPath = regPath; this.ValueName = valueName;
                this.TurnOnValue = turnOnValue; this.TurnOffValue = turnOffValue;
                this.ValueKind = valueKind;
            }
        }

        public struct RuleAndInfo
        {
            public RegRule[] Rules { get; set; }
            public ItemInfo ItemInfo { get; set; }
        }

        private VisibleRegRuleItem(ItemInfo info) : base(info)
        {
            ChkVisible = new VisibleCheckBox(this);
            ToolTipBox.SetToolTip(ChkVisible, info.Tip);
            TsiRegLocation = new RegLocationMenuItem(this);
            this.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { new ToolStripSeparator(), TsiRegLocation });
        }

        public VisibleRegRuleItem(RegRule[] rules, ItemInfo info)
            : this(info) { this.Rules = rules; }

        public VisibleRegRuleItem(RegRule rule, ItemInfo info)
            : this(info) { this.Rules = new[] { rule }; }

        public VisibleRegRuleItem(RuleAndInfo ruleAndInfo)
            : this(ruleAndInfo.Rules, ruleAndInfo.ItemInfo) { }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegRule[] Rules { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public VisibleCheckBox ChkVisible { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegLocationMenuItem TsiRegLocation { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ItemVisible
        {
            get
            {
                for(int i = 0; i < Rules.Length; i++)
                {
                    RegRule rule = Rules[i];
                    using(RegistryKey key = RegistryEx.GetRegistryKey(rule.RegPath))
                    {
                        string value = key?.GetValue(rule.ValueName)?.ToString().ToLower();
                        string turnOnValue = rule.TurnOnValue?.ToString().ToLower();
                        string turnOffValue = rule.TurnOffValue?.ToString().ToLower();
                        if(value == null || key.GetValueKind(rule.ValueName) != rule.ValueKind)
                        {
                            if(i < Rules.Length - 1) continue;
                        }
                        if(value == turnOnValue) return true;
                        if(value == turnOffValue) return false;
                    }
                }
                return true;
            }
            set
            {
                foreach(RegRule rule in Rules)
                {
                    object data = value ? rule.TurnOnValue : rule.TurnOffValue;
                    if(!ChangeHistory.TrySetRegistryValue(AppString.W("ChangeSetting", Text), rule.RegPath, rule.ValueName, data, rule.ValueKind, out string error))
                        throw new IOException(error);
                }
                if(RestartExplorer) ExplorerRestarter.Show();
            }
        }

        public string RegPath => Rules[0].RegPath;
        public string ValueName => Rules[0].ValueName;

        const string LM_SMWCPE = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        const string CU_SMWCPE = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        const string LM_SMWCE = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
        const string CU_SMWCE = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer";
        const string LM_SPMWE = @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\Explorer";
        const string CU_SPMWE = @"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Explorer";

        public static readonly RuleAndInfo CustomFolder = new RuleAndInfo
        {
            Rules = new[] {
                new RegRule(LM_SMWCPE, "NoCustomizeThisFolder", null, 1),
                new RegRule(LM_SMWCPE, "NoCustomizeWebView", null, 1),
                new RegRule(CU_SMWCPE, "NoCustomizeThisFolder", null, 1),
                new RegRule(CU_SMWCPE, "NoCustomizeWebView", null, 1)
            },
            ItemInfo = new ItemInfo
            {
                Text = AppString.Other.CustomFolder,
                Image = AppImage.Folder,
                Tip = AppString.Tip.CustomFolder,
                RestartExplorer = true
            }
        };

        public static readonly RuleAndInfo NetworkDrive = new RuleAndInfo
        {
            Rules = new[] {
                new RegRule(LM_SMWCPE, "NoNetConnectDisconnect", null, 1),
                new RegRule(CU_SMWCPE, "NoNetConnectDisconnect", null, 1)
            },
            ItemInfo = new ItemInfo
            {
                Text = $"{ResourceString.GetDirectString("@AppResolver.dll,-8556")} && {ResourceString.GetDirectString("@AppResolver.dll,-8557")}",
                Image = AppImage.NetworkDrive,
                RestartExplorer = true
            }
        };

        public static readonly RuleAndInfo RecycleBinProperties = new RuleAndInfo
        {
            Rules = new[] {
                new RegRule(LM_SMWCPE, "NoPropertiesRecycleBin", null, 1),
                new RegRule(CU_SMWCPE, "NoPropertiesRecycleBin", null, 1)
            },
            ItemInfo = new ItemInfo
            {
                Text = ResourceString.GetDirectString("@AppResolver.dll,-8553"),
                Image = AppImage.RecycleBin,
                RestartExplorer = true
            }
        };

        public static readonly RuleAndInfo SendToDrive = new RuleAndInfo
        {
            Rules = new[] {
                new RegRule(LM_SMWCPE, "NoDrivesInSendToMenu", null, 1),
                new RegRule(CU_SMWCPE, "NoDrivesInSendToMenu", null, 1)
            },
            ItemInfo = new ItemInfo
            {
                Text = ResourceString.GetDirectString("@shell32.dll,-9309"),
                Image = AppImage.Drive,
                Tip = AppString.Tip.SendToDrive,
                RestartExplorer = true
            }
        };

        public static readonly RuleAndInfo DeferBuildSendTo = new RuleAndInfo
        {
            Rules = new[] {
                new RegRule(LM_SMWCE, "DelaySendToMenuBuild", null, 1),
                new RegRule(CU_SMWCE, "DelaySendToMenuBuild", null, 1)
            },
            ItemInfo = new ItemInfo
            {
                Text = AppString.Other.BuildSendtoMenu,
                Image = AppImage.SendTo,
                Tip = AppString.Tip.BuildSendtoMenu
            }
        };

        public static readonly RuleAndInfo UseStoreOpenWith = new RuleAndInfo
        {
            Rules = new[] {
                new RegRule(LM_SPMWE, "NoUseStoreOpenWith", null, 1),
                new RegRule(CU_SPMWE, "NoUseStoreOpenWith", null, 1)
            },
            ItemInfo = new ItemInfo
            {
                Text = ResourceString.GetDirectString("@shell32.dll,-5383"),
                Image = AppImage.MicrosoftStore
            }
        };
    }

    sealed class NumberRegRuleItem : RuleItem, ITsiRegPathItem
    {
        public struct RegRule
        {
            public string RegPath { get; set; }
            public string ValueName { get; set; }
            public RegistryValueKind ValueKind { get; set; }
            public int MaxValue { get; set; }
            public int MinValue { get; set; }
            public int DefaultValue { get; set; }
        }

        readonly NumericUpDown NudValue = new NumericUpDown
        {
            TextAlign = HorizontalAlignment.Center,
            Width = 80.DpiZoom()
        };
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegLocationMenuItem TsiRegLocation { get; set; }

        public NumberRegRuleItem(RegRule rule, ItemInfo info) : base(info)
        {
            this.AddCtr(NudValue);
            ToolTipBox.SetToolTip(NudValue, info.Tip);
            TsiRegLocation = new RegLocationMenuItem(this);
            this.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { new ToolStripSeparator(), TsiRegLocation });
            this.Rule = rule;
            NudValue.Maximum = rule.MaxValue;
            NudValue.Minimum = rule.MinValue;
            NudValue.Font = new Font(this.Font.FontFamily, this.Font.Size + 3F);
            BindNumberEditor(NudValue, () => ItemValue, value => ItemValue = value, rule.DefaultValue);
        }

        public string RegPath => Rule.RegPath;
        public string ValueName => Rule.ValueName;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegRule Rule { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ItemValue
        {
            get
            {
                object value = Registry.GetValue(Rule.RegPath, Rule.ValueName, null);
                decimal number = value == null ? Rule.DefaultValue : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return (int)Math.Clamp(number, Rule.MinValue, Rule.MaxValue);
            }
            set
            {
                if(value < Rule.MinValue || value > Rule.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
                object data = Rule.ValueKind == RegistryValueKind.String || Rule.ValueKind == RegistryValueKind.ExpandString
                    ? value.ToString(CultureInfo.InvariantCulture) : value;
                if(!ChangeHistory.TrySetRegistryValue(AppString.W("ChangeSetting", Text), Rule.RegPath, Rule.ValueName, data, Rule.ValueKind, out string error))
                    throw new IOException(error);
                object actual = Registry.GetValue(Rule.RegPath, Rule.ValueName, null);
                if(actual == null || Convert.ToDecimal(actual, CultureInfo.InvariantCulture) != value)
                    throw new IOException(AppString.W("SavedValueMismatch"));
                if(RestartExplorer) ExplorerRestarter.Show();
            }
        }
    }

    sealed class StringRegRuleItem : RuleItem, ITsiRegPathItem
    {
        public struct RegRule
        {
            public string RegPath { get; set; }
            public string ValueName { get; set; }
        }

        readonly Button BtnValue = new Button
        {
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(80, 80, 80),
            Cursor = Cursors.Hand,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            TabStop = true
        };

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegLocationMenuItem TsiRegLocation { get; set; }

        public StringRegRuleItem(RegRule rule, ItemInfo info) : base(info)
        {
            this.AddCtr(BtnValue);
            ToolTipBox.SetToolTip(BtnValue, info.Tip);
            BtnValue.AccessibleName = info.Text + AppString.W("EditValueSuffix");
            BtnValue.AccessibleDescription = AppString.W("EditStringHint");
            TsiRegLocation = new RegLocationMenuItem(this);
            this.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { new ToolStripSeparator(), TsiRegLocation });
            this.Rule = rule;
            BtnValue.Text = ItemValue;
            BtnValue.Font = new Font(this.Font.FontFamily, this.Font.Size + 3F);
            BtnValue.Click += (sender, e) =>
            {
                using(InputDialog dlg = new InputDialog())
                {
                    dlg.Title = AppString.Menu.ChangeText;
                    dlg.Text = ItemValue;
                    if(dlg.ShowDialog() != DialogResult.OK) return;
                    try
                    {
                        ItemValue = dlg.Text;
                        BtnValue.Text = ItemValue;
                    }
                    catch(Exception ex) { AppMessageBox.Show(AppString.W("SaveSettingFailed") + ex.Message); }
                }
            };
        }

        public string RegPath => Rule.RegPath;
        public string ValueName => Rule.ValueName;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public RegRule Rule { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string ItemValue
        {
            get => Registry.GetValue(Rule.RegPath, Rule.ValueName, null)?.ToString();
            set
            {
                if(!ChangeHistory.TrySetRegistryValue(AppString.W("ChangeSetting", Text), Rule.RegPath, Rule.ValueName, value ?? "", RegistryValueKind.String, out string error))
                    throw new IOException(error);
                if(RestartExplorer) ExplorerRestarter.Show();
            }
        }
    }

    sealed class VisbleIniRuleItem : RuleItem, IChkVisibleItem
    {
        public struct IniRule
        {
            public string IniPath { get; set; }
            public string Section { get; set; }
            public string KeyName { get; set; }
            public string TurnOnValue { get; set; }
            public string TurnOffValue { get; set; }
        }

        public VisbleIniRuleItem(IniRule rule, ItemInfo info) : base(info)
        {
            this.Rule = rule;
            this.IniWriter = new IniWriter(rule.IniPath);
            ChkVisible = new VisibleCheckBox(this);
            ToolTipBox.SetToolTip(ChkVisible, info.Tip);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IniRule Rule { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IniWriter IniWriter { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public VisibleCheckBox ChkVisible { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ItemVisible
        {
            get => IniWriter.GetValue(Rule.Section, Rule.KeyName) == Rule.TurnOnValue;
            set
            {
                IniWriter.SetValue(Rule.Section, Rule.KeyName, value ? Rule.TurnOnValue : Rule.TurnOffValue);
                if(RestartExplorer) ExplorerRestarter.Show();
            }
        }
    }

    sealed class NumberIniRuleItem : RuleItem
    {
        public struct IniRule
        {
            public string IniPath { get; set; }
            public string Section { get; set; }
            public string KeyName { get; set; }
            public int MaxValue { get; set; }
            public int MinValue { get; set; }
            public int DefaultValue { get; set; }
        }

        public NumberIniRuleItem(IniRule rule, ItemInfo info) : base(info)
        {
            this.AddCtr(NudValue);
            this.Rule = rule;
            this.IniWriter = new IniWriter(rule.IniPath);
            ToolTipBox.SetToolTip(NudValue, info.Tip);
            NudValue.Maximum = rule.MaxValue;
            NudValue.Minimum = rule.MinValue;
            NudValue.Font = new Font(this.Font.FontFamily, this.Font.Size + 3F);
            BindNumberEditor(NudValue, () => ItemValue, value => ItemValue = value, rule.DefaultValue);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IniRule Rule { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IniWriter IniWriter { get; set; }

        readonly NumericUpDown NudValue = new NumericUpDown
        {
            TextAlign = HorizontalAlignment.Center,
            Width = 80.DpiZoom()
        };

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int ItemValue
        {
            get
            {
                string value = IniWriter.GetValue(Rule.Section, Rule.KeyName);
                decimal number = value.IsNullOrWhiteSpace() ? Rule.DefaultValue : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return (int)Math.Clamp(number, Rule.MinValue, Rule.MaxValue);
            }
            set
            {
                if(value < Rule.MinValue || value > Rule.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
                string requested = value.ToString(CultureInfo.InvariantCulture);
                IniWriter.SetValue(Rule.Section, Rule.KeyName, requested);
                if(IniWriter.GetValue(Rule.Section, Rule.KeyName) != requested)
                    throw new IOException(AppString.W("SavedValueMismatch"));
                if(RestartExplorer) ExplorerRestarter.Show();
            }
        }
    }

    sealed class StringIniRuleItem : RuleItem
    {
        public struct IniRule
        {
            public string IniPath { get; set; }
            public string Secation { get; set; }
            public string KeyName { get; set; }
        }


        readonly Button BtnValue = new Button
        {
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(80, 80, 80),
            Cursor = Cursors.Hand,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            TabStop = true
        };

        public StringIniRuleItem(IniRule rule, ItemInfo info) : base(info)
        {
            this.Rule = rule;
            this.IniWriter = new IniWriter(rule.IniPath);
            this.AddCtr(BtnValue);
            ToolTipBox.SetToolTip(BtnValue, info.Tip);
            BtnValue.AccessibleName = info.Text + AppString.W("EditValueSuffix");
            BtnValue.AccessibleDescription = AppString.W("EditStringHint");
            BtnValue.Text = ItemValue;
            BtnValue.Font = new Font(this.Font.FontFamily, this.Font.Size + 3F);
            BtnValue.Click += (sender, e) =>
            {
                using(InputDialog dlg = new InputDialog())
                {
                    dlg.Title = AppString.Menu.ChangeText;
                    dlg.Text = ItemValue;
                    if(dlg.ShowDialog() != DialogResult.OK) return;
                    try
                    {
                        ItemValue = dlg.Text;
                        BtnValue.Text = ItemValue;
                    }
                    catch(Exception ex) { AppMessageBox.Show(AppString.W("SaveSettingFailed") + ex.Message); }
                }
            };
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IniRule Rule { get; set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IniWriter IniWriter { get; set; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string ItemValue
        {
            get => IniWriter.GetValue(Rule.Secation, Rule.KeyName);
            set
            {
                IniWriter.SetValue(Rule.Secation, Rule.KeyName, value);
                if(IniWriter.GetValue(Rule.Secation, Rule.KeyName) != value)
                    throw new IOException(AppString.W("SavedValueMismatch"));
                if(RestartExplorer) ExplorerRestarter.Show();
            }
        }
    }
}