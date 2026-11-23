using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Controls;
using Rmemu.Controls.Interfaces;
using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;

namespace Rmemu
{
    sealed partial class MainForm
    {
        private readonly ModernExtensionsList modernExtensionsList = new ModernExtensionsList();
        private readonly Dictionary<Control, bool> unfilteredVisibility = new Dictionary<Control, bool>();
        private readonly Dictionary<Control, string> searchIndex = new Dictionary<Control, string>();
        private readonly Stack<Tuple<int, int>> navigationHistory = new Stack<Tuple<int, int>>();
        private readonly HashSet<FoldGroupItem> collapsedDuringFilter = new HashSet<FoldGroupItem>();
        private bool filterWasActive;
        private bool listSyncQueued;
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<MyListItem, object> boundRows = new System.Runtime.CompilerServices.ConditionalWeakTable<MyListItem, object>();
        private readonly Timer searchDelay = new Timer { Interval = 180 };
        private Tuple<int, int> currentRoute;
        private MyList activeList;
        private MyListItem inspectedItem;
        private bool changingRoute, filtering, loadingView;
        private string specialView;
        internal string LastNavigationError { get; private set; }

        private void Navigate(int tab, int index, bool remember = true)
        {
            if(workspace == null) return;
            var next = Tuple.Create(tab, index);
            if(remember && currentRoute != null && !currentRoute.Equals(next)) navigationHistory.Push(currentRoute);
            currentRoute = next;
            changingRoute = true;
            LastNavigationError = null;
            RestoreFilterBaseline();
            searchDelay.Stop(); searchBox.Clear(); stateFilter.SelectedIndex = 0;
            activeList = null;
            unfilteredVisibility.Clear(); searchIndex.Clear();
            inspectedItem = null; inspector.Visible = false;
            foreach(Control control in MainControls) control.Visible = false;
            modernExtensionsList.Visible = startPage.Visible = historyPage.Visible = errorView.Visible = emptyLabel.Visible = false;
            specialView = null;
            try
            {
                UseWaitCursor = true;
                if(tab == -1)
                {
                    specialView = "home"; MainBody.Visible = false; startPage.Visible = true;
                    pageTitle.Text = AppString.W("Overview");
                    pageGuide.Text = AppString.W("OverviewGuide");
                    resultLabel.Text = "";
                }
                else if(tab == -3)
                {
                    specialView = "history"; MainBody.Visible = false; historyPage.Visible = true;
                    pageTitle.Text = AppString.W("ChangeHistory");
                    pageGuide.Text = AppString.W("HistoryGuide");
                    UpdateHistoryText();
                }
                else if(tab == -2)
                {
                    specialView = "modern"; MainBody.Visible = true;
                    modernExtensionsList.LoadItems(); modernExtensionsList.Visible = true;
                    pageTitle.Text = AppString.W("ModernExtensions");
                    pageGuide.Text = AppString.W("ModernGuide");
                    CaptureList(modernExtensionsList);
                }
                else
                {
                    MainBody.Visible = true; loadingView = true;
                    try { SelectedCategory = tab; SelectedLocation = index; SwitchItem(); }
                    finally { loadingView = false; }
                    WorkspaceChanged();
                }
                PopulateScenePicker();
                UpdateWorkspaceButtons();
                ApplyWorkspaceTheme();
            }
            catch(Exception ex)
            {
                LastNavigationError = ex.Message;
                MainBody.Visible = startPage.Visible = historyPage.Visible = false;
                errorMessage.Text = ex.Message + AppString.W("ReadFailedHint");
                errorView.Visible = true; errorView.BringToFront();
                ShowFeedback(AppString.W("ReadFailedNoChange"));
                UpdateWorkspaceButtons();
            }
            finally { UseWaitCursor = false; changingRoute = false; }
        }

