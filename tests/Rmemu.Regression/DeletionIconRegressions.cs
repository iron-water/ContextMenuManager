using BluePointLilac.Methods;
using Microsoft.Win32;
using Rmemu.Controls;
using Rmemu.Methods;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class Program
{
    private static void RunDeletionAndIconRegressions()
    {
        ShellExDeletionPreservesOppositeRegistration(false);
        ShellExDeletionPreservesOppositeRegistration(true);
        RegistryExportCommitsOnlyVerifiedBackups();
        ResourceIconMapsLibrariesOnlyAsResources();
        BackupNamesRemovePathsQuotesAndControls();
    }

    private static void ShellExDeletionPreservesOppositeRegistration(bool selectedDisabled)
    {
        string scenario = selectedDisabled ? "DeleteDisabled" : "DeleteEnabled";
        string root = Path + @"\" + scenario + @"\shellex";
        string selected = root + (selectedDisabled ? @"\-ContextMenuHandlers\SameName" : @"\ContextMenuHandlers\SameName");
        string opposite = root + (selectedDisabled ? @"\ContextMenuHandlers\SameName" : @"\-ContextMenuHandlers\SameName");
        Guid selectedGuid = Guid.NewGuid();
        Guid otherGuid = Guid.NewGuid();
        using(var key = RegistryEx.GetRegistryKey(selected, true, true)) key.SetValue("", selectedGuid.ToString("B"));
        using(var key = RegistryEx.GetRegistryKey(opposite, true, true))
        {
            key.SetValue("", otherGuid.ToString("B"));
            key.SetValue("Preserve", new byte[] { 1, 17, 255 }, RegistryValueKind.Binary);
            using(var child = key.CreateSubKey("Child")) child.SetValue("Value", "unselected data");
        }
        using(var item = new ShellExItem(selectedGuid, selected)) item.DeleteMe();
        using(var key = RegistryEx.GetRegistryKey(selected)) Check(key == null, scenario + " removes the selected registration");
        Check((string)Registry.GetValue(opposite, "", null) == otherGuid.ToString("B")
            && ((byte[])Registry.GetValue(opposite, "Preserve", null)).SequenceEqual(new byte[] { 1, 17, 255 })
            && (string)Registry.GetValue(opposite + @"\Child", "Value", null) == "unselected data",
            scenario + " preserves a same-name opposite registration with another CLSID and its complete data");
    }

    private static void RegistryExportCommitsOnlyVerifiedBackups()
    {
        string source = Path + @"\Export fixture";
        using(var key = RegistryEx.GetRegistryKey(source, true, true)) key.SetValue("Preserve", "registry source remains");
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Rmemu.Export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string output = System.IO.Path.Combine(directory, "备份 with spaces.reg");
            File.WriteAllText(output, "previous backup", Encoding.UTF8);
            byte[] original = File.ReadAllBytes(output);
            bool rejected = false;
            try { ExternalProgram.ExportRegistry(source + @"\Missing", output); }
            catch(IOException ex) { rejected = ex.Message.Contains(source + @"\Missing") && ex.Message.Contains(output); }
            Check(rejected && File.ReadAllBytes(output).SequenceEqual(original), "missing registry source throws a useful error and preserves an existing backup");

            using(var locked = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                rejected = false;
                try { ExternalProgram.ExportRegistry(source, output); }
                catch(IOException) { rejected = true; }
                Check(rejected && File.ReadAllBytes(output).SequenceEqual(original), "locked export destination rejects replacement without damaging its original contents");
            }
            rejected = false;
            try { ExternalProgram.ExportRegistry(source, System.IO.Path.Combine(directory, "Absent", "backup.reg")); }
            catch(IOException) { rejected = true; }
            Check(rejected && File.ReadAllBytes(output).SequenceEqual(original), "missing destination directory fails without changing an existing backup");
            Check((string)Registry.GetValue(source, "Preserve", null) == "registry source remains", "all failed exports leave the source registration unchanged");
            Check(!Directory.GetFiles(directory, ".rmemu-export-*").Any(), "failed exports remove their staged files");

            ExternalProgram.ExportRegistry(source, output);
            string exported = File.ReadAllText(output, Encoding.Unicode);
            Check(exported.StartsWith("Windows Registry Editor Version 5.00\r\n", StringComparison.Ordinal)
                && exported.Contains("[" + source + "]", StringComparison.OrdinalIgnoreCase)
                && exported.Contains("\"Preserve\"=\"registry source remains\"", StringComparison.Ordinal),
                "successful export replaces the destination with a real registry header, requested key, and source values");
            Check(!Directory.GetFiles(directory, ".rmemu-export-*").Any(), "successful export leaves no staged file");
        }
        finally { Directory.Delete(directory, true); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetIconFixtureModuleHandle(string moduleName);

    [DllImport("kernel32.dll", EntryPoint = "FreeLibrary")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeIconFixtureModule(IntPtr module);

    private static void ResourceIconMapsLibrariesOnlyAsResources()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Rmemu.Icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string fixture = System.IO.Path.Combine(directory, "benign-resource-fixture.dll");
            File.Copy(System.IO.Path.Combine(Environment.SystemDirectory, "version.dll"), fixture);
            Check(GetIconFixtureModuleHandle(fixture) == IntPtr.Zero, "benign icon fixture begins outside the executable module list");
            MethodInfo loader = typeof(ResourceIcon).GetMethod("LoadIconResourceModule", BindingFlags.Static | BindingFlags.NonPublic);
            Check(loader != null, "icon extraction exposes its resource mapping implementation to behavioral regression coverage");
            IntPtr module = (IntPtr)loader.Invoke(null, new object[] { fixture });
            try
            {
                Check(module != IntPtr.Zero && (module.ToInt64() & 3) != 0, "icon resource mapping returns a tagged data/image-resource handle");
                Check(GetIconFixtureModuleHandle(fixture) == IntPtr.Zero, "resource mapping does not register or initialize the benign DLL as executable code");
            }
            finally { if(module != IntPtr.Zero) FreeIconFixtureModule(module); }
            using(var icon = ResourceIcon.GetIcon(fixture, -1)) { }
            using(var icon = ResourceIcon.GetIcon(fixture, int.MinValue))
                Check(icon == null, "missing icon resource IDs return no icon");
            Check(GetIconFixtureModuleHandle(fixture) == IntPtr.Zero, "icon extraction leaves no executable fixture module loaded");
            string missing = System.IO.Path.Combine(directory, "missing.dll");
            Check(ResourceIcon.GetIcon(missing, -1) == null, "missing library resource ID returns no icon without reading process resources");
            Check(ResourceIcon.GetIcon(missing, 0) == null, "missing library icon index returns no icon");
            string invalid = System.IO.Path.Combine(directory, "invalid.dll");
            File.WriteAllText(invalid, "This is a benign file, not a PE image.");
            Check(ResourceIcon.GetIcon(invalid, -1) == null, "invalid resource image returns no icon");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void BackupNamesRemovePathsQuotesAndControls()
    {
        string name = ObjectPath.RemoveIllegalChars("..\\folder/\"quoted\":name\r\n\t\0\u007f|*?<> ");
        Check(!name.Any(c => char.IsControl(c) || System.IO.Path.GetInvalidFileNameChars().Contains(c)),
            "backup filename sanitization removes path separators, quotes, and all control characters");
        Check(System.IO.Path.GetFileName(name) == name, "sanitized backup text remains a single filename component");
    }
}
