# Escape Route Briefs

Owner: Rahul. Status: **Draft — awaiting team approval.** Room placements are provisional until Ritu confirms room dimensions and placement constraints.

Five distinct escape routes for the Runner. Each route needs team approval before detailed modeling. Route 1 (Locksmith) is built end-to-end first because it is closest to the current key + locked door support.

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

Current interaction support: one key (`NetworkInventory.HasTestKey`), pickups (`NetworkPickup`), locked doors (`NetworkLockedDoor`), and an exit gated by a door (`NetworkEscapePoint`). Anything beyond this must be listed under **Needs from others**.

## Balance rules (all routes)

| Rule | Target |
|---|---|
| Items / objective tokens | exactly 4 per route |
| Total hold + wait time | 20–25 s |
| Rooms visited | 3–4, not all adjacent |
| Granny alerts | 1 big alert (location reveal or alarm) + 1 smaller noise cue |
| Final step | fixed exit Granny can guard |

Shared hold-interaction rules:

- Holding the interaction key fills a progress bar. Releasing keeps progress; it drains slowly (about 1 s of progress per 3 s idle).
- Granny hitting the Runner resets the current hold to zero.
- Items are not consumed. Shared tools stay in the Runner's inventory and can be used on any route.
- Codes and passcodes are entered automatically when the Runner holds the required tokens. No code-entry UI.

## Shared tools

One instance each, placed in exposed central spots so Granny can watch them.

| Object ID | Tool | Used by |
|---|---|---|
| `T_Screwdriver` | Screwdriver | Route 1, 2, 3 |
| `T_Hammer` | Hammer | Route 1, 4, 5 |
| `T_Crowbar` | Crowbar | Route 4, 5 |

## Route summary

| # | Name | Escape fantasy | Rooms (provisional) | Items | Hold + wait | Status |
|---:|---|---|---|---:|---:|---|
| 1 | Locksmith | Break out through the boarded Pantry back door | Discussion room, Classroom, Pantry | 4 | 20 s | Draft |
| 2 | Blackout | Restore power to open the electric shutter | UPS, Pantry, Washroom, Small room | 4 | 24 s | Draft |
| 3 | Hacker | Hack three computers to get the security door code | Classroom, Discussion room, Labs | 4 | 25 s | Draft |
| 4 | Tunnel | Dig under the floor and escape through a hatch | UPS, Small room, Labs | 4 | 24 s | Draft |
| 5 | Frozen Key | Free a key from ice and carry it to the main entrance | Pantry, UPS, main entrance | 4 | 17 s + bell | Draft |

---

## Route 1 — Locksmith

**Escape fantasy:** The Runner smashes open a key cabinet, finds the door code on the Classroom TV, and unboards the Pantry back door.

**Sequence:**

1. Pick up `T_Hammer` — shared tool spot.
2. Smash `R1_KeyCabinet` — Discussion room — hold 3 s, loud noise. Drops `R1_DoorKey`; pick it up.
3. Turn on `R1_TV` — Classroom — hold 2 s. Shows the passcode; grants passcode token `R1_Passcode`. The other boards (sliding whiteboard, 2 blackboards, sliding blackboard) show crossed-out decoy codes.
4. Pick up `T_Screwdriver` — shared tool spot.
5. Unscrew `R1_DoorBoards` — Pantry — hold 12 s.
6. Unlock `R1_BackDoor` — Pantry — hold 3 s, requires `R1_DoorKey` + `R1_Passcode`.
7. Escape — walk through `R1_BackDoor` into `R1_Exit`.

**Items and interactables:**

| Object ID | What it is | Location | Interaction | States |
|---|---|---|---|---|
| `T_Hammer` | Hammer | Shared spot | Pickup | Available, Held |
| `T_Screwdriver` | Screwdriver | Shared spot | Pickup | Available, Held |
| `R1_KeyCabinet` | Wall-mounted glass key cabinet | Discussion room | Hold 3 s, needs `T_Hammer` | Intact, Smashed |
| `R1_DoorKey` | Brass door key | Inside cabinet | Pickup after smash | Hidden, Available, Held |
| `R1_TV` | Existing Classroom TV (`Classroom/tv.fbx`) | Classroom | Hold 2 s | Off, On (shows code) |
| `R1_Passcode` | Passcode token (no mesh) | Granted by TV | Automatic | NotKnown, Known |
| `R1_DoorBoards` | Screwed-on boards/plate over door | Pantry back door | Hold 12 s, needs `T_Screwdriver` | Attached, Removed |
| `R1_BackDoor` | Back door with keypad lock | Pantry | Hold 3 s, needs key + passcode, boards removed | Locked, Unlocked, Open |
| `R1_Exit` | Escape trigger volume | Behind back door | Enter | Inactive, Active |

