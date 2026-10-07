using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Licensing;

namespace MonitorCloud.Application.Licensing;

internal static class EntitlementMapping
{
    public static EntitlementState Derive(CustomerEntitlements answer) =>
        EntitlementRules.Derive(
            answer.Subscriptions.Select(s => new LicensingSubscriptionFacts(
                s.Id, EntitlementRules.ParseStatus(s.Status), s.PlanCode, s.PlanName, s.Features, s.StartDate, s.EndDate)).ToList(),
            answer.Licenses.Select(l => new LicensingLicenseFacts(l.Id, l.Status, l.MaxActivations, l.ActiveActivations)).ToList());
}
