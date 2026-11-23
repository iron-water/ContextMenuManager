using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Microsoft.Win32;
using Rmemu.Controls;
using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]

internal static partial class Program
{
    private static readonly string Fixture = @"Software\Rmemu.Regression\" + Guid.NewGuid().ToString("N");
    private static readonly string Path = @"HKEY_CURRENT_USER\" + Fixture;

    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length == 0) return RunIsolated();
        Directory.CreateDirectory(System.IO.Path.Combine(AppContext.BaseDirectory, "Config"));
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        AppString.LoadStrings();
        FluentTheme.Initialize();
        if(args[0] == "--undo-child")
        {
            bool restored = ChangeHistory.TryUndoLast(out string restoreError);
            if(!restored) Console.Error.WriteLine(restoreError);
            return restored ? 0 : 1;
        }
        if(args[0] == "--invalid-journal-child")
        {
            bool rejected = ChangeHistory.Entries.Count == 0 && ChangeHistory.LastError != null && !ChangeHistory.CanUndo
                && !ChangeHistory.TrySetRegistryValue("must refuse", Path + @"\Rejected", "Value", "never", RegistryValueKind.String, out _);
            return rejected ? 0 : 1;
        }
        if(args[0] == "--instance-owner-child")
        {
            if(SingleInstance.IsRunning()) return 1;
            Console.WriteLine("READY");
            Console.ReadLine();
            return 0;
        }
        if(args[0] == "--instance-contender-child") return SingleInstance.IsRunning() ? 0 : 1;
        if(args[0] == "--journal-writer-child")
        {
            if(args.Length != 2 || !args[1].StartsWith(@"HKEY_CURRENT_USER\Software\Rmemu.Regression\", StringComparison.Ordinal)) return 1;
            return ChangeHistory.TrySetRegistryValue("concurrent child fixture", args[1], "Value", "child", RegistryValueKind.String, out _) ? 0 : 1;
        }
        try
        {
            TrustedRecoveryStoreRejectsUntrustedMetadata();
            ElevatedHistoryIgnoresUserControlledJournal();
            BindingDoesNotWrite();
            NumericRuleBindingDoesNotWrite();
            IniRuleBindingDoesNotWrite();
            StringRuleLabelBindingDoesNotWrite();
            SettingsBindingDoesNotPersistConfiguration();
            VisibilityUndoPreservesData();
            RegistrySecurityIsPreservedByUndoAndMoves();
            ExternalChangesPreventUndo();
            ExtensionMoveIsReversible();
            NoOpVisibilityDoesNotWrite();
            StaleRegistrationsAreNotRecreated();
            ExtensionReadsFollowExternalMoves();
            EnablingPreservesOtherCommandFlags();
            FailedApplyRollsBackCompletedWrites();
            JournalFailureRollsBackChangeAndUndo();
            CreatedAncestorsAreRemovedByUndo();
            DisabledAppearanceStillNormalizesMarkers();
            SnapshotLimitsPreventMutation();
            RegistryCopiesPreserveUnexpandedData();
            DownloadWritesPreserveExistingFiles();
            UpdateScheduleHandlesFutureDates();
            ConfigSelectionHandlesUnwritableDirectories();
            WorkspaceStringsAreComplete();
            ManifestParsingIsNamespacedAndReadOnly();
            UnpackagedScannerRequiresRegisteredInprocClass();
            SingleInstanceReservesStartupBeforeWindowCreation();
            InvalidJournalIsPreserved();
            RunDeletionAndIconRegressions();
            PersistedJournalSurvivesProcessRestart();
            Console.WriteLine("PASS all isolated registry regression scenarios");
            return 0;
        }
        catch(Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            DeleteFixtureTree(Registry.CurrentUser, Fixture);
            if(ChangeHistory.IsElevated && AppConfig.ConfigDir.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            {
                string leaf = System.IO.Path.GetDirectoryName(ChangeHistory.StoragePath);
                if(Directory.Exists(leaf))
                {
                    TrustedRecoveryStore.ValidateDirectory(leaf);
                    Directory.Delete(leaf, true);
                }
            }
        }
    }

        private static void DeleteFixtureTree(RegistryKey parent, string path)
        {
            string name = path.Substring(path.LastIndexOf('\\') + 1);
            using(var key = parent.OpenSubKey(name, RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.FullControl))
            {
                if(key == null) return;
                foreach(string child in key.GetSubKeyNames()) DeleteFixtureTree(key, child);
            }
            parent.DeleteSubKey(name, false);
        }


    private static int RunIsolated()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Rmemu.Regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach(string file in Directory.GetFiles(AppContext.BaseDirectory))
                File.Copy(file, System.IO.Path.Combine(directory, System.IO.Path.GetFileName(file)));
            using(var process = Process.Start(new ProcessStartInfo(System.IO.Path.Combine(directory, "Rmemu.Regression.exe"), "--isolated") { UseShellExecute = false }))
            {
                if(!process.WaitForExit(60000)) { process.Kill(true); throw new TimeoutException("Regression process exceeded 60 seconds."); }
                return process.ExitCode;
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ShellItem NewVerb(string name)
    {
        using(var key = Registry.CurrentUser.CreateSubKey(Fixture + @"\shell\" + name))
        {
            key.SetValue("MUIVerb", "Regression " + name);
            key.SetValue("OriginalBinary", new byte[] { 0, 1, 127, 255 }, RegistryValueKind.Binary);
            key.SetValue("OriginalMulti", new[] { "甲", "beta" }, RegistryValueKind.MultiString);
            using(var command = key.CreateSubKey("command")) command.SetValue("", "notepad.exe \"%1\"");
        }
        return new ShellItem(Path + @"\shell\" + name);
    }

    private static void BindingDoesNotWrite()
    {
        int before = ChangeHistory.Entries.Count;
        using(var item = NewVerb("Bind"))
        using(var host = new MyListBox())
        using(var list = new MyList(host))
        {
            list.AddItem(item);
            Check(item.ChkVisible.Checked, "binding reads existing enabled state");
            Check(Registry.GetValue(item.RegPath, "LegacyDisable", null) == null && Registry.GetValue(item.RegPath, "ProgrammaticAccessOnly", null) == null,
                "binding does not write visibility values");
            Check(ChangeHistory.Entries.Count == before, "binding does not create a change entry");
        }
    }

    private static void VisibilityUndoPreservesData()
    {
        string path;
        using(var item = NewVerb("Visibility"))
        {
            path = item.RegPath;
            Check(ChangeHistory.TryChangeVisibility(item, false, out bool actual, out string error) && !actual, "disable succeeds: " + error);
            Check(!ChangeHistory.ReadVisibility(item), "read back confirms disabled state");
        }
        Check(ChangeHistory.TryUndoLast(out string undoError), "undo works after item disposal: " + undoError);
        Check(Registry.GetValue(path, "LegacyDisable", null) == null && Registry.GetValue(path, "ProgrammaticAccessOnly", null) == null,
            "undo restores absent visibility values");
        Check(((byte[])Registry.GetValue(path, "OriginalBinary", null)).SequenceEqual(new byte[] { 0, 1, 127, 255 }), "undo preserves binary data");
        Check(((string[])Registry.GetValue(path, "OriginalMulti", null)).SequenceEqual(new[] { "甲", "beta" }), "undo preserves multi-string data");
        Check((string)Registry.GetValue(path + "\\command", "", null) == "notepad.exe \"%1\"", "undo preserves command child");
    }

    private static void ExternalChangesPreventUndo()
    {
        using(var item = NewVerb("Conflict"))
        {
            Check(ChangeHistory.TryChangeVisibility(item, false, out _, out string error), "prepare conflict scenario: " + error);
            Registry.SetValue(item.RegPath, "ExternalChange", "keep me");
            Check(!ChangeHistory.TryUndoLast(out string refusal) && refusal.Contains("其他"), "undo refuses external registry changes");
            Check((string)Registry.GetValue(item.RegPath, "ExternalChange", null) == "keep me" && !ChangeHistory.ReadVisibility(item),
                "refused undo preserves external data and disabled state");
            using(var key = Registry.CurrentUser.OpenSubKey(Fixture + @"\shell\Conflict", true)) key.DeleteValue("ExternalChange");
            Check(ChangeHistory.TryUndoLast(out _), "undo succeeds after caller resolves conflict");
        }
    }

    private static void ExtensionMoveIsReversible()
    {
        Guid id = Guid.NewGuid();
        string relative = Fixture + @"\shellex\ContextMenuHandlers\" + id.ToString("B");
        using(var key = Registry.CurrentUser.CreateSubKey(relative))
        {
            key.SetValue("", id.ToString("B"));
            key.SetValue("Preserve", 42, RegistryValueKind.DWord);
        }
        string original = @"HKEY_CURRENT_USER\" + relative;
        string disabled = original.Replace("\\ContextMenuHandlers\\", "\\-ContextMenuHandlers\\");
        using(var item = new ShellExItem(id, original))
            Check(ChangeHistory.TryChangeVisibility(item, false, out bool actual, out string error) && !actual, "extension disable moves registration: " + error);
        Check(Registry.GetValue(original, "", null) == null && (int)Registry.GetValue(disabled, "Preserve", null) == 42, "extension move preserves original content");
        Check(ChangeHistory.TryUndoLast(out string undoError), "extension undo works after disposal: " + undoError);
        Check((int)Registry.GetValue(original, "Preserve", null) == 42 && Registry.GetValue(disabled, "", null) == null, "extension undo restores source and removes disabled copy");
    }

    private static void PersistedJournalSurvivesProcessRestart()
    {
        string relative = Fixture + @"\Persisted";
        string path = @"HKEY_CURRENT_USER\" + relative;
        using(var key = Registry.CurrentUser.CreateSubKey(relative))
        {
            key.SetValue("Value", "before");
            key.SetValue("Expandable", @"%TEMP%\literal", RegistryValueKind.ExpandString);
            key.SetValue("Qword", long.MinValue + 42, RegistryValueKind.QWord);
            using(var child = key.CreateSubKey("MixedCase")) child.SetValue("Binary", new byte[] { 0, 127, 255 }, RegistryValueKind.Binary);
        }
        Check(ChangeHistory.TrySetRegistryValue("persisted fixture", path, "Value", "after", RegistryValueKind.String, out string error), "prepare persistent change: " + error);
        using(var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--undo-child") { UseShellExecute = false }))
        {
            if(!process.WaitForExit(20000)) { process.Kill(true); throw new TimeoutException("Persisted journal child exceeded 20 seconds."); }
            Check(process.ExitCode == 0, ChangeHistory.IsElevated
                ? "second elevated process restores persisted trusted journal" : "second standard process restores persisted journal");
        }
        Check((string)Registry.GetValue(path, "Value", null) == "before", "persisted undo restores original value across restart");
        using(var key = RegistryEx.GetRegistryKey(path))
        {
            Check((string)key.GetValue("Expandable", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == @"%TEMP%\literal"
                && (long)key.GetValue("Qword") == long.MinValue + 42, "persisted undo preserves expandable and signed QWord data");
            using(var child = key.OpenSubKey("mixedcase")) Check(((byte[])child.GetValue("Binary")).SequenceEqual(new byte[] { 0, 127, 255 }), "persisted undo preserves case-insensitive child data");
        }
        string concurrentPath = Path + @"\ConcurrentWriter";
        using(var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--journal-writer-child " + concurrentPath) { UseShellExecute = false }))
        {
            if(!child.WaitForExit(20000)) { child.Kill(true); throw new TimeoutException("Journal writer child exceeded 20 seconds."); }
            Check(child.ExitCode == 0, "independent process publishes a journaled fixture");
        }
        string journal = ChangeHistory.StoragePath;
        byte[] bytes = File.ReadAllBytes(journal);
        Check(!ChangeHistory.TrySetRegistryValue("stale parent must refuse", Path + @"\StaleWriter", "Value", "never", RegistryValueKind.String, out _), "stale cached journal writer refuses mutation");
        Check(File.ReadAllBytes(journal).SequenceEqual(bytes) && Registry.GetValue(Path + @"\StaleWriter", "Value", null) == null, "stale writer preserves other process history and registry");
    }

    private static void NoOpVisibilityDoesNotWrite()
    {
        int entries = ChangeHistory.Entries.Count;
        int notifications = 0;
        EventHandler handler = (sender, e) => notifications++;
        ChangeHistory.Changed += handler;
        try
        {
            using(var item = NewVerb("NoOp"))
            {
                item.ItemVisible = true;
                Check(ChangeHistory.TryChangeVisibility(item, true, out bool actual, out string error) && actual, "same visibility is successful: " + error);
                Check(Registry.GetValue(item.RegPath, "LegacyDisable", null) == null, "same visibility does not add marker values");
            }
            Guid id = Guid.NewGuid();
            string relative = Fixture + @"\shellex\ContextMenuHandlers\NoOp";
            using(var key = Registry.CurrentUser.CreateSubKey(relative)) key.SetValue("", id.ToString("B"));
            using(var extension = new ShellExItem(id, @"HKEY_CURRENT_USER\" + relative)) extension.ItemVisible = true;
            Check(Registry.GetValue(@"HKEY_CURRENT_USER\" + relative, "", null) != null, "same extension visibility does not move registration");
            Check(ChangeHistory.Entries.Count == entries && notifications == 0, "no-op toggles do not journal or notify");
            string absent = Path + @"\NeverCreated";
            Check(ChangeHistory.TrySetRegistryValue("absent delete", absent, "Missing", null, RegistryValueKind.String, out _), "deleting absent value succeeds");
            using(var key = RegistryEx.GetRegistryKey(absent)) Check(key == null, "deleting absent value does not create a key");
        }
        finally { ChangeHistory.Changed -= handler; }
    }

    private static void StaleRegistrationsAreNotRecreated()
    {
        using(var item = NewVerb("Deleted"))
        {
            Registry.CurrentUser.DeleteSubKeyTree(Fixture + @"\shell\Deleted");
            Check(!ChangeHistory.TryChangeVisibility(item, false, out _, out string error) && error != null, "deleted shell registration is refused");
            using(var missing = RegistryEx.GetRegistryKey(item.RegPath)) Check(missing == null, "failed stale toggle does not recreate key");
        }
        Guid id = Guid.NewGuid();
        string relative = Fixture + @"\shellex\ContextMenuHandlers\Replaced";
        using(var key = Registry.CurrentUser.CreateSubKey(relative)) key.SetValue("", id.ToString("B"));
        using(var item = new ShellExItem(id, @"HKEY_CURRENT_USER\" + relative))
        {
            Guid replacement = Guid.NewGuid();
            Registry.SetValue(item.RegPath, "", replacement.ToString("B"));
            Check(!ChangeHistory.TryChangeVisibility(item, false, out _, out _), "replacement extension CLSID is refused");
            Check((string)Registry.GetValue(item.RegPath, "", null) == replacement.ToString("B"), "stale extension does not overwrite replacement");
        }
    }

    private static void ExtensionReadsFollowExternalMoves()
    {
        Guid id = Guid.NewGuid();
        string original = Path + @"\shellex\ContextMenuHandlers\ExternalMove";
        string disabled = Path + @"\shellex\-ContextMenuHandlers\ExternalMove";
        using(var key = RegistryEx.GetRegistryKey(original, true, true)) key.SetValue("", id.ToString("B"));
        using(var item = new ShellExItem(id, original))
        {
            RegistryEx.MoveTo(original, disabled);
            Check(!ChangeHistory.ReadVisibility(item) && item.RegPath == disabled, "extension read follows external move without writing");
            using(var duplicate = RegistryEx.GetRegistryKey(original, true, true)) duplicate.SetValue("", id.ToString("B"));
            Check(!ChangeHistory.TryChangeVisibility(item, true, out _, out _), "ambiguous enabled and disabled copies are refused");
            Check(Registry.GetValue(original, "", null) != null && Registry.GetValue(disabled, "", null) != null, "ambiguous toggle preserves both copies");
        }
    }

    private static void EnablingPreservesOtherCommandFlags()
    {
        using(var item = NewVerb("Flags"))
        {
            Registry.SetValue(item.RegPath, "CommandFlags", 0x38, RegistryValueKind.DWord);
            Check(ChangeHistory.TryChangeVisibility(item, true, out bool actual, out string error) && actual, "enable hidden CommandFlags verb: " + error);
            Check((int)Registry.GetValue(item.RegPath, "CommandFlags", 0) == 0x30, "enable clears only hidden flag and preserves unrelated bits");
            Check(ChangeHistory.TryUndoLast(out _), "flag change is undoable");
            Check((int)Registry.GetValue(item.RegPath, "CommandFlags", 0) == 0x38, "undo restores exact original flag bits");
        }
    }

    private static void FailedApplyRollsBackCompletedWrites()
    {
        string path = Path + @"\AtomicFailure\NewParent\Target";
        int entries = ChangeHistory.Entries.Count;
        var desired = new RegistryHistoryNode();
        desired.Values.Add(new RegistryHistoryValue { Name = "CompletedFirst", Kind = RegistryValueKind.String, Data = new[] { "temporary" } });
        desired.Values.Add(new RegistryHistoryValue { Name = new string('x', 16384), Kind = RegistryValueKind.String, Data = new[] { "invalid value name" } });
        Check(!ChangeHistory.ExecuteRegistryChange("atomic failure fixture", path,
            new[] { new RegistryHistoryChange { Path = path, Before = null, After = desired } }, out string error), "mid-apply native failure is reported");
        Check(error.Contains("恢复"), "failed transaction reports rollback result");
        using(var parent = RegistryEx.GetRegistryKey(Path + @"\AtomicFailure")) Check(parent == null, "failed creation removes completed values and created ancestor keys");
        Check(ChangeHistory.Entries.Count == entries, "failed transaction does not create a history entry");
        string existing = Path + @"\AtomicExisting";
        using(var key = RegistryEx.GetRegistryKey(existing, true, true)) key.SetValue("CompletedFirst", @"%TEMP%\before", RegistryValueKind.ExpandString);
        var original = new RegistryHistoryNode();
        original.Values.Add(new RegistryHistoryValue { Name = "CompletedFirst", Kind = RegistryValueKind.ExpandString, Data = new[] { @"%TEMP%\before" } });
        Check(!ChangeHistory.ExecuteRegistryChange("overwrite failure fixture", existing,
            new[] { new RegistryHistoryChange { Path = existing, Before = original, After = desired } }, out _), "failure after overwriting an existing value is reported");
        using(var key = RegistryEx.GetRegistryKey(existing))
            Check(key.GetValueKind("CompletedFirst") == RegistryValueKind.ExpandString
                && (string)key.GetValue("CompletedFirst", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == @"%TEMP%\before", "rollback restores original value kind and unexpanded data");
    }

    private static void JournalFailureRollsBackChangeAndUndo()
    {
        using(var item = NewVerb("PersistenceFailure"))
        {
            string journal = ChangeHistory.StoragePath;
            int entries = ChangeHistory.Entries.Count;
            byte[] originalJournal = File.ReadAllBytes(journal);
            using(var locked = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(!ChangeHistory.TryChangeVisibility(item, false, out bool actual, out _ ) && actual, "journal replace failure restores enabled visibility");
                Check(ChangeHistory.Entries.Count == entries, "failed journal save removes in-memory entry");
            }
            Check(File.ReadAllBytes(journal).SequenceEqual(originalJournal), "failed journal save preserves previous file");
            Check(ChangeHistory.TryChangeVisibility(item, false, out _, out _), "prepare undo persistence failure");
            ChangeHistoryEntry entry = ChangeHistory.Entries.Last();
            using(var locked = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(!ChangeHistory.TryUndoLast(out _) && !entry.IsUndone, "failed undo save keeps entry undoable");
                Check(!ChangeHistory.ReadVisibility(item), "failed undo save restores disabled state");
            }
            Check(ChangeHistory.TryUndoLast(out _), "undo succeeds after journal lock is released");
        }
    }

    private static void SnapshotLimitsPreventMutation()
    {
        using(var item = NewVerb("TooDeep"))
        {
            string relative = Fixture + @"\shell\TooDeep";
            for(int depth = 0; depth < 25; depth++)
            {
                relative += @"\Child";
                using(var key = Registry.CurrentUser.CreateSubKey(relative)) { }
            }
            int entries = ChangeHistory.Entries.Count;
            Check(!ChangeHistory.TryChangeVisibility(item, false, out bool actual, out _) && actual, "snapshot depth limit refuses mutation");
            Check(Registry.GetValue(item.RegPath, "LegacyDisable", null) == null && ChangeHistory.Entries.Count == entries, "snapshot refusal leaves registry and history unchanged");
        }
    }

    private static void RegistryCopiesPreserveUnexpandedData()
    {
        string source = Path + @"\CopySource";
        string destination = Path + @"\CopyDestination";
        using(var key = RegistryEx.GetRegistryKey(source, true, true)) key.SetValue("Expandable", @"%TEMP%\literal", RegistryValueKind.ExpandString);
        RegistryEx.CopyTo(source, destination);
        using(var key = RegistryEx.GetRegistryKey(destination))
            Check((string)key.GetValue("Expandable", null, RegistryValueOptions.DoNotExpandEnvironmentNames) == @"%TEMP%\literal", "registry copy preserves unexpanded string bytes");
        bool refused = false;
        try { RegistryEx.CopyTo(source, source + @"\Descendant"); }
        catch(IOException) { refused = true; }
        Check(refused, "registry copy refuses descendant recursion");
        refused = false;
        try { RegistryEx.MoveTo(source, destination); }
        catch(IOException) { refused = true; }
        Check(refused && Registry.GetValue(source, "Expandable", null) != null, "registry move refuses existing destination and preserves source");
        using(var lower = RegistryEx.GetRegistryKey("hkcu\\" + Fixture)) Check(lower != null, "registry aliases ignore case");
    }

    private static void DownloadWritesPreserveExistingFiles()
    {
        string file = System.IO.Path.Combine(AppConfig.ConfigDir, "regression-download.xml");
        File.WriteAllText(file, "<Data><Original /></Data>");
        Check(!UAWebClient.WriteDownloadedText(file, "<html>error response</html>", "Data"), "wrong dictionary root is refused");
        Check(!UAWebClient.WriteDownloadedText(file, "<Data>"), "truncated XML is refused");
        Check(File.ReadAllText(file).Contains("Original"), "invalid downloads preserve existing dictionary");
        using(var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool refused = false;
            try { UAWebClient.WriteDownloadedText(file, "<Data><Updated /></Data>"); }
            catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) { refused = true; }
            Check(refused, "locked download destination reports replacement failure");
        }
        Check(File.ReadAllText(file).Contains("Original"), "replacement failure preserves original dictionary");
        Check(UAWebClient.WriteDownloadedText(file, "<Data><Updated /></Data>", "Data") && File.ReadAllText(file).Contains("Updated"), "validated dictionary is atomically published");
        Check(UAWebClient.WriteDownloadedText(file, "<Scene><File /></Scene>", "Scene"), "UWP dictionary retains its distinct Scene schema");
        Check(Directory.GetFiles(AppConfig.ConfigDir, "regression-download.xml.*.tmp").Length == 0, "download staging files are cleaned on success and failure");
        File.Delete(file);
    }

    private static void UpdateScheduleHandlesFutureDates()
    {
        DateTime today = new DateTime(2026, 10, 26);
        Check(!Updater.IsUpdateDue(DateTime.MaxValue, 90, today), "future update date does not overflow");
        Check(!Updater.IsUpdateDue(DateTime.MinValue, -1, today), "disabled update schedule never runs");
        Check(Updater.IsUpdateDue(today.AddDays(-7), 7, today) && !Updater.IsUpdateDue(today.AddDays(-6), 7, today), "update schedule respects exact day boundary");
    }

    private static void ConfigSelectionHandlesUnwritableDirectories()
    {
        string portable = System.IO.Path.Combine(AppConfig.ConfigDir, "portable-probe");
        string roaming = System.IO.Path.Combine(AppConfig.ConfigDir, "roaming-probe");
        Directory.CreateDirectory(portable);
        Check(AppConfig.SelectConfigDirectory(portable, roaming) == portable, "writable portable config is retained");
        Check(Directory.GetFileSystemEntries(portable).Length == 0, "config write probe leaves no files");
        using(var current = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            var directory = new DirectoryInfo(portable);
            var original = System.IO.FileSystemAclExtensions.GetAccessControl(directory);
            var restricted = System.IO.FileSystemAclExtensions.GetAccessControl(directory);
            restricted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(current.User,
                System.Security.AccessControl.FileSystemRights.CreateFiles, System.Security.AccessControl.AccessControlType.Deny));
            try
            {
                System.IO.FileSystemAclExtensions.SetAccessControl(directory, restricted);
                Check(AppConfig.SelectConfigDirectory(portable, roaming) == roaming, "unwritable portable config falls back to roaming");
            }
            finally { System.IO.FileSystemAclExtensions.SetAccessControl(directory, original); }
        }
        Directory.Delete(portable);
    }

    private static void ManifestParsingIsNamespacedAndReadOnly()
    {
        string manifest = System.IO.Path.Combine(AppConfig.ConfigDir, "regression-manifest.xml");
        Guid id = Guid.NewGuid();
        string xml = "<Package xmlns='http://schemas.microsoft.com/appx/manifest/foundation/windows10' xmlns:d='http://schemas.microsoft.com/appx/manifest/desktop/windows10/4' xmlns:fake='urn:unrelated'>"
            + "<Identity Name='Fixture' Publisher='CN=Fixture'/><Properties><DisplayName>ms-resource:Name</DisplayName></Properties><Applications><Application><Extensions>"
            + "<d:Extension Category='windows.fileExplorerContextMenus'><d:FileExplorerContextMenus><d:ItemType Type='*'><d:Verb Id='Valid' Clsid='" + id.ToString("B") + "'/><d:Verb Id='Invalid' Clsid='invalid'/></d:ItemType></d:FileExplorerContextMenus></d:Extension>"
            + "<fake:Extension Category='windows.fileExplorerContextMenus'><fake:ItemType Type='*'><fake:Verb Id='Ignore' Clsid='" + id.ToString("B") + "'/></fake:ItemType></fake:Extension>"
            + "</Extensions></Application></Applications></Package>";
        File.WriteAllText(manifest, xml);
        var catalog = new ModernExtensionCatalog();
        MethodInfo parser = typeof(ModernExtensionCatalog).GetMethod("ReadManifest", BindingFlags.Instance | BindingFlags.NonPublic);
        parser.Invoke(catalog, new object[] { "Fixture_1.0.0.0_x64__publisher", manifest });
        Check(catalog.Entries.Count == 2 && catalog.Entries.Any(entry => entry.ClassId == id), "manifest parser honors desktop namespaces and preserves declared CLSID");
        Check(catalog.Entries.All(entry => entry.IsPackaged && !entry.CanBlock) && catalog.Coverage.Warnings.Count == 1, "packaged and malformed CLSID entries remain read-only with coverage warning");
        Check(catalog.Entries[0].Package.Contains("ms-resource:Name") && File.ReadAllText(manifest) == xml, "unresolvable resource identifiers stay visible and manifest bytes are unchanged");
        File.WriteAllText(manifest, "<!DOCTYPE Package [<!ENTITY unsafe 'expanded'>]><Package>&unsafe;</Package>");
        bool refused = false;
        try { parser.Invoke(catalog, new object[] { "Fixture", manifest }); }
        catch(TargetInvocationException ex) when(ex.InnerException is XmlException) { refused = true; }
        Check(refused, "manifest parser prohibits DTD expansion");
        File.Delete(manifest);
    }

    private static void InvalidJournalIsPreserved()
    {
        string journal = ChangeHistory.StoragePath;
        byte[] original = File.ReadAllBytes(journal);
        const string invalid = "{\"Version\":1,\"Entries\":[{\"IsUndoable\":true,\"View\":256,\"RegistryChanges\":[null]}]}";
        try
        {
            File.WriteAllText(journal, invalid);
            using(var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--invalid-journal-child") { UseShellExecute = false }))
            {
                if(!child.WaitForExit(20000)) { child.Kill(true); throw new TimeoutException("Invalid journal child exceeded 20 seconds."); }
                Check(child.ExitCode == 0, "invalid persisted journal disables writes and undo");
            }
            Check(File.ReadAllText(journal) == invalid, "invalid journal remains byte-for-byte available for recovery");
        }
        finally { File.WriteAllBytes(journal, original); }
    }

    private static void CreatedAncestorsAreRemovedByUndo()
    {
        string path = Path + @"\CreatedForUndo\Nested\Target";
        Check(ChangeHistory.TrySetRegistryValue("created ancestors", path, null, "fixture", RegistryValueKind.String, out string error), "create nested registry value: " + error);
        Check((string)Registry.GetValue(path, "", null) == "fixture", "null registry value name targets default value");
        Check(ChangeHistory.TryUndoLast(out error), "undo new ancestors: " + error);
        using(var ancestor = RegistryEx.GetRegistryKey(Path + @"\CreatedForUndo")) Check(ancestor == null, "undo removes ancestors created by the transaction");
    }

    private static void DisabledAppearanceStillNormalizesMarkers()
    {
        using(var item = NewVerb("DisabledAppearance"))
        {
            Check(ChangeHistory.TryChangeVisibility(item, false, out _, out _), "prepare hidden appearance fixture");
            typeof(ShellItem).GetProperty("ShowAsDisabledIfHidden", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(item, true);
            Check(!ChangeHistory.ReadVisibility(item) && Registry.GetValue(item.RegPath, "LegacyDisable", null) == null
                && Registry.GetValue(item.RegPath, "ProgrammaticAccessOnly", null) == null, "disabled appearance still removes legacy hiding markers while retaining hidden state");
            Check(ChangeHistory.TryUndoLast(out _), "appearance normalization remains reversible");
            using(var key = RegistryEx.GetRegistryKey(item.RegPath, true)) key.DeleteValue("ShowAsDisabledIfHidden");
            Check(ChangeHistory.TryUndoLast(out _), "original hide remains reversible after appearance option is restored");
        }
    }

    private static void UnpackagedScannerRequiresRegisteredInprocClass()
    {
        using(var classes = Registry.CurrentUser.CreateSubKey(Fixture + @"\ClassesFixture"))
        {
            Guid registered = Guid.NewGuid(), packaged = Guid.NewGuid(), missing = Guid.NewGuid();
            using(var server = classes.CreateSubKey(@"CLSID\" + registered.ToString("B") + @"\InprocServer32")) server.SetValue("", "fixture-only.dll");
            using(var server = classes.CreateSubKey(@"CLSID\" + packaged.ToString("B") + @"\InprocServer32")) server.SetValue("", "fixture-only.dll");
            using(var index = classes.CreateSubKey(@"PackagedCom\ClassIndex\" + packaged.ToString("B"))) { }
            foreach(var verb in new[] { (Name: "Registered", Id: registered.ToString("B")), (Name: "Packaged", Id: packaged.ToString("B")),
                (Name: "Missing", Id: missing.ToString("B")), (Name: @"Group\shell\NestedMalformed", Id: "invalid") })
                using(var key = classes.CreateSubKey(@"*\shell\" + verb.Name)) key.SetValue("ExplorerCommandHandler", verb.Id);
            byte[] permissions = classes.GetAccessControl().GetSecurityDescriptorBinaryForm();
            var catalog = new ModernExtensionCatalog();
            MethodInfo parser = typeof(ModernExtensionCatalog).GetMethod("ReadShell", BindingFlags.Instance | BindingFlags.NonPublic);
            parser.Invoke(catalog, new object[] { classes, @"*\shell", "*" });
            Check(catalog.Entries.Count == 4 && catalog.Entries.Single(entry => entry.CanBlock).ClassId == registered,
                "unpackaged scanner permits only registered inproc class without packaged class index");
            Check(catalog.Entries.Any(entry => entry.ClassId == Guid.Empty) && catalog.Coverage.Warnings.Count == 1, "nested malformed command remains visible with a warning");
            Check(classes.GetAccessControl().GetSecurityDescriptorBinaryForm().SequenceEqual(permissions), "scanner does not alter registry ownership or permissions");
            using(var blocked = classes.OpenSubKey(ModernExtensionCatalog.BlockedKey)) Check(blocked == null, "scanner does not create blocking values");
        }
    }

    private static void SingleInstanceReservesStartupBeforeWindowCreation()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath, "--instance-owner-child")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true
        };
        using(var owner = Process.Start(start))
        {
            try
            {
                var ready = owner.StandardOutput.ReadLineAsync();
                if(!ready.Wait(20000)) throw new TimeoutException("Instance owner did not acquire its mutex.");
                Check(ready.Result == "READY", "first process reserves instance before creating a window");
                using(var contender = Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--instance-contender-child") { UseShellExecute = false }))
                {
                    if(!contender.WaitForExit(20000)) { contender.Kill(true); throw new TimeoutException("Instance contender exceeded 20 seconds."); }
                    Check(contender.ExitCode == 0, "concurrent process recognizes reserved instance without a window");
                }
            }
            finally
            {
                if(!owner.HasExited)
                {
                    owner.StandardInput.WriteLine("exit");
                    if(!owner.WaitForExit(5000)) owner.Kill(true);
                }
            }
        }
    }

    private static void NumericRuleBindingDoesNotWrite()
    {
        string path = Path + @"\NumericRules";
        var info = new ItemInfo { Text = "numeric fixture" };
        var rule = new NumberRegRuleItem.RegRule { RegPath = path, ValueName = "MultipleInvokePromptMinimum", ValueKind = RegistryValueKind.DWord, MinValue = 0, MaxValue = 32767, DefaultValue = 15 };
        using(var key = RegistryEx.GetRegistryKey(path, true, true))
        {
            int entries = ChangeHistory.Entries.Count;
            foreach(int original in new[] { 40000, -20, 0, 32767 })
            {
                key.SetValue(rule.ValueName, original, RegistryValueKind.DWord);
                using(var item = new NumberRegRuleItem(rule, info))
                {
                    Check((int)key.GetValue(rule.ValueName) == original, "numeric registry binding preserves stored boundary " + original);
                    Check(item.Controls.Cast<Control>().SelectMany(All).OfType<NumericUpDown>().Single().Value == Math.Clamp(original, 0, 32767), "numeric registry display clamps without writing");
                }
            }
            key.DeleteValue(rule.ValueName);
            using(var item = new NumberRegRuleItem(rule, info))
                Check(key.GetValue(rule.ValueName) == null && item.ItemValue == 15, "missing numeric registry value stays absent when default is displayed");
            key.SetValue(rule.ValueName, long.MaxValue, RegistryValueKind.QWord);
            using(var item = new NumberRegRuleItem(rule, info))
                Check((long)key.GetValue(rule.ValueName) == long.MaxValue && item.ItemValue == 32767, "large QWord is displayed without overflow or kind conversion");
            key.SetValue(rule.ValueName, "malformed", RegistryValueKind.String);
            using(var item = new NumberRegRuleItem(rule, info))
                Check(!item.Controls.Cast<Control>().SelectMany(All).OfType<NumericUpDown>().Single().Enabled && (string)key.GetValue(rule.ValueName) == "malformed", "malformed numeric registry value is disabled and preserved");
            Check(ChangeHistory.Entries.Count == entries, "numeric registry browsing does not journal");
            key.SetValue(rule.ValueName, 40000, RegistryValueKind.DWord);
            using(var item = new NumberRegRuleItem(rule, info))
            {
                item.Controls.Cast<Control>().SelectMany(All).OfType<NumericUpDown>().Single().Value = 123;
                Check((int)key.GetValue(rule.ValueName) == 123 && item.ItemValue == 123, "explicit numeric user edit persists and reads back");
                Check(ChangeHistory.TryUndoLast(out _ ) && (int)key.GetValue(rule.ValueName) == 40000, "numeric edit undo restores original out-of-range value");
                string journal = ChangeHistory.StoragePath;
                using(var locked = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bool failed = false;
                    try { item.ItemValue = 124; }
                    catch(IOException ex) { failed = !string.IsNullOrWhiteSpace(ex.Message); }
                    Check(failed && (int)key.GetValue(rule.ValueName) == 40000, "failed numeric edit reports an error and preserves original registry data");
                }
            }
        }
    }

    private static void IniRuleBindingDoesNotWrite()
    {
        string file = System.IO.Path.Combine(AppConfig.ConfigDir, "numeric-rule-fixture.ini");
        var rule = new NumberIniRuleItem.IniRule { IniPath = file, Section = "Fixture", KeyName = "Value", MinValue = 0, MaxValue = 32767, DefaultValue = 15 };
        var info = new ItemInfo { Text = "INI fixture" };
        using(var absent = new NumberIniRuleItem(rule, info)) Check(!File.Exists(file) && absent.ItemValue == 15, "missing numeric INI file stays absent when default is displayed");
        foreach(string original in new[] { "40000", "-20", "9223372036854775807", "malformed" })
        {
            File.WriteAllText(file, "[Fixture]\r\nValue=" + original + "\r\n; preserve original formatting\r\n");
            byte[] bytes = File.ReadAllBytes(file);
            using(var item = new NumberIniRuleItem(rule, info))
                Check(File.ReadAllBytes(file).SequenceEqual(bytes), "numeric INI binding preserves bytes for " + original);
        }
        File.WriteAllText(file, "[Fixture]\r\nValue=40000\r\n");
        using(var item = new NumberIniRuleItem(rule, info))
        {
            item.Controls.Cast<Control>().SelectMany(All).OfType<NumericUpDown>().Single().Value = 123;
            Check(item.ItemValue == 123 && new IniWriter(file).GetValue("Fixture", "Value") == "123", "explicit numeric INI edit persists and reads back");
            using(var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try { item.ItemValue = 124; }
                catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException) { failed = !string.IsNullOrWhiteSpace(ex.Message); }
                Check(failed && item.ItemValue == 123, "failed numeric INI edit reports an error and retains previous value");
            }
        }
        File.Delete(file);
    }

    private static void StringRuleLabelBindingDoesNotWrite()
    {
        string path = Path + @"\StringRules";
        Registry.SetValue(path, "Text", "original");
        var info = new ItemInfo { Text = "string fixture" };
        using(var item = new StringRegRuleItem(new StringRegRuleItem.RegRule { RegPath = path, ValueName = "Text" }, info))
        {
            Button editor = item.Controls.Cast<Control>().SelectMany(All).OfType<Button>().Single();
            editor.Text = "display only";
            Check(editor.TabStop && !string.IsNullOrWhiteSpace(editor.AccessibleName) && !string.IsNullOrWhiteSpace(editor.AccessibleDescription), "string registry editor exposes keyboard and accessibility semantics");
            Check((string)Registry.GetValue(path, "Text", null) == "original", "string registry label synchronization does not write");
            item.ItemValue = "explicit";
            Check(item.ItemValue == "explicit", "explicit string registry edit reads back");
            Check(ChangeHistory.TryUndoLast(out _), "explicit string registry edit is reversible");
        }
        string file = System.IO.Path.Combine(AppConfig.ConfigDir, "string-rule-fixture.ini");
        File.WriteAllText(file, "[Fixture]\r\nText=original\r\n");
        byte[] bytes = File.ReadAllBytes(file);
        using(var item = new StringIniRuleItem(new StringIniRuleItem.IniRule { IniPath = file, Secation = "Fixture", KeyName = "Text" }, info))
        {
            Button editor = item.Controls.Cast<Control>().SelectMany(All).OfType<Button>().Single();
            editor.Text = "display only";
            Check(editor.TabStop && !string.IsNullOrWhiteSpace(editor.AccessibleName) && !string.IsNullOrWhiteSpace(editor.AccessibleDescription), "string INI editor exposes keyboard and accessibility semantics");
            Check(File.ReadAllBytes(file).SequenceEqual(bytes), "string INI label synchronization does not write");
            item.ItemValue = "explicit";
            Check(item.ItemValue == "explicit", "explicit string INI edit reads back");
        }
        File.Delete(file);
    }

    private static void SettingsBindingDoesNotPersistConfiguration()
    {
        bool existed = File.Exists(AppConfig.ConfigIni);
        byte[] bytes = existed ? File.ReadAllBytes(AppConfig.ConfigIni) : Array.Empty<byte>();
        using(var settings = new AppSettingBox()) settings.LoadItems();
        Check(File.Exists(AppConfig.ConfigIni) == existed && (!existed || File.ReadAllBytes(AppConfig.ConfigIni).SequenceEqual(bytes)), "opening settings does not create or rewrite configuration");
    }

    private static void RegistrySecurityIsPreservedByUndoAndMoves()
    {
        const System.Security.AccessControl.AccessControlSections sections = System.Security.AccessControl.AccessControlSections.Owner
            | System.Security.AccessControl.AccessControlSections.Group | System.Security.AccessControl.AccessControlSections.Access;
        using(var item = NewVerb("Security"))
        using(var key = RegistryEx.GetRegistryKey(item.RegPath, RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.FullControl))
        {
            var security = key.GetAccessControl(sections);
            security.SetAccessRuleProtection(true, true);
            security.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null),
                System.Security.AccessControl.RegistryRights.QueryValues, System.Security.AccessControl.AccessControlType.Allow));
            key.SetAccessControl(security);
            string original = key.GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections);
            Check(ChangeHistory.TryChangeVisibility(item, false, out _, out _) && ChangeHistory.TryUndoLast(out _), "protected DACL fixture supports disable and undo");
            Check(key.GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections) == original, "disable and undo preserve owner group protected DACL and extra ACE");
            string destination = Path + @"\MovedSecurity\Verb";
            RegistryEx.MoveTo(item.RegPath, destination);
            using(var moved = RegistryEx.GetRegistryKey(destination))
                Check(moved.GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections) == original, "cross-parent move preserves exact source security descriptor");
            Check(ChangeHistory.TryUndoLast(out _), "security-preserving move is reversible");
            using(var restored = RegistryEx.GetRegistryKey(item.RegPath))
                Check(restored.GetAccessControl(sections).GetSecurityDescriptorSddlForm(sections) == original, "move undo restores exact security descriptor");
        }
        string source = Path + @"\DeniedMoveSource", destinationPath = Path + @"\DeniedMoveDestination";
        using(var created = RegistryEx.GetRegistryKey(source + @"\DeniedChild", true, true)) { }
        using(var key = RegistryEx.GetRegistryKey(source + @"\DeniedChild", RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.FullControl))
        using(var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            key.SetValue("Original", "preserved");
            var original = key.GetAccessControl(sections);
            var denied = key.GetAccessControl(sections);
            denied.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(identity.User,
                System.Security.AccessControl.RegistryRights.Delete, System.Security.AccessControl.AccessControlType.Deny));
            try
            {
                key.SetAccessControl(denied);
                bool refused = false;
                try { RegistryEx.MoveTo(source, destinationPath); }
                catch(IOException) { refused = true; }
                Check(refused && (string)key.GetValue("Original") == "preserved", "move preflight refuses source subtree lacking restoration permissions");
                using(var missing = RegistryEx.GetRegistryKey(destinationPath)) Check(missing == null, "refused protected move does not leave a destination copy");
            }
            finally { key.SetAccessControl(original); }
        }
    }

    private static void TrustedRecoveryStoreRejectsUntrustedMetadata()
        // Descriptor checks run under either privilege; filesystem creation is exercised elevated.
    {
        var trusted = TrustedRecoveryStore.CreateDirectorySecurity();
        TrustedRecoveryStore.ValidateSecurity(trusted, true);
        Check(trusted.AreAccessRulesProtected, "trusted recovery descriptor has protected inheritance");
        var user = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
        var writable = TrustedRecoveryStore.CreateDirectorySecurity();
        writable.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(user,
            System.Security.AccessControl.FileSystemRights.Modify, System.Security.AccessControl.AccessControlType.Allow));
        bool rejected = false;
        try { TrustedRecoveryStore.ValidateSecurity(writable, true); }
        catch(IOException) { rejected = true; }
        Check(rejected, "trusted recovery rejects an ordinary-user writable ACL");
        var wrongOwner = TrustedRecoveryStore.CreateDirectorySecurity();
        wrongOwner.SetOwner(user);
        rejected = false;
        try { TrustedRecoveryStore.ValidateSecurity(wrongOwner, true); }
        catch(IOException) { rejected = true; }
        Check(rejected, "trusted recovery rejects an untrusted owner");
        string existing = System.IO.Path.Combine(AppConfig.ConfigDir, "untrusted-recovery-fixture");
        const System.Security.AccessControl.AccessControlSections sections = System.Security.AccessControl.AccessControlSections.Owner
            | System.Security.AccessControl.AccessControlSections.Group | System.Security.AccessControl.AccessControlSections.Access;
        Directory.CreateDirectory(existing);
        var directory = new DirectoryInfo(existing);
        string original = System.IO.FileSystemAclExtensions.GetAccessControl(directory, sections).GetSecurityDescriptorSddlForm(sections);
        rejected = false;
        try { TrustedRecoveryStore.EnsureTrustedDirectory(existing); }
        catch(IOException) { rejected = true; }
        Check(rejected && System.IO.FileSystemAclExtensions.GetAccessControl(directory, sections).GetSecurityDescriptorSddlForm(sections) == original,
            "precreated untrusted recovery directory is refused without changing ownership or ACL");
        Directory.Delete(existing);
        if(ChangeHistory.IsElevated)
        {
            string created = System.IO.Path.Combine(AppConfig.ConfigDir, "trusted-recovery-fixture");
            TrustedRecoveryStore.EnsureTrustedDirectory(created);
            TrustedRecoveryStore.ValidateDirectory(created);
            Check(Directory.Exists(created), "elevated recovery directory is created atomically with trusted security");
            Directory.Delete(created);
        }
    }

    private static void ElevatedRejectedStagingFileIsPreserved()
    {
        if(!ChangeHistory.IsElevated) return;
        string staging = ChangeHistory.StoragePath + ".tmp";
        File.WriteAllText(staging, "preserve rejected staging artifact");
        var file = new FileInfo(staging);
        const System.Security.AccessControl.AccessControlSections sections = System.Security.AccessControl.AccessControlSections.Owner
            | System.Security.AccessControl.AccessControlSections.Group | System.Security.AccessControl.AccessControlSections.Access;
        var original = System.IO.FileSystemAclExtensions.GetAccessControl(file, sections);
        var untrusted = System.IO.FileSystemAclExtensions.GetAccessControl(file, sections);
        untrusted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
            System.Security.AccessControl.FileSystemRights.Modify, System.Security.AccessControl.AccessControlType.Allow));
        try
        {
            System.IO.FileSystemAclExtensions.SetAccessControl(file, untrusted);
            string descriptor = System.IO.FileSystemAclExtensions.GetAccessControl(file, sections).GetSecurityDescriptorSddlForm(sections);
            using(var item = NewVerb("RejectedStage"))
            {
                Check(!ChangeHistory.TryChangeVisibility(item, false, out bool actual, out _) && actual, "untrusted elevated staging file refuses save and rolls back registry");
                Check(File.ReadAllText(staging) == "preserve rejected staging artifact"
                    && System.IO.FileSystemAclExtensions.GetAccessControl(file, sections).GetSecurityDescriptorSddlForm(sections) == descriptor,
                    "rejected staging file retains its original content and security descriptor");
            }
        }
        finally
        {
            System.IO.FileSystemAclExtensions.SetAccessControl(file, original);
            File.Delete(staging);
        }
    }

    private static void ElevatedHistoryIgnoresUserControlledJournal()
    {
        string userJournal = System.IO.Path.Combine(AppConfig.BackupDir, "ChangeHistory.json");
        if(!ChangeHistory.IsElevated)
        {
            Check(ChangeHistory.StoragePath == userJournal, "ordinary history keeps its existing user configuration store");
            return;
        }
        bool existed = File.Exists(userJournal);
        byte[] original = existed ? File.ReadAllBytes(userJournal) : null;
        const string untrusted = "{\"Version\":1,\"Entries\":[{\"Description\":\"untrusted-user-journal-fixture\",\"IsUndoable\":false,\"RegistryChanges\":[]}]}";
        try
        {
            File.WriteAllText(userJournal, untrusted);
            Check(ChangeHistory.StoragePath != userJournal && ChangeHistory.Entries.All(entry => entry.Description != "untrusted-user-journal-fixture")
                && ChangeHistory.LastError == null, "elevated history does not import user-controlled legacy journal");
            TrustedRecoveryStore.ValidateDirectory(System.IO.Path.GetDirectoryName(ChangeHistory.StoragePath));
            Check(File.ReadAllText(userJournal) == untrusted, "elevated history leaves untrusted user journal untouched");
        }
        finally
        {
            if(existed) File.WriteAllBytes(userJournal, original);
            else File.Delete(userJournal);
        }
    }

    private static IEnumerable<Control> All(Control control)
    {
        yield return control;
        foreach(Control child in control.Controls)
        foreach(Control nested in All(child))
            yield return nested;
    }

    private static void Check(bool value, string description)
    {
        if(!value) throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
