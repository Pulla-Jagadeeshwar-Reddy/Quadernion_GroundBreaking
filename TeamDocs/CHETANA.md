# Chetana — Online Sessions and Networked Interactions

## Goal

Own the online 1v1 session flow and make shared gameplay interactions authoritative and consistent between Granny and Runner.

## Work plan — 16 effort points

| Points | Deliverable | Completion check |
|---:|---|---|
| 4 | Implement the two-player online lobby/session flow on the project's existing NGO/Relay setup: host/join, room code, capacity, and role assignment. | Two separate instances connect reliably; a third player is refused with a clear message. |
| 3 | Synchronize lobby readiness, host-controlled match start, disconnect/reconnect-to-lobby behavior, and transition into the match. | Both clients see consistent status; only the host can start once both players are ready. |
| 5 | Implement networked item spawn/pickup/inventory or objective progress, and shared door/escape interactions. | Simultaneous attempts resolve consistently and both clients agree on item ownership, door state, objective progress, and escape result. |
| 2 | Define and document the small interaction/state contract used by routes and combat (object identity, interact request/result, round result). | Rahul and Pulla can integrate without inventing competing network state systems. |
| 2 | Run host/client integration and edge-case checks; fix state divergence and document known limitations. | Pickup, door, escape, host start, and disconnect scenarios pass on two instances. |

## Guidelines

- Preserve the current NGO/Relay foundation and working `OnlineVerticalSlice`; avoid replacing transport/network architecture as part of this phase.
- Treat the host/server as authority for item ownership, objective progress, door/escape completion, and round result. Clients request actions; they do not independently decide shared outcomes.
- Keep the player count and lobby capacity at exactly two for this phase: one Granny and one Runner.
- Keep gameplay interaction APIs small and communicate them before Rahul and Pulla integrate dependent content.
- Handle missing/late references and disconnects without leaving a permanently stuck lobby or match. Surface errors through a status result that Pulla can display.
- Do not implement multi-runner rescue, capture timers, matchmaking, spectators, or persistent accounts in this milestone.

## Dependencies and handoffs

- Coordinate lobby visual scene and spawn/transition points with Ritu.
- Provide Pulla the lobby state/events needed to display host/join, room code, roles, ready state, errors, and match start.
- Agree on item and route objective identifiers with Rahul before route scripts are integrated.
- Agree on combat/round-end state handoff with Pulla; keep combat presentation in Pulla's ownership and shared outcome authoritative here.
- By the end of Kickoff, publish a draft interaction/session contract so Pulla can build against placeholders. By Week 1, finalize it using Rahul's approved route-1 IDs/states and Ritu's room/spawn constraints; do not wait for finished models.
- Give Rahul and Ritu a short list of required interaction anchors/object types before they finalize route prefabs and room placement.

## Done means

Host and client can connect through the lobby, ready and start one 1v1 match, interact with route objects, and observe matching round outcomes, including tested disconnect behavior.
