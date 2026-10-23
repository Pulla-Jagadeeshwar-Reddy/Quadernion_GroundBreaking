# First-Floor Game — Team Guide

## Current target

- Online 1v1: one player is Granny and one is the Runner.
- Add the first-floor environment and five planned escape routes, with route designs approved by the team before full implementation.
- Add a pre-game lobby where the two players connect, see each other, choose or receive roles, ready up, and start the match.
- Keep teammate rescue, multi-runner play, and capture timers out of this phase. A match ends when the Runner escapes or Granny eliminates the Runner under the agreed combat rules.

## Ownership and fair workload

Each person owns a distinct deliverable area and should aim for **16 effort points** using the points in their individual plan. Points are relative effort estimates, not time promises. If a task proves substantially larger, the team should trade or split tasks so the totals stay close; do not silently add it to one person's workload.

| Owner | Primary ownership | Effort target |
|---|---|---:|
| Ritu | First-floor and lobby environment modeling, materials/collisions, ambience | 16 |
| Rahul | Escape-route design, route-specific modeling, and traversal challenge geometry | 16 |
| Chetana | Online session flow and replicated interactions | 16 |
| Pulla | Character/combat gameplay and player-facing UI, including lobby UI | 16 |

## Workflow and Unity safety

1. Keep the working `OnlineVerticalSlice` as a known-good fallback. Make the expanded first-floor game in a separate scene, such as `OnlineFirstFloor`.
2. Ritu owns integration edits to the shared environment scene. Ritu models the structural environment and lobby space; Rahul models route-specific props and challenge geometry. Rahul hands models to Ritu as prefabs, with scale/pivot/collider/material notes, for placement. Other team members should deliver prefabs, models, scripts, and a short integration note rather than simultaneously editing that scene.
3. Use the team's GitHub repository as the single source of truth. Check in `.meta` files with every Unity asset. Do not check in `Library`, `Temp`, `Logs`, `UserSettings`, or local build output. Keep the project's `.gitignore` and `.gitattributes` files in the repository.
4. Work in small changesets with clear names. Update the task's status and blockers in the relevant personal file. Avoid moving or renaming imported assets without coordinating with Ritu.
5. Agree on interaction object names/components and the route objective interface before parallel implementation. Chetana owns the network interaction contract; Rahul supplies route requirements; Pulla supplies combat/player-state requirements.
6. Before merging an integration, pull the latest version, resolve conflicts in Unity where possible, and open the scene to check for missing references.

## GitHub setup and daily workflow

### One-time setup

1. The repository owner creates a **private GitHub repository** for the Unity project and invites the other three teammates as collaborators.
2. In the Unity Editor, set **Edit → Project Settings → Editor → Version Control Mode** to **Visible Meta Files**, and **Asset Serialization** to **Force Text**. Commit these settings and all `.meta` files so asset references stay stable across machines.
3. Use the Unity project root (the folder containing `Assets`, `Packages`, and `ProjectSettings`) as the Git repository root. Commit the provided `.gitignore` and `.gitattributes` at that level.
4. Install Git LFS and run `git lfs install` once on each computer. The `.gitattributes` tracks large binary source assets such as FBX/Blend models and Unity packages with LFS; ordinary scripts, scenes, prefabs, materials, metadata, and project settings stay as regular Git files.
5. Publish the project to GitHub, then have teammates clone it with GitHub Desktop and open the cloned project using the same Unity editor version. Do not share a copy of the `Library` folder.

### Branch and pull-request workflow

- Use a lightweight GitHub Flow: `main` stays playable; each task gets a short-lived branch from the latest `main`, such as `feature/route-one-models`, `feature/lobby-ui`, or `fix/player-floor-collision`.
- Before starting: **Fetch origin**, then **Pull** `main`; create the task branch in GitHub Desktop. Do not commit work directly to `main`.
- Work only on the owned files/scene where possible. Save, review the changed-file list, and confirm Unity `.meta` files accompany new/moved assets.
- Commit small, coherent changes with descriptive messages, push the branch, and open a pull request targeting `main`. Use the repository's pull-request template, link the task, explain the change, and list the Unity/online checks performed.
- Request at least one review from a teammate who owns or depends on the changed area. The author resolves feedback, waits for the automated **Unity project checks** to pass, then merges using **Squash and merge**. Delete the merged branch.
- After merging, everyone fetches and pulls `main`; open the updated Unity project and confirm the scene/assets load. Start the next task from this updated baseline.
- Avoid having two people edit the same Unity scene, prefab, or binary model at the same time. Ritu integrates shared environment-scene changes; Rahul hands over route prefabs on his branch for Ritu to place.
- Never force-push shared branches or commit secrets, credentials, local settings, builds, or generated caches.

### Pull-request integration and repository settings

- Keep PRs focused and small; do not bundle unrelated environment, gameplay, and networking changes just to reduce PR count.
- Set a GitHub ruleset/branch protection for `main` after the repository is published: require a pull request, at least one approval, the **validate-project** status check, and prevent force-pushes/deletion. Keep the branch current with `main` before merging.
- The automated check validates Unity `.meta` pairing and rejects generated/Plastic workspace files. It does **not** compile the project or replace a Unity Editor playtest. The PR author must report Editor checks and host/client playtest results when relevant.
- For model or scene changes, attach a screenshot or short description of the result and identify the scene/prefab changed. Ritu reviews environment integration; Rahul reviews route asset/layout changes; Chetana reviews networking/session changes; Pulla reviews controls/UI/combat changes. Ask for one additional cross-owner review when a PR crosses areas.
- Avoid parallel edits to the same `.unity`, `.prefab`, `.blend`, or other binary file. Split those edits or coordinate a single editor before beginning.

