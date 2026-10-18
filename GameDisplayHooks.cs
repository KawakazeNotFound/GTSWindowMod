using System;
using GTSWindowMod.Core;
using HarmonyLib;

namespace GTSWindowMod
{
    // Three named runtime hooks only; game binaries remain unchanged on disk.
    internal sealed class GameDisplayHooks : IDisposable
    {
        private static GameDisplayHooks _active;
        private readonly Harmony _harmony;
        private readonly GameDisplayIntegration _integration;
        private readonly Action<Exception> _failure;
        internal GameDisplayHooks(GameDisplayIntegration integration, Action<Exception> failure)
        {
            if (_active != null) throw new InvalidOperationException("Display hooks already installed.");
            _integration = integration; _failure = failure;
            _harmony = new Harmony(Plugin.Guid + ".display");
            _active = this;
            try
            {
                _harmony.Patch(integration.ApplyMethod, prefix: new HarmonyMethod(typeof(GameDisplayHooks), nameof(ApplyPrefix)),
                    postfix: new HarmonyMethod(typeof(GameDisplayHooks), nameof(RefreshPostfix)));
                _harmony.Patch(integration.ConstructMethod, postfix: new HarmonyMethod(typeof(GameDisplayHooks), nameof(RefreshPostfix)));
                _harmony.Patch(integration.HelperMethod, prefix: new HarmonyMethod(typeof(GameDisplayHooks), nameof(HelperPrefix)));
            }
            catch { Dispose(); throw; }
        }
        private static void ApplyPrefix(object __instance)
        {
            var active = _active;
            if (active == null) return;
            try { active._integration.BeforeApplyGraphics(__instance); }
            catch (Exception ex) { active._failure(ex); }
        }
        private static void RefreshPostfix()
        {
            var active = _active;
            if (active == null) return;
            try { active._integration.RefreshUi(); }
            catch (Exception ex) { active._failure(ex); }
        }
        private static bool HelperPrefix(object[] __args)
        {
            var active = _active;
            if (active == null) return true;
            try { return active._integration.BeforeNativeHelper(__args); }
            catch (Exception ex) { active._failure(ex); return true; }
        }
        public void Dispose()
        {
            if (ReferenceEquals(_active, this)) _active = null;
            _harmony.UnpatchSelf();
        }
    }
}
