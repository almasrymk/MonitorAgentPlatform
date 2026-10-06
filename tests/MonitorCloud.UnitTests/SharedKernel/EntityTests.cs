using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.SharedKernel;

public sealed class EntityTests
{
    private sealed class Widget : AggregateRoot
    {
        public Widget()
        {
        }

        public Widget(Guid id) : base(id)
        {
        }

        public void Touch(DateTimeOffset at) => Raise(new WidgetTouched(Id, at));
    }

    private sealed class Gadget : Entity
    {
        public Gadget(Guid id) : base(id)
        {
        }
    }

    private sealed record WidgetTouched(Guid WidgetId, DateTimeOffset At) : DomainEvent(At);

    [Fact]
    public void New_entities_get_a_version_7_id()
    {
        var widget = new Widget();

        widget.Id.ShouldNotBe(Guid.Empty);
        widget.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Entities_with_the_same_type_and_id_are_equal()
    {
        var id = Guid.CreateVersion7();

        new Widget(id).ShouldBe(new Widget(id));
        new Widget(id).GetHashCode().ShouldBe(new Widget(id).GetHashCode());
    }

    [Fact]
    public void Entities_of_different_types_are_not_equal()
    {
        var id = Guid.CreateVersion7();

        new Widget(id).Equals(new Gadget(id)).ShouldBeFalse();
        new Widget(id).Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void Aggregates_collect_and_clear_domain_events()
    {
        var widget = new Widget();
        var at = new DateTimeOffset(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

        widget.Touch(at);
        widget.Touch(at);

        widget.DomainEvents.Count.ShouldBe(2);
        var first = widget.DomainEvents.First().ShouldBeOfType<WidgetTouched>();
        first.OccurredAt.ShouldBe(at);
        first.EventId.Version.ShouldBe(7);

        widget.ClearDomainEvents();
        widget.DomainEvents.ShouldBeEmpty();
    }
}
