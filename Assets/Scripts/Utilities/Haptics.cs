// SnookerKit — haptic feedback helpers (CONTRACTS §2). Every call is gated by the user's hapticsOn setting
// (null-safe: a missing SettingsService simply disables haptics) and wrapped so platforms without a vibrator
// (editor/desktop) are silent no-ops.
using System;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Static haptics entry points. Every call checks <see cref="SettingsService"/>.hapticsOn and
    /// swallows platform failures — haptics are decoration and must never affect gameplay stability.</summary>
    public static class Haptics
    {
        /// <summary>Strong vibration for decisive moments (frame win, big pot). Uses the only native haptics API
        /// on Unity 2022.3 mobiles — Handheld.Vibrate — wrapped for devices/platforms without a vibrator.</summary>
        public static void HeavyImpact()
        {
            if (!HapticsAllowed()) return;
            try
            {
                Handheld.Vibrate();
            }
            catch (Exception)
            {
                // No vibrator on this platform (editor/desktop) — silently ignore.
            }
        }

        /// <summary>Reserved light-tick hook. Unity 2022.3 has no built-in short-pulse API without a native
        /// plugin, so v1 ships this as a guarded no-op placeholder sharing the HeavyImpact settings gate
        /// (documented v1 limitation — call sites can integrate it already).</summary>
        public static void LightTick()
        {
            if (!HapticsAllowed()) return;
            // Intentional no-op.
        }

        /// <summary>True only when a live <see cref="SettingsService"/> reports hapticsOn; a missing service
        /// or settings object counts as "off" (defensive default).</summary>
        private static bool HapticsAllowed()
        {
            try
            {
                SettingsService settings;
                if (!ServiceRegistry.TryGet<SettingsService>(out settings) || settings == null) return false;
                if (settings.Settings == null || !settings.Settings.hapticsOn) return false;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
    }
}