        private static bool IsAdministrator()
        {
            using(WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static int NavigationGroup(int tab, int index)
        {
            if(tab == 1 || tab == 2 && index > 1) return 0;
            return tab;
        }

        private void PopulateScenePicker()
        {
            populatingScenes = true;
            try
            {
                scenePicker.SelectedIndex = -1; scenePicker.Items.Clear(); sceneRoutes.Clear();
                int group = NavigationGroup(currentRoute.Item1, currentRoute.Item2);
                if(group == 0)
                {
                    AddScenes(0, GeneralItems, "");
                    AddScenes(1, TypeItems, AppString.W("ScenePrefixType") + " · ");
                    for(int i = 3; i < OtherRuleItems.Length; i++)
                        if(OtherRuleItems[i] != null) sceneRoutes.Add(Tuple.Create(2, i, AppString.W("ScenePrefixAdvanced") + " · " + OtherRuleItems[i]));
                }
                else if(group == 2)
                {
                    sceneRoutes.Add(Tuple.Create(2, 0, AppString.W("UsefulMenus")));
                    sceneRoutes.Add(Tuple.Create(2, 1, AppString.W("AppMenuSettings")));
                }
                else if(group == 4) AddScenes(4, AboutItems, "");
                foreach(var entry in sceneRoutes) scenePicker.Items.Add(entry.Item3);
                scenePicker.SelectedIndex = sceneRoutes.FindIndex(r => r.Item1 == currentRoute.Item1 && r.Item2 == currentRoute.Item2);
            }
            finally { populatingScenes = false; }
        }
        private void AddScenes(int tab, string[] names, string prefix)
        {
            for(int i = 0; i < names.Length; i++) if(names[i] != null) sceneRoutes.Add(Tuple.Create(tab, i, prefix + names[i]));
        }

        private void WorkspaceChanged()
        {
            if(workspace == null || loadingView) return;
            specialView = null;
            startPage.Visible = historyPage.Visible = errorView.Visible = modernExtensionsList.Visible = false;
            MainBody.Visible = true;
            int tab = SelectedCategory, index = SelectedLocation;
            if(index < 0) return;
            var route = Tuple.Create(tab, index);
            if(!changingRoute && currentRoute != null && !currentRoute.Equals(route)) navigationHistory.Push(currentRoute);
            currentRoute = route;
            var names = tab == 0 ? GeneralItems : tab == 1 ? TypeItems : tab == 2 ? OtherRuleItems : AboutItems;
            pageTitle.Text = tab == 0 && index == 0 ? AppString.W("FileMenu") : tab == 0 && index == 4 ? AppString.W("DesktopMenu") : names[index];
            pageGuide.Text = GuideFor(tab, index);
            CaptureList(MainControls.OfType<MyList>().FirstOrDefault(c => c.Visible));
            if(!changingRoute) PopulateScenePicker();
            UpdateWorkspaceButtons();
        }

        private static string GuideFor(int tab, int index)
        {
            if(tab == 0)
            {
                switch(index)
                {
                    case 0: return AppString.W("GuideFile");
                    case 1: return AppString.W("GuideFolder");
                    case 2: return AppString.W("GuideDirectory");
                    case 3: return AppString.W("GuideBackground");
                    case 4: return AppString.W("GuideDesktop");
                    case 5: return AppString.W("GuideDrive");
                    case 6: return AppString.W("GuideAllObjects");
                    case 7: return AppString.W("GuideComputer");
                    case 8: return AppString.W("GuideRecycleBin");
                    case 9: return AppString.W("GuideLibrary");
                    case 11: return AppString.W("GuideNew");
                    case 12: return AppString.W("GuideSendTo");
                    case 13: return AppString.W("GuideOpenWith");
                    case 15: return AppString.W("GuideWinX");
                }
            }
            if(tab == 1) return index == 9 ? AppString.W("GuideAnalysis") : AppString.W("GuideType");
            if(tab == 2) return index == 0 ? AppString.W("GuideEnhance") : index == 1 ? AppString.W("GuideDetailed") : AppString.W("GuideAdvanced");
            if(tab == 4) return index == 0 ? AppString.W("GuideAppSetting") : index == 1 ? AppString.W("GuideLanguage") : index == 2 ? AppString.W("GuideDictionaries") : index == 3 ? AppString.W("GuideAbout") : AppString.W("GuideDonate");
            return AppString.W("GuideDefault");
        }

        private void CaptureList(MyList list)
        {
            activeList = list; inspectedItem = null; inspector.Visible = false;
            unfilteredVisibility.Clear(); searchIndex.Clear();
            if(list != null) foreach(Control child in list.Controls) { unfilteredVisibility[child] = child.Visible; searchIndex[child] = DescribeItem(child); }
            ApplyFilter();
        }
        private void RestoreFilterBaseline()
        {
            filtering = true;
            try
            {
                foreach(var entry in unfilteredVisibility)
                    if(!entry.Key.IsDisposed && entry.Key.Parent == activeList) entry.Key.Visible = entry.Value;
            }
            finally { filtering = false; }
        }
        private void QueueListSync(MyList list)
        {
            if(changingRoute || loadingView || list != activeList || listSyncQueued || IsDisposed || !IsHandleCreated) return;
            listSyncQueued = true;
            BeginInvoke(new Action(() =>
            {
                listSyncQueued = false;
                if(IsDisposed || list.IsDisposed || list != activeList) return;
                var current = new HashSet<Control>(list.Controls.Cast<Control>());
                foreach(Control removed in unfilteredVisibility.Keys.Where(key => !current.Contains(key)).ToArray())
                { unfilteredVisibility.Remove(removed); searchIndex.Remove(removed); }
                foreach(Control child in current)
                    if(!unfilteredVisibility.ContainsKey(child)) { unfilteredVisibility[child] = child.Visible; searchIndex[child] = DescribeItem(child); }
                if(inspectedItem != null && (inspectedItem.IsDisposed || inspectedItem.Parent != list)) { inspectedItem = null; inspector.Visible = false; }
                ApplyFilter(); UpdateWorkspaceButtons();
            }));
        }
        private void BindItem(MyListItem item)
        {
            if(item == null) return;
            if(boundRows.TryGetValue(item, out _)) return;
            boundRows.Add(item, new object());
            item.MouseDown += (s, e) => InspectItem(item);
            item.Enter += (s, e) => InspectItem(item);
            if(item is FoldGroupItem group) group.FoldStateChanged += (s, e) =>
            {
                if(group.Parent != activeList || filtering) return;
                if(searchBox.TextLength > 0 || stateFilter.SelectedIndex > 0)
                {
                    if(group.IsFold) collapsedDuringFilter.Add(group); else collapsedDuringFilter.Remove(group);
                    ApplyFilter();
                }
            };
            item.VisibleChanged += (s, e) =>
            {
                if(!filtering && item.Parent == activeList && searchBox.TextLength == 0 && stateFilter.SelectedIndex == 0) unfilteredVisibility[item] = item.Visible;
            };
            item.TextChanged += (s, e) =>
            {
                if(item.Parent != activeList || !searchIndex.ContainsKey(item)) return;
                searchIndex[item] = DescribeItem(item);
            };
            if(item.ContextMenuStrip != null) item.ContextMenuStrip.Closed += (s, e) =>
            {
                if(item.IsDisposed || item.Parent != activeList) return;
                searchIndex[item] = DescribeItem(item);
                if(inspectedItem == item) InspectItem(item);
                if(searchBox.TextLength > 0 || stateFilter.SelectedIndex > 0) ApplyFilter();
            };
        }
        private static string ReadProperty(Control item, string property)
        {
            try { return item.GetType().GetProperty(property)?.GetValue(item, null)?.ToString(); }
            catch { return null; }
        }
        private static string DescribeItem(Control item) => string.Join("\n", new[]
        {
            item is MyListItem row ? row.Text : item.Text, ReadProperty(item, "SearchText"), ReadProperty(item, "RegPath"),
            ReadProperty(item, "ItemFilePath"), ReadProperty(item, "ItemCommand"), ReadProperty(item, "Guid")
        }.Where(v => !string.IsNullOrWhiteSpace(v)));
        private static bool IsMenuEntry(Control child) => child is MyListItem && !(child is NewItem) && !(child is FoldGroupItem)
            && child.GetType().Name != "SelectItem" && child.GetType().Name != "ModernCoverageItem";

        private void ApplyFilter()
        {
            if(filtering || activeList == null) return;
            filtering = true; activeList.SuspendLayout();
            try
            {
                string query = searchBox.Text.Trim();
                bool hasFilter = query.Length > 0 || stateFilter.SelectedIndex > 0;
                if(!hasFilter || !filterWasActive) collapsedDuringFilter.Clear();
                filterWasActive = hasFilter;
                int shown = 0, total = 0;
                var matches = new Dictionary<Control, bool>();
                foreach(Control child in activeList.Controls)
                {
                    if(!unfilteredVisibility.TryGetValue(child, out bool visible)) { visible = child.Visible; unfilteredVisibility[child] = visible; searchIndex[child] = DescribeItem(child); }
                    bool entry = IsMenuEntry(child);
                    if(entry) total++;
                    bool match = query.Length == 0 || searchIndex[child].IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                    if(child is FoldSubItem sub && sub.FoldGroupItem != null && query.Length > 0)
                        match |= sub.FoldGroupItem.Text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                    if(stateFilter.SelectedIndex > 0)
                    {
                        if(child is IChkVisibleItem item)
                        {
                            try { match &= ChangeHistory.ReadVisibility(item) == (stateFilter.SelectedIndex == 1); }
                            catch { match = false; }
                        }
                        else if(entry) match = false;
                    }
                    if(!hasFilter && child is FoldSubItem folded && folded.FoldGroupItem != null) visible = !folded.FoldGroupItem.IsFold;
                    if(hasFilter) visible = entry && match;
                    matches[child] = visible;
                }
                if(hasFilter)
                    foreach(FoldGroupItem group in activeList.Controls.OfType<FoldGroupItem>())
                        matches[group] = activeList.Controls.OfType<FoldSubItem>().Any(sub => sub.FoldGroupItem == group && matches[sub]);
                if(hasFilter)
                    foreach(FoldSubItem sub in activeList.Controls.OfType<FoldSubItem>())
                        if(sub.FoldGroupItem != null && collapsedDuringFilter.Contains(sub.FoldGroupItem)) matches[sub] = false;
                foreach(Control child in activeList.Controls)
                {
                    child.Visible = matches[child];
                    if(matches[child] && IsMenuEntry(child)) shown++;
                }
                emptyLabel.Visible = hasFilter && shown == 0 || !hasFilter && activeList.Controls.Count == 0;
                if(emptyLabel.Visible) emptyLabel.BringToFront();
                resultLabel.Text = hasFilter ? AppString.W("FilterResult", shown, total) : AppString.W("ListTotal", total);
                if(inspectedItem != null && !inspectedItem.Visible) { inspectedItem = null; inspector.Visible = false; }
            }
            finally { activeList.ResumeLayout(true); filtering = false; }
        }

        private void InspectItem(MyListItem item)
        {
            if(item is NewItem || item is FoldGroupItem || item.GetType().Name == "SelectItem") return;
            inspectedItem = item; selectedName.Text = item.Text.Split('\n')[0];
            selectedSource.Text = string.Join(Environment.NewLine, new[] { ReadProperty(item, "RegPath"), ReadProperty(item, "ItemFilePath"), ReadProperty(item, "ItemCommand"), ReadProperty(item, "Guid") }.Where(v => !string.IsNullOrWhiteSpace(v)));
            if(selectedSource.TextLength == 0) selectedSource.Text = AppString.W("NoSourceInfo");
            detailsButton.Enabled = item.ContextMenuStrip != null && item.ContextMenuStrip.Items.Count > 0;
            inspector.Visible = true;
        }
        private void ShowInspectedMenu()
        {
            if(inspectedItem != null && !inspectedItem.IsDisposed) inspectedItem.ContextMenuStrip?.Show(detailsButton, new Point(0, detailsButton.Height));
        }
        private void CopyInspectedSource()
        {
            if(selectedSource.TextLength == 0) return;
            try { Clipboard.SetText(selectedSource.Text); ShowFeedback(AppString.W("SourceCopied")); }
            catch(System.Runtime.InteropServices.ExternalException) { ShowFeedback(AppString.W("ClipboardBusy")); }
        }
        private void SelectAnalysisFile()
        {
            using(var dialog = new OpenFileDialog { Title = AppString.W("PickFileTitle"), CheckFileExists = true })
                if(dialog.ShowDialog(this) == DialogResult.OK) { ShellList.CurrentFileObjectPath = dialog.FileName; Navigate(1, 9); }
        }
        private void AddCurrentMenu()
        {
            NewItem entry = activeList?.Controls.OfType<NewItem>().FirstOrDefault();
            if(entry == null) return;
            try { entry.AddNewItem?.Invoke(); CaptureList(activeList); UpdateWorkspaceButtons(); }
            catch(Exception ex) { ShowFeedback(AppString.W("NewMenuFailed", ex.Message)); }
        }
        private void GoBack()
        {
            if(navigationHistory.Count == 0) return;
            var route = navigationHistory.Pop(); Navigate(route.Item1, route.Item2, false);
        }
        private void ReloadWorkspace()
        {
            if(currentRoute == null) return;
            Navigate(currentRoute.Item1, currentRoute.Item2, false);
        }
        private void UndoChange()
        {
            if(!ChangeHistory.CanUndo) return;
            if(ChangeHistory.TryUndoLast(out string error)) { ReloadWorkspace(); ShowFeedback(AppString.W("UndoDone")); }
            else ShowFeedback(error ?? AppString.W("NothingToUndo"));
        }
        private void HistoryChanged(object sender, EventArgs e)
        {
            if(IsDisposed) return;
            if(InvokeRequired) { BeginInvoke(new Action(() => HistoryChanged(sender, e))); return; }
            UpdateWorkspaceButtons();
            if(specialView == "history") UpdateHistoryText();
            else if(activeList != null) ApplyFilter();
        }
        private void UpdateHistoryText()
        {
            historyRows.SuspendLayout();
            foreach(Control child in historyRows.Controls.Cast<Control>().ToArray()) child.Dispose();
            var entries = ChangeHistory.Entries;
            if(entries.Count == 0)
            {
                historyRows.Controls.Add(new Label { Text = AppString.W("HistoryEmpty"), Height = 160.DpiZoom(), Width = Math.Max(1, historyRows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2), TextAlign = ContentAlignment.MiddleCenter });
            }
            else foreach(var entry in entries.Reverse())
            {
                var card = new FluentCard { Height = 104.DpiZoom(), Width = Math.Max(1, historyRows.Width - SystemInformation.VerticalScrollBarWidth - 2), Padding = new Padding(16, 12, 16, 10).DpiZoom(), Margin = new Padding(0, 0, 0, 8).DpiZoom() };
                var name = new Label { Text = entry.Description, Dock = DockStyle.Top, Height = 25.DpiZoom(), AutoEllipsis = true };
                var state = new Label { Text = $"{entry.Timestamp:yyyy-MM-dd HH:mm}  ·  {entry.Status}", Dock = DockStyle.Top, Height = 23.DpiZoom(), AutoEllipsis = true };
                var source = new Label { Text = entry.Target, Dock = DockStyle.Fill, AutoEllipsis = true };
                card.Controls.Add(source); card.Controls.Add(state); card.Controls.Add(name);
                historyRows.Controls.Add(card);
            }
            historyRows.ResumeLayout(true);
            ShowFeedback(ChangeHistory.LastError ?? AppString.W("HistoryFooter"));
        }
        private void ShowFeedback(string message)
        {
            if(resultLabel == null) return;
            resultLabel.Text = message?.Replace("\r\n", " · ").Replace('\n', ' ');
            resultLabel.Visible = !string.IsNullOrEmpty(message);
            resultLabel.AccessibleDescription = message;
        }
        private void UpdateWorkspaceButtons()
        {
            if(undoButton == null || currentRoute == null) return;
            int group = NavigationGroup(currentRoute.Item1, currentRoute.Item2);
            backButton.Enabled = navigationHistory.Count > 0;
            breadcrumb.Text = AppString.W("Workspace") + "  /  " + (group == -1 ? AppString.W("Overview") : group == 0 ? AppString.W("MenuManagement") : group == -2 ? AppString.W("ModernExtensions") : group == 2 ? AppString.W("MenuEnhancements") : group == -3 ? AppString.W("ChangeHistory") : AppString.W("Settings"));
            commandBar.Visible = group != -1;
            scenePicker.Visible = sceneRoutes.Count > 0;
            searchSurface.Visible = activeList != null;
            stateFilter.Visible = activeList != null && activeList.Controls.OfType<IChkVisibleItem>().Any();
            undoButton.Visible = group == -3 || ChangeHistory.CanUndo;
            undoButton.Enabled = ChangeHistory.CanUndo;
            addButton.Visible = activeList != null && activeList.Controls.OfType<NewItem>().Any();
            if(addButton.Visible) addButton.Text = AppString.W("AddItem");
            resultLabel.Visible = group != -1 && !string.IsNullOrEmpty(resultLabel.Text);
            foreach(var entry in navigationButtons)
            {
                entry.Item3.Selected = entry.Item1 == group;
                entry.Item3.AccessibleDescription = entry.Item3.Selected ? AppString.W("CurrentSection") : AppString.W("OpenSection", entry.Item3.Text);
                entry.Item3.Invalidate();
            }
            LayoutCommands();
        }
        private void ShowWorkspaceMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(AppString.W("PickFileMenu"), null, (s, e) => SelectAnalysisFile());
            menu.Items.Add(AppString.W("PickFolderMenu"), null, (s, e) =>
            {
                using(var dialog = new FolderBrowserDialog { Description = AppString.W("PickFolderTitle"), UseDescriptionForTitle = true })
                    if(dialog.ShowDialog(this) == DialogResult.OK) { ShellList.CurrentFileObjectPath = dialog.SelectedPath; Navigate(1, 9); }
            });
            menu.Items.Add(AppString.W("OpenConfigDir"), null, (s, e) => ExternalProgram.OpenDirectory(AppConfig.ConfigDir));
            if(!IsAdministrator()) menu.Items.Add(AppString.W("RelaunchAdmin"), null, (s, e) =>
            {
                try { SingleInstance.Restart(elevated: true); }
                catch(System.ComponentModel.Win32Exception ex) { ShowFeedback(ex.NativeErrorCode == 1223 ? AppString.W("ElevationCancelled") : ex.Message); }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(AppString.W("RestartExplorerMenu"), null, (s, e) =>
            {
                if(MessageBox.Show(this, AppString.W("RestartExplorerConfirm"), AppString.W("ApplyMenuChanges"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                {
                    ExternalProgram.RestartExplorer(); ExplorerRestarter.Hide(); ShowFeedback(AppString.W("ExplorerRestartRequested"));
                }
            });
            menu.Closed += (s, e) => { if(!IsDisposed) BeginInvoke(new Action(menu.Dispose)); else menu.Dispose(); };
            FluentTheme.Apply(menu); menu.Show(moreButton, new Point(0, moreButton.Height));
        }
        private void WorkspaceKeyDown(object sender, KeyEventArgs e)
        {
            if(e.Control && e.KeyCode == Keys.F && searchSurface.Visible) { searchBox.Focus(); searchBox.SelectAll(); }
            else if(e.KeyCode == Keys.F5) ReloadWorkspace();
            else if(e.Control && e.KeyCode == Keys.Z && !TextEditorHasFocus(this)) UndoChange();
            else if(e.Alt && e.KeyCode == Keys.Left) GoBack();
            else if(e.KeyCode == Keys.Escape && searchBox.TextLength > 0) searchBox.Clear();
            else if(e.KeyCode == Keys.Escape && inspector.Visible) inspector.Visible = false;
            else return;
            e.Handled = e.SuppressKeyPress = true;
        }
        private static bool TextEditorHasFocus(Control root)
        {
            if(root is TextBoxBase box && box.Focused && !box.ReadOnly) return true;
            foreach(Control child in root.Controls) if(child.ContainsFocus && TextEditorHasFocus(child)) return true;
            return false;
        }
        private void WorkspaceThemeChanged(object sender, EventArgs e)
        {
            if(IsDisposed) return;
            if(InvokeRequired) { BeginInvoke(new Action(ApplyWorkspaceTheme)); return; }
            ApplyWorkspaceTheme();
        }
        private void ApplyWorkspaceTheme()
        {
            if(workspace == null) return;
            FluentTheme.Apply(this);
            BackColor = FluentTheme.Background;
            foreach(Control control in new Control[] { workspace, workspace.Controls[0], viewHost, contentHost, pageHeader, commandBar, navigation, navigation.Parent, navigation.Parent.Parent, startPage, historyPage, historyRows }) control.BackColor = FluentTheme.Background;
            foreach(Control control in pageHeader.Controls) control.BackColor = FluentTheme.Background;
            foreach(Label label in navigation.Controls.OfType<Label>()) { label.BackColor = FluentTheme.Background; label.ForeColor = FluentTheme.Muted; }
            pageGuide.ForeColor = breadcrumb.ForeColor = resultLabel.ForeColor = permissionLabel.ForeColor = FluentTheme.Muted;
            inspector.BackColor = selectedSource.BackColor = FluentTheme.Surface;
            searchBox.BackColor = FluentTheme.Surface;
            permissionLabel.Parent.BackColor = FluentTheme.Background;
            foreach(Control child in permissionLabel.Parent.Controls) child.BackColor = FluentTheme.Background;
            foreach(Control row in startPage.Controls) row.BackColor = row is FluentCard ? FluentTheme.Surface : FluentTheme.Background;
            int mode = FluentTheme.Mode == "Dark" ? 2 : FluentTheme.Mode == "Light" ? 1 : 0;
            if(themePicker.SelectedIndex != mode) themePicker.SelectedIndex = mode;
            UpdateWorkspaceButtons();
            Invalidate(true);
        }
    }
}