**Traversal constraints:** No special traversal. Key cabinet mounted at 1.4–1.6 m so it is visible over furniture. Boards cover the full door frame up to 2.1 m.

**Granny counterplay:** Cabinet smash is a loud noise cue. TV glow is visible from the Classroom doorway. The 12 s unscrew at the back door is the main point to contest; Pantry is a dead end, so Granny can trap the Runner there.

**Completion condition:** `R1_BackDoor` is Open and the Runner enters `R1_Exit`.

**Needs from others:**

- Ritu: wall space in the Discussion room for the cabinet; confirm the Pantry has an exterior-facing door frame for the back door.
- Chetana: hold-to-interact with progress, multi-item inventory, noise event, token-gated door (key + passcode + boards removed).
- Pulla: hold progress bar, noise cue for Granny, hit resets hold.

---

## Route 2 — Blackout

**Escape fantasy:** The Runner opens the UPS panel, replaces three blown fuses, rewires it, and the power surge opens the electric shutter.

**Sequence:**

1. Pick up `T_Screwdriver` — shared tool spot.
2. Unscrew `R2_PanelCover` — UPS room — hold 4 s.
3. Find `R2_Fuse1` (Pantry), `R2_Fuse2` (Washroom), `R2_Fuse3` (Small room).
4. Rewire `R2_Panel` — UPS room — hold 10 s, requires all 3 fuses. Fuses are inserted as part of the rewire.
5. Power returns: alarm sounds, `R2_Shutter` opens over 10 s. Granny gets a location ping at the shutter.
6. Escape — through `R2_Shutter` into `R2_Exit`.

**Items and interactables:**

| Object ID | What it is | Location | Interaction | States |
|---|---|---|---|---|
| `T_Screwdriver` | Screwdriver | Shared spot | Pickup | Available, Held |
| `R2_PanelCover` | Screwed metal cover on the UPS (`UPS/ups.obj`) | UPS room | Hold 4 s, needs screwdriver | Closed, Removed |
| `R2_Fuse1`–`R2_Fuse3` | Cartridge fuses | Pantry, Washroom, Small room | Pickup | Available, Held |
| `R2_Panel` | Exposed wiring/fuse slots | UPS room | Hold 10 s, needs cover removed + 3 fuses | Broken, Rewired |
| `R2_Shutter` | Electric roller shutter | Near UPS room | Automatic after rewire | Closed, Opening, Open |
| `R2_Exit` | Escape trigger volume | Behind shutter | Enter | Inactive, Active |

**Traversal constraints:** One fuse sits behind a crouch gap (1.3 m high, 1.0 m wide) so it cannot be grabbed while sprinting past.

**Granny counterplay:** Fuses are spread across three rooms. The shutter alarm and 10 s opening time give Granny a loud, guaranteed chance to reach the exit; this is the riskiest ending of the five.

**Completion condition:** `R2_Shutter` is Open and the Runner enters `R2_Exit`.

**Needs from others:**

- Ritu: shutter opening in an exterior wall near the UPS room; crouch-gap placement for one fuse.
- Chetana: hold interaction, multi-item inventory, timed shutter state, alarm/location-ping event.
- Pulla: progress bar, alarm cue and location ping for Granny.

---

## Route 3 — Hacker

**Escape fantasy:** The Runner hacks three computers to assemble the security door code, then opens the keypad.

**Sequence:**

1. Hack `R3_PC1` — Classroom — crouch in `R3_CrouchSpot1`, hold 7 s. Grants code token 1.
2. Hack `R3_PC2` — Discussion room — crouch in `R3_CrouchSpot2`, hold 7 s. Grants code token 2.
3. Hack `R3_PC3` — Labs — crouch in `R3_CrouchSpot3`, hold 7 s. Grants code token 3 and triggers an alarm that reveals the Runner's location to Granny for 3 s.

Hacking only works while the Runner is crouched inside that PC's crouch spot. The spot hides a crouched Runner from the doorway, so the Runner is not a sitting duck during the 7 s hold.
4. Pick up `T_Screwdriver` — shared tool spot.
5. Unscrew `R3_KeypadCover` — security door — hold 4 s.
6. Escape — keypad accepts the code automatically, `R3_SecurityDoor` opens, Runner enters `R3_Exit`.

**Items and interactables:**

