using MonitorCloud.Application.Licensing.Contracts;

namespace MonitorCloud.ContractTests;

/// <summary>
/// What a contract run needs: a gateway and the product keys of the scenarios. The fake builds them in memory; the live
/// run reads them from the environment (keys created by <c>tools/MonitorCloud.LicensingSeeder</c>).
/// </summary>
public interface IContractFixture
{
    ILicensingGateway Gateway { get; }

    /// <summary>An active licence with free seats (at least 3).</summary>
    string ActiveKey { get; }

    /// <summary>An active licence with exactly 2 seats in total, both free at the start of the run.</summary>
    string? LimitedKey { get; }

    string? SuspendedKey { get; }

    string? ExpiredKey { get; }

    /// <summary>The Licensing customer that owns <see cref="ActiveKey"/>.</summary>
    Guid CustomerId { get; }

    /// <summary>The plan codes that must be published.</summary>
    IReadOnlyList<string> PlanCodes { get; }

    /// <summary>Makes the next call answer 429 (fake only).</summary>
    bool CanSimulateRateLimit { get; }

    void SimulateRateLimit();

    /// <summary>Appends a change for the customer (fake) or triggers a real one (live: no-op, checked if present).</summary>
    void AppendChange();

    string NewDeviceId();
}
