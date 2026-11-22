using System;

namespace RogueStars.Rules.Core
{
    /// <summary>
    /// Marks a command that only the server may issue (purchase completion, invasion outcomes, mail delivery...).
    /// The server rejects such commands when they arrive from a client via POST /v1/command.
    /// The offline client may still execute them locally (e.g. sandbox shop, tests).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ServerOnlyAttribute : Attribute
    {
    }
}
