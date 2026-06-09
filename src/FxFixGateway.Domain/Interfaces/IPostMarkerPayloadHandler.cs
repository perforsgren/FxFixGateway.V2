namespace FxFixGateway.Domain.Interfaces
{
    public interface IPostMarkerPayloadHandler
    {
        void OnPayloadReceived(int sequenceNo, string status, string xml);
    }
}