### Existing Unity Version Control connection

The project may still show its previous Unity Version Control connection in Unity Hub. For this workflow, use GitHub Desktop/Git for commits, pulls, branches, and reviews; do not make parallel changes in Plastic/Unity Version Control. Keep `.plastic` excluded from Git. Do not delete the old cloud repository or its data as part of GitHub setup unless the team deliberately decides to retire it.

## Lobby ownership and handoffs

- Ritu creates the lobby's visual space/environment and any lobby-specific ambient sound.
- Pulla owns the lobby screens and player-facing controls: host/join, room code display/input, role/readiness/status display, and feedback for connection or start failures.
- Chetana owns the online session flow and state: create/join, two-player capacity, role assignment, ready state synchronization, host start, disconnect handling, and transition into the match.
- Keep this lobby implementation scoped to exactly two players. Do not build matchmaking, parties, spectators, or multi-runner rescue yet.

## Timeline and handoff gates

This is a relative five-week plan, not a calendar commitment. A phase may overlap once its listed handoff is available. Keep the first playable route ahead of modeling all five routes.

| When | Owners and work | Required handoff / who can start next |
|---|---|---|
| **Kickoff (Days 1–2)** | Ritu checks imported floor assets, room scale, and scene constraints. Rahul drafts five distinct route concepts. Chetana inspects the current NGO/Relay slice and lists required network states. Pulla writes the character/combat controls and lobby UI wireframe. | Rahul gives the team route concepts and the first-route candidate. Ritu gives room dimensions/scale and character clearance. Chetana gives a draft interaction/session contract. Pulla gives movement, interaction, and screen requirements. These let the team approve scope and start grayboxing in parallel. |
| **Week 1 — approve and graybox** | Ritu builds a collision-safe structural blockout of the first-floor and a simple lobby blockout. Rahul gets five route briefs approved, then marks route footprints, route-1 item/exit points, and geometry/prop needs. Chetana finalizes lobby and gameplay interaction states. Pulla validates character dimensions and creates UI/movement prototypes against placeholders. | **Ritu → everyone:** scene scale, room layout, spawn anchors, collision-safe graybox. **Rahul → Ritu/Chetana/Pulla:** approved route briefs, stable objective IDs, route-1 diagram and placeholder locations. **Chetana → Pulla:** session events/statuses and interaction request/result contract. **Pulla → Ritu/Rahul:** character height, crouch clearance, and interaction reach. The first three enable environment, gameplay scripting, and UI implementation without waiting for finished art. |
| **Week 2 — model and implement the first route** | Ritu models/refines the floor shell, room structures, doors/walls, materials, collision, barriers, and lobby visual space. Rahul models route-1 escape props/items and route-specific traversal challenge pieces; creates prefabs and begins simple blockouts for other routes. Chetana implements host/join, two-player lobby state, and networked route-1 interactions. Pulla implements role movement, combat feedback, lobby UI, and HUD using placeholders/contracts. | **Ritu → Rahul/Pulla:** collision-ready first-floor and lobby layout. **Rahul → Ritu:** route-1 prefabs plus placement/collider notes. **Rahul → Chetana:** route-1 IDs, interaction states, and completion rule. **Chetana → Pulla:** working lobby/session callbacks and shared gameplay state. Pulla can build UI against documented/mock states before the network backend is complete. |
| **Week 3 — integrate and prove the loop** | Ritu places Rahul's route-1 assets and completes first-pass ambience. Rahul revises route-1 readability and produces route 2–5 asset/geometry briefs. Chetana connects the first route and lobby to live network state. Pulla tunes controls/combat and runs two-instance playtests. | **Team → all owners:** one online match can start from the lobby, play route 1, and resolve escape or elimination consistently on host/client. Bug list and accepted route-1 layout become the baseline for expanding content. |
| **Weeks 4–5 — expand and polish** | Ritu integrates structural/environment changes and collision for remaining approved routes. Rahul models and hands off remaining route props/challenge geometry in agreed batches. Chetana wires and verifies each route's replicated objective/escape states. Pulla balances each route and updates UI/HUD feedback. All four test lobby, match, disconnect, and rematch. | Each batch uses the same handoff: Rahul provides prefab + route/objective sheet; Ritu places it and confirms collision; Chetana connects authoritative state; Pulla tests player feedback and balance. Finish when all five routes are playable and the shared acceptance checklist passes. |

## Milestones and acceptance checks

1. **Stable baseline:** both players can join the existing online slice; preserve a recoverable scene/project state.
2. **Environment integration:** first-floor test area is visible and traversable; floor, stairs/edges where applicable, and walls block movement; unreachable areas are sealed or clearly bounded.
3. **Lobby:** two separate game instances can host/join, see the other player and role/readiness, and start the same match. Disconnect or failed connection gives a clear way to recover.
4. **First complete route:** one route can be completed online from collecting/using its required items through the escape result. Both players see consistent item, door, and win state.
5. **Route expansion and polish:** add the remaining four routes, verify route clarity and balance, then check ambience, performance, and match restart.

## Online playtest checklist

- Run host and client as separate instances/builds; a single editor instance is not a two-player test.
- Verify Granny and Runner spawn at their intended positions and remain grounded.
- Check that item pickup, inventory/objective progress, doors, combat, escape, and round result agree on both clients.
- Test simultaneous interaction attempts, leaving the lobby, disconnecting during a match, and returning to the lobby/rematch flow.
- Record reproducible bugs with scene, steps, expected result, actual result, and host/client role.

## Shared definition of done

A task is done when its owner has integrated or handed off the work, committed required Unity metadata, tested their acceptance checks, documented any limitations, and confirmed the result with the teammate who owns the dependent system.
