# 显示设置接管边界（GTSWindowMod 1.0.0）

## 依据与接口

接口依据为用户已有的 NewMainMenu / BorderlessHelper 源码快照；没有调用新的反编译器。源码快照说明结构，实际加载后仍须通过运行时绑定及游戏验收确认当前版本匹配。

仅挂钩三个方法：

1. `NewMainMenu.NewMainMenu.ConstructSubMenu_Graphics()`：后置刷新接管标识与灰显。
2. `NewMainMenu.NewMainMenu.ApplyGraphicsSettings(bool)`：前置选择显示管理方，后置刷新 UI；原方法继续执行，因此画质、VSync、保存、撤销保留原路径。
3. `BorderlessHelper.SetBorderlessResolution(int,int,bool,FullScreenMode,int)`：Fullscreen 参数原样放行；Mod 管理窗口时阻止此 helper 再启动延迟的窗口调整。

关键字段：`NewMainMenu.INSTANCE.m_Graphics.WindowMode`、`NMM_Globals.Controls.WindowMode`、`NMM_Globals.Controls.ScreenResDB`、`NMM_Globals.SubMenus.Graphics.BigButtons`。模式枚举按确切名称 Windowed / Borderless / Fullscreen 验证，不猜测未知枚举值。

## 状态与 UI

| 用户选择 | 显示管理方 | 分辨率 / Try Resolution | 模式入口 |
|---|---|---|---|
| Windowed | Mod | 灰显，标注 Mod managed | 保持可选 |
| Borderless | Mod | 灰显，标注 Mod managed | 保持可选 |
| 原生最大化 | Mod（映射原生 Windowed） | 灰显，标注 Mod managed | 保持可选 |
| Fullscreen | 游戏 | 恢复原始可操作状态 | 标注 Fullscreen: Game |

选择 Fullscreen 的预览阶段恢复原生分辨率控件，但直到游戏执行 Apply / Try Resolution 才实际交还管理权。取消预览保留原生 undo/apply 流程，已最大化的 Mod 窗口也保持最大化。

全屏期间原生最小/最大尺寸消息约束、Mod 菜单与尺寸维护全部暂停。退出全屏后重新启用。保存的普通窗口矩形独立于全屏矩形。无边框没有额外按钮；退出无边框使用游戏设置的 Window Mode 入口。

接管阶段对 `GlobalLerp.CancelDelay("APPLY_RESOLUTION", false)` 的调用仅取消这个显示相关旧任务，不完成它，也不处理其他延迟任务。游戏在场景载入时回放的非全屏 helper 请求不会擅自替换已由 Mod 管理的模式。

## 可恢复性与范围

- 控件用其自身的 label / isDisabled / tooltip API 标注与置灰；保留原始禁用状态和 tooltip 时长。
- 仅通过确切标签 Try Resolution 定位同一个 Graphics 子菜单内的对应底部按钮；Cancel 与 Apply & Save 不禁用。
- 菜单重建后释放旧快照，重新标注新控件。卸载时恢复控件、标签、工具提示、按钮触发器和透明度，并只移除本插件自己的 Harmony ID。
- 不复制游戏源码或程序集，不修改磁盘上的游戏 DLL。
- Windows 隐藏窗口测试与 CLR 合成挂钩测试都不证明 Unity 游戏中的可视效果、原生独占全屏建立成功或鼠标命中正确。
