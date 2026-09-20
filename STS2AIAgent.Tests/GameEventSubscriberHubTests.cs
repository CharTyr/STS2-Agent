using STS2AIAgent.Server;

namespace STS2AIAgent.Tests;

internal static class GameEventSubscriberHubTests
{
    public static void NormalConsumersKeepOrderUntilCapacity()
    {
        var hub = new GameEventSubscriberHub<long>(3);
        var subscriber = hub.Subscribe();

        Assert.Equal(0, hub.Publish(101));
        Assert.Equal(0, hub.Publish(102));
        Assert.Equal(0, hub.Publish(103));
        Assert.Equal(1, hub.Count);
        Assert.True(subscriber.Reader.TryRead(out var first));
        Assert.True(subscriber.Reader.TryRead(out var second));
        Assert.True(subscriber.Reader.TryRead(out var third));
        Assert.Equal(101L, first);
        Assert.Equal(102L, second);
        Assert.Equal(103L, third);
    }

    public static async Task FullQueueClosesInsteadOfDroppingOldest()
    {
        var hub = new GameEventSubscriberHub<long>(2);
        var subscriber = hub.Subscribe();

        Assert.Equal(0, hub.Publish(101));
        Assert.Equal(0, hub.Publish(102));
        Assert.Equal(1, hub.Publish(130));
        Assert.Equal(0, hub.Count);

        Assert.True(subscriber.Reader.TryRead(out var first));
        Assert.True(subscriber.Reader.TryRead(out var second));
        Assert.Equal(101L, first);
        Assert.Equal(102L, second);
        Assert.False(subscriber.Reader.TryRead(out _));
        Assert.False(await subscriber.Reader.WaitToReadAsync());
    }

    public static void SlowSubscriberDoesNotAffectHealthySubscriber()
    {
        var hub = new GameEventSubscriberHub<long>(2);
        var slow = hub.Subscribe();
        var healthy = hub.Subscribe();

        Assert.Equal(0, hub.Publish(101));
        Assert.True(healthy.Reader.TryRead(out var healthyFirst));
        Assert.Equal(101L, healthyFirst);
        Assert.Equal(0, hub.Publish(102));
        Assert.True(healthy.Reader.TryRead(out var healthySecond));
        Assert.Equal(102L, healthySecond);

        Assert.Equal(1, hub.Publish(103));
        Assert.Equal(1, hub.Count);
        Assert.True(healthy.Reader.TryRead(out var healthyThird));
        Assert.Equal(103L, healthyThird);

        Assert.True(slow.Reader.TryRead(out var slowFirst));
        Assert.True(slow.Reader.TryRead(out var slowSecond));
        Assert.Equal(101L, slowFirst);
        Assert.Equal(102L, slowSecond);
        Assert.False(slow.Reader.TryRead(out _));

        Assert.Equal(0, hub.Publish(104));
        Assert.True(healthy.Reader.TryRead(out var healthyFourth));
        Assert.Equal(104L, healthyFourth);
    }

    public static void InitialItemPrecedesPublishedEvents()
    {
        var hub = new GameEventSubscriberHub<long>(2);
        var subscriber = hub.Subscribe();
        // The service writes the snapshot through the channel writer before any poll can publish,
        // which is the ordering the hub has to preserve.
        Assert.True(subscriber.Writer.TryWrite(100));
        Assert.Equal(0, hub.Publish(101));

        Assert.True(subscriber.Reader.TryRead(out var initial));
        Assert.True(subscriber.Reader.TryRead(out var next));
        Assert.Equal(100L, initial);
        Assert.Equal(101L, next);
    }
}
