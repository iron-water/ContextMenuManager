using Microsoft.Win32;
using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Rmemu.Methods
{
    internal sealed class ModernExtensionEntry
    {
        public string Name { get; internal set; }
        public string Package { get; internal set; }
        public string Provider { get; internal set; }
        public string ItemTypes { get; internal set; }
        public Guid ClassId { get; internal set; }
        public string Source { get; internal set; }
        public string ManifestPath { get; internal set; }
        public string RegistryPath { get; internal set; }
        public bool IsPackaged { get; internal set; }
        public bool CanBlock { get; internal set; }
        public string BlockingNote { get; internal set; }
        public string SearchText => string.Join(" ", Name, Package, Provider, ItemTypes,
            ClassId == Guid.Empty ? "" : ClassId.ToString("B"), Source, ManifestPath, RegistryPath);
    }

    internal sealed class ModernExtensionCoverage
    {
        private readonly List<string> warnings = new List<string>();
        public int RegisteredPackages { get; internal set; }
        public int ReadManifests { get; internal set; }
        public int RegistrationSources { get; internal set; }
        public IReadOnlyList<string> Warnings => warnings;
        internal void Warn(string text)
        {
            if(!warnings.Contains(text)) warnings.Add(text);
        }
        public override string ToString() =>
            AppString.W("CoverageSummary", RegisteredPackages, ReadManifests, warnings.Count);
    }

    internal sealed class ModernExtensionCatalog
    {
        internal const string BlockedKey = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
        private const string Repository = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository";
        private const string DesktopNamespace = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/";
        private readonly List<ModernExtensionEntry> entries = new List<ModernExtensionEntry>();
        public IReadOnlyList<ModernExtensionEntry> Entries => entries;
        public ModernExtensionCoverage Coverage { get; } = new ModernExtensionCoverage();

        public static ModernExtensionCatalog Scan()
        {
            var catalog = new ModernExtensionCatalog();
            catalog.ReadPackages();
            catalog.ReadUnpackagedCommands();
            catalog.entries.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
            return catalog;
        }

        private void ReadPackages()
        {
            var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registrations = new List<(RegistryHive Hive, string Path)>
            {
                (RegistryHive.CurrentUser, Repository + @"\Packages"),
                (RegistryHive.CurrentUser, @"Software\Classes\ActivatableClasses\Package")
            };
            using(WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if(identity.User != null)
                    registrations.Add((RegistryHive.LocalMachine,
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\" + identity.User.Value));
            }
            foreach(var registration in registrations)
            {
                try
                {
                    using(RegistryKey hive = RegistryKey.OpenBaseKey(registration.Hive, RegistryView.Default))
                    using(RegistryKey root = hive.OpenSubKey(registration.Path))
                    {
                        if(root == null) continue;
                        Coverage.RegistrationSources++;
                        foreach(string name in root.GetSubKeyNames())
                        {
                            string[] parts = name.Split('_');
                            if(parts.Length < 5) continue;
                            packages.TryAdd(name, null);
                            families.Add(parts[0] + "_" + parts[parts.Length - 1]);
                            try
                            {
                                using(RegistryKey package = root.OpenSubKey(name))
                                {
                                    string location = package?.GetValue("Path") as string
                                        ?? package?.GetValue("PackageRootFolder") as string
                                        ?? package?.GetValue("InstallLocation") as string;
                                    if(!string.IsNullOrWhiteSpace(location)) packages[name] = location;
                                }
                            }
                            catch(Exception ex) when(IsReadError(ex))
                            { Coverage.Warn(AppString.W("WarnPackageInfo", name, ex.Message)); }
                        }
                    }
                }
                catch(Exception ex) when(IsReadError(ex))
                { Coverage.Warn(AppString.W("WarnEnumSource", registration.Hive + "\\" + registration.Path, ex.Message)); }
            }
            try
            {
                using(RegistryKey root = Registry.CurrentUser.OpenSubKey(Repository + @"\Families"))
                {
                    if(root != null)
                    {
                        Coverage.RegistrationSources++;
                        foreach(string name in root.GetSubKeyNames()) families.Add(name);
                    }
                }
            }
            catch(Exception ex) when(IsReadError(ex))
            { Coverage.Warn(AppString.W("WarnFamilies", ex.Message)); }

            bool apiAvailable = true;
            foreach(string family in families)
            {
                if(!apiAvailable) break;
                try
                {
                    foreach(string package in GetFamilyPackages(family)) packages.TryAdd(package, null);
                }
                catch(Exception ex) when(ex is DllNotFoundException || ex is EntryPointNotFoundException)
                {
                    apiAvailable = false;
                    Coverage.Warn(AppString.W("WarnNoApi") + " " + ex.Message);
                }
                catch(Win32Exception ex)
                { Coverage.Warn(AppString.W("WarnFamilyQuery", family, ex.Message)); }
            }
            if(Coverage.RegistrationSources == 0)
                Coverage.Warn(AppString.W("WarnNoSources"));
            // Bundles (resource ID "~") only carry AppxBundleManifest.xml; their installed member packages are listed separately.
            foreach(string bundle in packages.Keys.Where(IsBundle).ToArray()) packages.Remove(bundle);
            Coverage.RegisteredPackages = packages.Count;
            foreach(var package in packages)
            {
                string location = package.Value;
                if(!string.IsNullOrWhiteSpace(location) && location.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    location = Path.GetDirectoryName(location);
                try
                {
                    string apiPath = GetPackageLocation(package.Key);
                    if(!string.IsNullOrWhiteSpace(apiPath)) location = apiPath;
                }
                catch(Win32Exception ex) when(ex.NativeErrorCode == 1168 && !Directory.Exists(location ?? ""))
                {
                    // Stale registration of a version that is no longer installed for this user.
                    Coverage.RegisteredPackages--;
                    continue;
                }
                catch(Exception ex) when(ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is Win32Exception)
                {
                    if(string.IsNullOrWhiteSpace(location))
                    {
                        Coverage.Warn(AppString.W("WarnLocate", package.Key, ex.Message));
                        continue;
                    }
                }
                if(string.IsNullOrWhiteSpace(location))
                {
                    Coverage.Warn(AppString.W("WarnNoPath", package.Key));
                    continue;
                }
                try { ReadManifest(package.Key, Path.Combine(location, "AppxManifest.xml")); }
                catch(Exception ex) when(IsReadError(ex) || ex is XmlException || ex is ArgumentException)
                { Coverage.Warn(AppString.W("WarnManifest", package.Key, ex.Message)); }
            }
        }

        private void ReadManifest(string fullName, string path)
        {
            XDocument document;
            using(var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            })) document = XDocument.Load(reader);
            Coverage.ReadManifests++;
            XElement root = document.Root;
            if(root == null) return;
            XNamespace foundation = root.Name.Namespace;
            XElement identity = root.Element(foundation + "Identity");
            XElement properties = root.Element(foundation + "Properties");
            string identityName = (string)identity?.Attribute("Name");
            string provider = ResolvePackageString(fullName, identityName, (string)properties?.Element(foundation + "PublisherDisplayName"))
                ?? (string)identity?.Attribute("Publisher") ?? AppString.W("NoPublisher");
            string packageName = ResolvePackageString(fullName, identityName, (string)properties?.Element(foundation + "DisplayName"))
                ?? identityName ?? fullName;
            // One verb is usually declared for many item types; show it once with the types merged.
            var merged = new Dictionary<string, ModernExtensionEntry>(StringComparer.OrdinalIgnoreCase);
            foreach(XElement extension in root.Descendants().Where(e => IsDesktop(e, "Extension")
                && (string)e.Attribute("Category") == "windows.fileExplorerContextMenus"))
            {
                foreach(XElement itemType in extension.Descendants().Where(e => IsDesktop(e, "ItemType")))
                {
                    foreach(XElement verb in itemType.Elements().Where(e => IsDesktop(e, "Verb")))
                    {
                        string rawId = (string)verb.Attribute("Clsid");
                        Guid.TryParse(rawId, out Guid classId);
                        if(classId == Guid.Empty)
                            Coverage.Warn(AppString.W("WarnInvalidClsid", fullName, verb.Attribute("Id")?.Value, rawId));
                        string verbName = (string)verb.Attribute("Id") ?? AppString.W("UnnamedMenu");
                        string type = (string)itemType.Attribute("Type") ?? AppString.W("NoItemType");
                        string key = verbName + "|" + classId.ToString("N");
                        if(merged.TryGetValue(key, out ModernExtensionEntry existing))
                        {
                            if(!existing.ItemTypes.Split(new[] { ", " }, StringSplitOptions.None).Contains(type, StringComparer.OrdinalIgnoreCase))
                                existing.ItemTypes += ", " + type;
                            continue;
                        }
                        var entry = new ModernExtensionEntry
                        {
                            Name = verbName,
                            Package = packageName + " (" + fullName + ")",
                            Provider = provider,
                            ItemTypes = type,
                            ClassId = classId,
                            Source = AppString.W("SourceManifest"),
                            ManifestPath = path,
                            RegistryPath = @"HKEY_CLASSES_ROOT\PackagedCom\Package\" + fullName
                                + (classId == Guid.Empty ? "" : @"\Class\" + classId.ToString("B")),
                            IsPackaged = true,
                            CanBlock = false,
                            BlockingNote = AppString.W("PackagedNote")
                        };
                        merged.Add(key, entry);
                        entries.Add(entry);
                    }
                }
            }
        }

        /// <summary>Resolves manifest "ms-resource:" strings from the package's resources.pri (data only, no provider code runs).</summary>
        private static string ResolvePackageString(string fullName, string identityName, string value)
        {
            if(string.IsNullOrWhiteSpace(value)) return null;
            if(!value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return value;
            string id = value.Substring("ms-resource:".Length);
            var candidates = new List<string>();
            if(id.StartsWith("//", StringComparison.Ordinal)) candidates.Add("ms-resource:" + id);
            else if(id.StartsWith("/", StringComparison.Ordinal)) candidates.Add("ms-resource://" + identityName + id);
            else
            {
                candidates.Add("ms-resource://" + identityName + "/Resources/" + id);
                candidates.Add("ms-resource://" + identityName + "/" + id);
            }
            foreach(string candidate in candidates)
            {
                var buffer = new StringBuilder(512);
                try
                {
                    if(SHLoadIndirectString("@{" + fullName + "?" + candidate + "}", buffer, buffer.Capacity, IntPtr.Zero) == 0
                        && buffer.Length > 0 && !buffer.ToString().StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                        return buffer.ToString();
                }
                catch(Exception ex) when(ex is DllNotFoundException || ex is EntryPointNotFoundException) { break; }
            }
            return value; // Unresolvable (e.g. package not installed): keep the identifier visible.
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHLoadIndirectString(string source, StringBuilder output, int outputSize, IntPtr reserved);

        private static bool IsBundle(string fullName)
        {
            string[] parts = fullName.Split('_');
            return parts.Length >= 5 && parts[parts.Length - 2] == "~";
        }

        private static bool IsDesktop(XElement element, string localName)
        {
            string uri = element.Name.NamespaceName;
            return element.Name.LocalName == localName && uri.StartsWith(DesktopNamespace, StringComparison.Ordinal)
                && int.TryParse(uri.Substring(DesktopNamespace.Length), out int version) && version >= 4;
        }

        private void ReadUnpackagedCommands()
        {
            // HKCR is the effective current-user view, including per-user overrides of machine registrations.
            using(RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Default))
            {
                foreach(string root in new[] { "*", "AllFilesystemObjects", "Directory", @"Directory\Background",
                    "Folder", "Drive", "DesktopBackground", "LibraryFolder", @"LibraryFolder\Background" })
                    ReadShell(classes, root + @"\shell", root);
                try
                {
                    using(RegistryKey associations = classes.OpenSubKey("SystemFileAssociations"))
                    {
                        if(associations != null)
                            foreach(string type in associations.GetSubKeyNames())
                                ReadShell(classes, @"SystemFileAssociations\" + type + @"\shell", type);
                    }
                }
                catch(Exception ex) when(IsReadError(ex))
                { Coverage.Warn(AppString.W("WarnSfa", ex.Message)); }
            }
        }

        private void ReadShell(RegistryKey classes, string path, string itemType)
        {
            try
            {
                using(RegistryKey shell = classes.OpenSubKey(path))
                {
                    if(shell == null) return;
                    ReadVerbs(classes, shell, itemType);
                }
            }
            catch(Exception ex) when(IsReadError(ex))
            { Coverage.Warn(AppString.W("WarnCheckHkcr", path, ex.Message)); }
        }

        private void ReadVerbs(RegistryKey classes, RegistryKey parent, string itemType)
        {
            foreach(string name in parent.GetSubKeyNames())
            {
                try
                {
                    using(RegistryKey verb = parent.OpenSubKey(name))
                    {
                        if(verb == null) continue;
                        string rawId = verb.GetValue("ExplorerCommandHandler") as string;
                        if(!string.IsNullOrWhiteSpace(rawId))
                        {
                            Guid.TryParse(rawId, out Guid classId);
                            string provider = AppString.W("UnregisteredCom");
                            bool canBlock = false;
                            if(classId != Guid.Empty)
                            {
                                using(RegistryKey com = classes.OpenSubKey(@"CLSID\" + classId.ToString("B")))
                                using(RegistryKey server = com?.OpenSubKey("InprocServer32"))
                                using(RegistryKey packaged = classes.OpenSubKey(@"PackagedCom\ClassIndex\" + classId.ToString("B")))
                                {
                                    provider = com?.GetValue(null) as string ?? AppString.W("UnnamedCom");
                                    canBlock = packaged == null && !string.IsNullOrWhiteSpace(server?.GetValue(null) as string);
                                }
                            }
                            else Coverage.Warn(AppString.W("WarnHandlerClsid", verb.Name, rawId));
                            entries.Add(new ModernExtensionEntry
                            {
                                Name = verb.GetValue("MUIVerb") as string ?? verb.GetValue(null) as string ?? name,
                                Package = AppString.W("Unpackaged"),
                                Provider = provider,
                                ItemTypes = itemType,
                                ClassId = classId,
                                Source = AppString.W("SourceRegistry"),
                                RegistryPath = verb.Name,
                                CanBlock = canBlock,
                                BlockingNote = canBlock
                                    ? AppString.W("BlockableNote")
                                    : AppString.W("NotBlockableNote")
                            });
                        }
                        using(RegistryKey submenu = verb.OpenSubKey("shell"))
                            if(submenu != null) ReadVerbs(classes, submenu, itemType);
                    }
                }
                catch(Exception ex) when(IsReadError(ex))
                { Coverage.Warn(AppString.W("WarnCheckKey", parent.Name + "\\" + name, ex.Message)); }
            }
        }

        internal static bool IsReadError(Exception ex) => ex is UnauthorizedAccessException
            || ex is SecurityException || ex is IOException;

        private static IEnumerable<string> GetFamilyPackages(string family)
        {
            uint count = 0, length = 0;
            int error = GetPackagesByPackageFamily(family, ref count, null, ref length, IntPtr.Zero);
            if(error == 0) return Array.Empty<string>();
            if(error != 122) throw new Win32Exception(error);
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length * sizeof(char)));
            try
            {
                var pointers = new IntPtr[count];
                error = GetPackagesByPackageFamily(family, ref count, pointers, ref length, buffer);
                if(error != 0) throw new Win32Exception(error);
                var names = new string[count];
                for(int i = 0; i < names.Length; i++) names[i] = Marshal.PtrToStringUni(pointers[i]);
                return names;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static string GetPackageLocation(string fullName)
        {
            uint length = 0;
            int error = GetPackagePathByFullName(fullName, ref length, null);
            if(error != 122) throw new Win32Exception(error);
            var path = new StringBuilder(checked((int)length));
            error = GetPackagePathByFullName(fullName, ref length, path);
            if(error != 0) throw new Win32Exception(error);
            return path.ToString();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetPackagesByPackageFamily(string packageFamilyName, ref uint count,
            [Out] IntPtr[] packageFullNames, ref uint bufferLength, IntPtr buffer);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, StringBuilder path);
    }
}
