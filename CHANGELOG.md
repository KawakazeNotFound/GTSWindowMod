# Changelog

## 1.0.0

First public release as **GTSWindowMod**.

- Resizable native Windows frame with maximize/restore and normal-window position/size memory.
- Borderless fullscreen with no extra return button, overlay, or Mod hotkeys.
- Native game Fullscreen retains the original game fullscreen/resolution path; switching back reclaims window management.
- Graphics settings show ownership; Mod-owned resolution controls are greyed, while mode selection and unrelated graphics settings remain usable.
- Preserve existing preview configuration through the unchanged plugin GUID `com.giantess.windowmod`.
- Public NuGet build references, locked dependencies, Windows Actions, 93 automatic checks, package checksums and provenance metadata.

Automated checks use synthetic fixtures, not a running Unity game. Game-specific visual/input and mixed-DPI behavior should be tested on the target installation.
