# SignalDesk architecture

The backend separates incident records from routing, notification dispatch,
and retry behavior. `SignalDesk.Relay.RetryPolicy` is the production retry
implementation. A deliberately retained class with the same short name exists
under `SignalDesk.Legacy`; callers must not silently select it.

The frontend displays incidents and offers a non-authoritative retry preview.
Its calculation is intentionally simpler than the backend policy and must not
be presented as production retry behavior.