| Object ID | What it is | Location | Interaction | States |
|---|---|---|---|---|
| `R3_PC1`–`R3_PC3` | Desktop computer + monitor | Classroom, Discussion room, Labs | Hold 7 s, only while crouched in its crouch spot | Locked, Hacking, Hacked |
| `R3_CrouchSpot1`–`3` | Low cover beside each PC (desk side panel or partition) + trigger volume | Next to each PC | Crouch inside to enable hacking | Empty, Occupied |
| `R3_CodeToken1`–`3` | Code fragments (no mesh) | Granted by PCs | Automatic | NotKnown, Known |
| `T_Screwdriver` | Screwdriver | Shared spot | Pickup | Available, Held |
| `R3_KeypadCover` | Screwed cover over keypad | Security door | Hold 4 s, needs screwdriver | Closed, Removed |
| `R3_SecurityDoor` | Metal security door with keypad | Near Labs | Automatic with 3 tokens + cover removed | Locked, Open |
| `R3_Exit` | Escape trigger volume | Behind door | Enter | Inactive, Active |

Items: 3 code tokens + screwdriver = 4.

**Traversal constraints:** Each crouch spot is a 1.0 m × 1.0 m floor area behind low cover 1.2–1.3 m high. The cover hides a crouched Runner (1.1 m) from the doorway but not a standing one (2.0 m). The PC's interact point is within the 3 m reach from inside the spot. The cover sits between the spot and the room's main door.

**Granny counterplay:** The crouch spot hides the Runner from the doorway, so Granny has to walk into the room and check behind the cover. Monitors still flicker and hum while hacking (noise cue), so Granny knows which room to check. The PC3 alarm reveals the Runner for 3 s. A Runner caught in the spot is cornered, since the cover blocks one side.

**Completion condition:** `R3_SecurityDoor` is Open and the Runner enters `R3_Exit`.

**Needs from others:**

- Ritu: a door frame near the Labs for the security door; desk space for each PC with a 1.0 m × 1.0 m crouch spot beside it.
- Chetana: hold interaction, code tokens, location-reveal event; hack allowed only while the Runner is crouched inside the crouch-spot volume.
- Pulla: progress bar, location reveal on Granny's HUD, crouch state exposed for the hack check.

---

## Route 4 — Tunnel

**Escape fantasy:** The Runner breaks through the concrete floor, digs down, and escapes through a padlocked hatch.

**Sequence:**

1. Pick up `R4_Shovel` — UPS room. While held, the Runner cannot sprint.
2. Pick up `T_Hammer` — shared tool spot.
3. Break `R4_Concrete` — Small room — hold 6 s, loud noise.
4. Dig `R4_DigSpot` — Small room — hold 10 s. Hole stays visible at each stage.
5. Pick up `T_Crowbar` — shared tool spot. Pry `R4_Hatch` lid — hold 5 s.
6. Pick up `R4_HatchKey` — Labs, behind a crouch gap.
7. Unlock `R4_Hatch` padlock — hold 3 s. Escape — drop through into `R4_Exit`.

**Items and interactables:**

| Object ID | What it is | Location | Interaction | States |
|---|---|---|---|---|
| `R4_Shovel` | Shovel | UPS room | Pickup, slows Runner | Available, Held |
| `T_Hammer` | Hammer | Shared spot | Pickup | Available, Held |
| `R4_Concrete` | Concrete floor patch | Small room | Hold 6 s, needs hammer | Intact, Broken |
| `R4_DigSpot` | Dirt hole (3 depth stages) | Small room | Hold 10 s, needs shovel | Stage0, Stage1, Stage2, Dug |
| `T_Crowbar` | Crowbar | Shared spot | Pickup | Available, Held |
| `R4_Hatch` | Metal hatch with padlock | In dig hole | Pry 5 s (crowbar), unlock 3 s (key) | Buried, Exposed, Pried, Unlocked, Open |
| `R4_HatchKey` | Heavy iron key | Labs, behind crouch gap | Pickup | Available, Held |
| `R4_Exit` | Escape trigger volume | Inside hatch | Enter | Inactive, Active |

Items: shovel, hammer, crowbar, hatch key = 4.

**Traversal constraints:** `R4_HatchKey` sits behind a crouch gap 1.3 m high, 1.0 m wide. Dig spot footprint 1.5 m × 1.5 m.

**Granny counterplay:** All holds happen at one spot and the hole is visible, so Granny can check progress. Concrete breaking is loud. To keep it fair, the dig room needs two entrances.

**Completion condition:** `R4_Hatch` is Open and the Runner enters `R4_Exit`.

**Needs from others:**

- Ritu: a dig room with two entrances; replaceable floor section 1.5 m × 1.5 m; crouch-gap spot in Labs.
- Chetana: hold interaction, staged dig state, multi-state hatch.
- Pulla: no-sprint while holding shovel, progress bar, noise cue.

