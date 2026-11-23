using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Rmemu.Controls;
using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Rmemu
{
    sealed partial class MainForm
    {
        private Panel workspace, viewHost, pageHeader, commandBar, contentHost, inspector, historyPage;
        private FlowLayoutPanel navigation, startPage, historyRows;
        private Label pageTitle, pageGuide, breadcrumb, resultLabel, emptyLabel, selectedName, permissionLabel;
        private TextBox selectedSource, searchBox;
        private FluentSelect stateFilter, themePicker, scenePicker;
        private FluentSearchBox searchSurface;
        private FluentButton undoButton, backButton, detailsButton, sourceButton, addButton, refreshButton, moreButton, maximizeButton;
        private Panel errorView;
        private Label errorMessage;
        private readonly List<Tuple<int, int, FluentButton>> navigationButtons = new List<Tuple<int, int, FluentButton>>();
        private readonly List<Tuple<int, int, string>> sceneRoutes = new List<Tuple<int, int, string>>();
        private readonly Font pageFont = new Font(FluentTheme.BodyFont.FontFamily, 22F, FontStyle.Bold);
        private readonly Font sectionFont = new Font(FluentTheme.BodyFont.FontFamily, 14F, FontStyle.Bold);
        private readonly Font heroFont = new Font(FluentTheme.BodyFont.FontFamily, 19F, FontStyle.Bold);
        private bool populatingScenes;

        private void InitializeWorkspace()
        {
            SuspendLayout();
            Font = FluentTheme.BodyFont;
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);
            Rectangle screen = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(960.DpiZoom(), screen.Width), Math.Min(680.DpiZoom(), screen.Height));
            Size saved = AppConfig.MainFormSize;
            Size desired = saved.IsEmpty ? new Size(1160, 780).DpiZoom() : saved;
            Size = new Size(Math.Max(MinimumSize.Width, Math.Min(desired.Width, screen.Width)), Math.Max(MinimumSize.Height, Math.Min(desired.Height, screen.Height)));
            MaximizedBounds = screen;
            SuspendMainBodyWhenMove = SuspendMainBodyWhenResize = false;
            Controls.Remove(ToolBar); Controls.Remove(SideBar); Controls.Remove(StatusBar);
            ToolBar.Dispose(); SideBar.Dispose(); StatusBar.Dispose();
            workspace = new Panel { Dock = DockStyle.Fill, Name = "Workspace" };
            Controls.Add(workspace);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208.DpiZoom()));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38.DpiZoom()));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            workspace.Controls.Add(grid);
            Panel caption = BuildCaption();
            grid.Controls.Add(caption, 0, 0);
            grid.SetColumnSpan(caption, 2);
            Panel rail = BuildNavigation();
            grid.Controls.Add(rail, 0, 1);
            viewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 12, 28, 12).DpiZoom(), Margin = Padding.Empty, Name = "ViewHost" };
            grid.Controls.Add(viewHost, 1, 1);

            pageHeader = new Panel { Dock = DockStyle.Top, Height = 106.DpiZoom() };
            backButton = MakeButton("", GoBack, 26, "\uE72B", true);
            backButton.AccessibleName = AppString.W("Back");
            breadcrumb = new Label { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            pageTitle = new Label { Font = pageFont, AutoEllipsis = true, Name = "PageTitle" };
            pageGuide = new Label { AutoEllipsis = true, Name = "PageGuide" };
            pageHeader.Controls.AddRange(new Control[] { backButton, breadcrumb, pageTitle, pageGuide });
            pageHeader.Resize += (s, e) =>
            {
                backButton.SetBounds(0, 0, 26.DpiZoom(), 24.DpiZoom());
                breadcrumb.SetBounds(32.DpiZoom(), 0, Math.Max(1, pageHeader.Width - 32.DpiZoom()), 24.DpiZoom());
                pageTitle.SetBounds(0, 31.DpiZoom(), pageHeader.Width, 39.DpiZoom());
                pageGuide.SetBounds(0, 76.DpiZoom(), pageHeader.Width, 28.DpiZoom());
            };
            commandBar = BuildCommandBar();
            resultLabel = new Label { Dock = DockStyle.Bottom, Height = 26.DpiZoom(), TextAlign = ContentAlignment.MiddleLeft, AccessibleName = AppString.W("Feedback"), AutoEllipsis = true };
            inspector = BuildInspector();
            contentHost = new Panel { Dock = DockStyle.Fill, Name = "ContentHost" };
            MainBody.Parent = contentHost;
            MainBody.Dock = DockStyle.Fill;
            MainBody.Padding = Padding.Empty;
            modernExtensionsList.Parent = MainBody;
            modernExtensionsList.Visible = false;
            emptyLabel = new Label { Text = AppString.W("NoMatches"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false, Font = new Font("Segoe UI", 11F) };
            MainBody.Controls.Add(emptyLabel);
            startPage = BuildStartPage();
            historyPage = BuildHistoryPage();
            errorView = BuildErrorView();
            contentHost.Controls.AddRange(new Control[] { startPage, historyPage, errorView });
            viewHost.Controls.Add(contentHost);
            viewHost.Controls.Add(inspector);
            viewHost.Controls.Add(resultLabel);
            explorerRestarter.Parent = viewHost;
            explorerRestarter.Dock = DockStyle.Bottom;
            explorerRestarter.Visible = false;
            viewHost.Controls.Add(commandBar);
            viewHost.Controls.Add(pageHeader);
            foreach(MyList list in MainControls.OfType<MyList>().Concat(new[] { modernExtensionsList }))
            {
                list.ControlAdded += (s, e) => { BindItem(e.Control as MyListItem); QueueListSync(list); };
                list.ControlRemoved += (s, e) => QueueListSync(list);
                MyList hoverList = list;
                hoverList.HoveredItemChanged += (s, e) =>
                {
                    // Honors the "show file path in status bar" setting; the feedback line replaces the old status bar.
                    if(!AppConfig.ShowFilePath || hoverList != activeList || hoverList.HoveredItem == null) return;
                    MyListItem hovered = hoverList.HoveredItem;
                    string path = new[] { "ItemFilePath", "RegPath", "GroupPath" }.Select(prop => ReadProperty(hovered, prop))
                        .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                    ShowFeedback(path ?? hovered.Text.Split('\n')[0]);
                };
                foreach(MyListItem item in list.Controls.OfType<MyListItem>()) BindItem(item);
            }
            searchBox.TextChanged += (s, e) => { searchDelay.Stop(); searchDelay.Start(); };
            searchDelay.Tick += (s, e) => { searchDelay.Stop(); ApplyFilter(); };
            stateFilter.SelectedIndexChanged += (s, e) => ApplyFilter();
            ChangeHistory.Changed += HistoryChanged;
            FluentTheme.Changed += WorkspaceThemeChanged;
            FormClosed += (s, e) =>
            {
                ChangeHistory.Changed -= HistoryChanged;
                FluentTheme.Changed -= WorkspaceThemeChanged;
                searchDelay.Dispose();
                pageFont.Dispose(); sectionFont.Dispose(); heroFont.Dispose();
            };
            KeyPreview = true;
            KeyDown += WorkspaceKeyDown;
            Resize += (s, e) => { if(maximizeButton != null) { maximizeButton.Glyph = WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922"; maximizeButton.Invalidate(); } };
            ApplyWorkspaceTheme();
            Navigate(-1, 0);
            ResumeLayout(true);
        }

        private Panel BuildCaption()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Name = "WindowCaption", Padding = new Padding(12, 0, 0, 0).DpiZoom() };
            var icon = new Label { Text = "\uE700", Font = FluentIcons.Font, Width = 24.DpiZoom(), Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleCenter, Name = "BrandIcon" };
            var name = new Label { Text = "Rmemu", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 138.DpiZoom(), WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            FluentButton minimize = MakeButton("", () => WindowState = FormWindowState.Minimized, 46, "\uE921", true);
            maximizeButton = MakeButton("", ToggleMaximize, 46, "\uE922", true);
            FluentButton close = MakeButton("", Close, 46, "\uE8BB", true);
            minimize.AccessibleName = AppString.W("Minimize"); maximizeButton.AccessibleName = AppString.W("MaximizeRestore"); close.AccessibleName = AppString.W("CloseWindow");
            foreach(FluentButton button in new[] { minimize, maximizeButton, close }) { button.Height = 36.DpiZoom(); button.Margin = Padding.Empty; button.TabStop = false; buttons.Controls.Add(button); }
            panel.Controls.Add(name); panel.Controls.Add(icon); panel.Controls.Add(buttons);
            MouseEventHandler drag = (s, e) => { if(e.Button == MouseButtons.Left && e.Clicks == 1) { ReleaseCapture(); SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero); } };
            panel.MouseDown += drag; name.MouseDown += drag; icon.MouseDown += drag;
            panel.DoubleClick += (s, e) => ToggleMaximize(); name.DoubleClick += (s, e) => ToggleMaximize();
            return panel;
        }

        private Panel BuildNavigation()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(12, 18, 12, 14).DpiZoom() };
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 146.DpiZoom(), Padding = new Padding(8, 0, 8, 0).DpiZoom() };
            var appearance = new Label { Text = AppString.W("Appearance"), Dock = DockStyle.Top, Height = 28.DpiZoom() };
            themePicker = new FluentSelect { Dock = DockStyle.Top, Height = 34.DpiZoom(), AccessibleName = AppString.W("AppTheme") };
            themePicker.Items.AddRange(new object[] { AppString.W("ThemeSystem"), AppString.W("ThemeLight"), AppString.W("ThemeDark") });
            themePicker.SelectedIndex = FluentTheme.Mode == "Dark" ? 2 : FluentTheme.Mode == "Light" ? 1 : 0;
            themePicker.SelectedIndexChanged += (s, e) =>
            {
                try { FluentTheme.SetMode(new[] { "System", "Light", "Dark" }[themePicker.SelectedIndex]); }
                catch(Exception ex) { ShowFeedback(AppString.W("ThemeSaveFailed", ex.Message)); }
            };
            permissionLabel = new Label { Text = IsAdministrator() ? AppString.W("AdminMode") : AppString.W("StandardMode"), Dock = DockStyle.Bottom, Height = 58.DpiZoom(), AutoEllipsis = true };
            footer.Controls.Add(themePicker); footer.Controls.Add(appearance); footer.Controls.Add(permissionLabel);
            navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = Padding.Empty, Margin = Padding.Empty, Name = "Navigation", AccessibleName = AppString.W("MainNavigation") };
            navigation.Controls.Add(new Label { Text = AppString.W("Workspace"), Height = 30.DpiZoom(), Width = 170.DpiZoom(), Padding = new Padding(10, 0, 0, 0).DpiZoom() });
            AddNavigationButton(AppString.W("Overview"), -1, 0, "\uE80F");
            AddNavigationButton(AppString.W("MenuManagement"), 0, 0, "\uE700");
            AddNavigationButton(AppString.W("ModernExtensions"), -2, 0, "\uE71D");
            AddNavigationButton(AppString.W("MenuEnhancements"), 2, 0, "\uE710");
            AddNavigationButton(AppString.W("ChangeHistory"), -3, 0, "\uE81C");
            AddNavigationButton(AppString.W("Settings"), 4, 0, "\uE713");
            var locate = MakeButton(AppString.W("LocateByFile"), SelectAnalysisFile, 184, "\uE721", true);
            locate.Margin = new Padding(0, 24, 0, 0).DpiZoom();
            locate.TextAlign = ContentAlignment.MiddleLeft; locate.Height = 40.DpiZoom();
            navigation.Controls.Add(locate);
            panel.Controls.Add(navigation); panel.Controls.Add(footer);
            return panel;
        }

        private void AddNavigationButton(string text, int tab, int index, string glyph)
        {
            var button = MakeButton(text, () => Navigate(tab, index), 184, glyph, true);
            button.IsNavigation = true;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Height = 40.DpiZoom();
            button.Margin = new Padding(0, 0, 0, 4).DpiZoom();
            navigationButtons.Add(Tuple.Create(tab, index, button));
            navigation.Controls.Add(button);
        }

        private FluentButton MakeButton(string text, Action action, int width = 100, string glyph = null, bool quiet = false)
        {
            var button = new FluentButton { Font = FluentTheme.BodyFont, Text = text, Width = width.DpiZoom(), Height = 34.DpiZoom(), Margin = new Padding(0, 0, 8, 0).DpiZoom(), Glyph = glyph, Quiet = quiet, AccessibleName = text };
            button.Width = button.FitWidth(button.Width);
            button.Click += (s, e) => action();
            return button;
        }

        private Panel BuildCommandBar()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 52.DpiZoom(), Name = "ContextCommands" };
            scenePicker = new FluentSelect { AccessibleName = AppString.W("ChooseLocation") };
            scenePicker.SelectedIndexChanged += (s, e) =>
            {
                if(populatingScenes || scenePicker.SelectedIndex < 0) return;
                var route = sceneRoutes[scenePicker.SelectedIndex];
                Navigate(route.Item1, route.Item2);
            };
            searchSurface = new FluentSearchBox(); searchBox = searchSurface.Editor;
            stateFilter = new FluentSelect { AccessibleName = AppString.W("FilterByState") };
            stateFilter.Items.AddRange(new object[] { AppString.W("AllStates"), AppString.W("Enabled"), AppString.W("Disabled") }); stateFilter.SelectedIndex = 0;
            refreshButton = MakeButton("", ReloadWorkspace, 34, "\uE72C"); refreshButton.AccessibleName = AppString.W("RefreshF5");
            undoButton = MakeButton(AppString.W("Undo"), UndoChange, 96, "\uE7A7");
            moreButton = MakeButton("", ShowWorkspaceMenu, 34, "\uE712"); moreButton.AccessibleName = AppString.W("MoreActions");
            addButton = MakeButton(AppString.W("NewMenu"), AddCurrentMenu, 110, "\uE710"); addButton.Primary = true;
            panel.Controls.AddRange(new Control[] { scenePicker, searchSurface, stateFilter, refreshButton, undoButton, moreButton, addButton });
            panel.Resize += (s, e) => LayoutCommands();
            return panel;
        }

        private void LayoutCommands()
        {
            if(commandBar == null) return;
            int gap = 8.DpiZoom(), height = 34.DpiZoom(), width = commandBar.ClientSize.Width;
            int right = width;
            bool twoRows = width < 640.DpiZoom() && scenePicker.Visible && searchSurface.Visible;
            int desiredHeight = (twoRows ? 94 : 52).DpiZoom();
            if(commandBar.Height != desiredHeight) commandBar.Height = desiredHeight;
            int actionTop = (twoRows ? 44 : 2).DpiZoom();
            foreach(Control control in new Control[] { moreButton, refreshButton, undoButton, addButton, stateFilter })
            {
                if(!control.Visible) continue;
                int itemWidth = control == stateFilter ? 116.DpiZoom() : control == addButton ? addButton.FitWidth(110.DpiZoom()) : control == undoButton ? undoButton.FitWidth(96.DpiZoom()) : 34.DpiZoom();
                right -= itemWidth; control.SetBounds(Math.Max(0, right), actionTop, itemWidth, height); right -= gap;
            }
            int left = 0;
            int inputRight = twoRows ? width : right;
            if(scenePicker.Visible)
            {
                int itemWidth = Math.Min(220.DpiZoom(), Math.Max(150.DpiZoom(), inputRight / 3));
                scenePicker.SetBounds(0, 2.DpiZoom(), itemWidth, height); left = itemWidth + gap;
            }
            searchSurface.SetBounds(left, 2.DpiZoom(), Math.Max(40.DpiZoom(), inputRight - left), height);
        }

        private Panel BuildInspector()
        {
            var panel = new FluentCard { Dock = DockStyle.Bottom, Height = 112.DpiZoom(), Visible = false, Padding = new Padding(16, 10, 16, 10).DpiZoom(), Name = "SourceInspector" };
            selectedName = new Label { Dock = DockStyle.Top, Height = 25.DpiZoom(), AutoEllipsis = true, Font = new Font(FluentTheme.BodyFont.FontFamily, 10F, FontStyle.Bold) };
            selectedSource = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, AccessibleName = AppString.W("SourceAndCommand") };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 216.DpiZoom(), Padding = new Padding(12, 8, 0, 0).DpiZoom(), WrapContents = true };
            detailsButton = MakeButton(AppString.W("ItemActions"), ShowInspectedMenu, 92);
            sourceButton = MakeButton(AppString.W("CopySource"), CopyInspectedSource, 92);
            actions.Controls.AddRange(new Control[] { detailsButton, sourceButton });
            panel.Controls.Add(selectedSource); panel.Controls.Add(actions); panel.Controls.Add(selectedName);
            return panel;
        }

        private FlowLayoutPanel BuildStartPage()
        {
            var page = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = Padding.Empty, Name = "Overview" };
            var hero = new FluentCard { Height = 168.DpiZoom(), Margin = new Padding(0, 0, 0, 22).DpiZoom(), Padding = new Padding(24, 18, 24, 18).DpiZoom() };
            var art = new MenuIllustration { Dock = DockStyle.Right, Width = 206.DpiZoom() };
            var title = new Label { Text = AppString.W("HeroTitle"), Font = heroFont, Dock = DockStyle.Top, Height = 39.DpiZoom() };
            var description = new Label { Text = AppString.W("HeroText"), Dock = DockStyle.Fill };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36.DpiZoom(), WrapContents = false };
            var choose = MakeButton(AppString.W("HeroLocate"), SelectAnalysisFile, 150, "\uE721"); choose.Primary = true;
            actions.Controls.Add(choose);
            actions.Controls.Add(MakeButton(AppString.W("BrowseMenus"), () => Navigate(0, 0), 108, null, true));
            var text = new Panel { Dock = DockStyle.Fill };
            text.Controls.Add(description); text.Controls.Add(actions); text.Controls.Add(title);
            hero.Controls.Add(text); hero.Controls.Add(art);
            var label = new Label { Text = AppString.W("CommonPlaces"), Font = sectionFont, Height = 34.DpiZoom(), Margin = new Padding(0, 0, 0, 8).DpiZoom() };
            var tasks = new TableLayoutPanel { Height = 184.DpiZoom(), ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 20).DpiZoom(), Padding = Padding.Empty };
            tasks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); tasks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tasks.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); tasks.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            AddTask(tasks, 0, 0, AppString.W("FileMenu"), AppString.W("FileMenuInfo"), "\uE8A5", () => Navigate(0, 0));
            AddTask(tasks, 1, 0, AppString.W("DesktopMenu"), AppString.W("DesktopMenuInfo"), "\uE7F4", () => Navigate(0, 4));
            AddTask(tasks, 0, 1, AppString.W("NewFile"), AppString.W("NewFileInfo"), "\uE710", () => Navigate(0, 11));
            AddTask(tasks, 1, 1, AppString.W("Win11Menu"), AppString.W("Win11MenuInfo"), "\uE71D", () => Navigate(-2, 0));
            var tip = new FluentCard { Height = 66.DpiZoom(), Padding = new Padding(16, 12, 16, 8).DpiZoom(), Margin = Padding.Empty };
            var tipIcon = new Label { Text = "\uE73E", Font = FluentIcons.Font, Dock = DockStyle.Left, Width = 34.DpiZoom(), TextAlign = ContentAlignment.TopCenter };
            var tipText = new Label { Text = AppString.W("Tip"), Dock = DockStyle.Fill };
            tip.Controls.Add(tipText); tip.Controls.Add(tipIcon);
            page.Controls.AddRange(new Control[] { hero, label, tasks, tip });
            page.SizeChanged += (s, e) =>
            {
                int width = Math.Max(1, page.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
                foreach(Control control in page.Controls) control.Width = width;
                art.Visible = width >= 620.DpiZoom();
            };
            return page;
        }

        private void AddTask(TableLayoutPanel host, int col, int row, string title, string description, string glyph, Action action)
        {
            var tile = new FluentTaskTile { Text = title, Description = description, Glyph = glyph, Dock = DockStyle.Fill, AccessibleName = title, AccessibleDescription = description, Margin = new Padding(col == 0 ? 0 : 6, 0, col == 0 ? 6 : 0, 10).DpiZoom() };
            tile.Click += (s, e) => action();
            host.Controls.Add(tile, col, row);
        }

        private Panel BuildHistoryPage()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            historyRows = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = Padding.Empty };
            historyRows.SizeChanged += (s, e) => { foreach(Control child in historyRows.Controls) child.Width = Math.Max(1, historyRows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2); };
            panel.Controls.Add(historyRows);
            return panel;
        }

        private Panel BuildErrorView()
        {
            var panel = new FluentCard { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(28).DpiZoom() };
            var heading = new Label { Text = AppString.W("CannotRead"), Font = sectionFont, Dock = DockStyle.Top, Height = 38.DpiZoom() };
            errorMessage = new Label { Dock = DockStyle.Fill };
            var retry = MakeButton(AppString.W("Retry"), ReloadWorkspace, 120, "\uE72C"); retry.Dock = DockStyle.Bottom;
            panel.Controls.Add(errorMessage); panel.Controls.Add(retry); panel.Controls.Add(heading);
            return panel;
        }

        private void ToggleMaximize()
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Borderless window: the 1px padding exposes the form surface; draw the frame there.
            if(FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Normal)
                using(var pen = new Pen(FluentTheme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Invalidate(false); }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                if(FormBorderStyle == FormBorderStyle.None) parameters.Style |= 0x00040000 | 0x00020000 | 0x00010000 | 0x00080000;
                return parameters;
            }
        }
        protected override void WndProc(ref Message m)
        {
            const int hitTest = 0x0084;
            if(m.Msg == 0x0083 && m.WParam != IntPtr.Zero && FormBorderStyle == FormBorderStyle.None)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if(m.Msg == hitTest && FormBorderStyle == FormBorderStyle.None && WindowState == FormWindowState.Normal)
            {
                long position = m.LParam.ToInt64();
                Point point = PointToClient(new Point(unchecked((short)(position & 0xffff)), unchecked((short)((position >> 16) & 0xffff))));
                int grip = 5.DpiZoom();
                bool left = point.X < grip, right = point.X >= Width - grip, top = point.Y < grip, bottom = point.Y >= Height - grip;
                if(left || right || top || bottom)
                {
                    m.Result = new IntPtr(top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : 11);
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }
}
