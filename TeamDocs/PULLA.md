# Pulla — Character Gameplay, Combat, and Player UI

## Goal

Make the Runner and Granny responsive and understandable to play, and build the player-facing UI for both the pre-game lobby and match.

## Work plan — 16 effort points

| Points | Deliverable | Completion check |
|---:|---|---|
| 4 | Implement and tune movement/control for both roles, including Runner crouching and grounded/spawn behavior. | Both roles move reliably in the first-floor environment, fit intended traversal spaces, and do not fall through valid floor geometry. |
| 4 | Implement Granny's crossbow combat presentation and Runner hit response, including critical/non-critical feedback and agreed elimination behavior. | Hits provide clear feedback; combat state and round-end result agree with Chetana's network contract. |
| 3 | Build the lobby UI: host/join controls, room code display/input, player/role/readiness display, ready/start controls, and connection/error feedback. | A pair of players can understand their lobby state and start only when the session permits it. |
| 3 | Build match HUD and interaction feedback for objective progress, item/door use, hit/slow status, and win/loss result. | Runner and Granny can understand current state without relying on the Console. |
| 2 | Balance and playtest controls, combat readability, and one complete route with the team. | Reported tuning changes are tested in host and client roles and do not break movement or interaction. |

## Guidelines

- Keep movement and combat presentation in this ownership area; use Chetana's shared network contract for authoritative shared state rather than adding parallel networking logic.
- Coordinate character dimensions, spawn clearance, and crouch geometry with Ritu before final tuning.
- Ask Rahul for the physical dimensions and behavior notes for route traversal challenges.
- Match the agreed 1v1 combat rules. Do not add teammate rescue, capture timers, or a multi-life system in this phase.
- Ensure lobby buttons call Chetana's session flow and display its real state; do not show a ready/connected state based only on local UI toggles.
- Keep control instructions and feedback visible to a first-time player, and test with keyboard/mouse as supported by the current prototype.

## Dependencies and handoffs

- Chetana supplies lobby and match session state, role, ready, interaction, and round-result events.
- Ritu supplies lobby layout, match spawn anchors, and collision-safe traversal spaces.
- Rahul supplies approved escape-route steps and traversal challenge requirements.
- At Kickoff, provide Ritu the character height, capsule width, movement speed assumptions, and lobby player-space needs; provide Rahul crouch clearance and interaction reach. This lets both model/place to gameplay scale immediately.
- Start movement/UI work with graybox assets and Chetana's draft API in Week 1; do not block on final art or a complete network backend.
- By Week 2, send Ritu/Rahul any geometry changes required by character clearance and send Chetana the exact HUD/lobby state fields needed for display.

## Done means

Both roles can move and play their part in the match, the lobby communicates real online session state, HUD communicates current objectives and outcomes, and all changes pass the shared two-instance playtest checklist.
