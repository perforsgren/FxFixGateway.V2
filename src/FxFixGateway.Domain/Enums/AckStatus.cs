namespace FxFixGateway.Domain.Enums
{
    /// <summary>
    /// Status för acknowledgment av en trade.
    /// </summary>
    public enum AckStatus
    {
        /// <summary>
        /// Väntar på att skickas.
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Har skickats till venue.
        /// </summary>
        Sent = 1,

        /// <summary>
        /// Misslyckades att skicka.
        /// </summary>
        Failed = 2
    }
}
