using BluePointLilac.Methods;
using Rmemu.Methods;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal static partial class Program
{
    /// <summary>Every workspace text key used in code exists in zh-CN and en-US with the same placeholders.</summary>
    private static void WorkspaceStringsAreComplete()
    {
        string root = AppContext.BaseDirectory;
        while(root != null && !File.Exists(System.IO.Path.Combine(root, "Rmemu.sln"))) root = System.IO.Path.GetDirectoryName(root);
        Check(root != null, "repository root found for localization check");
        var keyPattern = new Regex(@"AppString\.W\(\s*""(\w+)""");
        var ternaryPattern = new Regex(@"AppString\.W\([^()""]*\?\s*""(\w+)""\s*:\s*""(\w+)""");
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach(string file in Directory.EnumerateFiles(System.IO.Path.Combine(root, "Rmemu"), "*.cs", SearchOption.AllDirectories))
        {
            if(file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;
            string text = File.ReadAllText(file);
            foreach(Match match in keyPattern.Matches(text)) keys.Add(match.Groups[1].Value);
            foreach(Match match in ternaryPattern.Matches(text)) { keys.Add(match.Groups[1].Value); keys.Add(match.Groups[2].Value); }
        }
        Check(keys.Count > 250, "workspace UI text is looked up through the language dictionary");
        var zh = new IniReader(new StringBuilder(Rmemu.Properties.Resources.AppLanguageDic));
        var en = new IniReader(System.IO.Path.Combine(root, "languages", "en-US.ini"));
        var placeholder = new Regex(@"\{(\d+)\}");
        var missing = new List<string>();
        foreach(string key in keys)
        {
            string zhValue = zh.GetValue("Workspace", key), enValue = en.GetValue("Workspace", key);
            if(string.IsNullOrEmpty(zhValue) || string.IsNullOrEmpty(enValue)) { missing.Add(key); continue; }
            var zhSlots = placeholder.Matches(zhValue).Select(m => m.Value).Distinct().OrderBy(v => v);
            var enSlots = placeholder.Matches(enValue).Select(m => m.Value).Distinct().OrderBy(v => v);
            if(!zhSlots.SequenceEqual(enSlots)) missing.Add(key + " (placeholders differ)");
        }
        Check(missing.Count == 0, "every workspace key exists in zh-CN and en-US with matching placeholders"
            + (missing.Count == 0 ? "" : ": " + string.Join(", ", missing)));
        string bundled = File.ReadAllText(System.IO.Path.Combine(root, "languages", "zh-CN.ini")).Replace("\r\n", "\n").TrimStart('﻿');
        string embedded = Rmemu.Properties.Resources.AppLanguageDic.Replace("\r\n", "\n").TrimStart('﻿');
        Check(bundled == embedded, "languages/zh-CN.ini matches the embedded default dictionary");
        Check(!AppString.W("Overview").StartsWith("[", StringComparison.Ordinal), "workspace lookup resolves a known key");
        Check(AppString.W("NoSuchWorkspaceKey") == "[Workspace.NoSuchWorkspaceKey]", "missing workspace key is visible instead of blank");
        Check(AppString.W("FilterResult", 3, 9).Contains("3") && AppString.W("FilterResult", 3, 9).Contains("9"), "workspace format strings receive arguments");
    }
}
