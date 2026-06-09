using System.Threading.Channels;

namespace FxFixGateway.Infrastructure.PostMarker
{
    /// <summary>
    /// Bounded channel for transport ACK items.
    /// FullMode = Wait — lost ACK stalls the PostMarker session, so we never drop.
    /// </summary>
    public sealed class PostMarkerAckChannel
    {
        private readonly Channel<AckItem> _channel;

        public PostMarkerAckChannel()
        {
            _channel = Channel.CreateBounded<AckItem>(new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public ChannelWriter<AckItem> Writer => _channel.Writer;
        public ChannelReader<AckItem> Reader => _channel.Reader;
    }

    public sealed record AckItem(int SequenceNumber);
}