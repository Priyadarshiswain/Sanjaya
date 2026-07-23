const supportedKinds = new Set(["incident.opened", "incident.acknowledged"]);

export function replayEvents(events, dispatch) {
  for (const event of events) {
    if (!supportedKinds.has(event.kind)) {
      continue;
    }

    dispatch(event);
  }
}
