namespace FxFixGateway.Domain.Enums
{
    /// <summary>
    /// Status för acknowledgment av en trade.
    /// </summary>
    public enum AckStatus
    {
        /// <summary>Väntar på att skickas.</summary>
        Pending = 0,

        /// <summary>Accept-ACK (TrdRptStatus=0) har skickats till venue.</summary>
        Sent = 1,

        /// <summary>Misslyckades att skicka - kommer att försökas igen.</summary>
        Failed = 2,

        /// <summary>Reject-ACK (TrdRptStatus=1) har skickats till venue.</summary>
        Rejected = 3
    }
}
