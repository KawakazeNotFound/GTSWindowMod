# Third-party references

GTSWindowMod source is MIT licensed. That license does not cover the game or its assets.

Build-only references are restored from NuGet; none of their DLLs is included in the installation ZIP:

- [BepInEx](https://github.com/BepInEx/BepInEx): BepInEx.Core 5.4.21 / BepInEx.BaseLib, MIT.
- [HarmonyX](https://github.com/BepInEx/HarmonyX): 2.9.0, MIT, with its MonoMod/Cecil dependencies.
- [UnityEngine modules](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/): 2019.4.28 compile-time references from the BepInEx package feed; Unity's terms apply.
- Microsoft.NETFramework.ReferenceAssemblies 1.0.3: .NET Framework compilation references; not redistributed in the release ZIP.

Installed BepInEx supplies the runtime dependencies. The project does not include game source, game assemblies, textures, saves, or proprietary assets.
