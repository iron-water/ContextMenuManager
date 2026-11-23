using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Microsoft.Win32;
using Rmemu.Controls.Interfaces;
using Rmemu.Methods;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Rmemu.Controls
{
    internal sealed class ModernExtensionsList : MyList
    {
        public ModernExtensionsList()
        {
            ChangeHistory.Changed += HistoryChanged;
        }

        public void LoadItems()
        {
            SuspendLayout();
            try
            {
                ClearItems();
                ModernExtensionCatalog catalog = ModernExtensionCatalog.Scan();
                AddItem(new ModernCoverageItem(catalog));
                foreach(ModernExtensionEntry entry in catalog.Entries) AddItem(new ModernExtensionItem(entry));
                FluentTheme.Apply(this);
            }
            finally { ResumeLayout(); }
        }

        private void HistoryChanged(object sender, EventArgs e)
        {
            foreach(ModernExtensionItem item in Controls.OfType<ModernExtensionItem>()) item.RefreshStatus();
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing) ChangeHistory.Changed -= HistoryChanged;
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernCoverageItem : MyListItem
    {
        private readonly ModernExtensionCatalog catalog;
        public string SearchText => Text + " " + string.Join(" ", catalog.Coverage.Warnings);

        public ModernCoverageItem(ModernExtensionCatalog catalog)
        {
            this.catalog = catalog;
            HasImage = false;
            WrapText = true;
            Text = AppString.W("ModernHeader", catalog.Entries.Count) + "\n"
                + catalog.Coverage + "\n"
                + AppString.W("ModernScope") + "\n"
                + (catalog.Coverage.Warnings.Count == 0
                    ? AppString.W("ModernScopeNote")
                    : AppString.W("ModernIncomplete"));
            var details = new Button { Text = AppString.W("CoverageReport"), AutoSize = true };
            AddCtr(details);
            details.Click += (sender, e) => ModernExtensionItem.ShowText(this, AppString.W("CoverageReportTitle"), BuildReport());
        }

        private string BuildReport()
        {
            var report = new StringBuilder();
            report.AppendLine(catalog.Coverage.ToString());
            report.AppendLine(AppString.W("DeclarationsFound", catalog.Entries.Count));
            report.AppendLine();
            report.AppendLine(AppString.W("ReportPackages"));
            report.AppendLine(AppString.W("ReportManifest"));
            report.AppendLine(AppString.W("ReportUnpackaged"));
            report.AppendLine(AppString.W("ReportHkcr"));
            report.AppendLine();
            report.AppendLine(AppString.W("ReportNotProof"));
            report.AppendLine(AppString.W("ReportReadOnly"));
            report.AppendLine();
            report.AppendLine(catalog.Coverage.Warnings.Count == 0 ? AppString.W("ReportNoErrors") : AppString.W("CoverageWarnings"));
            foreach(string warning in catalog.Coverage.Warnings) report.AppendLine("• " + warning);
            return report.ToString();
        }
    }

    internal sealed class ModernExtensionItem : MyListItem, IBtnShowMenuItem
    {
        private readonly ModernExtensionEntry entry;
        private readonly Button blockButton = new Button { AutoSize = true };
        private readonly ToolStripMenuItem blockMenu = new ToolStripMenuItem();
        public string SearchText => entry.SearchText;
        public string ItemFilePath => entry.ManifestPath;
        public string RegPath => entry.RegistryPath;
        public string ValueName => entry.IsPackaged ? null : "ExplorerCommandHandler";
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public MenuButton BtnShowMenu { get; set; }

        public ModernExtensionItem(ModernExtensionEntry entry)
        {
            this.entry = entry;
            HasImage = false;
            WrapText = true;
            BtnShowMenu = new MenuButton(this);
            AddCtr(blockButton);
            var details = new ToolStripMenuItem(AppString.W("ViewDetails"));
            details.Click += (sender, e) => ShowDetails();
            var copyDetails = new ToolStripMenuItem(AppString.W("CopyDetails"));
            copyDetails.Click += (sender, e) => RunAction(() => Clipboard.SetText(BuildDetails()));
            var copyId = new ToolStripMenuItem(AppString.W("CopyClsid")) { Enabled = entry.ClassId != Guid.Empty };
            copyId.Click += (sender, e) => RunAction(() => Clipboard.SetText(entry.ClassId.ToString("B")));
            var openManifest = new ToolStripMenuItem(AppString.W("ViewManifest")) { Enabled = entry.ManifestPath != null };
            openManifest.Click += (sender, e) => RunAction(() =>
                ShowText(this, AppString.W("ManifestTitle"), File.ReadAllText(entry.ManifestPath)));
            var copyManifestPath = new ToolStripMenuItem(AppString.W("CopyManifestPath")) { Enabled = entry.ManifestPath != null };
            copyManifestPath.Click += (sender, e) => RunAction(() => Clipboard.SetText(entry.ManifestPath));
            var copyManifest = new ToolStripMenuItem(AppString.W("CopyManifest")) { Enabled = entry.ManifestPath != null };
            copyManifest.Click += (sender, e) => RunAction(() => Clipboard.SetText(File.ReadAllText(entry.ManifestPath)));
            var openRegistry = new ToolStripMenuItem(AppString.W("OpenRegistry")) { Enabled = entry.RegistryPath != null };
            openRegistry.Click += (sender, e) => RunAction(() =>
                ExternalProgram.JumpRegEdit(entry.RegistryPath, ValueName, AppConfig.OpenMoreRegedit));
            var copyRegistry = new ToolStripMenuItem(AppString.W("CopyRegistryPath")) { Enabled = entry.RegistryPath != null };
            copyRegistry.Click += (sender, e) => RunAction(() => Clipboard.SetText(entry.RegistryPath));
            blockMenu.Click += (sender, e) => ToggleBlocked();
            ContextMenuStrip.Items.AddRange(new ToolStripItem[] { details, copyDetails, copyId,
                new ToolStripSeparator(), openManifest, copyManifestPath, copyManifest,
                new ToolStripSeparator(), openRegistry, copyRegistry,
                new ToolStripSeparator(), blockMenu });
            ContextMenuStrip.Opening += (sender, e) => RefreshStatus();
            blockButton.Click += (sender, e) => ToggleBlocked();
            DoubleClick += (sender, e) => ShowDetails();
            RefreshStatus();
        }

        private static string ShortTypes(string types)
        {
            string[] parts = types.Split(new[] { ", " }, StringSplitOptions.None);
            return parts.Length <= 8 ? types : string.Join(", ", parts.Take(8)) + " " + AppString.W("MoreTypes", parts.Length);
        }

        internal void RefreshStatus()
        {
            BlockState state = ReadBlockState();
            string status = entry.IsPackaged ? AppString.W("PackagedReadOnly")
                : state.Error != null ? AppString.W("BlockReadFailed")
                : state.Machine ? AppString.W("BlockedMachine")
                : state.User ? AppString.W("BlockedUser") : AppString.W("NotBlocked");
            Text = AppString.W("ModernRow", entry.Name, status, entry.Package, entry.Provider, ShortTypes(entry.ItemTypes),
                entry.ClassId == Guid.Empty ? AppString.W("InvalidClsid") : entry.ClassId.ToString("B"), entry.Source);
            blockButton.Text = blockMenu.Text = entry.CanBlock
                ? (state.User ? AppString.W("Unblock") : AppString.W("BlockClsid")) : AppString.W("ReadOnlyLimits");
            blockButton.Enabled = blockMenu.Enabled = entry.CanBlock && !state.Machine && state.Error == null;
        }

        private void ToggleBlocked()
        {
            if(!entry.CanBlock) return;
            BlockState state = ReadBlockState();
            if(state.Error != null || state.Machine)
            {
                MessageBox.Show(this, state.Error ?? AppString.W("MachineBlocked"),
                    AppString.W("ModernMenu"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshStatus();
                return;
            }
            string action = state.User ? AppString.W("Unblock") : AppString.W("BlockClsid");
            string explanation = AppString.W("BlockConfirm", action, entry.Name, entry.ClassId.ToString("B"),
                entry.BlockingNote, ModernExtensionCatalog.BlockedKey);
            if(MessageBox.Show(this, explanation, AppString.W("ModernMenu"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if(!ChangeHistory.TrySetRegistryValue(AppString.W("BlockHistory", action, entry.Name),
                @"HKEY_CURRENT_USER\" + ModernExtensionCatalog.BlockedKey, entry.ClassId.ToString("B"),
                state.User ? null : "", RegistryValueKind.String, out string error))
            {
                MessageBox.Show(this, error, AppString.W("ChangeFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RefreshStatus();
            ExplorerRestarter.Show();
            MessageBox.Show(this, AppString.W("BlockUpdated"),
                AppString.W("NeedsExplorerRestart"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private BlockState ReadBlockState()
        {
            var state = new BlockState();
            if(entry.ClassId == Guid.Empty) return state;
            try
            {
                using(RegistryKey user = Registry.CurrentUser.OpenSubKey(ModernExtensionCatalog.BlockedKey))
                using(RegistryKey machine = Registry.LocalMachine.OpenSubKey(ModernExtensionCatalog.BlockedKey))
                {
                    string id = entry.ClassId.ToString("B");
                    state.User = user?.GetValueNames().Any(n => string.Equals(n, id, StringComparison.OrdinalIgnoreCase)) == true;
                    state.Machine = machine?.GetValueNames().Any(n => string.Equals(n, id, StringComparison.OrdinalIgnoreCase)) == true;
                }
            }
            catch(Exception ex) when(ModernExtensionCatalog.IsReadError(ex))
            { state.Error = AppString.W("BlockedKeyUnreadable", ex.Message); }
            return state;
        }

        private string BuildDetails()
        {
            BlockState state = ReadBlockState();
            return AppString.W("ModernDetails", entry.Name, entry.Package, entry.Provider, entry.ItemTypes,
                    entry.ClassId == Guid.Empty ? AppString.W("InvalidClsid") : entry.ClassId.ToString("B"),
                    entry.Source, entry.ManifestPath ?? AppString.W("NotApplicable"),
                    entry.RegistryPath + (entry.IsPackaged ? " " + AppString.W("IfRegistered") : ""),
                    AppString.W(state.User ? "Present" : "NotSet"), AppString.W(state.Machine ? "Present" : "NotSet"))
                + (state.Error == null ? "" : "\r\n" + state.Error)
                + "\r\n\r\n" + entry.BlockingNote
                + "\r\n\r\n" + AppString.W("DetailsFooter");
        }

        private void ShowDetails() => ShowText(this, AppString.W("DetailsTitle"), BuildDetails());

        private void RunAction(Action action)
        {
            try { action(); }
            catch(Exception ex) when(ModernExtensionCatalog.IsReadError(ex) || ex is System.ComponentModel.Win32Exception
                || ex is System.Runtime.InteropServices.ExternalException || ex is InvalidOperationException)
            { MessageBox.Show(this, ex.Message, AppString.W("ActionFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }


        internal static void ShowText(IWin32Window owner, string title, string text)
        {
            using(var dialog = new Form
            {
                Text = title, StartPosition = FormStartPosition.CenterParent,
                Font = FluentTheme.BodyFont,
                Size = new Size(840, 610).DpiZoom(), MinimumSize = new Size(540, 380).DpiZoom(),
                ShowInTaskbar = false, MinimizeBox = false
            })
            using(var box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                WordWrap = false, Dock = DockStyle.Fill, Text = text
            })
            using(var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8.DpiZoom())
            })
            using(var close = new Button { Text = AppString.W("Close"), AutoSize = true, DialogResult = DialogResult.Cancel })
            using(var copy = new Button { Text = AppString.W("CopyAll"), AutoSize = true })
            {
                copy.Click += (sender, e) =>
                {
                    try { Clipboard.SetText(text); }
                    catch(System.Runtime.InteropServices.ExternalException ex)
                    { MessageBox.Show(dialog, ex.Message, AppString.W("CopyFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                buttons.Controls.Add(close);
                buttons.Controls.Add(copy);
                dialog.Controls.Add(box);
                dialog.Controls.Add(buttons);
                dialog.CancelButton = close;
                FluentTheme.Apply(dialog);
                dialog.ShowDialog(owner);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing) ContextMenuStrip?.Dispose();
            base.Dispose(disposing);
        }

        private sealed class BlockState
        {
            public bool User;
            public bool Machine;
            public string Error;
        }
    }
}
