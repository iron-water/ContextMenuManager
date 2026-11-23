using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using BluePointLilac.Controls;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

internal static class Program
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly AppAssembly = typeof(FluentTheme).Assembly;
    private static readonly string FixtureId = Guid.TryParseExact(Environment.GetEnvironmentVariable("RMEMU_DESKTOP_FIXTURE_ID"), "N", out Guid id) ? id.ToString("N") : Guid.NewGuid().ToString("N");
    private static readonly string FixtureKey = @"Software\Rmemu.DesktopChecks\" + FixtureId;
    private static string FixturePath => @"HKEY_CURRENT_USER\" + FixtureKey;
    private static readonly ReviewReport Report = new();
    private static readonly HashSet<IntPtr> DismissedDialogs = new();
    private static string output = "";
    private static string context = "startup";
    private static Form? main;
    private static Form? expectedModal;
    private static bool scheduled;
    private static bool completed;
    private static int sequence;

    // Run without --isolated. The launcher copies the complete output so Application.StartupPath,
    // settings, language files and the real Program.Main all use the temporary application directory.
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("Rmemu.DesktopChecks [--output <directory>] [--timeout-seconds <seconds>]");
            Console.WriteLine("Run on Windows from an interactive desktop. Default output: artifacts/full-review; timeout: 600 seconds.");
            Console.WriteLine("Invokes the actual Program/MainForm in isolated temporary output/config, tests Light/Dark at 960x680 and 1440x900 logical pixels, traverses every route and scroll viewport, cancels real app modal fixtures, and toggles only temporary HKCU fixtures.");
            Console.WriteLine("Writes report.json, screenshots/*.png and paged contact-sheet-*.png. Exit 1 means errors or incomplete coverage; warnings and exact coverage limits are recorded in JSON. Native dialogs, UAC and DWM decoration are outside capture coverage.");
            return 0;
        }
        output = Path.GetFullPath(GetArgument(args, "--output") ?? Path.Combine("artifacts", "full-review"));
        Directory.CreateDirectory(output);
        if(!args.Contains("--isolated", StringComparer.Ordinal)) return LaunchIsolated(args);
        try
        {
            CultureAndConfig();
            Report.StartedUtc = DateTimeOffset.UtcNow;
            Report.Runtime = RuntimeInformation.FrameworkDescription;
            Report.OperatingSystem = RuntimeInformation.OSDescription;
            Report.ConfigDirectory = Path.Combine(AppContext.BaseDirectory, "Config");
            Report.Limits.AddRange(new[]
            {
                "DrawToBitmap captures real managed windows without relying on screen foreground. Native DWM frames, shadows, system file/icon dialogs and secure desktop/UAC are not captured.",
                "Both themes run at 960x680 and 1440x900 logical pixels (scaled by actual DeviceDpi / 96). This checks 100%-equivalent geometry; it does not switch monitor DPI or prove other DPI settings.",
                "All application route arrays and special routes are traversed. Existing machine registrations are read only; optional/absent providers can yield empty lists. Every initially visible list row is checked for scroll reachability; collapsed groups are not automatically expanded.",
                "Modal coverage constructs actual application Form types, supplies harmless input and cancels. It covers layout and cancellation, not destructive confirmation, admin/UAC, Explorer restart, executing commands, deleting menus or native common dialog interaction.",
                "Only a unique temporary HKCU Software\\Rmemu.DesktopChecks subtree is modified by the toggle scenario. Production configuration is isolated; application reads may still query real system menu registrations.",
                "Geometry, clipping, critical-control occlusion, contrast and screenshot content are measured. Human aesthetic judgment, assistive technology behavior and packaged Windows 11 dynamic menu completeness require separate review."
            });
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => AddIssue("error", "ui-thread-exception", context, e.Exception.ToString());
            Application.Idle += FindMainWindow;
            using var guard = new System.Windows.Forms.Timer { Interval = 120 };
            guard.Tick += (_, _) => CloseUnexpectedDialogs();
            EventHandler? startGuard = null;
            startGuard = (_, _) => { Application.Idle -= startGuard; guard.Start(); };
            Application.Idle += startGuard;
            var startup = AppAssembly.GetType("Rmemu.Program", throwOnError: true)!.GetMethod("Main", Members)
                ?? throw new MissingMethodException("Rmemu.Program.Main");
            Report.StartupEntryPoint = startup.DeclaringType!.FullName + "." + startup.Name;
            startup.Invoke(null, null);
            guard.Stop();
            if(!completed) AddIssue("error", "startup-or-premature-exit", context, "The real Program.Main exited before desktop coverage completed.");
        }
        catch(Exception exception)
        {
            AddIssue("error", "fatal", context, Unwrap(exception).ToString());
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(FixtureKey, false); }
            catch(Exception exception) { AddIssue("error", "fixture-cleanup", context, exception.Message); }
            SaveReport();
        }
        return Report.Issues.Any(i => i.Severity == "error") ? 1 : 0;
    }

    private static int LaunchIsolated(string[] args)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "Rmemu.DesktopChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            CopyTree(AppContext.BaseDirectory, temporary);
            var start = new ProcessStartInfo(Path.Combine(temporary, "Rmemu.DesktopChecks.exe"))
            {
                WorkingDirectory = temporary,
                UseShellExecute = false
            };
            start.ArgumentList.Add("--isolated");
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(output);
            start.Environment["APPDATA"] = Path.Combine(temporary, "UserProfile", "Roaming");
            start.Environment["RMEMU_DESKTOP_FIXTURE_ID"] = FixtureId;
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated desktop runner.");
            int seconds = int.TryParse(GetArgument(args, "--timeout-seconds"), out int requested) && requested > 0 ? requested : 1800;
            if(!child.WaitForExit((int)Math.Min(int.MaxValue, seconds * 1000L)))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                File.WriteAllText(Path.Combine(output, "timeout.json"), JsonSerializer.Serialize(new
                {
                    error = "Desktop review exceeded its process deadline.", timeoutSeconds = seconds,
                    partialReport = "report.json is checkpointed after each route; inspect it for the last completed route."
                }, JsonOptions));
                return 1;
            }
            return child.ExitCode;
        }
        catch(Exception exception)
        {
            File.WriteAllText(Path.Combine(output, "launcher-error.json"), JsonSerializer.Serialize(new { error = exception.ToString() }, JsonOptions));
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(FixtureKey, false); }
            catch(Exception exception) { Console.Error.WriteLine("Registry fixture cleanup failed: " + exception.Message); }
            try { Directory.Delete(temporary, recursive: true); }
            catch(Exception exception) { Console.Error.WriteLine("Temporary directory cleanup failed: " + exception.Message); }
        }
    }

    private static void CopyTree(string source, string destination)
    {
        foreach(string directory in Directory.EnumerateDirectories(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(directory));
            Directory.CreateDirectory(target);
            CopyTree(directory, target);
        }
        foreach(string file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }

    private static void CultureAndConfig()
    {
        // Configuration is written before first touching any production type with static settings.
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        string config = Path.Combine(AppContext.BaseDirectory, "Config");
        Directory.CreateDirectory(config);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Directory.CreateDirectory(Path.Combine(roaming, "Microsoft", "Windows", "SendTo"));
        File.WriteAllText(Path.Combine(config, "Config.ini"), "[General]\r\nLanguage=default\r\nUpdateFrequency=-1\r\nAutoBackup=0\r\nHideDisabledItems=0\r\n[Window]\r\nTopMost=0\r\n");
        File.WriteAllText(Path.Combine(config, "Theme.ini"), "[Appearance]\r\nMode=Light\r\n");
    }

    private static void FindMainWindow(object? sender, EventArgs e)
    {
        if(scheduled) return;
        main = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().FullName == "Rmemu.MainForm");
        if(main == null || !main.Visible) return;
        scheduled = true;
        Application.Idle -= FindMainWindow;
        main.BeginInvoke((Action)(async () =>
        {
            try { await RunReview(main); completed = true; }
            catch(Exception exception) { AddIssue("error", "review-exception", context, Unwrap(exception).ToString()); }
            finally
            {
                SaveReport();
                // Fixture toggles can set the real restart reminder. Hide that reminder so closing
                // this test window never offers to restart Explorer; no production flag is needed.
                if(Field(main, "explorerRestarter") is Control restarter) restarter.Visible = false;
                main.Close();
            }
        }));
    }

    private static async Task RunReview(Form form)
    {
        string actualConfig = AppAssembly.GetType("Rmemu.Methods.AppConfig", true)!.GetField("ConfigDir", Members)?.GetValue(null) as string
            ?? throw new MissingFieldException("AppConfig.ConfigDir");
        Report.ConfigDirectory = actualConfig;
        if(!string.Equals(Path.GetFullPath(actualConfig), Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Config")), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production startup selected configuration outside the isolated application directory: " + actualConfig);
        Report.Apartment = Thread.CurrentThread.GetApartmentState().ToString();
        if(Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("UI coverage requires STA.");
        Report.DeviceDpi = form.DeviceDpi;
        var routes = ReadRoutes(form.GetType());
        Report.Routes = routes;
        var sizes = new[] { (Name: "compact", Width: 960, Height: 680), (Name: "desktop", Width: 1440, Height: 900) };
        foreach(string mode in new[] { "Light", "Dark" })
        {
            FluentTheme.SetMode(mode);
            await Settle();
            foreach(var size in sizes)
            {
                form.WindowState = FormWindowState.Normal;
                form.Size = new Size(Scale(size.Width, form.DeviceDpi), Scale(size.Height, form.DeviceDpi));
                await Settle();
                if(form.Size != new Size(Scale(size.Width, form.DeviceDpi), Scale(size.Height, form.DeviceDpi)))
                    AddIssue("error", "dimension-not-applied", $"{mode}/{size.Name}", $"Requested {size.Width}x{size.Height} logical pixels at {form.DeviceDpi} DPI; actual window is {form.Width}x{form.Height} pixels.");
                foreach(var route in routes)
                {
                    context = $"{mode}/{size.Name}/{route.Category}:{route.Index}";
                    var visit = new RouteVisit { Theme = mode, Dimension = size.Name, Category = route.Category, Index = route.Index, Label = route.Label };
                    Report.Visits.Add(visit);
                    try
                    {
                        Navigate(form, route.Category, route.Index);
                        await Settle();
                        string? failure = Property(form, "LastNavigationError") as string;
                        string status = (Field(form, "resultLabel") as Control)?.Text ?? "";
                        visit.Status = status;
                        if(!string.IsNullOrWhiteSpace(failure) || status.Contains("加载失败", StringComparison.Ordinal))
                            AddIssue("error", "route-load-failure", context, failure ?? status);
                        var title = Field(form, "pageTitle") as Control ?? throw new MissingFieldException("MainForm.pageTitle");
                        var search = Field(form, "searchBox") as Control ?? throw new MissingFieldException("MainForm.searchBox");
                        visit.Title = title.Text;
                        RequireCritical(title, "page-title", visit);
                        if(string.IsNullOrWhiteSpace(title.Text)) AddIssue("error", "missing-title", context, "Page title is empty.");
                        var list = Field(form, "activeList") as MyList;
                        visit.ListType = list?.GetType().FullName;
                        visit.ListItems = list?.Controls.Count ?? 0;
                        visit.SearchEnabled = search.Enabled;
                        if(list != null) RequireCritical(search, "search", visit);
                        else if(search.Visible) AddIssue("error", "unused-search-visible", context, "A page without a list still displays a nonfunctional search field.");
                        if(list != null && !search.Enabled) AddIssue("error", "search-disabled", context, "A loaded list has a disabled search box.");
                        if(route.Category != 4 && route.Category != -1 && route.Category != -3 && list == null)
                            AddIssue("error", "missing-list", context, "A list route did not expose an active list.");
                        visit.Snapshots.Add(Capture(form, mode, size.Name, $"route-{route.Category}-{route.Index}", visit));
                        await CoverScrollPages(form, visit, mode, size.Name, route);
                        if(list != null && search is TextBox textBox) await CheckSearch(form, list, textBox, visit);
                    }
                    catch(Exception exception) { AddIssue("error", "route-exception", context, Unwrap(exception).ToString()); }
                    SaveReport();
                }
                await CoverModalForms(form, mode, size.Name);
            }
        }
        await CheckFixtureToggle(form);
        await CheckWorkspaceTransitions(form);
        BuildContactSheets();
    }

    private static async Task CheckWorkspaceTransitions(Form form)
    {
        context = "workspace-state-transitions";
        void Check(bool okay, string code, string message)
        {
            if(!okay) AddIssue("error", code, context, message);
            else Console.WriteLine("PASS " + code);
        }
        Navigate(form, 4, 0);
        await Settle();
        var settings = (MyList)Field(form, "activeList")!;
        var search = (TextBox)Field(form, "searchBox")!;
        Control[] before = settings.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
        search.Text = before.First().Text;
        await Task.Delay(260);
        Navigate(form, -1, 0); Navigate(form, 4, 0);
        await Settle();
        Check(before.All(c => c.Visible), "settings-filter-restores-on-revisit", "Persistent settings rows remained hidden after leaving a filtered page.");
        search.Focus();
        var undoKey = new KeyEventArgs(Keys.Control | Keys.Z);
        form.GetType().GetMethod("WorkspaceKeyDown", Members)!.Invoke(form, new object[] { form, undoKey });
        Check(!undoKey.Handled, "nested-text-undo-is-not-registry-undo", "Ctrl+Z was consumed as a registry undo while the nested search editor had focus.");
        Navigate(form, 2, 0);
        await Settle();
        var list = (MyList)Field(form, "activeList")!;
        Type groupType = AppAssembly.GetType("Rmemu.Controls.FoldGroupItem", true)!;
        Type subType = AppAssembly.GetType("Rmemu.Controls.FoldSubItem", true)!;
        Type pathType = AppAssembly.GetType("Rmemu.Methods.ObjectPath+PathType", true)!;
        using var group = (MyListItem)Activator.CreateInstance(groupType, new object[] { FixturePath, Enum.Parse(pathType, "Registry") })!;
        using var match = (MyListItem)Activator.CreateInstance(subType, nonPublic: true)!;
        using var other = (MyListItem)Activator.CreateInstance(subType, nonPublic: true)!;
        group.Text = "Desktop group fixture";
        match.Text = "Desktop match " + FixtureId;
        other.Text = "Desktop different " + FixtureId;
        subType.GetProperty("FoldGroupItem")!.SetValue(match, group);
        subType.GetProperty("FoldGroupItem")!.SetValue(other, group);
        list.AddItem(group); list.AddItem(match); list.AddItem(other);
        await Task.Delay(260);
        search.Text = match.Text;
        await Task.Delay(260);
        Check(match.Visible && !other.Visible, "group-query-filters-siblings", "A search exposed a nonmatching group sibling.");
        groupType.GetProperty("IsFold")!.SetValue(group, true);
        groupType.GetProperty("IsFold")!.SetValue(group, false);
        await Settle();
        Check(match.Visible && !other.Visible, "group-expansion-preserves-filter", "Expanding a group bypassed active filtering.");
        match.Text = "Desktop renamed " + FixtureId;
        search.Text = match.Text;
        await Task.Delay(260);
        Check(match.Visible && !other.Visible, "renamed-row-search-index-updates", "The renamed row is not found by its new name.");
        form.GetType().GetMethod("InspectItem", Members)!.Invoke(form, new object[] { match });
        search.Text = "Desktop definitely absent " + FixtureId;
        await Task.Delay(260);
        Check(!((Control)Field(form, "inspector")!).Visible, "filtered-selection-clears-inspector", "A filtered-out item remains in the source inspector.");
        search.Clear();
        await Task.Delay(260);
        Navigate(form, -1, 0);
        Size restored = form.Size;
        form.GetType().GetMethod("ToggleMaximize", Members)!.Invoke(form, null);
        await Settle();
        Rectangle work = Screen.FromControl(form).WorkingArea;
        Check(form.WindowState == FormWindowState.Maximized && form.ClientSize.Width <= work.Width + 2 && form.ClientSize.Height <= work.Height + 2,
            "custom-caption-maximize-respects-work-area", "The custom caption maximizes outside the working area.");
        form.GetType().GetMethod("ToggleMaximize", Members)!.Invoke(form, null);
        await Settle();
        Check(form.WindowState == FormWindowState.Normal && form.Size == restored, "custom-caption-restores-window", "The custom caption failed to restore the previous size.");
        Size originalMinimum = form.MinimumSize;
        form.MinimumSize = new Size(Scale(760, form.DeviceDpi), Scale(520, form.DeviceDpi));
        form.Size = new Size(Scale(800, form.DeviceDpi), Scale(560, form.DeviceDpi));
        Navigate(form, 0, 0);
        await Settle();
        Capture(form, FluentTheme.Mode, "constrained", "command-wrapping", null);
        var commands = (Control)Field(form, "commandBar")!;
        Control[] commandControls = commands.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
        bool overlaps = commandControls.Any(a => commandControls.Any(b => a != b && Rectangle.Intersect(a.Bounds, b.Bounds).Width > 2 && Rectangle.Intersect(a.Bounds, b.Bounds).Height > 2));
        Check(!overlaps, "constrained-commands-do-not-overlap", "Contextual commands overlap at 800x560 logical size.");
        form.MinimumSize = originalMinimum;
        form.Size = restored;
        Navigate(form, -1, 0);
        SaveReport();
    }

    private static List<Route> ReadRoutes(Type type)
    {
        var routes = new List<Route> { new(-1, 0, "Home"), new(-2, 0, "Windows 11 modern menus"), new(-3, 0, "Change history") };
        foreach(var source in new[] { ("GeneralItems", 0), ("TypeItems", 1), ("OtherRuleItems", 2), ("AboutItems", 4) })
        {
            if(type.GetField(source.Item1, Members)?.GetValue(null) is not string[] labels)
                throw new MissingFieldException(type.FullName, source.Item1);
            for(int index = 0; index < labels.Length; index++)
                if(labels[index] != null) routes.Add(new Route(source.Item2, index, labels[index]));
        }
        return routes;
    }

    private static void Navigate(Form form, int category, int index)
    {
        var method = form.GetType().GetMethod("Navigate", Members, null, new[] { typeof(int), typeof(int), typeof(bool) }, null)
            ?? throw new MissingMethodException("MainForm.Navigate(int,int,bool)");
        method.Invoke(form, new object[] { category, index, false });
    }

    private static async Task CoverScrollPages(Form form, RouteVisit visit, string theme, string size, Route route)
    {
        var list = Field(form, "activeList") as MyList;
        var expectedRows = list?.Controls.Cast<Control>().Where(c => c.Visible).ToArray() ?? Array.Empty<Control>();
        var reachedRows = new HashSet<Control>();
        void MarkRows()
        {
            foreach(var row in expectedRows)
                if(VisibleBounds(row).Height >= Math.Min(row.Height, Scale(16, form.DeviceDpi)) && VisibleBounds(row).Width > 0) reachedRows.Add(row);
        }
        MarkRows();
        // Scroll every actual overflowing viewport, including task/settings pages and navigation.
        var viewports = Descendants(form).OfType<ScrollableControl>()
            .Where(c => c.Visible && c.AutoScroll && c.VerticalScroll.Visible).ToArray();
        int viewportIndex = 0;
        foreach(var viewport in viewports)
        {
            viewport.AutoScrollPosition = Point.Empty;
            await Settle();
            int step = Math.Max(1, viewport.ClientSize.Height - Scale(32, form.DeviceDpi));
            int maximum = Math.Max(0, viewport.DisplayRectangle.Height - viewport.ClientSize.Height);
            int page = 0;
            for(int offset = 0; ; offset = (int)Math.Min(maximum, (long)offset + step))
            {
                viewport.AutoScrollPosition = new Point(0, offset);
                await Settle();
                MarkRows();
                visit.Snapshots.Add(Capture(form, theme, size, $"route-{route.Category}-{route.Index}-scroll{viewportIndex}-page{page++}", visit));
                if(offset >= maximum) break;
            }
            viewport.AutoScrollPosition = Point.Empty;
            await Settle();
            viewportIndex++;
        }
        visit.ReachableListItems = reachedRows.Count;
        visit.InitiallyVisibleListItems = expectedRows.Length;
        foreach(var row in expectedRows.Except(reachedRows))
            AddIssue("error", "unreachable-list-row", context, ControlPath(row) + " could not be reached in any actual scroll viewport.");
    }

    private static async Task CheckSearch(Form form, MyList list, TextBox search, RouteVisit visit)
    {
        var rows = list.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
        search.Text = "__Rmemu_desktop_no_match_" + Guid.NewGuid().ToString("N");
        await Task.Delay(420);
        var remaining = rows.Where(c => c.Visible && c.GetType().GetProperty("RegPath", Members) != null).ToArray();
        visit.SearchNoMatchRemainingMenuRows = remaining.Length;
        if(remaining.Length != 0) AddIssue("error", "search-did-not-filter", context, $"{remaining.Length} source-backed rows remained visible for a unique no-match query.");
        search.Clear();
        await Task.Delay(420);
        if(rows.Any(c => !c.IsDisposed && !c.Visible)) AddIssue("error", "search-did-not-restore", context, "Clearing search did not restore initially visible rows.");
    }

    private static async Task CoverModalForms(Form owner, string theme, string size)
    {
        foreach(Type type in AppAssembly.GetTypes().Where(t => typeof(Form).IsAssignableFrom(t) && !t.IsAbstract &&
            (t.Namespace == "Rmemu.Controls" || t.Namespace == "Rmemu.Controls.Interfaces" || t.DeclaringType != null && t != owner.GetType())))
        {
            context = $"{theme}/{size}/modal/{type.FullName}";
            var modal = new ModalVisit { Theme = theme, Dimension = size, FormType = type.FullName ?? type.Name };
            Report.Modals.Add(modal);
            try
            {
                using Form? dialog = CreateModal(type);
                if(dialog == null)
                {
                    modal.Limit = "No reviewed safe constructor. This type was not invoked.";
                    AddIssue("warning", "modal-constructor-uncovered", context, modal.Limit);
                    continue;
                }
                expectedModal = dialog;
                FluentTheme.Apply(dialog);
                PopulateModal(dialog);
                Exception? callbackError = null;
                dialog.Shown += (_, _) => dialog.BeginInvoke((Action)(async () =>
                {
                    try
                    {
                        await Settle();
                        modal.Snapshot = Capture(dialog, theme, size, "modal-" + type.Name, null);
                        modal.Displayed = dialog.Visible;
                        if(!Descendants(dialog).Any(c => c.Visible && c.Width > 0 && c.Height > 0))
                            AddIssue("error", "empty-modal", context, "Actual modal has no visible controls.");
                    }
                    catch(Exception exception) { callbackError = exception; }
                    finally { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); }
                }));
                DialogResult result = dialog.ShowDialog(owner);
                modal.Cancelled = result == DialogResult.Cancel;
                if(callbackError != null) throw callbackError;
                if(!modal.Cancelled) AddIssue("error", "modal-not-cancelled", context, "Fixture modal did not close with Cancel.");
            }
            catch(Exception exception) { AddIssue("error", "modal-exception", context, Unwrap(exception).ToString()); }
            finally { expectedModal = null; }
            SaveReport();
        }
    }

    private static Form? CreateModal(Type type)
    {
        if(type.Name == "MessageBoxForm")
            return (Form?)Activator.CreateInstance(type, Members, null,
                new object[] { "Desktop coverage: harmless message fixture.\r\nLayout and cancellation only.", "Rmemu desktop checks", MessageBoxButtons.OKCancel, MessageBoxIcon.Information, DialogResult.Cancel, false }, null);
        if(type.Name == "ShellStoreForm")
        {
            EnsureFixture();
            return (Form?)Activator.CreateInstance(type, new object[] { FixturePath + @"\shell", (Func<string, bool>)(_ => false), false });
        }
        return type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) == null ? null : (Form?)Activator.CreateInstance(type, nonPublic: true);
    }

    private static void PopulateModal(Form dialog)
    {
        var items = dialog.GetType().GetProperty("Items", Members);
        if(items?.CanWrite == true && items.PropertyType == typeof(string[])) items.SetValue(dialog, new[] { "Desktop fixture", "Second fixture" });
        foreach(string propertyName in new[] { "InputedText", "ItemText", "Arguments", "Command" })
        {
            var property = dialog.GetType().GetProperty(propertyName, Members);
            if(property?.CanWrite == true && property.PropertyType == typeof(string)) property.SetValue(dialog, "Desktop fixture");
        }
        if(dialog.GetType().Name == "DonateListForm")
            dialog.GetType().GetMethod("ShowDonateList", Members)?.Invoke(dialog, new object[] { "|Date|Name|Method|Amount|Note|\n|:--:|:--:|:--:|:--:|:--:|\n|2026-01-01|Fixture|Test|1|Layout only|" });
    }

    private static void EnsureFixture()
    {
        using var key = Registry.CurrentUser.CreateSubKey(FixtureKey + @"\shell\DesktopFixture");
        key.SetValue("MUIVerb", "Desktop fixture — temporary HKCU only");
        using var command = key.CreateSubKey("command");
        command.SetValue("", "desktop-checks-never-execute.exe");
    }

    private static async Task CheckFixtureToggle(Form form)
    {
        context = "fixture-toggle";
        EnsureFixture();
        Type shellList = AppAssembly.GetType("Rmemu.Controls.ShellList", true)!;
        FieldInfo field = shellList.GetField("CurrentCustomRegPath", Members) ?? throw new MissingFieldException("ShellList.CurrentCustomRegPath");
        object? original = field.GetValue(null);
        try
        {
            field.SetValue(null, FixturePath);
            Navigate(form, 2, 5);
            await Settle();
            var list = Field(form, "activeList") as MyList ?? throw new InvalidOperationException("Fixture route did not load a list.");
            Control row = list.Controls.Cast<Control>().Single(c => (Property(c, "RegPath") as string) == FixturePath + @"\shell\DesktopFixture");
            var toggle = Property(row, "ChkVisible") as Control ?? throw new MissingMemberException("Fixture ShellItem.ChkVisible");
            list.Owner?.ScrollControlIntoView(row);
            await Settle();
            if(VisibleBounds(toggle).IsEmpty || !toggle.Enabled) throw new InvalidOperationException("Fixture toggle is not visibly reachable and enabled.");
            MethodInfo userToggle = toggle.GetType().GetMethod("OnUserToggle", Members) ?? throw new MissingMethodException("VisibleCheckBox.OnUserToggle");
            userToggle.Invoke(toggle, null);
            await Settle();
            bool disabled = Registry.GetValue(FixturePath + @"\shell\DesktopFixture", "LegacyDisable", null) != null ||
                Registry.GetValue(FixturePath + @"\shell\DesktopFixture", "ProgrammaticAccessOnly", null) != null;
            if(!disabled) throw new InvalidOperationException("Actual UI toggle handler did not disable the temporary registry fixture.");
            userToggle.Invoke(toggle, null);
            await Settle();
            bool restored = Registry.GetValue(FixturePath + @"\shell\DesktopFixture", "LegacyDisable", null) == null &&
                Registry.GetValue(FixturePath + @"\shell\DesktopFixture", "ProgrammaticAccessOnly", null) == null;
            if(!restored) throw new InvalidOperationException("Actual UI toggle handler did not restore the temporary registry fixture.");
            Report.FixtureTogglePassed = true;
            Report.FixtureToggleMethod = "VisibleCheckBox.OnUserToggle on a real route-loaded ShellItem, disable/read-back/enable/read-back";
            Report.FixtureSnapshot = Capture(form, FluentTheme.Mode, "desktop", "fixture-toggle-restored", null);
        }
        finally { field.SetValue(null, original); }
    }

    private static Snapshot Capture(Form form, string theme, string size, string label, RouteVisit? visit)
    {
        form.PerformLayout();
        form.Update();
        string relative = $"screenshots/{++sequence:D5}-{theme.ToLowerInvariant()}-{size}-{SafeName(label)}.png";
        string file = Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(file, ImageFormat.Png);
        var snapshot = new Snapshot { File = relative, WindowWidth = form.Width, WindowHeight = form.Height, ClientWidth = form.ClientSize.Width, ClientHeight = form.ClientSize.Height };
        var colors = new HashSet<int>();
        for(int y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 80))
            for(int x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 100)) colors.Add(bitmap.GetPixel(x, y).ToArgb());
        snapshot.SampledColors = colors.Count;
        if(colors.Count < 3) AddIssue("error", "blank-screenshot", context, relative + " has fewer than three sampled colors.");
        foreach(Control control in Descendants(form).Where(c => c.Visible))
        {
            Rectangle visible = VisibleBounds(control);
            bool scrollClipped = IsVerticalScrollContent(control);
            bool outside = control.Parent != null && !ContainsWithTolerance(control.Parent.ClientRectangle, control.Bounds, 2);
            var measure = new ControlMeasure
            {
                Path = ControlPath(control), Type = control.GetType().FullName ?? control.GetType().Name,
                Text = control.Text, Enabled = control.Enabled, Bounds = Rect(control.Bounds),
                VisibleBounds = Rect(visible), OutsideParentClient = outside, ScrollContent = scrollClipped
            };
            snapshot.Controls.Add(measure);
            if(outside && control.Parent != null)
            {
                bool allowedScroll = control.Parent is ScrollableControl parentScroll && parentScroll.AutoScroll;
                // A scroll viewport may legitimately clip vertically. Horizontal clipping without
                // an actual horizontal scroll range is a layout failure, including inside lists.
                bool horizontalOverflow = control.Left < -2 || control.Right > control.Parent.ClientSize.Width + 2;
                bool horizontalScrollable = control.Parent is ScrollableControl horizontal && horizontal.AutoScroll && horizontal.HorizontalScroll.Visible;
                if(!allowedScroll || horizontalOverflow && !horizontalScrollable)
                    AddIssue("error", "control-outside-client", context, measure.Path + " " + measure.Bounds + " exceeds parent " + Rect(control.Parent.ClientRectangle));
            }
        }
        Report.ScreenshotFiles.Add(relative);
        return snapshot;
    }

    private static void RequireCritical(Control control, string name, RouteVisit visit)
    {
        Rectangle full = control.RectangleToScreen(control.ClientRectangle);
        Rectangle visible = VisibleBounds(control);
        bool fullyVisible = control.Visible && control.Width > 0 && control.Height > 0 && visible.Width >= full.Width - 2 && visible.Height >= full.Height - 2;
        bool unobscured = fullyVisible && !OccludedAtCenter(control);
        double contrast = Contrast(control.ForeColor, control.BackColor);
        visit.CriticalControls.Add(new CriticalMeasure { Name = name, Visible = fullyVisible, Unobscured = unobscured, Contrast = contrast, Bounds = Rect(full), VisibleBounds = Rect(visible) });
        if(!fullyVisible || !unobscured) AddIssue("error", "critical-control-hidden", context, name + " is hidden, clipped or covered: " + ControlPath(control));
        if(control.Enabled && contrast < 3) AddIssue("error", "critical-low-contrast", context, $"{name} foreground/background contrast is {contrast:F2}:1.");
    }

    private static Rectangle VisibleBounds(Control control)
    {
        if(!control.Visible || control.Width <= 0 || control.Height <= 0) return Rectangle.Empty;
        Rectangle bounds = control.RectangleToScreen(control.ClientRectangle);
        for(Control? parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if(!parent.Visible) return Rectangle.Empty;
            bounds.Intersect(parent.RectangleToScreen(parent.ClientRectangle));
        }
        return bounds;
    }

    private static bool OccludedAtCenter(Control control)
    {
        Rectangle bounds = VisibleBounds(control);
        if(bounds.Width <= 0 || bounds.Height <= 0) return true;
        Point point = new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        for(Control child = control; child.Parent is Control parent; child = parent)
        {
            Control? front = parent.GetChildAtPoint(parent.PointToClient(point), GetChildAtPointSkip.Invisible);
            if(front != null && front != child && !IsAncestor(front, child)) return true;
        }
        return false;
    }

    private static bool IsAncestor(Control ancestor, Control child)
    {
        for(Control? current = child.Parent; current != null; current = current.Parent) if(current == ancestor) return true;
        return false;
    }

    private static bool IsVerticalScrollContent(Control control)
    {
        for(Control? current = control.Parent; current != null; current = current.Parent)
            if(current is ScrollableControl scroll && scroll.AutoScroll && scroll.VerticalScroll.Visible) return true;
        return false;
    }

    private static bool ContainsWithTolerance(Rectangle outer, Rectangle inner, int tolerance)
    {
        outer.Inflate(tolerance, tolerance);
        return outer.Contains(inner);
    }

    private static double Contrast(Color foreground, Color background)
    {
        static double Luminance(Color c)
        {
            static double Linear(byte v) { double x = v / 255.0; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        double first = Luminance(foreground), second = Luminance(background);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private static void BuildContactSheets()
    {
        const int columns = 4, rows = 6, tileWidth = 360, tileHeight = 255;
        for(int start = 0; start < Report.ScreenshotFiles.Count; start += columns * rows)
        {
            using var sheet = new Bitmap(columns * tileWidth, rows * tileHeight);
            using var graphics = Graphics.FromImage(sheet);
            using var font = new Font("Segoe UI", 8);
            graphics.Clear(Color.FromArgb(230, 232, 236));
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int count = Math.Min(columns * rows, Report.ScreenshotFiles.Count - start);
            for(int tile = 0; tile < count; tile++)
            {
                string relative = Report.ScreenshotFiles[start + tile];
                using var image = Image.FromFile(Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar)));
                float scale = Math.Min((tileWidth - 12f) / image.Width, (tileHeight - 38f) / image.Height);
                int left = tile % columns * tileWidth + 6, top = tile / columns * tileHeight + 6;
                graphics.DrawImage(image, new Rectangle(left, top, (int)(image.Width * scale), (int)(image.Height * scale)));
                graphics.DrawString(Path.GetFileNameWithoutExtension(relative), font, Brushes.Black, new RectangleF(left, top + tileHeight - 35, tileWidth - 12, 28));
            }
            string file = $"contact-sheet-{start / (columns * rows) + 1:D3}.png";
            sheet.Save(Path.Combine(output, file), ImageFormat.Png);
            Report.ContactSheets.Add(file);
        }
    }

    private static void CloseUnexpectedDialogs()
    {
        foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray())
        {
            if(dialog == main || dialog == expectedModal || !dialog.Visible || dialog.GetType().FullName == "Rmemu.MainForm") continue;
            if(!DismissedDialogs.Add(dialog.Handle)) continue;
            AddIssue("error", "unexpected-managed-dialog", context, dialog.GetType().FullName + ": " + dialog.Text);
            dialog.DialogResult = DialogResult.Cancel;
            dialog.Close();
        }
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint process);
            if(process != Environment.ProcessId || !IsWindowVisible(window)) return true;
            var className = new StringBuilder(128);
            GetClassName(window, className, className.Capacity);
            if(className.ToString() != "#32770" || !DismissedDialogs.Add(window)) return true;
            var text = new StringBuilder(1024);
            GetWindowText(window, text, text.Capacity);
            AddIssue("error", "unexpected-native-dialog", context, text.ToString());
            // IDCANCEL/WM_CLOSE never approves an operation. Do not send OK to unknown dialogs.
            PostMessage(window, 0x0111, new IntPtr(2), IntPtr.Zero);
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach(Control control in root.Controls)
        {
            yield return control;
            foreach(Control child in Descendants(control)) yield return child;
        }
    }

    private static object? Field(object target, string name) => target.GetType().GetField(name, Members)?.GetValue(target);
    private static object? Property(object target, string name) => target.GetType().GetProperty(name, Members)?.GetValue(target);
    private static string ControlPath(Control control)
    {
        var path = new Stack<string>();
        for(Control? current = control; current != null; current = current.Parent)
            path.Push(current.GetType().Name + "[" + (string.IsNullOrEmpty(current.Name) ? current.Parent?.Controls.IndexOf(current).ToString() : current.Name) + "]");
        return string.Join("/", path);
    }
    private static string Rect(Rectangle r) => $"{r.X},{r.Y},{r.Width},{r.Height}";
    private static string SafeName(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
    private static int Scale(int value, int dpi) => (int)Math.Round(value * dpi / 96.0);
    private static Task Settle() => Task.Delay(90);
    private static Exception Unwrap(Exception exception) => exception is TargetInvocationException wrapped && wrapped.InnerException is Exception inner ? inner : exception;
    private static string? GetArgument(string[] args, string key)
    {
        int index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static void AddIssue(string severity, string code, string location, string message)
    {
        if(!Report.Issues.Any(i => i.Severity == severity && i.Code == code && i.Context == location && i.Message == message))
            Report.Issues.Add(new Issue(severity, code, location, message));
    }
    private static void SaveReport()
    {
        Report.Completed = completed;
        Report.UpdatedUtc = DateTimeOffset.UtcNow;
        Report.ErrorCount = Report.Issues.Count(i => i.Severity == "error");
        Report.WarningCount = Report.Issues.Count(i => i.Severity == "warning");
        string file = Path.Combine(output, "report.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(Report, JsonOptions));
        File.Move(file + ".tmp", file, overwrite: true);
    }

    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}

internal sealed class ReviewReport
{
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public bool Completed { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public string Runtime { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string StartupEntryPoint { get; set; } = "";
    public string Apartment { get; set; } = "";
    public string ConfigDirectory { get; set; } = "";
    public int DeviceDpi { get; set; }
    public bool FixtureTogglePassed { get; set; }
    public string FixtureToggleMethod { get; set; } = "";
    public Snapshot? FixtureSnapshot { get; set; }
    public List<Route> Routes { get; set; } = new();
    public List<RouteVisit> Visits { get; } = new();
    public List<ModalVisit> Modals { get; } = new();
    public List<Issue> Issues { get; } = new();
    public List<string> ScreenshotFiles { get; } = new();
    public List<string> ContactSheets { get; } = new();
    public List<string> Limits { get; } = new();
}
internal sealed record Route(int Category, int Index, string Label);
internal sealed record Issue(string Severity, string Code, string Context, string Message);
internal sealed class RouteVisit
{
    public string Theme { get; set; } = "";
    public string Dimension { get; set; } = "";
    public int Category { get; set; }
    public int Index { get; set; }
    public string Label { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ListType { get; set; }
    public int ListItems { get; set; }
    public int InitiallyVisibleListItems { get; set; }
    public int ReachableListItems { get; set; }
    public bool SearchEnabled { get; set; }
    public int SearchNoMatchRemainingMenuRows { get; set; }
    public List<CriticalMeasure> CriticalControls { get; } = new();
    public List<Snapshot> Snapshots { get; } = new();
}
internal sealed class ModalVisit
{
    public string Theme { get; set; } = "";
    public string Dimension { get; set; } = "";
    public string FormType { get; set; } = "";
    public bool Displayed { get; set; }
    public bool Cancelled { get; set; }
    public string? Limit { get; set; }
    public Snapshot? Snapshot { get; set; }
}
internal sealed class Snapshot
{
    public string File { get; set; } = "";
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
    public int SampledColors { get; set; }
    public List<ControlMeasure> Controls { get; } = new();
}
internal sealed class ControlMeasure
{
    public string Path { get; set; } = "";
    public string Type { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Enabled { get; set; }
    public string Bounds { get; set; } = "";
    public string VisibleBounds { get; set; } = "";
    public bool OutsideParentClient { get; set; }
    public bool ScrollContent { get; set; }
}
internal sealed class CriticalMeasure
{
    public string Name { get; set; } = "";
    public bool Visible { get; set; }
    public bool Unobscured { get; set; }
    public double Contrast { get; set; }
    public string Bounds { get; set; } = "";
    public string VisibleBounds { get; set; } = "";
}