---

## Route 5 — Frozen Key

**Escape fantasy:** The Runner pries open a jammed freezer, smashes the key out of a block of ice, and runs a jingling key to the main entrance.

**Sequence:**

1. Pick up `T_Crowbar` — shared tool spot.
2. Pry open `R5_Freezer` — Pantry — hold 5 s. Pick up `R5_IceBlock`. While held, the Runner cannot sprint.
3. Pick up `T_Hammer` — shared tool spot.
4. Place the ice on `R5_SmashBench` — UPS room — and smash it, hold 6 s, loud noise. Drops `R5_BellKey`; pick it up.
5. While holding `R5_BellKey`, a bell rings and Granny sees the Runner's location every 8 s.
6. Unlock `R5_MainEntrance` padlock — hold 6 s. Escape — through the main entrance into `R5_Exit`.

**Items and interactables:**

| Object ID | What it is | Location | Interaction | States |
|---|---|---|---|---|
| `T_Crowbar` | Crowbar | Shared spot | Pickup | Available, Held |
| `R5_Freezer` | Chest freezer with jammed lid | Pantry | Hold 5 s, needs crowbar | Jammed, Open |
| `R5_IceBlock` | Block of ice with a key frozen inside | Inside freezer | Pickup, slows Runner | Hidden, Available, Held, Placed |
| `T_Hammer` | Hammer | Shared spot | Pickup | Available, Held |
| `R5_SmashBench` | Metal workbench/anvil | UPS room | Place ice, hold 6 s, needs hammer | Empty, IcePlaced, Smashed |
| `R5_BellKey` | Key with a bell tied to it | Drops from ice | Pickup, pings location every 8 s | Hidden, Available, Held |
| `R5_MainEntrance` | Main entrance with chain + padlock | Main entrance | Hold 6 s, needs bell key | Locked, Unlocked, Open |
| `R5_Exit` | Escape trigger volume | Outside main entrance | Enter | Inactive, Active |

Items: crowbar, ice block, hammer, bell key = 4.

**Traversal constraints:** Smash bench is in a different room from the freezer, so the slowed Runner must carry the ice across the floor. Bench top at 0.9 m.

**Granny counterplay:** The ice carry is slow, the smash is loud, and the bell key reveals the Runner every 8 s all the way to the main entrance, which should be the longest run from the bench.

**Completion condition:** `R5_MainEntrance` is Open and the Runner enters `R5_Exit`.

**Needs from others:**

- Ritu: freezer floor space in the Pantry; bench space in the UPS room; main entrance chained shut.
- Chetana: hold interaction, place-item interaction, periodic location-ping while an item is held.
- Pulla: no-sprint while holding ice, bell sound, location ping on Granny's HUD.

---

## Shared mechanics needed (build once, used by all routes)

| Mechanic | Owner | Used by |
|---|---|---|
| Hold-to-interact with progress, drain on release, reset on hit | Chetana (state), Pulla (HUD) | All |
| Multi-item inventory and non-mesh tokens (codes, passcode) | Chetana | All |
| Noise cue to Granny | Chetana, Pulla | 1, 3, 4, 5 |
| Alarm / location reveal to Granny | Chetana, Pulla | 2, 3, 5 |
| Slowed movement while carrying a heavy item | Pulla | 4, 5 |
| Multi-condition doors/exits | Chetana | All |

## Models to build (Rahul)

Reuse existing imported assets where possible: Classroom TV (`Classroom/tv.fbx`), UPS (`UPS/ups.obj`), Labs tables.

| Batch | Models |
|---|---|
| Route 1 (first) | `T_Hammer`, `T_Screwdriver`, `R1_KeyCabinet` (intact + smashed), `R1_DoorKey`, `R1_DoorBoards`, `R1_BackDoor` keypad lock |
| Route 2 | `R2_PanelCover`, `R2_Fuse`, `R2_Panel` wiring, `R2_Shutter`, crouch-gap piece |
| Route 3 | `R3_PC` (computer + monitor), `R3_CrouchCover` (low cover, 1.2–1.3 m), `R3_KeypadCover`, `R3_SecurityDoor` keypad |
| Route 4 | `R4_Shovel`, `T_Crowbar`, `R4_Concrete` (intact + broken), `R4_DigSpot` (3 stages), `R4_Hatch` + padlock, `R4_HatchKey` |
| Route 5 | `R5_Freezer`, `R5_IceBlock`, `R5_SmashBench`, `R5_BellKey`, `R5_MainEntrance` chain + padlock |
