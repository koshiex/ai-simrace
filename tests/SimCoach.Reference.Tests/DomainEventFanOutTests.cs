using FluentAssertions;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class DomainEventFanOutTests
{
    [Fact]
    public async Task Every_subscriber_receives_all_events_in_order()
    {
        var fanOut = new DomainEventFanOut();
        DomainEventSubscription a = fanOut.Subscribe("a");
        DomainEventSubscription b = fanOut.Subscribe("b");

        fanOut.Publish(DomainEvent.Lap(new LapEvent { LapNumber = 1 }));
        fanOut.Publish(DomainEvent.Lap(new LapEvent { LapNumber = 2 }));
        fanOut.Publish(DomainEvent.Session(new SessionEvent { SessionId = "s" }));
        fanOut.Complete();

        (await LapNumbers(a)).Should().Equal(1, 2);
        (await LapNumbers(b)).Should().Equal(1, 2);
    }

    [Fact]
    public async Task A_late_subscriber_sees_only_subsequent_events()
    {
        var fanOut = new DomainEventFanOut();
        fanOut.Publish(DomainEvent.Lap(new LapEvent { LapNumber = 1 }));
        DomainEventSubscription late = fanOut.Subscribe("late");
        fanOut.Publish(DomainEvent.Lap(new LapEvent { LapNumber = 2 }));
        fanOut.Complete();

        (await LapNumbers(late)).Should().Equal(2);
    }

    [Fact]
    public async Task Is_lossless_for_a_large_burst()
    {
        var fanOut = new DomainEventFanOut();
        DomainEventSubscription sub = fanOut.Subscribe("sub");
        for (int i = 1; i <= 5000; i++)
        {
            fanOut.Publish(DomainEvent.Lap(new LapEvent { LapNumber = i }));
        }

        fanOut.Complete();

        List<DomainEvent> received = [];
        await foreach (DomainEvent e in sub.ReadAllAsync())
        {
            received.Add(e);
        }

        received.Should().HaveCount(5000);
    }

    [Fact]
    public void Subscribing_after_completion_throws()
    {
        var fanOut = new DomainEventFanOut();
        fanOut.Complete();

        Action subscribe = () => fanOut.Subscribe("late");

        subscribe.Should().Throw<InvalidOperationException>();
    }

    private static async Task<List<int>> LapNumbers(DomainEventSubscription subscription)
    {
        List<int> numbers = [];
        await foreach (DomainEvent e in subscription.ReadAllAsync())
        {
            if (e is { Kind: DomainEventKind.Lap, Payload: LapEvent lap })
            {
                numbers.Add(lap.LapNumber);
            }
        }

        return numbers;
    }
}
