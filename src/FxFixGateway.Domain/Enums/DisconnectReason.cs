namespace FxFixGateway.Domain.Enums
{
    /// <summary>
    /// Describes why a FIX session was disconnected.
    /// </summary>
    public enum DisconnectReason
    {
        /// <summary>Unknown or unclassified disconnect.</summary>
        Unknown = 0,

        /// <summary>User manually closed the application.</summary>
        UserExit = 1,

        /// <summary>Application crashed or was force-terminated.</summary>
        Crash = 2,

        /// <summary>OS shutdown or restart triggered the disconnect.</summary>
        OsShutdown = 3,

        /// <summary>Counterparty sent a Logout message.</summary>
        CounterpartyLogout = 4,

        /// <summary>Network error or TCP connection lost.</summary>
        NetworkError = 5,

        /// <summary>Session was disabled by configuration.</summary>
        SessionDisabled = 6,
    }
}