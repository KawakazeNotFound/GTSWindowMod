## GTSWindowMod 1.0.0

Giantess Sandbox 的纯鼠标窗口增强插件，适用于 Windows + BepInEx 5。

- 拖边框自由缩放，原生最大化/还原并保留任务栏。
- 无边框全屏：没有额外按钮，也没有 Mod 快捷键。
- 游戏选择 Fullscreen 时交还原生全屏流程；切回 Windowed / Borderless 后重新接管。
- 设置界面标注管理方，接管期间仅灰显分辨率相关控件；画质、VSync、保存与取消保留原流程。
- 记住普通窗口的位置、大小和模式。

### 安装

将 `GTSWindowMod-v1.0.0.zip` 中的 `BepInEx` 目录合并到已安装 BepInEx 5 的游戏目录。此前使用预览版 GiantessWindowMod 的用户，请先移除旧插件 DLL，避免同时加载两份。配置 GUID 保持不变。

### 验证

Windows CI 编译，85 项逻辑/隐藏窗口检查与 8 项真实 Harmony 合成检查，全包 SHA-256 与提交信息校验。发布包不含游戏、Unity、BepInEx 或 Harmony DLL。

这些自动检查不是 Unity 游戏内画面或独占全屏的实测证明。详见包内验证说明。
