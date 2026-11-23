# Rmemu

Windows 右键菜单管理工具。基于 [iron-water/ContextMenuManager](https://github.com/iron-water/ContextMenuManager) 与蓝点 lilac 的 [ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager) 改进，保留 MIT 许可证及原作者署名。

现已使用 .NET 10 Windows Forms，提供 Fluent 风格的任务导航、浅色 / 深色 / 跟随系统主题、搜索筛选、菜单来源面板、持久化启停记录与支持范围内的撤销，以及 Windows 11 现代扩展诊断。

## 运行与构建

支持 Windows 10 / 11。开发需要 .NET 10 SDK；框架依赖版本运行需要 .NET 10 Desktop Runtime。旧版 Windows 与 .NET Framework 不再是本分支的运行目标。

```powershell
dotnet build Rmemu.sln -c Release
dotnet run --project Rmemu/Rmemu.csproj
```

发布包含运行时的 x64 版本，目标电脑无需另装 .NET：

```powershell
dotnet publish Rmemu/Rmemu.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

发布后分发整个输出目录；语言、字典等内容位于 `Config`。这是一份自包含的托管桌面应用。WinForms 的 Native AOT / trimming 不属于受支持发布方案，工程明确关闭这两个选项；自包含发布不等于 Native AOT。

在 Windows 上运行独立回归验证：

```powershell
dotnet run --project tests/Rmemu.Regression -c Release
```

该验证在临时目录及独立 HKCU 注册表项中运行，覆盖只读绑定、启停与值类型恢复、ShellEx 移动、外部修改冲突、跨进程恢复；结束后删除自己的数据。

全量桌面检查会启动实际程序，在浅色 / 深色与两档窗口尺寸下遍历全部页面、滚动区域和可安全取消的应用弹窗，输出几何检查、截图及覆盖限制：

```powershell
dotnet run --project tests/Rmemu.DesktopChecks -c Release -- --output artifacts/full-review
```

这需要 Windows 交互式桌面。不会执行真实菜单删除、任意命令、UAC 或资源管理器重启；报告会逐项说明未覆盖部分，不能把这些排除项当作已通过。

主题偏好存于 `Config\Theme.ini`。新导航与说明目前以中文提供；原有菜单字典保留多语言翻译。原生文件对话框、系统消息框及部分原生控件由 Windows 绘制，外观可能与应用内卡片略有差异。

## 第一次使用

1. 不知道项目来自哪里：在概览点击「选择文件定位」，或把文件 / 文件夹拖进窗口。
2. 整理菜单：选择左侧「菜单管理」，再从页面中的位置选择器切换文件、桌面、新建、发送到及高级分类。
3. 搜索名称、命令或来源，也可按启用 / 停用状态筛选。点击项目查看来源，再使用开关启停。
4. 打开「项目操作」查看重命名、导出注册表、修改命令等该项目实际支持的功能。优先使用停用；永久删除不属于通用撤销范围。
5. 在「修改记录与撤销」查看变更。支持恢复的启停操作会保存快照；撤销会核对当前注册表状态，发现外部修改时拒绝覆盖。
6. 菜单未更新时，从「更多」重启资源管理器。会先提示可能关闭文件夹窗口、影响文件操作，确认后才执行。

「文件夹」包括外壳文件夹对象，「目录」指磁盘上的普通目录，「目录背景」是文件夹窗口的空白区域。一个文件的最终右键菜单可能由多个分类共同组成；来源定位并不执行第三方处理程序，也不是 Explorer 最终菜单的完整模拟器。

快捷键：`Ctrl+F` 搜索、`F5` 刷新、`Ctrl+Z` 撤销支持恢复的启停、`Alt+←` 返回、`Esc` 清空搜索。文本编辑框内的 `Ctrl+Z` 保留为文本撤销。

## Windows 11 现代菜单

现代扩展页面读取当前用户应用包注册与真实的 `AppxManifest.xml`，识别 `windows.fileExplorerContextMenus` 声明；同时检查常用菜单位置的 `ExplorerCommandHandler` 注册。可查看包 / 提供程序、文件类型、CLSID 和清单 / 注册位置，不加载或执行第三方 COM 处理程序。

支持范围必须区分：

- 传统 Shell / ShellEx 项仍使用原有注册表机制管理。
- 对符合条件的非打包 COM 扩展，可使用系统 Shell Extensions Blocked 列表管理；实际是否被 Explorer 采纳取决于系统与扩展实现。
- 打包扩展提供来源诊断，不能把普通注册表黑名单当作所有 MSIX 菜单的可靠开关。不会修改应用包清单、注销整个应用或伪造已经停用。
- 无权限读取的包和扫描限制会在页面报告。扫描结果不等于当前文件完整可见菜单；动态条件仍由 Explorer 和扩展决定。

## 配置与恢复

优先使用可写的程序目录 `Config`；该目录不存在或不可写时使用 `%AppData%\Rmemu\Config`。主题设置、菜单字典及备份保存在本地。历史记录包含菜单来源路径与恢复数据，请按本地配置文件管理。

浏览菜单不为读取而更改注册表所有权。默认以标准用户权限运行；管理系统级项目需要相应权限，可从「更多 → 以管理员身份重新打开」请求 Windows 授权。修改受保护项目仍可能失败，界面会核对实际状态。

保留原有文件类型、发送到、打开方式、Win+X、增强菜单、软件规则、GUID 黑名单及旧版 IE 菜单工具。历史工具能否生效取决于对应 Windows / 软件版本。为避免改进后的程序被旧版覆盖，上游可执行文件的自动替换不再作为本分支更新方式；上游资源与版本信息仅作参考。

## 资源与许可证

原有图标、语言与规则字典来自 ContextMenuManager，相关署名保留在源码、语言文件和 [LICENSE](LICENSE)。原项目捐赠记录见 [Donate.md](Donate.md)。旧版截图位于 `Screenshot`，不代表现代化后的界面。
