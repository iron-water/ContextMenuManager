# Rmemu

A Windows context menu manager based on [iron-water/ContextMenuManager](https://github.com/iron-water/ContextMenuManager) and [BluePointLilac/ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager). Original MIT license and resource credits are retained. See [中文使用说明](README.md) for the current workflow and support boundaries.

This branch targets .NET 10 Windows Forms on Windows 10 / 11. It provides task navigation, light / dark / system appearance, current-list search and status filters, source inspection, local change history with supported undo operations, and Windows 11 modern extension diagnostics. The new navigation and guidance are currently in Chinese; legacy menu dictionaries retain their translations.

```powershell
dotnet build Rmemu.sln -c Release
dotnet run --project Rmemu/Rmemu.csproj
dotnet publish Rmemu/Rmemu.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

The SDK is required to build. Framework-dependent builds require the .NET 10 Desktop Runtime; the self-contained publication includes it. Distribute the full publish directory including `Config`. WinForms Native AOT and trimming are not supported publication modes and are disabled.

Use the home page to select a file and locate relevant menu categories. Select a location, search by name, command or source, inspect an entry, then toggle it. The application reads back the actual state. Supported registry changes have persistent snapshots; undo refuses to overwrite a state changed externally. Permanent deletion and manual command edits are outside general undo coverage.

Modern extension diagnostics read actual package manifests and `ExplorerCommandHandler` registrations without loading third-party handlers. Packaged entries are inspection-only; eligible unpackaged in-process COM handlers can be blocked for the current user. Provider and Windows behavior may vary. Coverage warnings are shown. The scanner does not claim to simulate Explorer's final menu.

The application starts with standard user permissions. Use the More menu to reopen with administrator permission when needed. Browsing does not take registry ownership. Explorer restart asks for confirmation because it can close folder windows or affect file operations. Automatic replacement with upstream executables is disabled for this independently modified branch.

Theme preferences, dictionaries and recovery data are local to the portable `Config` directory or `%AppData%\Rmemu\Config`. Old screenshots in `Screenshot` represent the upstream interface.
