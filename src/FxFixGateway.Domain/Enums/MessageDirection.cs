namespace FxFixGateway.Domain.Enums
{
    /// <summary>
    /// Riktning för FIX-meddelanden.
    /// </summary>
    public enum MessageDirection
    {
        /// <summary>
        /// Meddelande vi tagit emot från venue.
        /// </summary>
        Incoming = 0,

        /// <summary>
        /// Meddelande vi skickat till venue.
        /// </summary>
        Outgoing = 1
    }
}
