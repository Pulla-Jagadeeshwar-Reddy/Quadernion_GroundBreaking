# Escape Route Briefs

Owner: Rahul. Status: **Draft — not yet approved.**

Five distinct escape routes for the Runner. Each route needs team approval before detailed modeling. Route 1 should be small enough to finish end-to-end first.

## Design constraints

From `OnlinePlayer.prefab` and `NetworkPlayerController.cs`:

| Constraint | Value | Design rule |
|---|---|---|
| Standing height | 2.0 m | Barriers that must block a standing player reach above 2.0 m or the ceiling |
| Crouched height | 1.1 m | Crouch gaps 1.2–1.4 m high |
| Capsule width | 0.9 m (radius 0.45) | Passages and gaps at least 1.0 m wide |
| Step offset | 0.3 m | Ledges above 0.3 m block walking |
| Slope limit | 45° | Ramps shallower than 45° |
| Interaction reach | 3 m raycast | Interactables need a collider and must be reachable within 3 m |

Current interaction support: one key (`NetworkInventory.HasTestKey`), pickups (`NetworkPickup`), locked doors (`NetworkLockedDoor`), and an exit gated by a door (`NetworkEscapePoint`). Anything beyond this (multiple items, climbing, noise, powered objects) must be listed under **Needs from others** so Chetana and Pulla can scope it.

## Route summary

| # | Name | Escape fantasy | Rooms | Items | Status |
|---:|---|---|---|---:|---|
| 1 | | | | | Draft |
| 2 | | | | | Draft |
| 3 | | | | | Draft |
| 4 | | | | | Draft |
| 5 | | | | | Draft |

---

## Route 1 — _name_

**Escape fantasy:** _One sentence on what the Runner is doing._

**Sequence:**

1. _Step — room — action_
2. _Step — room — action_
3. _Escape — room — completion condition_

**Items and interactables:**

| Object ID | What it is | Location | Interaction | States |
|---|---|---|---|---|
| `R1_` | | | | |

**Traversal constraints:** _Crouch gaps, barriers, noisy surfaces, with dimensions._

**Granny counterplay:** _How Granny can contest or interrupt each step._

**Completion condition:** _Exactly what counts as escaped._

**Needs from others:**

- Ritu: _room or placement constraints_
- Chetana: _network states or interactions beyond the current contract_
- Pulla: _movement, combat or HUD feedback_

---

## Route 2 — _name_

_Same sections as Route 1._

---

## Route 3 — _name_

_Same sections as Route 1._

---

## Route 4 — _name_

_Same sections as Route 1._

---

## Route 5 — _name_

_Same sections as Route 1._
