using System;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// Marks a command that only the authoritative server may issue (receipt validation, battle results,
    /// cross-player outcomes). The server must reject client-submitted commands carrying this attribute;
    /// the offline client may execute them locally (dev/offline mode only).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ServerOnlyAttribute : Attribute { }

    public static class ServerOnly
    {
        /// <summary>True when the command type is marked [ServerOnly].</summary>
        public static bool Is(Type commandType) => commandType != null && Attribute.IsDefined(commandType, typeof(ServerOnlyAttribute), false);

        public static bool Is(GameCommand command) => command != null && Is(command.GetType());
    }
}
