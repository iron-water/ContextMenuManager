using BluePointLilac.Controls;
using BluePointLilac.Methods;
using Microsoft.Win32;
using Rmemu.Controls;
using Rmemu.Controls.Interfaces;
using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Security.AccessControl;

namespace Rmemu.Methods
{
    internal sealed class ChangeHistoryEntry
    {
        public ChangeHistoryEntry() { }
        public string Description { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string Target { get; set; }
        public bool IsUndoable { get; set; }
        public bool IsUndone { get; set; }
        public string NonUndoableReason { get; set; }
        public RegistryView View { get; set; }
        public List<RegistryHistoryChange> RegistryChanges { get; set; } = new List<RegistryHistoryChange>();
        [JsonIgnore] public bool HasSecuritySnapshots => RegistryChanges.All(change => Complete(change.Before) && Complete(change.After));
        private static bool Complete(RegistryHistoryNode node) => node == null || node.SecurityDescriptor != null && node.Children.Values.All(Complete);
        [JsonIgnore] public bool CanUndo => IsUndoable && !IsUndone && HasSecuritySnapshots;
        [JsonIgnore] public string Status => IsUndone ? AppString.W("StatusUndone")
            : IsUndoable && !HasSecuritySnapshots ? AppString.W("StatusNoSecurity")
            : IsUndoable ? AppString.W("StatusUndoable") : AppString.W("StatusNotUndoable", NonUndoableReason);
        public override string ToString() => $"{Timestamp:yyyy-MM-dd HH:mm:ss}  {Description}  ·  {Status}";
    }

    internal sealed class RegistryHistoryChange
    {
        public RegistryHistoryChange() { }
        public string Path { get; set; }
        public RegistryHistoryNode Before { get; set; }
        public RegistryHistoryNode After { get; set; }
    }

