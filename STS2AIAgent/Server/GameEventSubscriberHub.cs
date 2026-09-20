using System.Threading.Channels;

namespace STS2AIAgent.Server;

internal sealed class GameEventSubscriberHub<T>
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Channel<T>> _subscribers = new();
    private readonly int _capacity;
    private long _nextSubscriberId;

    public GameEventSubscriberHub(int capacity)
    {
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
    }

    public (long Id, ChannelReader<T> Reader, ChannelWriter<T> Writer) Subscribe()
    {
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        lock (_gate)
        {
            var subscriberId = ++_nextSubscriberId;
            _subscribers[subscriberId] = channel;
            return (subscriberId, channel.Reader, channel.Writer);
        }
    }

    public bool Unsubscribe(long subscriberId)
    {
        Channel<T>? channel;
        lock (_gate)
        {
            if (!_subscribers.Remove(subscriberId, out channel))
            {
                return false;
            }
        }

        channel.Writer.TryComplete();
        return true;
    }

    public int Publish(T item)
    {
        List<Channel<T>> stale = new();
        lock (_gate)
        {
            foreach (var (subscriberId, channel) in _subscribers.ToArray())
            {
                if (channel.Writer.TryWrite(item))
                {
                    continue;
                }

                if (_subscribers.Remove(subscriberId, out var removed))
                {
                    stale.Add(removed);
                }
            }
        }

        foreach (var channel in stale)
        {
            channel.Writer.TryComplete();
        }

        return stale.Count;
    }

    public void CompleteAll()
    {
        List<Channel<T>> channels;
        lock (_gate)
        {
            channels = _subscribers.Values.ToList();
            _subscribers.Clear();
        }

        foreach (var channel in channels)
        {
            channel.Writer.TryComplete();
        }
    }
}
