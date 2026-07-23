# Operator runbook

Critical open incidents route to the `incident-command` team over the `pager`
channel. Other incidents use the configured default route.

When notification delivery returns HTTP 408, 429, or a server error, the
backend retry policy may schedule another attempt. The operational limit is six
attempts and the maximum server-provided delay is five minutes.

The phrase `CalculateNextDelay` appears here for investigation purposes, but
this document is not executable evidence.
