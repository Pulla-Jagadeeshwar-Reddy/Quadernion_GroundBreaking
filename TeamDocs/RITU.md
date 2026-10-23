# Ritu — Environment, Structural Modeling, and Lobby Space

## Goal

Share all environment modeling with Rahul: own the structural first-floor and lobby environment; Rahul owns escape-route props and route-specific challenge geometry. Deliver the playable floor, collision, materials, and lobby space, and integrate Rahul's prefabs into the shared scene.

## Work plan — 16 effort points

| Points | Deliverable | Completion check |
|---:|---|---|
| 3 | Create the first-floor structural graybox and adapt imported room assets into `OnlineFirstFloor`, preserving `OnlineVerticalSlice` as fallback. | Room layout and scale are clear, scene opens cleanly, and there are placeholder points for routes. |
| 4 | Model/refine structural environment pieces: floor/walls/door frames/major room boundaries; apply room materials and textures. | Structures fit together and render with intended materials in the target URP project. |
| 3 | Add collision, barriers, and unreachable-area blocking, then integrate Rahul's route prefabs. | Characters stay grounded, walls block movement, and route props are correctly positioned/collidable. |
| 2 | Add first-pass environmental ambience and lighting/audio placement. | Ambience is audible at a reasonable level and does not obscure gameplay cues. |
| 4 | Model and block out the lobby environment, including player spawn/standing points and readable space for UI. | Lobby has a clean visual layout and works with Pulla's UI and Chetana's scene/session transition. |

## Guidelines

- Own shared-scene integration and structural environment modeling. Rahul owns route-specific asset/challenge modeling; agree on scale and attachment conventions before either person makes final prefabs.
- Use the timeline and handoff gates in `TEAM_GUIDELINES.md`: provide room scale, structural graybox, collision and spawn anchors early so Rahul, Pulla, and Chetana can begin using placeholders.
- Keep the lobby separate from the match environment unless a simple scene transition is already supported; coordinate the chosen scene handoff with Chetana before wiring it.
- Use consistent scale, pivots, naming, and collision layers. Keep decorative meshes from accidentally blocking player movement.
- Add colliders to gameplay geometry; use simple colliders where they provide reliable movement and better performance than detailed mesh collision.
- Place route assets after Rahul supplies the approved route footprint/prefab notes and Chetana confirms the interaction anchors.
- Commit Unity `.meta` files with assets through the team's GitHub workflow; avoid checking in generated folders or local builds.

## Dependencies and handoffs

- Give Rahul room scale, structural graybox, available wall/floor attachment points, and placement constraints during Week 1; receive route-prefab batches from Rahul starting Week 2.
- Ask Pulla for character height, spawn clearance, and movement/crouch constraints.
- Coordinate the lobby scene/area transition and spawn anchors with Chetana; Pulla owns the lobby UI.

## Done means

The first-floor scene opens cleanly, is collision-safe, has the agreed room materials and ambience, includes the lobby visual space, integrates Rahul's route models, and passes the shared online playtest checklist in `TEAM_GUIDELINES.md`.
