# ADR 0005 - Details of telemetry ingestion (M4-M5)

- Status: Proposed (applied in M4 and M5, awaiting product-owner review)
- Date: 2026-10-07

## Context

The plan describes the telemetry pipeline (05 section 3, 02 section 5). Five details were not covered, or the plan
could not be followed literally on the development machine.

## Decisions

1. **`DiskPercentMax` is stored in `MetricMinutes` and `MetricHours`.** The protocol sends `disk_percent_max`
   with every minute, and `DeviceStates.DiskPercent` is defined as that value. The column is what keeps the
   disk history chart and the device card consistent. 02 section 5 does not list it.
2. **Development runs the gRPC gateway on a second endpoint.** REST and SignalR stay on `http://localhost:5300`
   (HTTP/1.1). The gateway is on `http://localhost:5301`, HTTP/2 only. Without TLS, HTTP/2 needs prior knowledge,
   which Kestrel offers only on an endpoint that speaks HTTP/2 alone. With TLS, as in production, one port carries
   both through ALPN. Agents get `Agent:GatewayUrl` at enrollment, so the port is configuration only.
3. **Disk sizes per hour keep the last report of that hour.** The plan does not say which value is kept.
   `DiskUsageHours` is upserted with the latest sizes received in the hour. Sizes change slowly, so the last report
   is the most useful one.
4. **`MonitorPointSampler` moves to M6.** It writes `MonitorPointSamples` from the in-memory state of monitor
   points. That state comes from `MonitorPoint` and `MonitorPointState`, which the plan introduces in MC-601 (M6).
   The table and the sampler are added together with them.
5. **Live events never use the caller's cancellation.** A `Goodbye` is followed at once by the end of the agent's
   stream. The e2e flow showed that the Offline broadcast was cancelled together with the call. SignalR sends now
   run independently of the request that caused them.

## Consequences

None of these change the API or the protocol. Point 2 matters only for deployments without TLS.
