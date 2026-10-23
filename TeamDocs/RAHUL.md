# Rahul — Escape Routes and Route Modeling

## Goal

Share all modeling with Ritu: Ritu owns structural rooms/floor/lobby; you own the escape-route-specific props, items, and traversal challenge geometry. Design five distinct escape routes, get approval before detailed modeling, then hand prefabs and route requirements to Ritu and the scripters.

## Work plan — 16 effort points

| Points | Deliverable | Completion check |
|---:|---|---|
| 4 | Propose five distinct route concepts, with objectives/items, room footprint, and Granny counterplay; get team approval. | Team approves route briefs feasible in Ritu's first-floor layout. |
| 5 | Model route-specific props/items for route 1, then create remaining route assets in batches. | Prefabs have clear silhouettes, useful pivots, scale, materials, and collider guidance. |
| 3 | Model/block out route-specific challenge geometry (for example, crouch gaps, climb points, noisy surfaces, or barriers) and document placement. | Ritu can position pieces in rooms without changing the building shell; Pulla can tune character traversal. |
| 2 | Document route sequence and interaction/objective points, starting with route 1. | Chetana has stable object/objective IDs, required states, and completion conditions. |
| 2 | Playtest readability/counterplay and revise confusing or dominant steps. | A player can understand the route, and Granny has a reasonable way to contest it. |

## Route design requirements

- Do not assume every route uses the same six-part recipe. Give each route a distinct sequence and objective; keep item count and complexity reasonable for a 1v1 match.
- Coordinate model scope with Ritu: do not model structural walls, room shells, the main floor, or lobby architecture. Model only route props, route items, and route-specific challenge pieces.
- For every route, document: route name, escape fantasy, required parts/actions, where each part appears, interactions needed, traversal constraints, how Granny can interrupt, and completion condition.
- The first route should be small enough to complete end-to-end before the other four are fully built. Confirm the chosen route with the team.
- Keep route interactions compatible with Chetana's shared network interaction contract. Describe behavior and required data; do not create a separate interaction framework.
- Make props legible from the intended play distance and provide non-color-only visual cues where practical.

## Guidelines and handoffs

- Get Ritu's room/space constraints before finalizing route footprints.
- Week 1 handoff to Ritu/Chetana/Pulla: approved five-route briefs, route-1 graybox diagram/markers, placeholder prop list, object IDs, and challenge dimensions. This lets structural placement, networking, and movement work begin before polished models exist.
- From Week 2 onward, deliver route assets in small named prefab batches with dimensions, scale, pivot, materials, collider recommendations, interaction point, and placement screenshot/diagram. Ritu places them; do not edit the integrated scene directly.
- Hand Chetana a route checklist with stable object names, required states, and success condition.
- Hand Pulla the intended traversal/combat pressure and how Granny should contest the route.
- Deliver models/prefabs in a dedicated folder and include scale, pivot, collider, material, and placement notes. Avoid modifying the shared scene directly.

## Done means

All five route briefs are approved, route 1 is fully specified with integration-ready models, and remaining route models/challenge pieces are delivered in batches with enough documentation for Ritu to integrate and Chetana/Pulla to implement and test them.