    internal sealed class RegistryHistoryNode
    {
        public RegistryHistoryNode() { }
        public string SecurityDescriptor { get; set; }
        public List<RegistryHistoryValue> Values { get; set; } = new List<RegistryHistoryValue>();
        public SortedDictionary<string, RegistryHistoryNode> Children { get; set; }
            = new SortedDictionary<string, RegistryHistoryNode>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class RegistryHistoryValue
    {
        public RegistryHistoryValue() { }
        public string Name { get; set; }
        public RegistryValueKind Kind { get; set; }
        public string[] Data { get; set; }

        public object GetData()
        {
            switch(Kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString: return Data[0];
                case RegistryValueKind.MultiString: return Data;
                case RegistryValueKind.DWord: return int.Parse(Data[0], CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord: return long.Parse(Data[0], CultureInfo.InvariantCulture);
                case RegistryValueKind.Binary:
                case RegistryValueKind.None: return Convert.FromBase64String(Data[0]);
                default: throw new InvalidDataException(AppString.W("UnsupportedValueKind", Kind));
            }
        }
    }

    // The journal contains data only. It never retains list items, forms, or undo delegates.
    internal static class ChangeHistory
    {
        private sealed class Journal
        {
            public Journal() { }
            public int Version { get; set; } = 1;
            public List<ChangeHistoryEntry> Entries { get; set; } = new List<ChangeHistoryEntry>();
        }

        private sealed class SnapshotBudget
        {
            private int keys;
            private int values;
            private long bytes;
            public void AddKey(int depth)
            {
                if(++keys > 512 || depth > 24) throw new IOException(AppString.W("SnapshotTooDeep"));
            }
            public void AddValue(string name, string[] data)
            {
                bytes += name.Length * 2L + data.Sum(text => text.Length * 2L);
                if(++values > 4096 || bytes > 4 * 1024 * 1024)
                    throw new IOException(AppString.W("SnapshotTooLarge"));
            }
            public void AddSecurity(string descriptor)
            {
                bytes += descriptor.Length * 2L;
                if(bytes > 4 * 1024 * 1024) throw new IOException(AppString.W("SecuritySnapshotTooLarge"));
            }
        }

        private static readonly object Gate = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static string storagePath;
        internal static string StoragePath => storagePath ??= IsElevated ? TrustedRecoveryStore.GetJournalPath(AppConfig.ConfigDir)
            : System.IO.Path.Combine(AppConfig.BackupDir, "ChangeHistory.json");
        private static string JournalPath => StoragePath;
        private static readonly List<ChangeHistoryEntry> History = new List<ChangeHistoryEntry>();
        private static readonly IReadOnlyList<ChangeHistoryEntry> HistoryView = History.AsReadOnly();
        private static bool loaded;
        private static string loadError;
        private static byte[] journalFingerprint;
        internal static bool IsElevated
        {
            get
            {
                using(WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private sealed class JournalLease : IDisposable
        {
            private readonly Mutex mutex;
            public JournalLease()
            {
                string identity = System.IO.Path.GetFullPath(JournalPath).ToUpperInvariant();
                mutex = new Mutex(false, @"Global\Rmemu-Journal-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))));
                try
                {
                    try { if(!mutex.WaitOne(10000)) throw new IOException(AppString.W("JournalBusy")); }
                    catch(AbandonedMutexException) { }
                }
                catch { mutex.Dispose(); throw; }
            }
            public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
        }

        private static void EnsureStorage()
        {
            if(IsElevated) TrustedRecoveryStore.Ensure(AppConfig.ConfigDir);
        }

        private static void EnsureJournalUnchanged()
        {
            EnsureStorage();
            byte[] current = File.Exists(JournalPath) ? SHA256.HashData(File.ReadAllBytes(JournalPath)) : null;
            if(!(current == null && journalFingerprint == null) &&
                (current == null || journalFingerprint == null || !current.SequenceEqual(journalFingerprint)))
                throw new IOException(AppString.W("JournalChanged"));
        }
        private static RegistryView CurrentView => Environment.Is64BitProcess ? RegistryView.Registry64 : RegistryView.Registry32;

        public static event EventHandler Changed;
        public static string LastError { get; private set; }
        public static IReadOnlyList<ChangeHistoryEntry> Entries
        {
            get { lock(Gate) { Load(); return HistoryView; } }
        }
        public static bool CanUndo
        {
            get { lock(Gate) { Load(); return loadError == null && History.Any(entry => entry.CanUndo); } }
        }

        private static void Load()
        {
            if(loaded) return;
            loaded = true;
            try
            {
                EnsureStorage();
                if(!File.Exists(JournalPath)) return;
                if(new FileInfo(JournalPath).Length > 64L * 1024 * 1024)
                    throw new InvalidDataException(AppString.W("JournalTooLarge"));
                byte[] bytes = File.ReadAllBytes(JournalPath);
                Journal journal = JsonSerializer.Deserialize<Journal>(bytes, JsonOptions);
                journalFingerprint = SHA256.HashData(bytes);
                if(journal == null || journal.Version != 1 || journal.Entries == null)
                    throw new InvalidDataException(AppString.W("JournalUnsupported"));
                foreach(ChangeHistoryEntry entry in journal.Entries)
                {
                    if(entry == null || entry.RegistryChanges == null ||
                        (entry.IsUndoable && (entry.RegistryChanges.Count == 0 ||
                            entry.View != RegistryView.Registry32 && entry.View != RegistryView.Registry64)))
                        throw new InvalidDataException(AppString.W("JournalIncomplete"));
                    ValidateChanges(entry.RegistryChanges);
                }
                History.AddRange(journal.Entries);
            }
            catch(Exception ex)
            {
                loadError = AppString.W("JournalUnreadable", JournalPath) + "\r\n" + ex.Message;
                LastError = loadError;
            }
        }

        private static void ValidateNode(RegistryHistoryNode node) => ValidateNode(node, new SnapshotBudget(), 0);

        private static void ValidateNode(RegistryHistoryNode node, SnapshotBudget budget, int depth)
        {
            if(node == null) return;
            budget.AddKey(depth);
            if(node.Values == null || node.Children == null) throw new InvalidDataException(AppString.W("SnapshotIncomplete"));
            if(node.SecurityDescriptor != null)
            {
                var security = new RegistrySecurity();
                security.SetSecurityDescriptorSddlForm(node.SecurityDescriptor, SecuritySections);
                budget.AddSecurity(node.SecurityDescriptor);
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(RegistryHistoryValue value in node.Values)
            {
                if(value == null || value.Name == null || !names.Add(value.Name) || value.Data == null ||
                    value.Data.Any(text => text == null) || value.Kind != RegistryValueKind.MultiString && value.Data.Length != 1)
                    throw new InvalidDataException(AppString.W("SnapshotValuesBad"));
                value.GetData();
                budget.AddValue(value.Name, value.Data);
            }
            node.Values.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
            var children = new SortedDictionary<string, RegistryHistoryNode>(StringComparer.OrdinalIgnoreCase);
            foreach(var child in node.Children)
            {
                if(string.IsNullOrEmpty(child.Key) || child.Key.Contains('\\') || child.Value == null || !children.TryAdd(child.Key, child.Value))
                    throw new InvalidDataException(AppString.W("SnapshotKeysBad"));
                ValidateNode(child.Value, budget, depth + 1);
            }
            node.Children = children;
        }

        private static void Save()
        {
            EnsureJournalUnchanged();
            if(!IsElevated) Directory.CreateDirectory(System.IO.Path.GetDirectoryName(JournalPath));
            string temporaryPath = JournalPath + ".tmp";
            if(IsElevated) TrustedRecoveryStore.ValidateFileIfPresent(temporaryPath);
            try
            {
                using(FileStream stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, new Journal { Entries = History }, JsonOptions);
                    if(stream.Length > 64L * 1024 * 1024) throw new IOException(AppString.W("JournalFull"));
                    stream.Flush(true);
                }
                byte[] writtenFingerprint = SHA256.HashData(File.ReadAllBytes(temporaryPath));
                EnsureJournalUnchanged();
                File.Move(temporaryPath, JournalPath, true);
                journalFingerprint = writtenFingerprint;
            }
            finally
            {
                if(File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static void EnsureAvailable()
        {
            Load();
            if(loadError != null) throw new InvalidOperationException(loadError);
            EnsureJournalUnchanged();
        }

        public static bool ReadVisibility(IChkVisibleItem item)
        {
            if(item is ShellExItem extension)
            {
                string current = extension.RegPath;
                string alternate = AlternateShellExPath(current);
                bool currentExists = KeyExists(current, CurrentView);
                bool alternateExists = KeyExists(alternate, CurrentView);
                if(currentExists == alternateExists)
                    throw new InvalidOperationException(currentExists ? AppString.W("BothLocations") : AppString.W("MenuKeyDeleted"));
                string actualPath = currentExists ? current : alternate;
                using(RegistryKey key = OpenKey(actualPath, false, false, CurrentView))
                {
                    if(key == null) throw new IOException(AppString.W("MenuKeyDeleted"));
                    string registeredClass = key.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                    if(!Guid.TryParse(registeredClass, out Guid registeredGuid)) Guid.TryParse(RegistryEx.GetKeyName(actualPath), out registeredGuid);
                    if(registeredGuid != extension.Guid) throw new IOException(AppString.W("ExtensionClassChanged"));
                }
                if(!currentExists) extension.RegPath = actualPath;
                return !RegistryEx.GetKeyName(RegistryEx.GetParentPath(actualPath)).StartsWith("-", StringComparison.Ordinal);
            }
            if(item is ShellItem shell && !KeyExists(shell.RegPath, CurrentView))
                throw new InvalidOperationException(AppString.W("MenuKeyDeleted"));
            return item.ItemVisible;
        }

        public static bool TryChangeVisibility(IChkVisibleItem item, bool requested, out bool actual, out string error)
            => TryChangeVisibility(item, requested, out actual, out error, false);

        internal static bool TryChangeVisibility(IChkVisibleItem item, bool requested, out bool actual, out string error, bool normalize)
        {
            actual = false;
            error = null;
            bool changed = false;
            lock(Gate)
            {
                try
                {
                    using var lease = new JournalLease();
                    actual = ReadVisibility(item);
                    if(actual == requested && (!normalize || item is not ShellItem)) { LastError = null; return true; }
                    EnsureAvailable();
                    string text = ((MyListItem)item).Text;
                    string description = AppString.W(requested ? "HistoryEnable" : "HistoryDisable", text);
                    if(item is ShellExItem extension)
                    {
                        string source = extension.RegPath;
                        string destination = AlternateShellExPath(source);
                        RegistryHistoryNode original = Capture(source, CurrentView);
                        if(original == null) throw new IOException(AppString.W("MenuKeyDeleted"));
                        changed = ExecuteRegistryChange(description, source, new[]
                        {
                            new RegistryHistoryChange { Path = source, Before = original, After = null },
                            PlanChange(destination, null, CloneNode(original))
                        }, out error);
                        if(changed) extension.RegPath = destination;
                    }
                    else if(item is ShellItem shell)
                    {
                        RegistryHistoryNode original = Capture(shell.RegPath, CurrentView);
                        if(original == null) throw new IOException(AppString.W("MenuKeyDeleted"));
                        changed = ExecuteRegistryChange(description, shell.RegPath, new[]
                        {
                            new RegistryHistoryChange { Path = shell.RegPath, Before = original, After = shell.CreateVisibilitySnapshot(original, requested) }
                        }, out error);
                    }
                    else
                    {
                        int previousCount = History.Count;
                        item.ItemVisible = requested;
                        actual = ReadVisibility(item);
                        if(actual != requested) throw new InvalidOperationException(AppString.W("VisibilityRejected"));
                        if(History.Count == previousCount)
                        {
                            var entry = new ChangeHistoryEntry
                            {
                                Description = description, Timestamp = DateTimeOffset.Now, Target = text,
                                IsUndoable = false, NonUndoableReason = AppString.W("NoSafeRestore")
                            };
                            History.Add(entry);
                            try { Save(); }
                            catch(Exception saveError)
                            {
                                History.Remove(entry);
                                throw new IOException(AppString.W("JournalSaveFailed"), saveError);
                            }
                        }
                        changed = true;
                    }
                    try { actual = ReadVisibility(item); }
                    catch(Exception readError)
                    {
                        error = (error == null ? "" : error + "\r\n") + AppString.W("RereadFailed", readError.Message);
                        changed = false;
                    }
                }
                catch(Exception ex)
                {
                    error = AppString.W("VisibilityIncomplete") + ex.Message;
                    try { actual = ReadVisibility(item); }
                    catch(Exception readError) { error += "\r\n" + AppString.W("RereadFailed", readError.Message); }
                }
                LastError = error;
            }
            if(changed) Changed?.Invoke(null, EventArgs.Empty);
            return changed;
        }

        public static bool TrySetRegistryValue(string description, string path, string valueName,
            object value, RegistryValueKind kind, out string error)
        {
            bool success;
            lock(Gate)
            {
                try
                {
                    using var lease = new JournalLease();
                    EnsureAvailable();
                    ValidatePath(path);
                    valueName ??= "";
                    RegistryHistoryNode original = Capture(path, CurrentView);
                    RegistryHistoryNode desired = CloneNode(original) ?? new RegistryHistoryNode();
                    desired.Values.RemoveAll(candidate => candidate.Name.Equals(valueName, StringComparison.OrdinalIgnoreCase));
                    if(value != null) desired.Values.Add(EncodeValue(valueName, kind, value));
                    desired.Values.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
                    success = ExecuteRegistryChange(description, path, new[]
                    {
                        PlanChange(path, original, original == null && value == null ? null : desired)
                    }, out error);
                }
                catch(Exception ex) { success = false; error = AppString.W("ValueChangeFailed") + ex.Message; }
                LastError = error;
            }
            if(success) Changed?.Invoke(null, EventArgs.Empty);
            return success;
        }

        internal static bool TryMoveRegistration(string source, string destination, bool allowEmptyDestination, out string error)
        {
            bool success;
            lock(Gate)
            {
                try
                {
                    using var lease = new JournalLease();
                    EnsureAvailable();
                    RegistryHistoryNode original = Capture(source, CurrentView);
                    if(original == null) throw new IOException(AppString.W("SourceKeyDeletedAt", source));
                    RegistryHistoryNode existing = Capture(destination, CurrentView);
                    if(existing != null && (!allowEmptyDestination || existing.Values.Count != 0 || existing.Children.Count != 0))
                        throw new IOException(AppString.W("TargetExists", destination));
                    success = ExecuteRegistryChange(AppString.W("HistoryMove", source), source, new[]
                    {
                        new RegistryHistoryChange { Path = source, Before = original, After = null },
                        PlanChange(destination, existing, CloneNode(original))
                    }, out error);
                }
                catch(Exception ex) { success = false; error = AppString.W("MoveFailed") + ex.Message; }
                LastError = error;
            }
            if(success) Changed?.Invoke(null, EventArgs.Empty);
            return success;
        }

        internal static bool TrySwapRegistrations(string first, string second, out string error)
        {
            bool success;
            lock(Gate)
            {
                try
                {
                    using var lease = new JournalLease();
                    EnsureAvailable();
                    RegistryHistoryNode firstNode = Capture(first, CurrentView);
                    RegistryHistoryNode secondNode = Capture(second, CurrentView);
                    if(firstNode == null || secondNode == null) throw new IOException(AppString.W("SwapKeyDeleted"));
                    success = ExecuteRegistryChange(AppString.W("HistorySwap", first, second), first, new[]
                    {
                        new RegistryHistoryChange { Path = first, Before = firstNode, After = CloneNode(secondNode) },
                        new RegistryHistoryChange { Path = second, Before = secondNode, After = CloneNode(firstNode) },
                    }, out error);
                }
                catch(Exception ex) { success = false; error = AppString.W("SwapFailed") + ex.Message; }
                LastError = error;
            }
            if(success) Changed?.Invoke(null, EventArgs.Empty);
            return success;
        }

        internal static bool ExecuteRegistryChange(string description, string target, RegistryHistoryChange[] changes, out string error)
        {
            var entry = new ChangeHistoryEntry
            {
                Description = description, Timestamp = DateTimeOffset.Now, Target = target,
                IsUndoable = true, View = CurrentView, RegistryChanges = changes.ToList()
            };
            error = null;
            var rollback = new Stack<Action>();
            try
            {
                using var lease = new JournalLease();
                EnsureAvailable();
                ValidateChanges(entry.RegistryChanges);
                if(entry.RegistryChanges.All(change => Equal(change.Before, change.After))) return true;
                Apply(entry, false, rollback);
                if(!Matches(entry, false)) throw new IOException(AppString.W("VerifyFailed"));
                foreach(RegistryHistoryChange change in entry.RegistryChanges) change.After = Capture(change.Path, entry.View);
                History.Add(entry);
                try { Save(); }
                catch { History.Remove(entry); throw; }
                return true;
            }
            catch(Exception ex)
            {
                string rollbackError = Rollback(rollback);
                error = AppString.W("ChangeIncomplete") + ex.Message + rollbackError;
                return false;
            }
        }

        public static bool UndoLast()
        {
            bool success = TryUndoLast(out string error);
            if(!success && error != null) AppMessageBox.Show(error);
            return success;
        }

        public static bool TryUndoLast(out string error)
        {
            error = null;
            bool success = false;
            lock(Gate)
            {
                try
                {
                    using var lease = new JournalLease();
                    EnsureAvailable();
                    ChangeHistoryEntry entry = History.LastOrDefault(candidate => candidate.CanUndo);
                    if(entry == null) return false;
                    if(!Matches(entry, false))
                        throw new InvalidOperationException(AppString.W("UndoConflict") + entry.Target);
                    var rollback = new Stack<Action>();
                    try
                    {
                        Apply(entry, true, rollback);
                        if(!Matches(entry, true)) throw new IOException(AppString.W("UndoVerifyFailed"));
                        entry.IsUndone = true;
                        Save();
                    }
                    catch(Exception ex)
                    {
                        entry.IsUndone = false;
                        throw new IOException(ex.Message + Rollback(rollback), ex);
                    }
                    LastError = null;
                    success = true;
                }
                catch(Exception ex) { LastError = AppString.W("UndoFailed") + ex.Message; }
            }
            if(success) Changed?.Invoke(null, EventArgs.Empty);
            error = LastError;
            return success;
        }

        private static bool Matches(ChangeHistoryEntry entry, bool before)
            => entry.RegistryChanges.All(change => Equal(Capture(change.Path, entry.View), before ? change.Before : change.After));

        private static void Apply(ChangeHistoryEntry entry, bool before, Stack<Action> rollback)
        {
            if(!Matches(entry, !before)) throw new IOException(AppString.W("KeyChangedElsewhere"));
            foreach(RegistryHistoryChange change in entry.RegistryChanges)
                PreflightRemoval(change.Path, before ? change.After : change.Before, before ? change.Before : change.After, entry.View);
            // Build the destination before removing a moved source. Every native mutation has
            // a checked inverse, so a failure cannot silently leave an unjournalled partial edit.
            foreach(RegistryHistoryChange change in entry.RegistryChanges.OrderBy(change => (before ? change.Before : change.After) == null))
                ApplyNode(change.Path, before ? change.After : change.Before, before ? change.Before : change.After, entry.View, rollback);
        }

        private static string Rollback(Stack<Action> rollback)
        {
            var errors = new List<string>();
            while(rollback.Count != 0)
            {
                try { rollback.Pop()(); }
                catch(Exception ex) { errors.Add(ex.Message); }
            }
            return errors.Count == 0 ? AppString.W("RolledBack") : AppString.W("PartialRollback") + string.Join("\r\n", errors);
        }

        private static void ApplyNode(string path, RegistryHistoryNode expected, RegistryHistoryNode desired,
            RegistryView view, Stack<Action> rollback)
        {
            if(Equal(expected, desired)) return;
            if(expected == null)
            {
                EnsureKey(path, view, rollback);
                expected = new RegistryHistoryNode { SecurityDescriptor = ReadSecurity(path, view) };
            }
            // Restore security before creating children so descendants inherit the desired DACL.
            if(desired != null && desired.SecurityDescriptor != null && !EqualSecurity(expected.SecurityDescriptor, desired.SecurityDescriptor))
            {
                string originalSecurity = expected.SecurityDescriptor;
                SetCheckedSecurity(path, originalSecurity, desired.SecurityDescriptor, view);
                string appliedSecurity = ReadSecurity(path, view);
                rollback.Push(() =>
                {
                    if(!EqualSecurity(ReadSecurity(path, view), originalSecurity)) SetCheckedSecurity(path, appliedSecurity, originalSecurity, view);
                });
            }
            foreach(RegistryHistoryValue value in expected.Values)
            {
                RegistryHistoryValue replacement = desired?.Values.FirstOrDefault(candidate => candidate.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase));
                ChangeValue(path, value.Name, value, replacement, view, rollback);
            }
            if(desired != null)
                foreach(RegistryHistoryValue value in desired.Values)
                    if(!expected.Values.Any(candidate => candidate.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase)))
                        ChangeValue(path, value.Name, null, value, view, rollback);
            foreach(var child in expected.Children)
            {
                RegistryHistoryNode replacement = null;
                desired?.Children.TryGetValue(child.Key, out replacement);
                ApplyNode(path + "\\" + child.Key, child.Value, replacement, view, rollback);
            }
            if(desired != null)
                foreach(var child in desired.Children)
                    if(!expected.Children.ContainsKey(child.Key)) ApplyNode(path + "\\" + child.Key, null, child.Value, view, rollback);
            if(desired == null)
            {
                QueueRestore(path, expected, view, rollback);
                RemoveEmptyKey(path, view);
            }
        }

        // A deletion pushes one restoration unit; if a later parent deletion also queues,
        // LIFO still restores the parent first and the child sees its inherited ACL.
        private static void QueueRestore(string path, RegistryHistoryNode expected, RegistryView view, Stack<Action> rollback)
        {
            rollback.Push(() =>
            {
                if(KeyExists(path, view))
                {
                    // A parent deletion may already have restored this subtree.
                    if(Equal(Capture(path, view), expected)) return;
                    throw new IOException(AppString.W("KeyRecreated", path));
                }
                using(RegistryKey restored = OpenKey(path, true, true, view)) { }
                if(expected.SecurityDescriptor != null) SetCheckedSecurity(path, ReadSecurity(path, view), expected.SecurityDescriptor, view);
                foreach(RegistryHistoryValue value in expected.Values)
                    SetCheckedValue(path, value.Name, null, value, view);
                foreach(var child in expected.Children)
                    RestoreDeletedNode(path + "\\" + child.Key, child.Value, view);
            });
        }

        private static void RestoreDeletedNode(string path, RegistryHistoryNode node, RegistryView view)
        {
            if(KeyExists(path, view)) throw new IOException(AppString.W("KeyRecreated", path));
            using(RegistryKey created = OpenKey(path, true, true, view)) { }
            if(node.SecurityDescriptor != null) SetCheckedSecurity(path, ReadSecurity(path, view), node.SecurityDescriptor, view);
            foreach(RegistryHistoryValue value in node.Values)
                SetCheckedValue(path, value.Name, null, value, view);
            foreach(var child in node.Children)
                RestoreDeletedNode(path + "\\" + child.Key, child.Value, view);
        }


        private static void EnsureKey(string path, RegistryView view, Stack<Action> rollback)
        {
            if(KeyExists(path, view)) throw new IOException(AppString.W("TargetExists", path));
            string parent = RegistryEx.GetParentPath(path);
            if(parent.Contains('\\') && !KeyExists(parent, view)) EnsureKey(parent, view, rollback);
            using(RegistryKey created = OpenKey(path, true, true, view)) { }
            rollback.Push(() => RemoveEmptyKey(path, view));
        }

        private static void RemoveEmptyKey(string path, RegistryView view)
        {
            using(RegistryKey key = OpenKey(path, false, false, view))
            {
                if(key == null) return;
                if(key.ValueCount != 0 || key.SubKeyCount != 0) throw new IOException(AppString.W("KeyHasExternalData", path));
            }
            using(RegistryKey parent = OpenKey(RegistryEx.GetParentPath(path), true, false, view))
                parent?.DeleteSubKey(RegistryEx.GetKeyName(path), false);
        }

        private static void ChangeValue(string path, string name, RegistryHistoryValue expected,
            RegistryHistoryValue desired, RegistryView view, Stack<Action> rollback)
        {
            if(EqualValue(expected, desired)) return;
            SetCheckedValue(path, name, expected, desired, view);
            rollback.Push(() => SetCheckedValue(path, name, desired, expected, view));
        }

        private static void SetCheckedValue(string path, string name, RegistryHistoryValue expected,
            RegistryHistoryValue desired, RegistryView view)
        {
            using(RegistryKey key = OpenKey(path, true, false, view))
            {
                if(key == null) throw new IOException(AppString.W("KeyDeleted", path));
                RegistryHistoryValue actual = key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)
                    ? EncodeValue(name, key.GetValueKind(name), key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)) : null;
                if(!EqualValue(actual, expected)) throw new IOException(AppString.W("ValueChangedElsewhere", path) + "\\" + name);
                if(desired == null) key.DeleteValue(name, false);
                else key.SetValue(name, desired.GetData(), desired.Kind);
            }
        }

        private static bool EqualValue(RegistryHistoryValue first, RegistryHistoryValue second)
            => ReferenceEquals(first, second) || first != null && second != null && first.Kind == second.Kind
                && first.Name.Equals(second.Name, StringComparison.OrdinalIgnoreCase) && first.Data.SequenceEqual(second.Data);

        private const AccessControlSections SecuritySections = AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access;

        private static string ReadSecurity(string path, RegistryView view)
        {
            using(RegistryKey key = OpenKey(path, false, false, view))
                return key?.GetAccessControl(SecuritySections).GetSecurityDescriptorSddlForm(SecuritySections);
        }

        private static bool EqualSecurity(string first, string second)
        {
            if(first == second) return true;
            if(first == null || second == null) return false;
            var one = new RegistrySecurity();
            var two = new RegistrySecurity();
            one.SetSecurityDescriptorSddlForm(first, SecuritySections);
            two.SetSecurityDescriptorSddlForm(second, SecuritySections);
            if(!Equals(one.GetOwner(typeof(SecurityIdentifier)), two.GetOwner(typeof(SecurityIdentifier)))
                || !Equals(one.GetGroup(typeof(SecurityIdentifier)), two.GetGroup(typeof(SecurityIdentifier)))) return false;
            var oneRules = one.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<RegistryAccessRule>().Select(NormalizeRule).ToArray();
            var twoRules = two.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<RegistryAccessRule>().Select(NormalizeRule).ToArray();
            return oneRules.OrderBy(rule => rule, StringComparer.Ordinal).SequenceEqual(twoRules.OrderBy(rule => rule, StringComparer.Ordinal));
        }

        private static string NormalizeRule(RegistryAccessRule rule)
            => rule.IdentityReference.Value + "|" + rule.RegistryRights + "|" + rule.AccessControlType
                + "|" + rule.InheritanceFlags + "|" + rule.PropagationFlags + "|" + rule.IsInherited;

        private static void SetCheckedSecurity(string path, string expected, string desired, RegistryView view)
        {
            if(!EqualSecurity(ReadSecurity(path, view), expected)) throw new IOException(AppString.W("PermissionsChanged", path));
            RegistryEx.GetRootAndSubRegPath(path, out RegistryKey systemRoot, out string relative);
            using(RegistryKey root = RegistryKey.OpenBaseKey(GetHive(systemRoot.Name), view))
            using(RegistryKey key = root.OpenSubKey(relative, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.FullControl))
            {
                if(key == null) throw new IOException(AppString.W("KeyDeleted", path));
                var original = key.GetAccessControl(SecuritySections);
                var replacement = new RegistrySecurity();
                replacement.SetSecurityDescriptorSddlForm(desired, SecuritySections);
                AccessControlSections changed = AccessControlSections.Access;
                if(!Equals(original.GetOwner(typeof(SecurityIdentifier)), replacement.GetOwner(typeof(SecurityIdentifier)))) changed |= AccessControlSections.Owner;
                if(!Equals(original.GetGroup(typeof(SecurityIdentifier)), replacement.GetGroup(typeof(SecurityIdentifier)))) changed |= AccessControlSections.Group;
                var update = new RegistrySecurity();
                update.SetSecurityDescriptorSddlForm(desired, changed);
                key.SetAccessControl(update);
            }
            if(!EqualSecurity(ReadSecurity(path, view), desired)) throw new IOException(AppString.W("PermissionsVerifyFailed", path));
        }

        private static RegistryHive GetHive(string root) => root switch
        {
            "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot, "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine, "HKEY_USERS" => RegistryHive.Users,
            "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig, _ => throw new InvalidDataException(AppString.W("UnknownHive"))
        };

        private static void PreflightRemoval(string path, RegistryHistoryNode expected, RegistryHistoryNode desired, RegistryView view)
        {
            if(expected == null) return;
            if(desired == null)
            {
                if(expected.SecurityDescriptor == null) throw new IOException(AppString.W("NoSecurityCannotRecreate", path));
                RegistryEx.GetRootAndSubRegPath(path, out RegistryKey systemRoot, out string relative);
                using(RegistryKey root = RegistryKey.OpenBaseKey(GetHive(systemRoot.Name), view))
                using(RegistryKey key = root.OpenSubKey(relative, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.FullControl))
                    if(key == null) throw new IOException(AppString.W("KeyDeleted", path));
            }
            foreach(var child in expected.Children)
            {
                RegistryHistoryNode replacement = null;
                desired?.Children.TryGetValue(child.Key, out replacement);
                PreflightRemoval(path + "\\" + child.Key, child.Value, replacement, view);
            }
        }

        internal static RegistryHistoryNode CloneNode(RegistryHistoryNode node)
        {
            if(node == null) return null;
            var copy = new RegistryHistoryNode { SecurityDescriptor = node.SecurityDescriptor };
            foreach(RegistryHistoryValue value in node.Values)
                copy.Values.Add(new RegistryHistoryValue { Name = value.Name, Kind = value.Kind, Data = (string[])value.Data.Clone() });
            foreach(var child in node.Children) copy.Children.Add(child.Key, CloneNode(child.Value));
            return copy;
        }

        private static RegistryHistoryChange PlanChange(string path, RegistryHistoryNode before, RegistryHistoryNode after)
        {
            // Include only ancestors this operation creates. Undo must not leave empty keys,
            // and must refuse if another application has since added data beneath them.
            if(before == null && after != null)
            {
                string parent = RegistryEx.GetParentPath(path);
                while(parent.Contains('\\') && !KeyExists(parent, CurrentView))
                {
                    var container = new RegistryHistoryNode();
                    container.Children.Add(RegistryEx.GetKeyName(path), after);
                    after = container;
                    path = parent;
                    parent = RegistryEx.GetParentPath(path);
                }
            }
            return new RegistryHistoryChange { Path = path, Before = before, After = after };
        }

        private static RegistryHistoryValue EncodeValue(string name, RegistryValueKind kind, object data)
        {
            string[] encoded;
            switch(kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString: encoded = new[] { (string)data }; break;
                case RegistryValueKind.MultiString: encoded = (string[])((string[])data).Clone(); break;
                case RegistryValueKind.DWord: encoded = new[] { Convert.ToInt32(data, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) }; break;
                case RegistryValueKind.QWord: encoded = new[] { Convert.ToInt64(data, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) }; break;
                case RegistryValueKind.Binary:
                case RegistryValueKind.None: encoded = new[] { Convert.ToBase64String((byte[])data) }; break;
                default: throw new InvalidDataException(AppString.W("UnsupportedValueKind", kind));
            }
            return new RegistryHistoryValue { Name = name, Kind = kind, Data = encoded };
        }

        private static bool Equal(RegistryHistoryNode first, RegistryHistoryNode second)
        {
            if(ReferenceEquals(first, second)) return true;
            if(first == null || second == null || first.Values.Count != second.Values.Count || first.Children.Count != second.Children.Count) return false;
            if(first.SecurityDescriptor != null && second.SecurityDescriptor != null && !EqualSecurity(first.SecurityDescriptor, second.SecurityDescriptor)) return false;
            foreach(RegistryHistoryValue value in first.Values)
                if(!EqualValue(value, second.Values.FirstOrDefault(candidate => candidate.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase)))) return false;
            foreach(var child in first.Children)
            {
                var matching = second.Children.FirstOrDefault(candidate => candidate.Key.Equals(child.Key, StringComparison.OrdinalIgnoreCase));
                if(matching.Value == null || !Equal(child.Value, matching.Value)) return false;
            }
            return true;
        }


        private static bool KeyExists(string path, RegistryView view)
        {
            using(RegistryKey key = OpenKey(path, false, false, view)) return key != null;
        }

        private static RegistryHistoryNode Capture(string path, RegistryView view)
        {
            using(RegistryKey key = OpenKey(path, false, false, view))
                return key == null ? null : Capture(key, new SnapshotBudget(), 0);
        }

        private static RegistryHistoryNode Capture(RegistryKey key, SnapshotBudget budget, int depth)
        {
            budget.AddKey(depth);
            var node = new RegistryHistoryNode { SecurityDescriptor = key.GetAccessControl(SecuritySections).GetSecurityDescriptorSddlForm(SecuritySections) };
            budget.AddSecurity(node.SecurityDescriptor);
            foreach(string name in key.GetValueNames().OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                RegistryValueKind kind = key.GetValueKind(name);
                object data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                string[] encoded;
                switch(kind)
                {
                    case RegistryValueKind.String:
                    case RegistryValueKind.ExpandString: encoded = new[] { (string)data }; break;
                    case RegistryValueKind.MultiString: encoded = (string[])data; break;
                    case RegistryValueKind.DWord: encoded = new[] { ((int)data).ToString(CultureInfo.InvariantCulture) }; break;
                    case RegistryValueKind.QWord: encoded = new[] { ((long)data).ToString(CultureInfo.InvariantCulture) }; break;
                    case RegistryValueKind.Binary:
                    case RegistryValueKind.None: encoded = new[] { Convert.ToBase64String((byte[])data) }; break;
                    default: throw new InvalidDataException(AppString.W("UnsupportedValueKind", kind));
                }
                budget.AddValue(name, encoded);
                node.Values.Add(new RegistryHistoryValue { Name = name, Kind = kind, Data = encoded });
            }
            foreach(string name in key.GetSubKeyNames().OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                using(RegistryKey child = key.OpenSubKey(name))
                {
                    if(child == null) throw new IOException(AppString.W("KeyDeletedWhileReading", name));
                    node.Children.Add(name, Capture(child, budget, depth + 1));
                }
            }
            return node;
        }


        private static string AlternateShellExPath(string path)
        {
            string parent = RegistryEx.GetParentPath(path);
            string part = RegistryEx.GetKeyName(parent);
            string[] parts = part.EndsWith(ShellExItem.DdhParts[0], StringComparison.OrdinalIgnoreCase) ? ShellExItem.DdhParts : ShellExItem.CmhParts;
            if(!parts.Contains(part, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException(AppString.W("ExtensionPathUnknown"));
            string alternate = part.Equals(parts[0], StringComparison.OrdinalIgnoreCase) ? parts[1] : parts[0];
            return RegistryEx.GetParentPath(parent) + "\\" + alternate + "\\" + RegistryEx.GetKeyName(path);
        }

        private static void ValidateChanges(IEnumerable<RegistryHistoryChange> changes)
        {
            var paths = new List<string>();
            foreach(RegistryHistoryChange change in changes)
            {
                if(change == null) throw new InvalidDataException(AppString.W("ChangeRecordIncomplete"));
                ValidatePath(change.Path);
                RegistryEx.GetRootAndSubRegPath(change.Path, out RegistryKey root, out string relative);
                string path = root.Name + "\\" + relative;
                if(paths.Any(existing => existing.Equals(path, StringComparison.OrdinalIgnoreCase)
                    || existing.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(existing + "\\", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(AppString.W("ChangePathsOverlap"));
                paths.Add(path);
                ValidateNode(change.Before);
                ValidateNode(change.After);
            }
        }

        private static void ValidatePath(string path)
        {
            if(string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0 || path.Split('\\').Any(string.IsNullOrWhiteSpace) || path.IndexOf('\\') <= 0)
                throw new InvalidDataException(AppString.W("RecordPathInvalid"));
        }

        private static RegistryKey OpenKey(string path, bool writable, bool create, RegistryView view)
        {
            bool rootOnly = path != null && !path.Contains('\\');
            if(!rootOnly) ValidatePath(path);
            int separator = path.IndexOf('\\');
            string rootName = (rootOnly ? path : path.Substring(0, separator)).ToUpperInvariant();
            RegistryHive hive;
            switch(rootName)
            {
                case "HKEY_CLASSES_ROOT": case "HKCR": hive = RegistryHive.ClassesRoot; break;
                case "HKEY_CURRENT_USER": case "HKCU": hive = RegistryHive.CurrentUser; break;
                case "HKEY_LOCAL_MACHINE": case "HKLM": hive = RegistryHive.LocalMachine; break;
                case "HKEY_USERS": case "HKU": hive = RegistryHive.Users; break;
                case "HKEY_CURRENT_CONFIG": case "HKCC": hive = RegistryHive.CurrentConfig; break;
                default: throw new InvalidDataException(AppString.W("UnknownHive"));
            }
            using(RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
            {
                string subkey = rootOnly ? "" : path.Substring(separator + 1);
                return create ? root.CreateSubKey(subkey, writable) : root.OpenSubKey(subkey, writable);
            }
        }
    }
}
