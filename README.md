# GTSWindowMod

Giantess Sandbox 的 **Windows 原生窗口增强插件**，适用于 BepInEx 5 / Unity 2019.4。纯鼠标操作，没有 Mod 快捷键、提示浮层或无边框返回按钮。

[下载最新 Release](https://github.com/KawakazeNotFound/GTSWindowMod/releases/latest) · [构建状态](https://github.com/KawakazeNotFound/GTSWindowMod/actions/workflows/build-and-release.yml)

## 功能与操作

| 模式 / 操作 | 行为 |
|---|---|
| 拖动边框 / 四角 | 自由缩放普通窗口；最大化时先点还原 |
| 标题栏最大化 / 还原、双击标题栏 | 原生 Windows 最大化，占用显示器工作区，给任务栏留出空间 |
| 拖动标题栏 | 移动窗口 |
| 游戏设置 Window Mode → Borderless | Mod 无边框全屏，铺满当前显示器 |
| 游戏设置 Window Mode → Fullscreen | 交还游戏原生全屏与分辨率流程，Mod 暂停尺寸/边框维护 |
| 游戏设置切回 Windowed / Borderless 并应用 | 重新接管窗口 |
| 右键标题栏 | 无边框全屏、重置窗口位置 |

普通窗口的位置、大小与最近模式会保存。全屏/最大化尺寸与普通窗口还原矩形分开保存，避免还原成全屏大小。默认首次启动为原生最大化。

### 与原生设置配合

- **Window Mode 始终保留可选**，注明 Fullscreen 由游戏管理，其他模式由 Mod 管理。
- Mod 模式下，Window Resolution 与 Try Resolution 灰显并注明接管。选择 Fullscreen 后恢复原生分辨率控件，点击原有应用按钮才实际交还管理权。
- 画质、VSync、FOV、Cancel、Apply & Save 保留原流程；仅隔离 Mod 接管的窗口修改分支。
- 没有额外屏幕按钮；退出无边框使用游戏设置的 Window Mode。

## 安装 / 更新

1. 在 Windows 游戏目录安装 BepInEx 5（5.4.21+；本地预览测试环境为 5.4.23.5）。
2. 关闭游戏，下载 Release 的 `GTSWindowMod-v1.0.0.zip`。
3. 将 ZIP 内的 `BepInEx` 目录合并到游戏目录，最终文件为：

```text
<游戏目录>/BepInEx/plugins/GTSWindowMod/GTSWindowMod.dll
```

首次运行生成独立配置：`BepInEx/config/com.giantess.windowmod.cfg`。

**从 GiantessWindowMod 预览版更新：** 先移除旧的 `GiantessWindowMod.dll`，只保留一份插件。为保留窗口记忆与配置兼容，GUID 继续使用 `com.giantess.windowmod`，无需改名或重建配置文件。

插件未附带游戏、Unity、BepInEx 或 Harmony DLL。卸载时关闭游戏并移除本插件目录即可；配置可保留。游戏自身可能保留上次窗口尺寸，插件不会清理游戏 PlayerPrefs。

## 配置

| 设置 | 默认值 | 说明 |
|---|---|---|
| General.Enabled | true | 启用插件，更改后重启 |
| General.Mode | Maximized | Windowed / Maximized / Borderless / Fullscreen，记忆最近模式 |
| General.RememberWindow | true | 保存正常窗口位置、大小与模式 |
| General.KeepWindowMode | true | 维护 Mod 模式的边框/布局；原生全屏始终交还游戏 |
| Window.MinimumClientWidth / Height | 640 / 360 | 拖动时的最小客户区尺寸，更改后重启 |

旧配置里的快捷键或 ShowStatusToast 条目即使残留也不再读取。插件保留游戏自身鼠标锁定规则，不绑定键盘快捷键。

## 构建 / 测试

需要 Windows、.NET SDK 10 和 .NET Framework 4.7.2 运行时。**干净克隆无需安装游戏，也无需相邻工程**：引用从 NuGet.org 与 BepInEx 公共 feed 恢复，版本和内容哈希已锁定。

```powershell
dotnet build .\GTSWindowMod.csproj -c Release -p:RestoreLockedMode=true
dotnet run --project .\tests\GTSWindowMod.Tests.csproj -c Release
dotnet build .\tests\HarmonySmoke\HarmonySmoke.csproj -c Release -p:RestoreLockedMode=true
& .\tests\HarmonySmoke\bin\Release\net472\HarmonySmoke.exe
& .\scripts\package.ps1 -LockedMode
```

产物位于 `dist/release/`，包含安装 ZIP、ZIP 校验值、`BUILD_INFO.json`。ZIP 内含每个文件的 SHA-256 清单。可独立验证：

```powershell
& .\scripts\verify-package.ps1 -PackagePath .\dist\release\GTSWindowMod-v1.0.0.zip -ExpectedVersion 1.0.0
```

高级本地适配可通过项目的 `ReferenceRoot`、HarmonySmoke 的 `BepInExCoreRoot` 指定已有引用目录；普通构建和 CI 使用锁定公共包。

## Actions / Release

- `main` push、PR、手动触发：Windows 编译、85 项逻辑/隐藏窗口检查、8 项真实 Harmony 合成挂钩检查、白名单打包与完整校验。
- 推送与项目版本匹配的 `v*` tag：在构建成功后发布正式 GitHub Release。
- 工作流默认只有读取权限，仅 tag 发布 job 拥有内容写权限；Actions 固定到提交 SHA。
- Release 包含提交、版本、运行编号、引用版本与校验信息；构建引用和测试依赖不进入安装包。

## 验证边界

93 项检查覆盖窗口状态机、原生隐藏窗口、反射菜单适配和真实 Harmony Patch/Unpatch 合成路径，**不等同于 Unity 游戏内画面、输入或独占全屏实测**。混合 DPI、多显示器、任务栏自动隐藏、相机比例和菜单布局仍需在目标游戏验证。原生全屏失败时的具体表现遵循游戏自身流程。

当前游戏布局通过确切类型和签名绑定；匹配失败报告 `UNKNOWN` 并暂停增强，不模糊扫描或禁用整页设置。详见 [接管边界](docs/INTEGRATION.md)、[验收清单](docs/VALIDATION.md)。

MIT License。第三方引用与游戏资产不属于本项目许可，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
