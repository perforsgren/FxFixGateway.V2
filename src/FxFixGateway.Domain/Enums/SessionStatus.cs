namespace FxFixGateway.Domain.Enums
{
    /// <summary>
    /// Möjliga runtime-statusar för en FIX-session.
    /// </summary>
    public enum SessionStatus
    {
        /// <summary>
        /// Sessionen är stoppad och kör inte.
        /// </summary>
        Stopped = 0,

        /// <summary>
        /// Sessionen håller på att starta.
        /// </summary>
        Starting = 1,

        /// <summary>
        /// Uppkopplad till socket, väntar på Logon-svar.
        /// </summary>
        Connecting = 2,

        /// <summary>
        /// Fullt inloggad och aktiv.
        /// </summary>
        LoggedOn = 3,

        /// <summary>
        /// Håller på att stänga ner.
        /// </summary>
        Disconnecting = 4,

        /// <summary>
        /// Ett fel har uppstått.
        /// </summary>
        Error = 5
    }
}
