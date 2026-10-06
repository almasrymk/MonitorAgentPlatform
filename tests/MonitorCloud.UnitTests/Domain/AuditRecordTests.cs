using MonitorCloud.Domain.Audit;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class AuditRecordTests
{
    private static AuditRecord Create(string action = "workspace.opened", string? details = null, string? actorName = "Platform Admin") =>
        AuditRecord.Create(null, AuditActorType.User, Guid.CreateVersion7(), actorName, action, "Tenant", "t-1", true, details, "192.0.2.1", "c-1", TestClock.DefaultStart);

    [Fact]
    public void Create_sets_every_field()
    {
        var record = Create(details: "reason: support call");

        record.Action.ShouldBe("workspace.opened");
        record.EntityType.ShouldBe("Tenant");
        record.EntityId.ShouldBe("t-1");
        record.Success.ShouldBeTrue();
        record.Details.ShouldBe("reason: support call");
        record.At.ShouldBe(TestClock.DefaultStart);
        record.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Action_is_required() => Should.Throw<DomainException>(() => Create(action: " "));

    [Fact]
    public void Long_details_are_truncated() =>
        Create(details: new string('d', 3000)).Details!.Length.ShouldBe(AuditRecord.DetailsMaxLength);

    [Fact]
    public void Missing_actor_name_falls_back_to_the_actor_type() =>
        Create(actorName: null).ActorName.ShouldBe("User");
}
