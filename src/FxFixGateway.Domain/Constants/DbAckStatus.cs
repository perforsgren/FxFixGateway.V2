namespace FxFixGateway.Domain.Constants
{
    /// <summary>
    /// Database status values for tradesystemlink ACK records.
    /// </summary>
    public static class DbAckStatus
    {
        /// <summary>Initial status - trade received, waiting for downstream processing.</summary>
        public const string New = "NEW";

        /// <summary>Ready to send ACK - downstream system has set AckInternalTradeId.</summary>
        public const string ReadyToAck = "READY_TO_ACK";

        /// <summary>Accept ACK (AR TrdRptStatus=0) sent successfully to venue.</summary>
        public const string AckSent = "ACK_SENT";

        /// <summary>ACK sending failed - will be retried.</summary>
        public const string AckError = "ACK_ERROR";

        /// <summary>Trade has been rejected by operator - AR Reject (TrdRptStatus=1) is pending to be sent.</summary>
        public const string AckRejected = "ACK_REJECTED";

        /// <summary>Reject ACK (AR TrdRptStatus=1) sent successfully to venue.</summary>
        public const string AckRejectSent = "ACK_REJECT_SENT";
    }
}