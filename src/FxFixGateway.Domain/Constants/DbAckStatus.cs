namespace FxFixGateway.Domain.Constants
{
    /// <summary>
    /// Database status values for tradesystemlink ACK records.
    /// </summary>
    public static class DbAckStatus
    {
        /// <summary>Initial status - trade received, waiting for downstream processing.</summary>
        public const string New = "NEW";
        
        /// <summary>Ready to send ACK - downstream system has set InternalTradeId.</summary>
        public const string ReadyToAck = "READY_TO_ACK";
        
        /// <summary>ACK has been sent successfully.</summary>
        public const string AckSent = "ACK_SENT";
        
        /// <summary>ACK sending failed.</summary>
        public const string AckError = "ACK_ERROR";
    }
}