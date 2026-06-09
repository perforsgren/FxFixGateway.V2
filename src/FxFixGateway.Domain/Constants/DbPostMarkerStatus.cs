namespace FxFixGateway.Domain.Constants
{
    /// <summary>
    /// Status values for PostMarker workflow in tradesystemlink table.
    /// SystemCode = 'POSTMARKER_ACCEPT'
    /// </summary>
    public static class DbPostMarkerStatus
    {
        public const string New = "NEW";
        public const string ReadyToAccept = "READY_TO_ACCEPT";
        public const string AcceptSent = "ACCEPT_SENT";
        public const string AcceptError = "ACCEPT_ERROR";
    }
}