// SnookerKit — static service locator (frozen, CONTRACTS §2). Plain CLR dictionary: safe for non-UnityEngine.Object types.
using System.Collections.Generic;

namespace SnookerKit
{
    /// <summary>Registers/deregisters managers by type. Get never throws (returns null when absent);
    /// callers null-check the result. Not thread-safe — Unity main thread only.</summary>
    public static class ServiceRegistry
    {
        private static readonly Dictionary<System.Type, object> Services = new Dictionary<System.Type, object>();

        /// <summary>Registers (or replaces) a service instance. Managers call this in Awake.</summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null) return;
            Services[typeof(T)] = service;
        }

        /// <summary>Removes a registration. Managers call this in OnDestroy.</summary>
        public static void Deregister<T>() where T : class
        {
            Services.Remove(typeof(T));
        }

        /// <summary>Returns the registered instance or null — callers null-check (e.g. ServiceRegistry.Get&lt;MatchManager&gt;()?.Mode).</summary>
        public static T Get<T>() where T : class
        {
            object o;
            return Services.TryGetValue(typeof(T), out o) ? o as T : null;
        }

        /// <summary>Try-style accessor for defensive call sites.</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            object o;
            if (Services.TryGetValue(typeof(T), out o))
            {
                service = o as T;
                return service != null;
            }
            service = null;
            return false;
        }

        /// <summary>Clears all registrations (scene teardown safety in tests/editor bootstrap).</summary>
        public static void ClearAll()
        {
            Services.Clear();
        }
    }
}
