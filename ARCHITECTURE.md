# ArenaForge — Architecture

ArenaForge is a Unity editor tool that procedurally generates small Call-of-Duty-style arena maps:
roughly 60×60 metres, a two-storey building anchoring the middle, a structure on each flank, a fence
round the outside, scattered cover, two opposing spawns. The 60×60 map is the shape the tool was
built around rather than the only one it makes: how much is built on is decided by how much ground
there is, so a 400 × 400 m playfield comes out a town of three dozen buildings at the same density.

It never authors geometry in code. It selects, places and composes prefabs that already exist.

The feature that shapes every decision below is **non-destructive generation**. A map is not a saved
scene. It is a seed, a set of parameters, and a list of manual edit overrides. You can drag a crate
three metres to the left, change the cover density, regenerate, and the crate stays where you put it.

---

## 1. Three layers

| Layer             | Location           | May reference                | Owns |
|-------------------|--------------------|------------------------------|------|
| **Core**          | `Runtime/Core/`    | Newtonsoft.Json — nothing else | World data model, catalog, generators, constraint solving, validation, serialisation, seeded RNG, command/override model |
| **Unity adapter** | `Runtime/Unity/`   | Core, UnityEngine            | Logical-id → prefab binding; realising a world document into GameObjects |
| **Editor**        | `Editor/`          | Core, Unity adapter, UnityEditor | Tool window, scene handles, Undo integration, heatmap overlay |

Dependencies point one way only: Editor → Unity → Core. Core is a leaf.

`ArenaForge.Core.asmdef` sets `"noEngineReferences": true`, so this is not a convention anyone has to
remember — a `using UnityEngine;` in Core is a compile error, in CI as much as on a workstation. The
same asmdef sets `"overrideReferences": true` with a single precompiled reference to
`Newtonsoft.Json.dll`, so Core cannot quietly acquire other dependencies either.

**What the boundary is for, and what it is not for.** It exists so generation logic is testable
without an engine, without a scene, and without the editor loop — the 1000-seed property tests in a
later milestone are only affordable because a map can be generated and analysed as plain data. That
includes the line-of-sight analysis: visibility is computed with segment-vs-rectangle intersection in
Core, not with Unity physics raycasts.

Portability is a *consequence* of the boundary, not the goal. There is no second engine adapter, no
interchange format, and no plan to build one. If a second adapter ever appears, the boundary will be
refactored to fit it rather than guessed at now.

---

## 2. Seed + parameters + overrides

A `WorldDoc` holds a schema version, a parameters object, the generated object list, and an override
list. The seed lives inside the parameters rather than beside them: the generator takes one argument
describing what to build, so there is no second place a seed could disagree with the document it
produced. Resolving a document means running the generator and then applying overrides on top.

The obvious alternative is to generate once and save the resulting scene. That is rejected because it
throws away the generator the moment a human touches the output. With a baked scene, changing the
cover density means regenerating from scratch and losing every hand edit; in practice people stop
regenerating after the first manual pass, and the procedural tool becomes a one-shot scene
initialiser. Keeping the seed and parameters authoritative, with edits as a separate diff layer, means
regeneration stays cheap for the whole life of the map. That is the entire point of the project.

It also makes a map a few kilobytes of readable JSON rather than a large binary scene — reviewable in
a diff, cheap to store across hundreds of seeds in a test suite.

The cost is real and worth naming: an override refers to something the generator produced, and the
generator can stop producing it. Change the seed and `map/lane_mid/cover_03` may no longer exist.

**Orphaned overrides are surfaced, never dropped.** `Resolve()` returns them in a separate
`OrphanedOverrides` list, and the editor shows them as a warning with explicit keep/discard actions.
Silently discarding a user's edit because a generator parameter moved is the one failure mode that
would make the tool untrustworthy, so the design refuses to make that decision on the user's behalf.

### Two kinds of document, not one generalised one

A building is generated the same way a map is — from a seed and parameters — and it is a document
rather than a saved object, for all the reasons above. That made a decision unavoidable: either
`WorldDoc` generalises so its parameters become a typed payload, or a separate `BuildingDoc` shares
the override resolution. **A separate `BuildingDoc`.**

What the two documents actually share is one function: applying an override list to a list of
`PlacedObject`. A Move means the same thing on a crate in a lane and a crate on the second floor,
so that function moved out to `OverrideResolution` and both call it — the refactor CLAUDE.md rule 3
asks for once a second real case exists.

What they do not share is everything else. A map has parameters; a building has parameters *and* a
floor list. A map's ids start `map/`; a building's start `building/`. A map is analysed for
sightlines, connectivity and spawn fairness; a building is not analysed at all. Generalising
`WorldDoc` would have meant a polymorphic parameters payload with a discriminator, which is a
document-type plugin system with two members — and it would have put that machinery in the path of
serialisation, which every other suite in the project depends on. The cost of the split is one more
document type to serialise. The cost of the generalisation would have been paid in the one file
that must not become clever.

**`SchemaVersion` accounts for it, and old files still load.** The two documents have the same
shape from `generatedObjects` down, so a building read as a map would not fail — it would come back
as a map with default parameters, which is exactly the quiet failure the version check exists to
prevent. Both now carry a `kind` field, and `ArenaJson` checks it before the version, so a
mismatched file is refused for being the wrong kind rather than for a version line that was never
comparable. Adding that field took the world schema to version 2; version 1 is still read, because
a file that does not say what it is, written when a map was the only thing a document could be, is
a map. Reading upgrades it, so a v1 file loaded and saved again is a whole v2 document rather than
an old header over a new body. `BuildingDoc` starts its own numbering at 1.

**Stable ids** are readable generation paths — `map/lane_mid/structure_00`,
`map/lane_north/cover_07`, `map/lane_mid/structure_00/socket_02/prop_00` — not GUIDs and not content
hashes. A path is reproducible from the same seed and parameters, survives a regeneration that did not
disturb its branch, and tells a human reading a diff or a test failure where the object came from. A
hash changes when anything about the object changes, which is precisely wrong for something whose job
is to keep pointing at the same object across regenerations. User-added objects live under a separate
`user/` namespace so they can never collide with a generated id.

---

## 3. Logical asset ids and tag queries

Core refers to art as a **logical id** — `cover/low/crate_wood_01` — plus **tags**. It never sees a
prefab, a Unity GUID, or a file path. The Unity adapter owns a `CatalogAsset` that binds logical ids
to prefabs, and exports the engine-free half of that binding (id, tags, footprint, height, weight,
sockets) to the JSON catalog Core reads.

Unity GUIDs were rejected for three reasons. They are meaningless in a diff, so a world document
becomes unreviewable. They break when the art pack is reimported or swapped, which for a tool
explicitly built around a free third-party asset pack is a routine event, not an edge case. And they
would drag an engine concept into Core, collapsing the boundary in section 1.

The generator asks for *a piece of low cover about a metre wide*, not for a specific crate, so
selection is a tag query — require-all, require-any, exclude — weighted by each entry's `Weight`.
Deliberately not a query language: three sets, evaluated in a fixed order. Anything more is
speculative until a second caller needs it.

### Rows come from folders, because nobody fills in a hundred of them by hand

A catalog is the one part of this tool that is pure data entry, and data entry is the part people
stop doing. `CatalogSync` reads a folder of prefabs and writes the rows, because everything a row
needs has already been said somewhere in the project: a prefab in a `Walls` folder is a wall, and a
prefab is already exactly as big as it is.

**Size comes from a box collider where there is one and from the art where there is not.** The order
is not arbitrary — a collider is a size somebody chose and a mesh is whatever the art happens to
measure — but the fallback is not optional either. An art pack ships neither box colliders nor,
usually, colliders at all, and a row nothing measured keeps the defaults on `CatalogAsset.Row`,
which are a one-metre cube. Every prop in the pack then comes out the same size and every fence panel
comes out square, and a square footprint has no long axis for `WallRun` to lay along a line. That is
one unmeasured row turning into a boundary fence built at right angles to itself, with nothing
anywhere saying a number was missing. The sync reports how many rows it had to measure off the art,
because a mesh can be a little larger than the box a person would have drawn.

**Folders name tags.** The chain of folders under the root becomes a tag path and every prefix of it
is a tag, so `Covers/Low` produces `cover` and `cover/low` — the exact pair `CoverPlacer` queries.
A folder name the sync recognises *restarts* the path, so an organising folder above it — `Props`, a
vendor name, an art pack version — does not end up as the first segment of every tag underneath. One
it does not recognise *extends* the path, which is what lets `Covers/Low/Wooden` mean something
without the table having heard of wood. The recognised names are a closed list of the tags the
generators actually query, not a general vocabulary.

**The folder is not the authority over the catalog; the project is.** A catalog holds rows a folder
knows nothing about — an exported building, something bound by hand, art filed somewhere else
entirely — and a sync that removed a row for being absent from the folder it happened to scan would
delete somebody's work for having organised it differently. Weight and sockets survive a re-sync
because nothing in a folder says what they should be; footprint and height do not, so fixing a
collider and syncing again picks the fix up. That cuts both ways for art with no collider on it: a
size typed in by hand for a prefab the sync could not read is replaced the next time it can.

**A row with no prefab left is pruned, and that is a different question.** A deleted prefab leaves
its row behind pointing at nothing, and a row pointing at nothing is worse than no row: it is still
tagged, so every query still returns it and every weighted pick can still land on it — and what the
generator does with the one it picked is reserve the ground, write the placement, and realise
nothing. That is a hole in the map at a spot something was deliberately chosen for, and it is
invisible in the document, which records a perfectly ordinary object. The test is the row's own
reference rather than the scanned folder, so an orphan goes and a prefab the scan simply never
looked at stays. Pruned rows are counted in the sync's summary and named in the console, because
this is the one thing a sync does that takes something away.

The sweep runs **before** the scan is merged in. Delete a prefab, make a new one under the same
name, and the new one is a new asset with a new guid — so the dead row is not the row the scan
produces, it is a second row beside it, and leaving the sweep until afterwards left both in the
catalog. What that ordering costs is the weight and the sockets somebody typed onto the dead row,
which are facts about art the project no longer has.

**A prefab gets one row, however the scan found it.** A row is matched by the prefab it points at
first and by its logical id second, because an id is spelled from a folder and a file name and both
can change: rename `Crate.prefab`, or drag it from `Covers/Low` to `Covers/High`, and the reference
in the row follows the asset while the id no longer matches. Matching on the id alone wrote a second
row for a prefab that already had one, and nothing pruned it — both were live, both were tagged, and
the piece was twice as likely to be picked as anybody asked for. The id the surviving row keeps is
its own: a saved world document names the ids it placed, so renaming a row to follow a file would
quietly break every map already built from it. The tags do follow the folder, because those are what
a query asks about.

**A row says where the art is, not only how big it is.** The measured footprint is the box holding
every collider, and the row carries its offset from the pivot alongside its size; the same in the
vertical, where how far the colliders reach above the pivot is the height and how far they reach
below it is the `BaseOffset`. That is three numbers a row could once not say, and each of them was
being thrown away with a different consequence.

The vertical one is the obvious failure: a prefab modelled around its own centre — every Unity
primitive and a great deal of furniture — was stood half inside whatever it was on. The lift is added
in `Placement.AtQuarterTurn` rather than by each caller, so it is stated once as a fact about the
art: the constraint rules never see it, since all of them are about a rectangle on the ground, and
each caller goes on adding the height of its own surface — a storey's elevation, the terrain — on the
way out. What a piece has to fit *under* is `StandingHeight`, the two numbers together.

The horizontal one is quieter and was worse. A footprint that must be centred on the pivot can only
describe art modelled off to one side by growing to twice the size of the real thing, and erring
large is genuinely the safe direction for the rules that keep props out of each other — but it is the
wrong number for anything *cut to fit*. A flight of stairs pivoted at the foot of its run measured
nearly twice its length, so the stairwell opening cut from it came out four times the floor the
stairs actually cover, and the flight stood in the middle of a hole. The shell is therefore laid out
by where a piece's *art* goes rather than by where its pivot goes: `PivotFor` converts one to the
other, and for art centred on its pivot it is the identity, which is why nothing moved when it
arrived.

**The inspector groups the rows; the asset does not.** A synced art pack is a hundred rows, and a
flat list of them is a screen and a half in which finding the four walls means scrolling past ninety
crates. So `CatalogAssetEditor` files them under the first segment of their logical id —
`structure`, `cover`, `prop` — one foldout each with a count on it. That segment is already how the
generator asks for art, so the headings are the tool's own vocabulary rather than a second one
invented for the inspector.

It is a view and nothing more. `CatalogAsset` still holds one flat `List<Row>` in one order, and
every edit goes through `SerializedProperty`, so undo and the dirty flag behave as they do for a list
nobody has grouped. Grouping the stored rows instead would put a presentation decision into the file
the generator reads, and would make the row order mean something — where `ToCatalog` deliberately
sorts it away so that where a row sits in the inspector can never reach a generated map.

`Tools ▸ ArenaForge ▸ Setup Complete Workspace` scaffolds the folder layout this reads, with a fresh
catalog in it. The subfolders are not decoration — they are the names the sync recognises, so a
workspace and a sync fit together without anyone having to learn the table. It exists because the
demo is a *sample*: Package Manager overwrites it on re-import, and every project that starts by
adding rows to `DemoCatalog` loses them the first time it updates the package.

**It is one menu item because it is one job.** Scaffolding the folders, filing the demo art into
them and writing the starter art below were three commands, and the first two are useless on their
own: a workspace of empty folders syncs to an empty catalog, and art with nowhere to go cannot be
filed. `ArenaWorkspace.Setup` runs them in that order — with `Relocate` between the folders and the
demo, moving art an earlier layout filed into the folder it belongs in now — each of them
idempotent. Folders are
checked with `AssetDatabase.IsValidFolder` before they are created, because `CreateFolder` does not
refuse a name that is taken and quietly makes `Props 1` beside `Props`, which is a second workspace
holding half the art. Rerunning is what a person does when they cannot remember whether they ran it,
and it is what they are *meant* to do after re-importing the sample.

**The demo art is moved into the workspace rather than left where the sample put it.** The demo is
one flat folder of nine prefabs and seven materials, because that is what Package Manager copies —
and it is the only art most projects have on the first day. `DemoMigration` walks each prefab into
the folder that names its tag, so `structure_wall_panel_2m` lands in `Props/PropBuilding/Walls` and
the next sync reads `structure/wall` off the folder. `AssetDatabase.MoveAsset` keeps an asset's GUID, so the demo
scene and the demo catalog follow the move rather than breaking; copying instead would leave two of
everything with one bound to nothing.

Prefabs and the art they are made of are filed apart. A prop folder is read as a statement about
tags, so a material sitting in `PropBuilding/Walls` is a wall that is not a prefab — nothing the sync can
use and one more thing it would have to be told to ignore. Materials and meshes therefore go to
`Meshes`, alongside the starter art's own.

The migration knows a **closed list of exact names**, for the same reason the folder table is closed:
a rule over prefixes that filed everything called `structure_wall_*` would reach into a project's own
art and move it, and a tool that moves art you did not ask it to move is one you stop running. Every
way the art can be absent — never imported, renamed, deleted, already filed — is therefore the same
case, a name that is not found and nothing to move. A destination that is already taken is a warning
and not an overwrite: it means the workspace already holds something of that name, and destroying it
to make the counts tidy would be the tool wrecking the thing it was asked to organise.

**A folder says where a prop may be put, not what it is.** That is the whole reason the tree is as
deep as it is, and it is worth stating plainly because the shallow reading — one folder per kind of
object — produces a layout that cannot express the only question the placement stages ask.

```
Props/
├── PropBuilding/
│   ├── Decor/
│   │   ├── Decoration/         corners of a room
│   │   ├── Corners/            corners of a room, on two backs — an L-shaped sofa
│   │   ├── InteriorCovers/     scattered over a room's floor
│   │   ├── OutDecor/
│   │   │   ├── ContinueAround/ round the outside, tiled without a gap
│   │   │   └── UniqueGroup/    round the outside, in clusters
│   │   └── Centerpieces/       the middle of a room
│   ├── Doorways/ Floors/ Parapets/ Stairs/ Walls/ Windows/
├── Covers/  High/ Low/         tactical cover — what the firefight is fought around
├── fence/   WoodFence/ StoneFence/
├── Houses/
├── Road/    Kerb/              edging laid down each side of a carriageway
└── Spawns/
```

`Decoration` and `Decor/Centerpieces` are both furniture and they are two folders because one goes
in the corners and the other in the middle; `ContinueAround` and `UniqueGroup` are both exterior dressing
and differ only in whether a run of it closes. Collapse any pair and the distinction is gone before
the generator sees it.

`Corners` is the same argument once more. A piece filed there is modelled *as* a corner — an L-shaped
sofa with a back along negative Z and a second back along negative X — so it belongs in a true
geometric corner and in exactly one of the four turns there. Nothing filed in it answers the scatter's
query, which is what stops an L-sofa being drawn into the middle of a floor with half of it hanging
over open ground.

`CoverPlacer` queries `cover/low` and `cover/high` separately — high cover is what you stand behind
and low cover is what you crouch behind — so a workspace that stopped at `Covers` would file every
crate under a tag the placer never asks for and generate an arena with no cover in it.
`PropBuilding/Decor/Centerpieces` is a sideboard, spells `propbuilding/decor/centerpieces`, and must
never answer that query.

**`Covers` is the outdoor arena's, and nothing else may draw from it.** A room's floor scatter read
it once, which sounds like economy — cover is cover, and a crate is a crate wherever it stands — and
is not. What a workspace files in `Props/Covers` is the tactical furniture of an open arena, so the
query returned dumpsters, concrete barriers and sandbags to be strewn through somebody's living
room. The two folders answer two questions that only sound like one, so a room's clutter has a folder
of its own: `PropBuilding/Decor/InteriorCovers`, which `BuildingGenerator.InteriorCoverTag` queries
and the outdoor placer never sees. It is the third thing that goes inside a building, beside the
corner decor and the centrepiece, and the three are three folders for the reason every pair here is
two.

That folder was called `Covers` too, one level down, and what kept the two apart was the tag table's
`Branch` role alone: everything under `Decor` is read as that branch's own vocabulary, so nothing
filed there could ever answer a map query. The rule was right and it was invisible. Two folders one
level apart with the same name on them is a trap standing open whatever a table does with it — a
person filing a dumpster reads the folder name, and a row reading `propbuilding/decor/covers` does
not say at a glance which kind of cover it means — so the folder is named for what goes in it and
`covers` means one thing in a workspace again. `Branch` stays, because it is what makes the claim
true rather than conventional.

**A new layout that only applied to new workspaces would not be a layout.** A workspace is a folder
of somebody's own art, so `ArenaWorkspace.Relocate` walks a closed table of the folders earlier
versions created and moves what it finds — whole trees, so a project's own `Walls/Brick` survives as
`PropBuilding/Walls/Brick`. It moves rather than copies, for the reason the demo migration does: a
move keeps the GUID, so catalog rows and scene references follow. A source folder is deleted only
once it is empty, so a collision leaves the art and the folder it is in standing.

### The starter art, and why the tool authors that and nothing else

The last step of the setup writes four prefabs into the workspace — `ArenaAssetBuilder` — and it is
the one place in the package that builds geometry rather than
composing it. That is not a hole in section 3's rule. The rule is that the *generator* selects and
places art it did not make; a tool that writes an asset a person could have modelled leaves the
generator composing prefabs exactly as it would from a bought pack, and nothing in `Runtime/` knows
it exists.

It exists because the first art anyone has to hand is Unity's primitives, and a primitive is a fine
placeholder for a crate and a bad one for the shell. A stair made from a stretched cube has no steps
and a collider you cannot walk up; a parapet made from one is a metre-thick block round a roof; a
window made from one is not a window at all, because the thing a window is made of is the hole. All
three were what the tool was actually being judged on.

The window is five boxes rather than one with a hole in it, because a hole is not something a box
has: a panel under the sill, a lintel over the head, a jamb at each end and a bar down the middle of
the opening. What that buys is a hole in the *colliders* as much as in the mesh — a window you
cannot see or shoot through is a wall with a picture of a window on it, and the whole reason the
generator swaps these into exterior runs is so a building has sightlines out of it. It is in the
upper half of the wall, so what is left under the sill is the cover that makes the building worth
standing inside.

**A flight is hollow, and that is not a modelling opinion either.** Every step is a tread of its own
thickness with a riser closing the step under it, so the run is a diagonal with open air beneath it —
where the first version filled everything under the run down to the floor. Both look identical from
the outside and only one of them can be climbed twice: a building stacks its flights one above
another in the same shaft, so the underside of the flight above is the ceiling of the flight below.
Flat, that ceiling is at the height of the storey, and you climb the bottom of the run under three
metres of headroom and the top of it under none. Cut to the diagonal it is a constant 2.6 m over
every tread. The riser is what makes the tread affordable: a tread is thinner than its step is high,
so a flight of nothing but treads is a flight of slabs floating clear of one another with daylight
between them.

**The sizes are the point, more than the modelling.** The three pieces are a metre-square floor tile,
a metre-long fence and a two-by-three-metre flight, all modelled on their base and centred on their
footprint. A slab is laid in whole tiles and the stairwell opening is cut to the ones the flight
laps, so a metre tile is what lets a two-by-three flight open exactly two by three metres of floor
instead of rounding up to a two-metre grid; a metre of fence divides a roof of metre tiles, so the
ring closes with no overhang. The flight is two metres wide because the two-metre wall module leaves
corridors that wide, and a flight narrower than the space it stands in is a flight with a strip of
stairwell beside it. Art whose sizes disagree still works and simply carries the slack the catalog
declares — but the slack is the part a person sees.

The cost is the one the slab already has: a thicker floor tile leaves less headroom, so a 0.2 m tile
under a 3 m storey leaves 2.8 m for whatever stands on it. Art that misses that by more than a
few centimetres is still not offered to a floor at all; art that misses it by less is fitted to it.
That is the rule in section 7.

---

## 4. Determinism

Same seed and same parameters must produce byte-identical serialised output, on any machine, on any
run. Without that, the override system cannot work — a regeneration that shuffles ids invalidates
every edit — and the property-based test suite is meaningless because a failing seed cannot be
reproduced for inspection.

The rules that follow from it:

- **Own the RNG.** A PCG32/xorshift struct seeded from a `ulong`. Never `System.Random` (unspecified
  algorithm, varies across runtimes) and never `UnityEngine.Random` (global mutable state shared with
  everything else in the process).
- **Fork per subsystem.** `Fork(label)` derives a child stream by hashing the label with the parent
  seed, so lane generation and cover placement draw independently. Without this, adding one draw in an
  early stage reshuffles every later stage — parameters stop being independently tunable.
- **Stable hashing only.** FNV-1a. Never `string.GetHashCode`, which is randomised per process on
  modern .NET and will silently differ between two runs of the same seed.
- **No unordered iteration where order reaches the output.** A `Dictionary` or `HashSet` enumerates in
  an order that is not part of its contract. Anything feeding placement order, id assignment or
  serialisation iterates a `List` or an explicitly sorted key sequence. The catalog holds its entries
  in sorted order for exactly this reason.
- **Floats round-trip.** Serialised with `"R"` so a save/load cycle does not perturb a position by an
  ulp and change a constraint outcome.

Determinism is verified, not assumed: the test suites regenerate the same seed repeatedly and assert
byte-identical serialised documents.

**The ground is a function, not a grid.** `TerrainField` is three octaves of value noise plus a list
of graded pads, and every height is a hash of a lattice corner rather than a draw from a stream — so
a point can be asked for in any order, any number of times, from the generator or the realiser or a
test, and always comes back the same. A stream would make the answer depend on how many samples had
been taken before it, which is the one thing a field must not do. It also means a document carries a
rectangle and a height per structure rather than a heightmap, and the realiser rebuilds the ground
by replaying them.

---

## 5. Placement is a search under named rules

Cover is not placed, it is *proposed* and then judged. A candidate — a logical id and a pose — is
tested against a `ConstraintSet`, which answers with either an acceptance or the first rule that
refused it. Nothing reaches a world document without having been accepted.

The rules are a closed set of thirteen: inside the playfield, within a lane, on the grid, no overlap
within a margin, a minimum and a maximum distance from anything carrying a tag, not blocking a
doorway, clear of a spawn, against a wall, in a corner, in the centre, near a doorway, off a reserved
path. They are an enum and a switch, not an interface with thirteen implementations and not a rule
language. Every one of them is known here, Core is the only thing that evaluates them, and a
fourteenth would be a change to one switch. An extension point would be a guess about a caller that
does not exist.

The last three are one predicate read three ways. `AgainstWall` asks whether a footprint comes within
a distance of the region's edge along X *or* along Z; `InCorner` asks for both at once; `InCentre`
asks for neither. Keeping them one predicate is what makes a corner strictly stronger than a wall and
the middle of a room exactly the floor that is neither — three rules that could not drift apart into
three different ideas of how near a wall is near.

Five of them arrived after the first eight were written, and they are the evidence for that shape
rather than an exception to it: an enum member, a `case` label and a factory method each, with
nothing else in the project needing to know they exist. `InCentre` is the newest, and it cost one
`case` label built out of the predicate `AgainstWall` was already using. `OffReservedPath` takes no
distance where `NotBlockingDoorway` takes a clearance, because a walkway is not a thing to keep away
from — it is a strip of floor whose width already says how much room a person needs, and stating that
twice would let two callers disagree about how wide the same strip is.

`OffReservedPath` is also the one rule that has since been asked for by a second stage, and it took
the rule unchanged. A room's walkway and a road's carriageway are the same sentence — ground
something else has claimed to walk on, which a footprint may not stand in — so the road stage hands
its corridors to `ConstraintSet.AddReservedPath` and the cover placer names the rule it already had.
A parallel `OffRoad` would have been that sentence twice with two places to fix the next thing found
wrong with it. An outdoor verge looked like the thing the rule's unused distance was waiting for and
measured as the opposite: cover snaps to the placement grid, so a verge of even half a metre moves a
prop a whole cell further from the road and the road's own ground stops being covered from beside it
— 47 seeds in a thousand under the cover threshold, against none without. The rectangles a network reserves
are where the road is, and that is the whole of the width worth stating.

`AgainstWall`, `InCorner` and `InCentre` measure against the
*region* the set was built over rather than against a committed wall, because a building's walls are
art rather than placed objects — the rectangle a room's contents are proposed into is bounded by
exactly the walls that enclose it, so its edges *are* the walls. `NearDoorway` is `NotBlockingDoorway`
read the other way up, and is satisfied when no doorway has been declared, on the same terms
`MaxDistanceFrom` is satisfied when nothing committed carries its tag: a rule with nothing to measure
from cannot reject.

**The refusal is kept, not reduced to a boolean.** It is the only useful thing to say about a
placement that did not happen, and it is what the placement statistics in the document report:
`cover_rejected_no_overlap: 157` says a map is saturated, where a bare count of failures says only
that something went wrong.

**The sampler and the rules are kept deliberately in step.** Positions come from Poisson-disk
sampling over the cells a `PlacementGrid` still has open, and that grid claims exactly what the
constraints enforce — a structure's footprint plus its clearance, a spawn plus its apron, a
doorway plus the room to walk through it — grown by the reach of the *smallest* prop in the
catalog. Sizing that margin for the smallest rather than the largest is the difference between a
sampler that offers only certainties and one that offers everything worth trying: a gap that a
barrier cannot use may still take a crate, and the retry that swaps one for the other is the
placer's main way of filling a map. The grid is a hint. The constraint set decides.

The sampler asks for more positions than the target needs and shuffles them before trying any,
because Bridson's algorithm grows outward from its first sample: taking its output in order would
crowd the props around wherever that first sample fell. It also reseeds when its frontier dies
rather than stopping, so a lane a building has cut in two is filled on both sides of the building.

### Outside a building there is no sampler, because a line is not a scatter

`ExteriorPlacer` and `PerimeterFence` are the two placement stages with no grid and no Poisson disk
in them, and the reason is what they are placing. A run of planting round a building looks like
planting because each piece starts where the last one stopped; two independently sampled positions
are never flush, and a grid a metre across rounds the joins off into a row of separate bushes. So a
run is *tiled*: a cursor walks each side of a rectangle, the drawn piece is seated from that side's
own coordinate, and the cursor advances by that piece's own length. It is the way `BuildingGenerator`
tiles a wall run, pointed outward.

The cursor is `WallRun`, and it lives beside the two stages rather than inside either because they
walk it round different things for different reasons — one round a building it is dressing, the other
round the playfield it is closing. Everything either stage decides for itself is a parameter: which
art, which gaps, which rules, where a piece sits across the run, and what to do with one that was
accepted. Nothing is committed there; the accepting caller owns that, because only it knows what the
piece is called and what it belongs to.

What breaks a run is therefore always a rule or a rectangle, and the constraint set is unchanged —
the same `InsidePlayfield`, `ClearOfSpawn`, `NotBlockingDoorway` and `NoOverlap` everything else is
judged by, with a zero margin so two pieces may touch. A gap in a hedge is the approach to a doorway,
the heap of barrels the cluster pass stood there first, or the edge of the playfield, and it is
visibly one of those three rather than a spacing constant somebody chose. Nine pieces in ten come out
with another flush against them.

**The two breaks that have to happen are rectangles rather than rejections**, computed before the
walk begins and skipped over by it — the same mechanism the yard fence opens its gates with, and for
the same reason. One is the approach to each doorway the structure declares: a doorway a hedge has
grown across is a building with no way in, which is the same class of failure as a fence that closes
and deserves the same guarantee rather than the constraint's near-certainty. The other is the
stretch of wall a heap has taken. `NoOverlap` alone was not enough for that one, and could not be: a
heap is three separate pieces, so the ground beside a barrel and between two of them overlaps
nothing, and a run seated flush tiled straight into it — planting threaded through somebody's stack
of barrels and out the far side. So a heap reserves the ground it covers, carried back to the wall,
and the run stops before it and picks up after it.

**Which is also why a heap is seated flush.** It was drawn a hand's width out from the wall so that
`NoOverlap` with a margin could accept it, and a hand's width of open ground behind a heap is exactly
the width of a bush. The heap now touches the wall at zero clearance, as the run does, and the two
passes seat their art the same way — which leaves the reservation, rather than a gap in the geometry,
as the thing that keeps them out of each other.

"Always a rule" is exact rather than nearly exact, and it costs one line to keep it that way. A slot
is opened only when the shortest piece on offer fits in it, so a slot where all four draws overshot
is one the draws were unlucky in and not one the art cannot fill. Ending the run there — which is
what it used to do — left a hole up to a whole panel wide at the end of every face, readable by a
player as "the fence ran out" rather than as "the door is there". The last resort is now the shortest
piece, which takes no draw and so leaves the seeded stream reading exactly as it did.

The three folders under `OutDecor` and `fence` are three passes because each is a different claim
about where art may go, which is what a folder means here: `ContinueAround` is tiled, `UniqueGroup`
is a handful of pieces drawn round one anchor on one face, and `WoodFence` is tiled round the yard
rather than round the wall. Clusters go down before the run, because a heap somewhere specific is a
feature and a hedge is the filler between features — and a heap is held to the face it was given,
art and all, so two heaps cannot meet round a corner and become one heap bent through ninety
degrees.

**A fence may never close, and that is arranged rather than hoped for.** The gaps are rectangles
computed before a segment is drawn — one straight out from each of the house's own doorways, plus one
more on the ring — and the tiling skips any segment that would stand in one. Leaving it to
`NotBlockingDoorway` would not have worked: a doorway's clearance reaches two metres out from the
wall and the fence stands two and a half metres out, so the rule that keeps a bush out of a doorway
does not reach the fence at all. The drawn gap is added every time and not only when a house declared
no doorway, which is what makes "this fence is not a closed loop" a property of the algorithm rather
than of the metadata the structure stage happened to write.

### The boundary is the one run that faces inward

`PerimeterFence` tiles `fence/StoneFence` along all four sides of the playfield, and it is the one
stage in the tool that asks where the map *stops* rather than where a thing may go. Nothing about
where is drawn: the four sides are the four sides on every seed, and the stream decides only which
piece of art fills each slot.

**Stone, and only stone.** The two fence folders are two different things and this stage uses one of
them: `StoneFence` is the edge of the level and `WoodFence` is somebody's garden, which is what
`ExteriorPlacer` fences a yard with. A workspace with nothing in `StoneFence` gets no boundary and
draws nothing, rather than a boundary made of garden panels.

A hedge is laid on the outside of the wall it hugs. A boundary is laid on the inside of the line it
marks, because the line is the edge of the world and there is no outside to lay anything on — so the
four runs are built facing inward and each segment is seated flush from the playfield's own edge,
growing inward by its own thickness. A run straddling a shared line instead would leave a thin panel
a sliver of ground behind it and a thick one flush, which is a boundary whose flushness depends on
which art the draw came up with.

**Two rules, and the two that are absent are absent on purpose.** A spawn area spans the full width of
the map at its own end, so the low and high edges of the playfield lie *inside* one: a boundary held
`ClearOfSpawn` would be a boundary with its two ends missing, which are exactly the two ends a player
runs at. `NotBlockingDoorway` is not asked either — a hole in a hedge is a feature and a hole in a
world boundary is a way out of the level. What is left is `InsidePlayfield` and `NoOverlap`.

**Nothing else may stand where the boundary goes, and that is arranged twice.** It used to be the
other way round — the boundary ran after the dressing and yielded to whatever it found, on the
reading that a building flush against the playfield is the boundary along its own wall. That reading
holds while boundary art is a couple of metres long and collapses at the length real stone comes in.
The pack this tool is used with ships a thirty-metre panel, which needs thirty clear metres: a lamp
post the dressing had already stood on the edge refused a panel and took half a side of the level
with it, and a ten-metre building standing flush against a seventy-five-metre edge left seventeen
metres either side of itself, neither of which will take one. Measured over the pack's own catalog,
the default map came out with three quarters of its fence and an eighty-metre map with half.

So the boundary goes down **first** of the three anchored stages and `ExteriorPlacer` is handed it to
place around; and structures are held one panel-thickness inside the playfield —
`ArenaLayoutGenerator.Buildable`, measured off the catalog and zero when there is no stone in it — so
the fence tiles behind them, flush against the wall. It costs a third of a metre of playfield on the
sample art. A hedge can move along a metre and a hole in the edge of the world cannot.

**Which is why a structure may not put a declared door against the boundary.** Tiling the fence
behind a building rather than yielding to it has an obvious cost the first version did not pay: if
that wall has a door in it, the fence now stands across the door. The boundary cannot be the one to
give way — `NotBlockingDoorway` is absent from it on purpose, and a gate cut in the edge of the world
is a way out of the level — so the building gives way instead. `DoorsCanBeReached` requires every
doorway's approach, `CoverPlacer.DoorwayClearance` all round it, to fit inside the buildable
rectangle: not off the map, and not into the fence round it. A door a metre from the boundary is
unusable whether or not anything stands in front of it, so what is being refused is a bad placement
rather than a good one being spoiled. Four quarter turns are tried at every position, so the usual
outcome is a building that turns its doors along the lane rather than a cell left empty.

The doorways are therefore worked out *before* a placement is accepted and written to metadata only
once it is — the same rule the terrain follows, where a candidate no rule wanted leaves no trace.

**It closes, and closing it costs a doubled panel here and there.** Panels are indivisible and
section 3 says nothing here rescales art, so tiling a line from one end always stops short by a
remainder — up to a whole panel of bare ground at the end of every face. A slot the rules refused
leaves the same bare ground in the middle of the run, and once the cursor is within a panel of the
end it never gets going again, so one refusal can cost everything after it. Both are the same fault:
ground nothing stood on because of where the cursor happened to land.

`WallRun.CloseGaps` therefore goes back over every stretch the walk left bare — the remainder at the
end and the holes in the middle alike — and lays pieces into it *backwards* from its far end, so the
last one lands exactly on the line and the doubling up happens over ground the run already covers. A
stretch with something standing part way along it refuses everything seated from the end nearer the
obstruction, so a second pass fills it forwards from the other end; a stretch that refuses both at
once is one nothing could have stood in, and it stays open. A post in front of a post is the cheapest
thing a run can be short of; a hole a player walks through is the most expensive. Closing pieces are
judged against everything on the map except the run they are closing —
`ConstraintSet.Evaluate(Placement, int)` — so overlapping the run's own tail is allowed, overlapping a
building is not, and one that would stand in a gate is not placed.

That widens the one overlap a run is allowed to have in it. It used to be exactly "the last piece
over the second to last", which a test could state as adjacent numbering; it is now "two pieces of
one run", because a closing piece is stood up after the walk has finished and doubles up on whichever
piece happened to end where it started. Everything the property was written for — two heaps in one
place, a hedge through a house, two yards' fences meeting — is still ruled out.

**The four corners go round like a pinwheel.** Two runs reach every corner and only one of them may
have it: a run standing in ground the other can occupy is a rejection, and a rejection at a corner is
a hole a metre from the one place a player pressed into the edge of the level ends up. So each run
takes the corner at one of its ends and gives up the one at the other, going round, and each corner is
claimed exactly once. `WallRun.CornerSlack` — a millimetre — is the difference between that being a
fact about the geometry and a fact about which way two different float sums rounded.

Over seeds 1..200 of the default map the boundary stands on 99.5% of the perimeter, and the half a
percent is not open ground: it is the four corners, which the per-edge measurement credits to the run
that turns there rather than to the run that owns them. Every corner of every playfield is closed on
every seed. Measured against the pack's own catalog — thirty-metre panels, eighteen-metre houses —
every playfield from 40 × 40 to 400 × 400 m now stands at exactly that floor, which is to say the
boundary is complete and only the corner bookkeeping is missing from the figure.

**The ring is dead level, and the foundations do the rest.** Everything else on the ground is stood on
it; a fence panel is *planted* — pivot on the ground, whatever the art has below the pivot under it —
because stone boundary art is modelled with a deep footing and standing that on the surface leaves the
wall hovering over its own foundation. The height every panel takes is not the ground under that
panel, though: it is `RingHeight`, the highest ground anywhere on the path, sampled once before a
single segment goes down. Seating each panel on its own ground gives a wall that steps down every
slope it crosses — which is how a garden fence is built and is not how the edge of a level is built,
because every step is a ledge and the top edge goes up and down. One height for the whole ring gives a
level top all the way round and buries the footings in the low ground. What it costs is a wall that
stands taller than a panel over a hollow, which is the right way for the edge of the world to fail.

The structures have graded their foundations into the field by the time this runs, so a building on a
pad against the edge raises the ring with it — the pad is ground now, and a boundary the pad rose
through would be a boundary you could walk over from the doorstep.

Cover is scattered after all of this and is given every anchored placement, so the free stage places
around the anchored ones — the same order, and the same argument, as cover coming after the
structures.

### The roads go down between the two, and put nothing down

`RoadNetwork` is the only stage of the pipeline whose output is a reservation rather than an object,
and it is wedged between the anchored stages and the cover for reasons that leave it nowhere else to
go. It is laid to reach the doorways the structures declared, so it cannot run before they are
placed; and the ground it covers is ground a crate may not stand in, so it cannot run after the cover
is scattered. Computed afterwards — which is where it sat while nothing consumed it — it left crates
standing in the carriageway on a large share of seeds.

**What it hands on is rectangles.** A carriageway is a polyline and a width, which is a swept polygon
and not something any rule in section 5 can be asked about. `RoadNetwork.Corridors` is that strip as
axis-aligned `Rect2`s — one per stretch of centreline, grown by half the carriageway, overlapping at
the joints exactly as the polyline's own segments do. A stretch is not always a whole segment: the
smoothing string-pulls, so a segment is routinely tens of metres long, and the box round a diagonal
one that length is a square of that side. Cut so that no piece strays more than a quarter of a
carriageway off its own box's long axis, an axis-aligned run stays whole and the network's
reservation comes to 1.30 times the ground the carriageways cover rather than the 1.56 it claims
uncut.

**"A road does not run through a building" is a claim about the centreline, and the three readings of
it are worth separating.** The router shuts the ground a structure stands on — its recorded
foundation pad, which is the art's footprint with a metre of apron round it — and `CorridorClear`
holds a smoothed line half a carriageway off that pad before it will keep it. But the *route* is
cells, and the nearest cell centre a route may use sits half a cell off the shut ground, so the three
measures come apart by exactly that half cell. Over the relief sweep of `RoadValidationTests`: the
**centreline** never enters a footprint, on any of a thousand seeds, which is the guarantee and the
thing anybody means by it. The **swept carriageway** reaches into one on 392 seeds, by at most 0.5 m
— the apron absorbing the difference, which is what the apron is. The **corridor rectangles** clip a
structure on 414 of 3000, again by at most 0.5 m, and that is the conservatism that buys cover a box
it can be judged against at all. None of the three is a defect and only the first is promised;
tightening the second would mean enforcing clearance on the strip rather than on the cells, which is
a different router.

**Cover keeps off the corridors and prefers to stand beside them**, and the second half is what makes
a road worth fighting over rather than a strip of texture. It is a preference and not a rule: the
sampler is asked for more positions than a lane's target needs, so the ones within a metre of a
carriageway are simply tried first and the rest of the lane gets the remainder. Expressed as a rule it
would throw that remainder away — a hard "within so far of a road" rejects the far corners of a lane
outright, and the lane comes out with a third of the cover it asked for.

The preference is not decoration. Reserving a fifth of the default map as carriageway costs cover
coverage, because the metric counts a road as floor that wants cover within reach of it and no cover
may stand there. Over seeds 1..1000 of the default map, the roadless sweep runs 0.620 to 0.776 with a
mean of 0.712; with roads and no preference it runs down to 0.567 and ten seeds fall under the 0.6
threshold; with the preference it runs 0.602 to 0.771 with a mean of 0.700 and none do. The margin on
the worst seed is 0.002 against the roadless map's 0.020, and that is worth knowing rather than
rounding off: a road network on a sixty-metre arena is most of a lane gap wide and the arena has two
of them.

**A network is a function of the finished document, not of the moment it was laid.** The router
prices open ground higher than sheltered ground, so what is on the map when it runs changes where the
roads go — and the roads run before the cover. Cover is therefore left out of that measure entirely,
which is what makes rebuilding the network from a saved document give back the network its own cover
was placed around. Without that, a map's roads would move under the crates that were placed against
them the moment anything asked where they were.

So is the kerbing, and for the same reason once removed: it is placed *from* a network, so a rebuild
that counted it would route the next roads round the edging the last ones were given. The predicate
is one predicate — `RoadNetwork.IsPlacedAfterTheRoads` — because "everything the pipeline puts down
after the roads" is the actual rule and cover was only ever the first member of it.

### The road surface is painted and its edges are placed

The roads are the one feature of a map that is partly ground and partly objects, and the split is the
same one section 3 draws everywhere else. A carriageway is a *surface* — there is nothing to compose
it out of, and a strip of triangles down a polyline is a model this tool may not author — so it is
painted into a terrain's splat map by `TerrainSplatWriter`, on the same terms `TerrainWriter` hands
over a grid of heights. What stands up beside it is art, so `RoadKerbs` places it.

**`TerrainSplatWriter` rasterises the whole network into one buffer and uploads it once.** Painting a
path at a time is the obvious shape and it is quadratic in disguise: every `SetAlphamaps` re-uploads
the region it was given and Unity re-derives the basemap from it, so a network of thirty branches
pays for thirty full passes over ground that only changes once. The window is the bounding box of
`RoadNetwork.Corridors` with a texel of slack for the feather, read back before it is written so that
what the project already painted there is scaled rather than replaced.

**Every touched texel is renormalised, and that is what separates a road from a mud track.** Unity
divides an alphamap texel by its own total when it reads one, so writing the road channel to one over
a ground channel already at one gives a road at fifty per cent with the field showing through it —
and it does not read as a bug, it reads as a badly made road. So the other layers of a touched texel
are scaled to exactly what the road's coverage leaves them, and the weights sum to one before Unity
sees them. A texel whose other layers are already at nothing has nothing to scale and the road takes
all of it, which is the one case where the painted weight is not the coverage and is also what the
terrain would have renormalised it to anyway.

**Under four texels across, a road is a dashed line and no blending fixes it.** The information is
not in the buffer, so the writer warns at write time, naming the texel size, the narrowest
carriageway on the map, and the resolution that would be enough. It warns rather than refuses: a
coarse splat is still better than no surface, and how much alphamap memory a project can afford is
not a decision a writer gets to make.

**It costs the promise that a map is a few kilobytes of readable JSON, and only for the surface.** A
`TerrainData` is a binary asset — not diffable, not mergeable, not legible without opening Unity —
and that is a real thing to give up. What keeps the bargain honest is that the terrain stays
*derived*, on exactly the terms the heightmap already is: the whole dirty rect is rebuilt from the
document's own network on every write, so a carve made by hand is replaced rather than compounded,
and a lost `TerrainData` costs a regenerate rather than a map. The document is still the only thing
worth putting in version control.

**`RoadKerbs` is `WallRun` with a different line in it.** A kerb is the same problem as a hedge and a
boundary — art laid end to end so each piece starts where the last one stopped — so it walks the same
cursor, closes its gaps the same way, and is judged by the same `ConstraintSet`. What is different is
the line: a carriageway is a polyline at whatever angle the router found it, where every other run in
the tool walks the side of a rectangle. `WallRun.Face.Along` is that line. The run is seated against
the centreline at half the segment's own width, which is `WallRun.Flush` with a gap in it, so a
kerb's inner face lands exactly on the edge of the carriageway and the reservation the cover stage
places around is untouched by it. Every piece is a `PlacedObject` with a readable id —
`map/road/artery_00/kerb_004` — so an override survives a regeneration.

**An oblique run is judged against everything except itself, and the reason is the box.** A footprint
is a `Rect2` and every rule in section 5 is stated over one, so a piece at forty-five degrees is
judged by the square round it, which for anything longer than it is wide is far bigger than the
piece. Two of them laid end to end always overlap whatever the art is, so a run seated flush would
have every second piece refused for standing in the one before it. `WallRun` therefore evaluates an
oblique run's pieces against the map as it stood before the run began —
`ConstraintSet.Evaluate(Placement, int)`, which `CloseGaps` already needed for the tail of a run and
which an oblique run needs for the whole of one. Nothing else changes: a kerb through a building is
still refused, and so is one outside the playfield.

**The line stays where the router put it and the art is what rounds.** A piece is turned to the
nearest of the twenty-four `YawStep`s, so a run at forty degrees is tiled from pieces stood at
forty-five, each centred on the line and skewed across it by at most seven and a half degrees.
Snapping the line instead would move the thing being described, and for a road that is the one thing
that may not move — the carriageway was routed, graded and reserved before a kerb was thought about.
`YawStep.Nearest` picks the step by comparing the direction against the table's own literals rather
than by dividing an `atan2` by fifteen, because a direction falling on the boundary between two steps
would otherwise be decided by the last ulp of somebody's trigonometry and two machines would generate
two different maps.

**A run stops wherever another carriageway covers it.** The router's whole cost decay exists to make
a branch join a trunk that is already there, so two carriageways over the same ground is the ordinary
case; what is a defect is edging the one that is inside the other, which comes out as a line of stone
laid across a road. Measured over forty default seeds that was one kerb in nine. So each side's *kerb
line* — not the road's centre — is cut where it enters any other carriageway or any junction disc,
and the cut is per side because a road running along one side of another leaves the far one alone.
Asking about the centreline instead gives up both sides and costs a third of all the kerbing.

What survives that is the inside of a bend: a run on the concave side of a corner is nearer the far
arm of its own polyline than half a carriageway, so the last piece before a bend clips the road it is
edging. Nine kerbs in a thousand, all but two of them under 40 cm in. Cutting it means either a mitre
setback computed through a tangent — a transcendental in the middle of a deterministic placement — or
shutting a run against its own polyline, whose collinear stretches then sit exactly on the boundary
of the test and are decided by the last bit of a float. A kerb that follows the inside of a corner is
what a kerb does at a corner, and neither of those is worth what it costs.

**A workspace with nothing in `Props/Road/Kerb` gets no kerbs**, takes no draw and writes no
statistics — the bargain `PerimeterFence` makes with an empty `StoneFence` folder. `RoadKerbTests`
holds seeds 1..200 of the default map *with* roads on it to the bytes they serialised to in a build
with no kerb stage in the pipeline, which is the same guarantee `RoadPipelineTests` gives the maps
that never asked for a road.

**And `roadDensity` of zero is exactly the map it always was.** No road is laid, no corridor is
reserved, no position is preferred over another, and the rule that keeps cover off a carriageway has
nothing to reject. Asserted rather than reasoned: `RoadPipelineTests` holds seeds 1..200 of the
default map to the bytes they serialised to in a build with no road stage in the pipeline at all.

### The verge is spaced by distance, and the events go down first

`RoadFurniture` stands what a street has on it — lamp posts, benches, bins, a shelter — on the ground
behind the kerbing, and it is the one placement stage in the tool that is neither a scatter nor a
tiled run. A scatter would put a bench anywhere there was room for one; a run would lay lamp posts
end to end. What a verge has is pieces a spacing apart, and the spacing is the whole difficulty.

**Distance along the road, not position along the polyline.** A carriageway's centreline is smoothed
and string-pulled, so its vertices crowd round the bends and stand tens of metres apart on the
straights. Stepping that list — or interpolating a fraction of it — bunches props on the curves and
stretches them on the straights, whatever spacing the stage claims to be using. So each polyline gets
an `ArcTable` once: the cumulative distance to each of its own points, binary-searched and
interpolated within the piece. A polyline is straight between its points, so the table is exact
rather than a sampling of a curve, and every position the stage decides is a distance in metres.

**The events go down first and the spacing fills what is left.** Furniture at an even interval is the
loudest signal a level was generated — a row of identical lamp posts at exactly nine metres is a
thing no street has — so the pieces that carry the meaning are placed at the places a road has
something to mark, and the stretches between them are filled at a spacing jittered by a third off
this stage's own `Rng.Fork("road_furniture")` stream.

**Four kinds of event, two collectors, because a junction answers three of them.** In this network a
road's class only changes where two roads meet: a branch is an `Attachment` on the trunk it joins, so
the corner of that junction *is* the road-class transition, and a `Portal` is the same corner set
back by the clearance its doorway keeps — which is what puts a bin on the approach to a door rather
than in it. A `Terminal` gets no event, because a spawn is ground deliberately kept clear of props.
Only the bends need a collector of their own.

**A bend is measured off its own chord rather than through an angle.** How far the road runs off the
straight line between the points four metres either side of it is a length in metres, and comparing
lengths is arithmetic; turning a polyline into an angle is a call into trigonometry, which is not
bit-identical across runtimes — the argument `YawStep` makes about rotations, applied to measuring a
curve. The threshold is stated as a radius, sixteen metres, and converted to the depth an arc of that
radius reaches off its chord. Reading it over a window rather than at a vertex is what makes a
smoothed bend one apex instead of the six small turns it is built from.

**Turned by the road, seated beyond the kerb, stood on the ground.** The rotation is the run's own
tangent quantised to the nearest `YawStep`, through `WallRun.Face.Along` — the same cursor the kerbs
walk, asked for a yaw rather than for a tiling. Across the run a piece sits clear of the carriageway
by the thickest kerb the catalog holds plus two metres of verge. The height is `TerrainField.HeightAt`
with `Placement.AtYawStep` adding the entry's own `BaseOffset`, as for every other placed object on
the map: a raycast would answer with whatever colliders a scene happened to have, would depend on
physics settings that are not inputs to the document, and would put the result outside `WorldDoc`
where no override could reach it.

**Every candidate goes through `ConstraintSet`, and `OffReservedPath` is the one that does the work.**
A bench in a doorway is the same bug as a crate in one, so the rules are `InsidePlayfield`,
`ClearOfSpawn`, `NotBlockingDoorway`, `OffReservedPath` and `NoOverlap`. The reservation is the same
conservative box round a diagonal carriageway that `RoadKerbs` cannot be held to — a kerb has to be
flush against the road and a piece of furniture does not, so this one is held to it, and the verge is
sized against what it costs. Measured over seeds 1..200 at a road density of 1, the share of
candidates accepted runs 16.7% at no verge, 30.8% at a metre and a half, 32.7% at two metres and
33.2% at three, while the pieces refused for leaving the playfield go 4, 23, 69 over the last three.
Two metres is where the figure flattens and the cost does not. What is left is the braid: a third of
the network is two carriageways over the same ground by design, and the verge of a road running
inside another road is that other road, which no verge reaches. A position whose drawn side is
covered is offered the other one before it is given up.

**A workspace with nothing in `Props/Road/Furniture` gets no furniture** — the same bargain, and
`RoadFurnitureTests` holds seeds 1..200 of the kerbed default map to the bytes they serialised to in
a build with no furniture stage in the pipeline.

### A road does not make a map want less cover

Two things on a map are cover and one of them was not placed as cover, and the cover budget has to
say so at both ends.

**The denominator.** The floor a lane's cover target is counted off already leaves the carriageways
in, because a player moves through a road: a lane with one down it wants exactly the cover it wanted
without one. A kerb along that road's edge and a lamp post on its verge are the same claim about the
same ground, and they were not being treated that way — the road stages' own art was claimed into
that floor along with the hedges, so a map shrank its own cover target by everything a road had stood
on it. Measured over seeds 1..1000 of the default map at a road density of 1, cover coverage ran to a
mean of 0.598 with kerbing in the catalog against 0.700 without, and 531 of the thousand seeds fell
under the 0.6 threshold where none had. `CoverPlacer.IsRoadside` is the line that names the two
stages; the art still takes its own footprint out of the grid the *sampler* draws from, because
nothing may be placed inside a bench.

The distinction is not the one the dressing round a building makes. A hedge and a heap of barrels are
placed to be in the way — they are the yard, and the floor they cover has become something else. A
kerb is a line on the ground and a lamp post is a post.

**The numerator.** `MapAnalyzer` counts a piece of street furniture towards the floor being within
reach of cover, because in a sixty-metre arena it is: a player caught in the open beside a road
reaches the bench, and the bench is standing where a crate otherwise would. Kerbing does not count,
and needs no exclusion to not count — it carries no cover tag. That is a separate question from
whether either blocks a sightline, which is asked of every object on the map by `Occluder.TryCreate`
and answered by height alone: the shelter stands across the eye line and the bench does not, exactly
as high cover does and low cover does not.

### How many structures a map has is decided by the ground, and one of them is a demand

Every property in this project holds of a map with one hut on it and nothing else. The hut is inside
the playfield, it is clear of both spawns, the spawns are connected, the floor is covered — a
generator that quietly gave up on the rest of the map would come out green everywhere and produce an
empty field. So one structure is demanded outright, and how many more there are is a question about
the ground rather than a number in a file.

**The ground is divided into layout cells.** The run between the two spawn areas, across the lane
bands — not the spawns, which nothing may stand in, and not the gaps between the bands, which are the
routes past the structures — is cut into cells of about `MetresPerStructure`, which is 2500 m², a
fifty-metre square. Both axes are cut, so a band a hundred metres wide on a large map carries rows of
buildings across it rather than one row down the middle of a field. The count is rounded and never
below one per lane: flooring would throw away the remainder of every band, and the floor of one keeps
a small map's lanes from being skipped.

Fifty metres is the size of the arena this tool was built for, and that is the whole of the
argument. The default 60 × 60 m map comes out at one cell per lane — three structures, which is the
composition it has always had and the map every threshold in this project was measured on — so the
density that map has is the density every larger map is tiled at. A 40 × 40 m map has room for one or
two, a 100 × 100 m map for about three, a 200 × 200 m for nine, and a 400 × 400 m for about three
dozen. The rule it replaced was one building and a house per 1800 m² *capped at the number of flank
lanes*, which meant every map above about eighty metres came out with the same three structures on
more and more empty grass.

**One cell is a demand and the rest are offers.** The anchor — a building, in the middle lane, in the
cell nearest the centre — is offered a list of places in preference order: its own cell narrowed to a
window on the centre of the map, then the whole middle lane, then every other lane. Only when all of
them refuse does generation fail, naming the tag and the ground it was asked to fit it on. Widening
the window before giving up the lane is the right order round: a building a few metres off centre
still anchors the middle lane, and one in a flank does not. Every other cell takes a structure if one
will stand there and stays empty if none will, because "as many as the ground will hold" is not a
number anything can be held to in advance — and a cell that fell back into another lane would be two
structures on one cell's worth of ground.

**Large against small is the size budget, not a slot with a name on it.** Every cell but the anchor
draws from one list — everything tagged `structure/building` or `structure/house` — weighted, and
filtered to what fits `StructureDensity × ¼ × the cell's own area`. A cell with room for a two-storey
building may come up with one or with a hut; a cell with room for neither takes the smallest structure
the catalog has, because a cell is ground that is meant to be built on. What is measured against the
budget is the structure **and its yard**: a house is fenced `YardMargin` out from its own walls, so
the ground it takes is the footprint grown by that. Measuring the walls alone let the two-storey
building into a sixty-metre flank cell that could hold its walls and nothing else, three times over,
and the floor that was left came out measurably worse covered — the 1000-seed sweep put the worst seed
at 0.56 against a threshold of 0.6. With the yard counted, a sixty-metre flank cell affords the small
house and not the building, and the sweep is back at its documented 0.62–0.78.

Two structures keep `2 × YardMargin` between them for the same reason. It was two metres flat, which
was invisible while a map held three structures on sixty metres and is the normal case on a map that
holds three dozen: any closer and two houses' garden fences are threaded through each other.

The query for a cell's art is `structure` **with** `structure/building` or `structure/house`, and the
`WithAny` half is not decoration. `structure` is carried by every piece of structural art in a
workspace — wall panels, floor tiles, door frames, a flight of stairs — because those are what a
*building* is built out of. A cell that asked by that tag alone would stand a two-metre wall panel in
a lane and call it a house, which is exactly what the pack's own catalog produced the first time.

---

## 6. In the editor, the document is authoritative and the scene is derived

`ArenaMap` is the component a scene holds, and what it holds is the document's JSON — not an object
graph, and not the GameObjects. Unity can serialise a string into a scene, snapshot it for
`Undo.RegisterCompleteObjectUndo` and put it back again, none of which it can do for a `WorldDoc`;
and it means the map in the scene and the map in a saved file are the same bytes, so there is one
persistence path rather than two. The realised GameObjects are rebuilt from the document on demand
and are never the source of anything.

That inverts the usual editor-tool relationship, and the capture layer is where the inversion is
paid for. A user drags a crate; a GameObject moves; nothing about the map has changed yet.
`ArenaEditCapture` diffs each realised instance against the pose the document resolved it to and
records the difference as an override. Three decisions in it are worth naming:

- **It watches only while the tool window is open.** Capture mutates the user's document, and a
  global editor hook doing that quietly in every scene that happens to contain a map is a worse
  bargain than a tool that only watches while it is on screen.
- **It records an edit when the instance comes to rest**, not on every tick of a drag. One gesture
  makes one override and one undo step, rather than ten a second of both.
- **It collapses its undo group back to before the gesture began**, so Unity's own transform-move
  entry and the document change come back together on one Ctrl+Z. Without that the two would undo
  separately, and between them the scene would disagree with the document — which the next capture
  tick would faithfully record as a fresh edit, fighting the undo.

Regeneration re-runs the generator and re-applies the override list. Orphans go to the window as a
warning list with explicit keep and discard actions, per section 2; the tool never decides.

**Two surfaces, one map.** The tool is a window and a scene-view Overlay, and they are split by
whether you *read* a thing or *act* with it. The window is the instrument panel: parameters, the
validation metrics, the exposure heatmap, the override list, orphaned overrides, the catalog. The
overlay is the pair of hands: which map is bound, the seed and a re-roll, Generate, Regenerate and
Clear, Save, Load and export, how many edits are live, and what the object you just clicked on is.
Arranging a map means looking at the map, and every control you need while doing that is now in the
same half of the screen as the thing it moves.

The rejected alternative is the easy one — put the same panels in both. It fails on maintenance
before it fails on taste: two copies of a panel are two panels to keep in step, and an overlay that
carries the validation table has become a window that cannot be resized or docked. So the rule is
that no *panel* appears in both, and a panel appearing in both is the signal that the split is drawn
in the wrong place.

**A button is not a panel.** The seed field, the map size, Generate, Regenerate, Clear, Save and Load
are on both surfaces, and that is the split working rather than leaking. A panel duplicated in both is
two things to keep in step; a field duplicated in both is one value with two writers, because both go
through the same component. What the rule is protecting is the reading half — the measurements — and
a control belongs wherever your eyes already are when you want it.

The map size is the one *parameter* that earned a place on both, and it earned it from item mode:
the overlay already resized the building you were looking at, so a tool where resizing the arena
around it meant going to another window had a seam in it that had to be explained. The other eight
parameters stayed in the window, which is the test that this is one field rather than the parameter
panel arriving by instalments. Like the seed, it is re-read from the component on every tick rather
than assumed, because the window can write it too.

**The overlay has two modes, and item mode is wider than map mode.** Map mode is the arena, and it
carries the hands plus one shaping parameter — how big the map is — because the window is its
instrument panel for everything else. Item mode is one building on its own, and it carries the
building's whole shaping parameters — footprint, floors, floor height, content density — a re-roll
per floor, and the export button. That is not the split breaking down:
a building has no window panel, because every panel the window holds is a measurement of an arena
and none of them means anything about a stack of storeys. The parameters set once and forgotten
stay on the component's own inspector. If a building ever grows something worth *reading* rather
than reaching for, that is when it earns a panel in the window, and the parameters would move there
with it.

Neither surface drives the other. There is no controller, no event bus, and no message from one to
the other; both bind to the same `ArenaMap` and re-read it. What that leaves is the small amount of
sharing that is real, and it is deliberately small:

- `MapOperations` holds the whole-map operation both of them perform — take an undo snapshot, run
  something that can throw, and either collapse the result into one named step or revert to the
  group it started from. It lived in the window while the window was the only caller. The overlay
  made it a second one, which is when it moved, per rule 3 in CLAUDE.md. The two file panels moved
  the same way and for the same reason: a second copy of "which folder, which extension, what the
  file is called by default" is a second copy to keep in step. What did *not* move is how the
  outcome is reported — the window has a status line and the overlay has a console message, and
  neither is the other's.
- The seed is the one value both surfaces show and both can write, so each re-reads it from the
  component rather than assuming it is the only thing that could have changed it.
- `ArenaEditCapture` compares the document it was built from against the one the map now holds. A
  regeneration from the overlay replaces the document and every realised instance with it, and a
  capture that did not notice would read those vanished instances as a screen full of deletions the
  user never made.

**The layout guides are a renderer, not a second layout.** With the guides on, the overlay draws the
playfield, each lane band and the two spawn areas from `ArenaLayout.Build(parameters)` — the
component's current parameters, not the document's. That is the whole point of drawing them: the
lanes on screen are the lanes the *next* generation will use, so raising the lane count shows you
what you are about to get before you commit to it. No arena arithmetic is duplicated in the editor
assembly, and nothing was added to Core to support this.

**The road network is drawn the same way and from the opposite direction: off a cache, never
recomputed.** The guides are cheap enough to rebuild on every repaint — `ArenaLayout.Build` is
arithmetic over a rectangle — and a network is not: it is a routing sweep over the whole playfield,
the expensive half of rebuilding the ground. A repaint runs per scene view and per camera in it, so
a callback that built one to draw it would be re-finding the roads several times a frame while
nothing about them had changed. That is also the argument against the obvious shape, a MonoBehaviour
with an `OnDrawGizmos`: gizmos are exactly that callback, and having no place to put the answer is
what makes them the wrong tool for a derived thing this expensive. So `ArenaMap.Roads` keeps the
network the last realise produced — `ArenaLayoutGenerator.Terrain` already hands one back, because
the splat writer needs the same network the heights came from — and `ArenaRoadGuides` draws that or
draws nothing. It is not serialised, on the same grounds the terrain is not: it is derived from the
document, and a stored copy is the same map twice with two chances to disagree. A domain reload
therefore empties it and the next Generate, Regenerate or Load fills it again.

What it draws is the polyline the router found, at a thickness in screen pixels standing in the
ratio the carriageway widths do — arteries thick, branches thin, through `Handles.DrawAAPolyLine`,
which is the one primitive here that takes a width. Drawing the strip itself would be drawing the
road surface, which the splat map already does properly. A junction is a disc at
`RoadNetwork.JunctionRadius`, the radius it actually has and the one `RoadKerbs` cuts against, rather
than a fixed sphere that would claim the same size for a junction on a sixty-metre arena and on a
four-hundred-metre town. The three kinds are told apart by colour, because what makes a portal a
portal is what it is attached to and not how big it is.

### Merge to Prefab is a workflow, not a new kind of art

The thing a person actually builds is rarely one prefab. A desk with a monitor on it and a chair
pulled up to it is a workstation, and a generator that places its three pieces separately will put the
chair through the desk — because nothing in the catalog says they belong together. Arranging them by
hand in a scene is the natural way to say it. `MergeToPrefab` — `Tools/ArenaForge/Merge to Prefab`,
and the same entry on the hierarchy's context menu — parents the selection under a new empty at the
average of their pivots, saves that as a prefab in a workspace folder, and binds the catalog row.

The pivot is the average rather than the centre of the box round the group, which drifts towards
whichever piece is largest, or the first object's own pivot, which puts the group's origin inside the
desk. Where the group is *held* is what the pivot decides; how big it is said to be is measured off
the art afterwards, by `CatalogSync`.

**The row it writes is the row a sync would have written.** Same tags off the same folder table, same
logical id, same measurement through `CatalogSync.Apply` — so pressing *Sync from Folders* afterwards
finds the new prefab and changes nothing. That is the whole claim: the tool saves four steps, it does
not add a fifth kind of thing to the project, and a folder that spells no tags is refused before
anything is created rather than written as a row no query can return. `CatalogBinding` is the add-or-
replace both this and `BuildingExport` do, which moved out of the latter when this became its second
caller, per rule 3 in CLAUDE.md.

**Where a tool writes is browsed, not listed.** All three tools that ask a person for a folder show a
path field and the editor's own folder panel, through `ArenaWorkspace.ProjectFolder`, which turns the
absolute path the panel answers with into the `Assets/…` one the asset API takes. A project keeps art
where it keeps art, and a dropdown of the folders the workspace happened to create cannot reach the
rest of it. What a folder *means* is still the folder table's answer and not the browser's: a folder
that spells no tags is refused by name when the tool runs, and a folder outside the project is
refused at the browser, because Unity cannot make an asset there at all.

**The pieces keep their collision and the root gets a trigger.** A table with four chairs pulled up
to it fills perhaps a third of the rectangle it stands in, so one solid box on the merged root walls
off the other two thirds — a player cannot walk between the chairs, cannot step into the gap at the
end of the table, and cannot see why not. So the merge takes nothing off the pieces: each is finished
art carrying collision somebody chose, and a chair goes on blocking where the chair is. What it adds
is one `BoxCollider` on the root at the bounds of every mesh in the group, with `isTrigger` set. The
footprint still has to be stated somewhere — `CatalogSync.TryMeasure` reads box colliders where a
prefab has any, and the clearance the generator keeps round a placed piece is that rectangle — and a
trigger is how a collider answers that question without also being a wall. It is measured off the art
through `CatalogSync.TryMeasureArt` rather than off `TryMeasure`, because the children keep their
colliders and a box read off those would be a copy of whatever the artist left on the parts rather
than a measurement of the group. Note that this is the opposite bargain from the wrap below, for the
opposite input: a wrap is a raw import, and a merge is an arrangement of pieces that are already
right.

**And a mesh nothing blocks for gets a box of its own.** Preserving the pieces' collision is only an
answer where there is collision to preserve, and half a bought art pack ships with none — so a group
made of it came out of the merge as scenery a player walks straight through, with the root's trigger
stopping nothing by design. Every mesh in the group with no solid collider over it is given a
`BoxCollider` at its own bounds, on its own object: its own and not the group's, because a box round
the whole arrangement repeated once per piece is the invisible wall the trigger exists to avoid. What
is asked is whether the mesh is *blocked*, not whether it holds the component — the walk goes up to
the merged root, because a wrapped model is one solid box on a wrapper with the art's colliders
stripped from under it, and a pass that asked each mesh about itself would put a box back on every
part the wrap cleared. A trigger is not an answer either way, which is what keeps this pass and the
root's own box independent of each other.

### Two tools for art the generator cannot fix

Both of these exist because of the same boundary: the generator selects and places prefabs, and there
are two facts about a prefab that no amount of work on the placement side can recover.

**Which way a piece faces.** Everything placed against a wall is turned on the assumption that the
front of a piece is down its own positive Z — `WallFacing` states it and says outright that nothing
can check it. A model imported facing sideways is stood sideways on every seed, and the placement
code cannot tell that from a piece meant to face that way. `WrapObject` —
`Tools/ArenaForge/Wrap Object` — parents the model under an empty at the origin, turns the *model*
inside it in quarter turns, and saves the wrapper as a prefab. The parent's axes never move, which is
the whole tool: what the generator turns is the prefab root, so a wrap that turned the pair together
would have moved the problem rather than fixed it. It writes no catalog row — a wrapped model is art
on its way into a folder, and *Sync from Folders* is what files it.

**The wrapper also carries the collision, and the model carries none.** A model arrives from a pack
with whatever colliders the artist left on it — a mesh collider per part, thirty of them, or nothing
at all — and neither is a size the generator can use. So the wrap strips every collider under the
parent, measures the meshes, and puts one `BoxCollider` on the parent round the lot. The order is the
trick: `CatalogSync.TryMeasure` reads box colliders where a prefab has any and meshes where it has
none, so stripping *before* measuring makes it answer about the art, and the box written from that
answer is exactly what the next sync of the folder reads back. The rectangle the placement rules keep
clear and the shape a player walks into are then one measurement rather than two. It is refitted after
every turn and again on the way out, because a quarter turn swaps a box's two horizontal sides and a
box left at its old size stands at right angles to the art inside it — invisible in the scene view,
and what a player walks into. A wrapper round something with no mesh in it gets no box: a default
one-metre cube round nothing is a collider in an empty room.

**Where a house is walked into.** How big a structure is, is visible in its meshes; which wall the
door is in is not, so a `DoorwayMarker` child is how the person who made the model says so — see §5.
`DoorwaySetup` — `Tools/ArenaForge/Doorway Setup` — adds a correctly named marker with a trigger box
and selects it, so the next thing that happens is dragging it onto a wall with Unity's own handles.
Saving does both halves: it applies the markers to the prefab and writes what they measure into the
row bound to that prefab. Either alone is a trap — the generator reads the catalog and never the
prefab, so a marker applied and not written declares its doors to nobody, and a row written without
applying describes markers that vanish when the scene closes. The row is found by the prefab it
points at rather than by logical id: an id is spelled from a folder and a file name and either can be
edited, and the prefab reference is what the binding actually *is*.

Neither tool makes a new kind of thing. A wrapped model is an ordinary prefab and a marker is an
ordinary named child with a collider on it, so what these produce and what a person produces by hand
are read by the same sync — the same claim Merge to Prefab makes about rows, for the same reason.

## 7. The building generator: per-floor seeds, a floor plan, and two ways in

A building is a second generator over the same machinery: a footprint, a floor count, a floor
height, a shell and a set of rooms, each filled from the catalog by tag query under a
`ConstraintSet` and the same Poisson sampler a lane uses. Nothing about placement is new. Three
things about it are.

**Each floor stores its own seed.** The initial value is a `Fork` of the building seed, so a
building nobody has touched is fully determined by its parameters exactly as a map is. After that
the *stored* seed is what generation reads, and re-rolling floor 2 rewrites one number. That is the
whole feature: floors 1 and 3 then serialise to the bytes they already had, which is what makes a
per-floor re-roll worth having rather than a regenerate button with extra steps. Deriving the floor
streams from the building seed at generation time would look identical on the first run and be
useless on the second — there would be no value a re-roll could change except the building seed,
and changing it would move every floor. It is CLAUDE.md rule 2's fork-per-subsystem argument one
level down, with the fork's result written into the document instead of recomputed.

The seed a re-roll writes comes from the caller, not from the building seed. Picking a fresh seed
is the one thing in the tool that is meant not to be repeatable, the same bargain the map's seed
re-roll makes; what stays deterministic is the document, and once the number is written down that
floor generates the same way for ever.

**A floor is a bounded region, and says so where it is not a lane.** `ConstraintSet` is built over
either an `ArenaLayout` or a plain rectangle. Six of the eight rules do not notice the difference —
they are statements about a footprint, a grid and what is already committed. `WithinLane` and
`ClearOfSpawn` name arena features a storey has no equivalent of, and over a bounded region they
throw rather than being quietly satisfied: a rule that can never reject anything would sit in the
evaluation order and in the placement statistics reporting zero rejections, which reads as a rule
that passed rather than one that was never asked.

**A storey is a stack, not a plane.** The slab is laid at the storey's own height and everything
else on the floor stands on top of it, so what a wall or a crate has to fit under is the floor
height *less the slab's thickness*. The shell first built put all of it at one height, and that is
worth spelling out because it is the single largest source of flicker a generated building can have:
a crate is then sunk a slab-thickness into the floor with its underside in the slab's own plane, and
a wall as tall as the storey has its top face in the same plane as the top of the slab above it —
over the whole length of every wall in the building. Two surfaces at one depth is a thing a renderer
has no way to order, and the result is a ceiling that crawls as the camera moves.

Floors stack without intersecting because a floor is never offered a catalog entry meaningfully
taller than that headroom — its walls as much as its crates. That is a selection rule rather than a constraint,
deliberately: every rule in the constraint set is two-dimensional, and adding the one rule that was
not would make the set mean something different. Elevation is added to the pose on the way out,
after the rules have had their say, so two storeys of one building never meet in a rule and never
need to.

**The headroom is forgiving, and the wall runs are fitted to it.** A 3 m storey on a 0.1 m slab
wants a 2.9 m wall, and asking for that exactly is asking a number the user typed to agree with a
number the art pack declared. Both ways of missing are quiet. Too tall and no wall art is offered at
all, so every storey comes back as one undivided room — indistinguishable from a catalog with no
walls in it. Too short and there is a strip of daylight along the top of every wall in the building.
In single precision it is worse than quiet: a 0.2 m slab under a 3.1 m storey leaves a headroom about
a ten-millionth of a metre under 2.9, so the 2.9 m wall an art pack ships for that storey is refused
for a difference nobody can see or type.

So art within `HeadroomTolerance` — five centimetres — of fitting is admitted, and the wall run is
then **stretched onto the ceiling**: `Pose` carries a vertical scale alongside its uniform one, the
generator sets it to the ratio between what the piece measures and what the storey leaves, and the
realiser writes it to the instance's `localScale.y`. A short wall grows to meet the slab and one the
tolerance let through is squashed under it, so what is admitted on tolerance never actually
intersects. The lift that stands a piece on its own base is scaled with it, or art modelled around
its centre would sink into the floor by half the difference.

The two halves are one decision. A tolerance without the stretch would put wall tops a few
centimetres inside the slab above; the stretch without a tolerance would still refuse the wall that
prompted all this. Five centimetres is sized to forgive a near miss in a floor height and not to
forgive art that is simply too big for the storey.

**Nothing else is rescaled**, and that boundary is the point. Section 3's rule is that this tool
composes art rather than authoring or distorting it, and a wall is the one piece whose height comes
from a parameter rather than from the catalog — what it has to meet is the underside of the slab
above, and where that is is not the art pack's decision. The doorway goes with it because the two
stand in one run and a frame at its own height in a fitted wall is a step in the top of the wall.
Crates, decor, slabs, flights and parapets are placed at the size the catalog declares. A crate the
tolerance admits may therefore stand up to five centimetres into the slab above it, which is the
price of one number meaning one thing throughout.

The shell is not placed under the constraint set at all, and that is the point of the division. A
wall goes where the plan says, because the plan is what decides where a wall goes; the contents are
*proposed* and judged, because where a crate goes is a question with a wrong answer. Committing the
walls into the room's set and letting the sampler discover them would be the same result reached by
search — content is inset from its room by a whole wall thickness before a candidate is drawn, so
nothing in a room can reach a wall in the first place.

**A floor is a plan before it is a pile of crates.** `FloorPlan` partitions the footprint by binary
space partition: the storey starts as one region and is cut in two along its longer axis at a line
drawn from the stream, and each half is cut again until it is too small to be worth dividing. The
leaves are the rooms; every cut is a wall with one module of it taken out for a doorway. Contents
are then placed per room rather than over the whole storey, under the same `ConstraintSet`.

Two things follow from that shape and are worth naming.

**Connectivity is structural rather than checked.** A BSP tree has exactly one path between any two
leaves, so putting a doorway in every cut opens all of them — there is no reachability pass, and
none is needed. The test asserts it anyway, by walking the doorways the generator actually emitted,
because "we reasoned it cannot happen" is the class of claim that stops being true when someone adds
a second kind of cut.

**The plan is laid out in modules, not in metres.** A wall run is tiled from a piece of art at the
length the catalog declares and nothing is stretched *along* the run — the fitting above is vertical
and does not touch a module — which means a cut that did not land on a module boundary would leave a
run that cannot be built. The module
is the long side of the wall entry the floor drew, and the doorway is one module wide. That is the
same rule as section 3, one level down: the generator selects and composes art, it does not author
geometry. A catalog with no `structure/wall` art still builds — the floor comes back as one undivided
room, which is exactly what a catalog of crates and barriers produced before the shell existed. The
shell is art the catalog either has or does not.

The slab, though, tiles on the **floor tile's own footprint** rather than on that module, centred in
the plan. Sharing one grid is only right when the art pack happens to make its floor exactly as wide
as its wall is long; when it does not, every tile either misses its neighbour or laps over it — and
two slabs over one patch of ground have coplanar top faces, which is the flicker that reads as the
floor breaking up underfoot. Centring means a mismatch leaves a strip of bare ground at the edge of
the storey instead. A gap is a failure the art pack can be seen to have caused; an overlap is one the
tool appears to have caused.

**Every run straddles its own line, so junctions are buried rather than flush.** `FloorPlan.Bounds`
is the rectangle the walls' *centrelines* enclose, outside walls included, and the module count is
taken from the footprint less one whole wall thickness so the outer faces still land inside the
footprint the building declares. A ring built from straight pieces has to overlap at its corners —
there is no way to close one otherwise unless a module happens to be as long as a wall is thick — so
what the rule buys is not the absence of overlap but the absence of *coplanarity*: a run that arrives
at another now ends half a thickness inside it, rather than flush with its far face. With the
perimeter tucked inside its bounds, as it first was, every corner and every T-junction put two faces
in one plane, and a three-storey building had some forty flickering strips down its outside. What is
left is the horizontal pair at the top of a junction, which is a couple of hundredths of a square
metre seen only from above; see FUTURE.md.

**No cut may leave a room narrower than 2.5 m.** `FloorPlan.MinRoomSide` is stated in metres and
converted to whole modules per floor, and it is checked *before* a split rather than after: the two
regions a cut would leave are measured, and a cut that would leave either of them under the minimum
is not made at all — the region stays one larger room. A minimum stated in modules would mean four
metres on one art pack and a metre and a half on another, which is the same rule saying two different
things; and a check applied afterwards has nothing to do but accept what it finds. The recursion still
terminates for the reason it always did — every split strictly shrinks both halves — and a footprint
too small to split at all comes back as one room rather than as a loop looking for a legal cut.

A corridor is a leaf that came out one module wide and at least twice as long: it is left clear,
because circulation you have to climb over is not circulation. On an art pack whose wall module is
shorter than the minimum above, no leaf is ever that narrow and a floor is all rooms — which is the
minimum doing its job rather than the corridor rule being dead. What a person called a corridor when
the partition could leave one-module strips was, at two metres across, a room they could not use.

The rooms are furnished; a doorway is kept clear by `NotBlockingDoorway`, the same rule that keeps a
map's cover out of a building's entrance, or a crate standing in an opening would quietly undo the
connectivity above.

**The ground floor gets two ways in, as far apart as the shell allows.** One door makes a building a
cul-de-sac: the only way out is the way you came, so the room behind the door is where a fight ends
rather than ground either side can move through. `Partition.AddEntrances` draws a first side, then
asks the *opposite* wall for the second and the two walls beside it after that, and wherever the
second lands it has to be half the shell's diagonal from the first. Two doors in adjacent modules are
one wide door and two near a shared corner are a door with a corner in it; the separation is a share
of the diagonal rather than a distance so that it means the same thing on a shell of any size. A side
with no module clear of the stairwell is walked past exactly as the single entrance always was, and a
shell whose every other wall is spoken for keeps the one door it managed — there is no fifth wall to
try.

**A building declares those two openings, and only those.** Where a person may walk between two rooms
is a fact about a floor and every cut carries one; where they may walk in from the map outside is a
fact about the *building*. `BuildingGenerator` writes the ground floor's own shell doorways into the
document metadata — the four runs at the head of the wall list, and no storey above, where a door in
an outside wall would open onto a drop — and `BuildingExport` carries them into the catalog row. That
is what a map keeps clear when it places the building; counting interior doors as ways in is how a
building comes to declare a dozen entrances, none of which reaches the map.

**A door you can walk through is not yet a route you can walk.** Keeping the opening clear says
nothing about the six metres of floor between it and the next door, and a room whose two doors are
separated by a crate wedged between two walls is a room a player walks into and cannot leave. So the
plan reserves the route as well as the opening: `PlanRoom.Paths` is a chain of 1.2-metre strips
joining each of a room's doorways to the next, and on to the stairwell when the shaft is in that
room, and `OffReservedPath` refuses anything proposed onto one. Nothing a room holds — its
centrepiece, its cover or its decor — is exempt, and the centrepiece least of all: it wants exactly
the floor in the middle that a chain of strips runs through.

A chain rather than a star or a route between every pair: a chain joins all of the doors, which is
what a connected graph is, and it does it with one fewer leg than there are doors. A star would
reserve the exact middle of the room, which is where the cover is supposed to stand. Each leg leaves
its doorway perpendicular to its own wall before it turns, so what is reserved is the floor you
actually walk on rather than a strip hugging the wall beside it. A room with one door and no shaft reserves nothing: one door is not a passageway, and
the doorway clearance already makes it usable.

The strips are geometry, not draws. Where a walkway runs is a fact about where the partition put the
doors, so the stage takes nothing from any stream and adding it moved no placement in any building
that already existed by shifting one.

**A room is furnished three times, and no pass is another with a different tag on it.** The three
are the three folders, and each one asks a different question about the same floor.

*Centrepieces* — `propbuilding/decor/centerpieces`, the sofa or the table — go down first, under `InCentre`
with the walkway width as their clearance: a piece that cannot be reached from any wall of the room
is a piece in the middle of it. The open cells are offered in order of how near they are to the
centre of the floor, so the first piece lands as close to the middle as anything will fit. First
because it is what the room is arranged around; run last it would be competing for the middle with
whatever the sampler happened to drop there, and losing about as often as not.

*Cover* is scattered into what is left: proposed anywhere the floor is open, judged on whether it
fits.

*Decor* — `prop/decor` or `propbuilding/decor/decoration`, the plants and lamps and bins — goes only
into the corners, under `InCorner`, and each of the four is furnished at most once. The rule alone
would not give that: it asks whether a piece has two walls within reach and says nothing about
*which* two, so four plants heaped in one corner satisfy it exactly as well as four in four corners.
So the corners are enumerated and walked in an order the room's stream draws, and each is offered the
open floor nearest it. The wall between two corners is not a second-best corner — it is where nothing
goes, which is what filing this art under `Decoration` rather than under `Decor` says.

The three draw from separate forks of the floor's seed, and each is handed everything the ones before
it committed. A catalog that has never held a piece of `propbuilding/decor/centerpieces` art builds the
building it always built, byte for byte: the stage returns before it draws or commits anything when
the query comes back empty.

Centrepieces and decor are inset from the room by one wall thickness where cover is inset by a
thickness *and* the wall margin. That margin exists to keep cover off the walls; the other two have
their own opinion about how near a wall to stand, and a plant half a metre out from the corner is a
plant somebody has moved to hoover behind.

**Furniture is turned by the wall behind it, not by the stream.** A quarter turn drawn at random is
the right answer for a crate lying in a lane and the wrong one for everything with a front: a sofa is
modelled facing its own positive Z, so a sofa turned at random faces the wall three times out of four.
`WallFacing` is the rule — positive Z to the room, negative Z to the wall — and the two passes that
put things against walls use it. The corner pass takes its turn from the corner outright and draws
nothing: there is exactly one of the four that puts a back to a wall, and for a piece filed in
`Decor/Corners` it is the one that puts *both* of its backs — negative Z and negative X — against the
two walls meeting there. The scatter still draws a turn and overrides it only for a piece that landed
near a wall, so a crate in the middle of a floor keeps the turn its stream gave it; a room where
everything faced inwards would be a ring of furniture rather than a room.

The facing is decided from the piece's **pivot**, not from its footprint, and that is not a
simplification. A footprint is the rectangle a piece occupies *after* it has been turned, so asking
which wall a footprint is nearest and then turning the piece changes the answer to the question just
asked — an oblong drawn lying along one wall stands end-on to another once turned. A pivot does not
move when a piece is turned, so the caller passes the pivot and the piece's own reach from it
(`WallFacing.Radius`, which is the same number at every quarter turn) plus the clear floor it will
allow behind the piece. A rotation is also the one thing no constraint looks at: every rule in
`ConstraintSet` is a statement about a footprint, and a footprint is the same rectangle whether the
sofa in it faces the room or the plaster.

### Windows are the one thing in the shell placed by reading the plan rather than drawing

A window is not a piece the plan makes room for. It is a `structure/window` swapped into a module a
wall run has already been tiled with, so what it has to measure is the *wall* — anything else is a
gap in the middle of a run, which is the same bargain `structure/doorway` makes and the reason the
generated starter window is 2 × 2.9 m rather than on the metre grid the rest of the starter set is
modelled to.

**Only the four outside runs.** `FloorPlan.Walls` puts the sides of the storey before any cut, so
this is an index comparison rather than a geometry test — and it is a real rule rather than a
convenience, because a window in a cut is a window between two rooms, which is a serving hatch.

**One to a room, on each side of the building that room reaches, in the middle of its stretch of
it.** This is the part worth arguing about, because the obvious rule — glaze whatever will take a
window — gives a wall of glass every two metres, and the obvious fix for *that* — every second
module, or never two in a row — spaces windows evenly against a wall that is not evenly divided.
The count runs on past the corner of one room into the next, and where a window lands in a room
comes out of arithmetic about the whole side rather than out of anything about the room. So the run
is walked in the rooms behind it: each room's stretch of the outside wall gets one window in the
middle. A large room gets a window, a small room gets a window, a room that turns a corner gets one
on each side, and the result reads as a building rather than as a pattern.

**Three modules are refused, and a room that wanted one of them goes without.** Not shifted along:
a window that is not in the middle of the room's stretch is not the middle of anything, and a rule
that says so is one nobody can predict by looking at the building. The refused three are the *ends*
of a run, because a run ends buried in the run it meets, so an end module is the corner of the
building and is shared with the run coming the other way — two rooms two modules deep in one corner
would otherwise glaze both sides of it and leave a tenth of a metre of wall holding the corner up.
A module *any* doorway on the floor reaches — the obvious half being the way into the building,
because a room with the front door in the middle of its facade does not also need a window there, and
the half that is not obvious being the interior door: a cut ends buried in the outside wall it runs up
to, so a cut two modules long with its door in the end module puts that door inside the outside wall,
and a window in the same module is a window standing in a doorway. Measured as rectangles rather than
as module indices, which belong to different runs. And a module *next to a window already placed*, which is what two
one-module rooms side by side would otherwise produce: four metres of uninterrupted glass, which is
the look the rule exists to avoid whatever the reason for it. On the sample building that comes out
at four to six windows in eighteen perimeter modules.

Refusing the ends is also what makes the last rule enough on its own. A window at the end of one run
could reach a window on the run it meets, and neither run is allowed to put one there.

**Nothing is drawn for any of this.** Which room is behind which module is a fact about a partition
that has already been made, so windows are a *reading* of the plan rather than a stage with a stream
of its own. The art is picked from a fork — `Rng.Fork` is a function of a stream's seed rather than
of its position — so a workspace that syncs a window prefab into its catalog and presses Regenerate
gets the building it already had with windows in it, not a differently partitioned one. That is
rule 2 in CLAUDE.md doing exactly what it is there for, and it is asserted object by object and pose
by pose, because "much the same" is what a perturbed stream produces.

### Stairs are the one thing a storey does not decide for itself

A stairwell has to be at the same place on every storey or it is not a stairwell. Every storey is
partitioned from its own stored seed and re-rolling one has to leave the others alone. Those two
facts only fit together if the shaft is decided *above* the floors: `PlanStairwell` is a function of
the parameters and the catalog and of nothing else, forked from the building seed, and every floor's
partition then works around the rectangle it returns. Re-rolling a storey rearranges its rooms around
the shaft rather than moving the shaft.

That is the first thing in this generator that the floors have in common, and it is deliberately not
a conversation between them — floor 3 never learns anything about floor 1. What they share is an
input both were given.

**The shaft is placed conservatively, because the module grid is not a building-level fact.** Each
storey draws its own wall art, so each has its own module and its own plan bounds. The shaft is
therefore confined to the rectangle inside *every* grid the catalog could produce: a storey's walls
enclose `floor((W − t) / m) · m` centred in the footprint and their inner faces are half a thickness
inside that, so `W − m − 2t` is inside all of them for any module `m` and thickness `t` on offer.
The exact answer would need the shell of every floor, which would make where the stairs go depend on
what the floors drew — and re-rolling a floor would then move the stairs on all the others.

**Three things are reserved, and they are not the same number.** No cut may cross the shaft, or a wall
would stand over the opening. But a wall *alongside* it is fine and often what you want; it is
only a doorway in that wall that is a problem, because walking through it puts you into the shaft. So
`FloorPlan.Build` takes the shaft and a doorway clearance separately, and the doorway pick skips the
modules that open onto it. Reserving the clearance against every cut instead — which is what this
first did — pushes every split of the region holding the shaft out to its edges, and a floor of
one-module strips is a floor of corridors: the corridor count overtook the room count on the first
sweep, which is how the mistake was found.

**A room's furniture is kept off the shaft *and off the floor round it*.** That is the third
reservation and it is the one that is about walking rather than about building. A storey used to give
up exactly the tiles the opening covers and not a centimetre more, which is the right rule for a hole
in a floor and the wrong one for the way upstairs: a crate off the opening by a centimetre is a crate
on the bottom step, and a room whose scatter closed in on all four sides of the shaft was a room the
storey above was reached through sideways. The shaft is two or three metres across in a room that may
be five, so the difference between "off the opening" and "clear of the stairs" is most of what makes
the floor above reachable at a run. `Stairwell.Landing` — the shaft grown by `StairClearance`, which
is twice `DoorwayClearance` — is what a room's three content passes are furnished against. A flight of
stairs is a doorway to the storey above, and it is approached, turned onto and passed by rather than
walked through in one direction, so it gets twice the approach a door gets.

It is a separate rectangle from the shaft rather than a wider shaft because the two are asked
different questions. The shaft is what the *floor* gives up — the tiles the opening costs, the ground
no cut may cross, the hole in the slab — and widening it would move walls and take floor out from
under the storey above. The landing is only what nothing may be *stood on*.

**A doorway into the shaft is forbidden outright, and a cut that cannot avoid one is not made.** The
first version let a run whose every module was against the stairs take one anyway, on the grounds
that an awkward door beat a sealed room. It is not an awkward door: on every storey above the ground
the shaft is a hole, so that doorway is a hole in the floor with a frame round it, and a step through
it is a fall. Nothing this tool can read says which end of a flight is its top — see FUTURE.md — so
there is no such thing here as a door that opens onto the landing rather than onto the drop, and the
rule has to be absolute.

What makes it affordable is that refusing the door and refusing the wall are the same decision. When
no module of a cut's run is legal, `Subdivide` tries the other axis and then leaves the region whole,
so the two rooms that would have been sealed off from each other are one larger room instead. Every
cut that *is* made still has a doorway, so connectivity stays structural and needs no reachability
pass. The way in is handled the same way, one level up: the entrance side is drawn and then walked
round to the next side if that one has no module clear of the shaft.

**The hole in the slab is what makes it stairs rather than a sculpture.** Every storey above the
ground has the slab tiles over the shaft left out, and so does the roof. A flight is selected against
the *floor height* rather than the headroom every other piece is held under, because a flight has to
reach the storey above; it is the one exception to the rule that stacks the storeys, and the hole is
what it is an exception for.

**The flight is anchored flush into a slab joint, and the opening is cut to the flight.** A slab is
laid in whole tiles and nothing here authors geometry, so the tiles a storey has to give up are a
whole number of them whatever happens. What is avoidable is *how many*: cut against a rectangle that
ignores the tile grid, "the tiles that lap it" is up to one more tile on each side than the flight
covers, which is how a 1 × 5.5 m flight ended up adrift in the middle of a 4 × 8 m hole. So
`PlanStairwell` anchors the flight's low corner on a slab joint, and `Stairwell.Shaft` is the
smallest whole-tile rectangle holding it — the ground the stairwell claims, which the partition keeps
its cuts and doorways out of and which nothing is stood on.

**What is cut, though, is `Stairwell.Flight` and not `Stairwell.Shaft`, and it is cut on the grid the
storey actually laid.** The shaft has to be a fact about the *building*, so `PlanStairwell` works the
grid out from the largest floor tile the catalog offers over the plan bounds the longest wall module
gives — conservative, and exact for the catalog an art pack normally is: one wall, one floor tile. A
workspace is not that catalog. A sync files the demo's 2 m slab and the starter's 1 m tile both under
`structure/floor`, either can be drawn for a storey, and cutting the building's rounded rectangle out
of a metre grid opened 2 × 4 m for a 2 × 3 m flight on every floor. The spare metre is not slack in a
corner: it lands at the top of the run, so you climb the flight and step off the last tread into the
storey below. Each storey therefore cuts *its own* grid against the flight's own rectangle, and on a
metre tile that is exactly the ground the stairs stand on.

**Where a coarse tile cannot reach the edge of the opening, the strip it gave up is floored again.**
A 2 m tile cannot open 3 m, so a storey that drew one still gives up 4 m — and `FloorTheMargin` lays
the leftover from the finest floor tile the catalog has, on a grid over exactly the cells the coarse
pass skipped, so the two can never overlap. The margin is laid flush with the *top* of the slab
rather than on its base: two tiles of different thicknesses stood on one base is a step at the top of
the flight, which would move the fault rather than remove it. A catalog with one floor tile has
nothing finer to reach for and lays its slab exactly as it did before any of this.

Where a flight may stand is that slab rather than the clear floor inside the walls, and that is a
deliberate reversal. On the common art pack the joints at the edge of the slab fall on the outside
walls' own centrelines, so holding a flight a whole wall clear of them leaves no joint but the middle
ones — and a stairwell in the middle of a small floor turns the plan into corridors, by exactly the
mechanism two paragraphs up. A flight may therefore end up buried half a wall thickness inside an
outside wall, which is the same straddle every junction in the shell already makes.

**What it may not reach is the edge of the slab itself, because something is standing on that edge.**
The strip a flight is held off is not a whole module and not a whole wall: it is the width of floor
the shell actually stands on, half a thickness for an outside wall — which straddles the bounds the
slab is laid to — and a whole depth for the roof's parapet, which is inset until its outer face is
flush with the roof and is therefore entirely inboard of it. Anything cut out of that strip is floor
taken from under a wall, and a wall stands on *top* of its storey's slab: what is left is a wall
hanging a slab's thickness clear of the storey below it, and a tenth of a metre of daylight running
along the outside of the building at that floor line is the most visible thing on it. The strip is
narrow, so it costs about what it looks like it costs — the anchor snaps to a tile joint, so on the
common pack a flight gives up the outermost ring of tiles and no more of the floor than that. A
building too small to hold a flight clear of its own walls keeps the stairwell and gives up the
strip, because a storey with no way up is the worse of the two failures and the only one a person
cannot walk round.

**The interior opening is left open.** A railing round it was tried and taken out again: the only art
a catalog has for the job is its `structure/parapet`, and a parapet is sized to be seen from across a
roof, so a ring of it round a stairwell is a wall through the middle of a room. Fencing the hole is
worth doing with art meant for it — see FUTURE.md — and not worth doing by reusing the nearest thing.

**The top is capped and fenced.** A final slab goes over the top storey, built from that storey's own
slab art — the roof is its ceiling, and a ceiling made of something else is a different building above
the top floor — and a `structure/parapet` ring is tiled round the four edges of it.

**A run spans the whole side of the roof, and only its centreline is inset.** That distinction is the
whole of why the ring closes, and getting it wrong is why the corners leaked through two attempts at
fixing them. A run has to sit half a thickness in from the edge it guards or its outer face hangs
over the drop; but insetting it *along* its own travel as well shortens it at both ends by that same
half thickness, and counting whole pieces into an already-shortened span loses whatever else did not
divide. Both losses land at the corners, which is precisely where the two runs were supposed to meet.
So the inset applies across the run, the span is the full side, and the four runs close by burying
each end in the middle of the next — the wall junction rule one level up.

The first version instead stopped the runs along Z half a piece short of the runs along X, to keep
the corners from lapping at all: on a roof "seen from directly above" is where the person is
standing, so the coplanar top faces a wall junction gets away with are not acceptable. That is right
about the cost and wrong about the price worth paying — what it bought was a visible gap at all four
corners, and a fence with four holes in it is not a fence. A corner lap costs a square of top face
the size of the parapet's own thickness.

**It is tiled round the roof slab, not round the walls.** The slab is what a person stands on and
falls off, and where its edge is comes from the tiles rather than from the wall centrelines. Lining
the ring up with the walls put it half a wall thickness outboard of the floor it was fencing.

The pieces are counted **up** rather than down, so a side the art does not divide overhangs by a
fraction of a piece rather than falling short of the corner. Art laid out on the same grid as the
floor tile has neither: a metre-long parapet round a roof of metre tiles divides exactly, which is
what the starter set in section 3 is sized for.

There are stairs on the top storey too, and the roof is cut for them. A parapet enclosing somewhere
nobody can reach would be a fence round a field, and getting roof access out of the general case
rather than a special one is what makes a single-storey building work the same way.

**The opening in the roof is fenced along its flanks, and only its flanks.** It is the one hole in a
surface that is otherwise railed all the way round, and what is under it is a flight dropping most of
a storey — so it is the one place a ring of `structure/parapet` earns its keep, standing on a roof
doing the job it was sized for, which is exactly why the same ring inside the building was taken out
again. What it may not be is closed: one end of a flight is the way off it and the other is the drop,
nothing this tool can read says which, and a fence across both is a fence between the roof and the
only way down from it. A flank is a drop whichever way the flight climbs, so the flanks are railed
and the ends are left alone. A flight as wide as it runs does not say which of its sides are its
flanks either, and gets no rail rather than a rail across the head of its run.

A run of it is left out where it would stand inside the ring round the edge of the roof — two
parapets over one piece of ground, with the coplanar top faces that always follow, and on a roof the
top face is what a person is standing on. Nothing is lost by leaving it out, because what someone
would fall over there is the edge of the roof and the edge of the roof is already fenced. Holding the
opening off the edge in the first place is what makes that the rare case rather than the usual one.

A building's declared `Height` counts the roof, because that is the height an exported building is
bound into a catalog with, and a row that under-reports by a parapet is wrong in the same way a
footprint that under-reported would be. It is read back from the metadata the generator wrote rather
than recomputed, since how thick the slab came out and how tall the parapet is are facts about the
art that was drawn rather than about the parameters.

A catalog with no stairs art builds a stack of separate storeys, exactly as it did before; one with
no floor art gets no roof and therefore no parapet. The shell is art the catalog either has or does
not — the same bargain the walls and the doorways make.

The entrance is still on the ground floor and nowhere else. The way *up* is now inside, but a doorway
in the outside wall of floor 2 would still open onto a drop.

### Export is how a generated thing leaves

Both generators bake to a prefab, and both prefabs are leaves: no document, no seeds, and nothing
that points back at a generator. `PrefabBake` is the half they share — save the realised hierarchy,
then strip the components that only mean something to a document — and it moved out of
`BuildingExport` when `MapExport` became its second caller, per rule 3 in CLAUDE.md.

What they do with the result is where they part. A map is exported because it is *finished*: there
is nothing left to place it into, so the prefab is the end of the road and the `ArenaMap` in the
scene stays the editable source. A building is exported so the arena generator can place it, which
is the one join between the two generators.

`BuildingExport` writes a prefab and a `CatalogAsset` row under `structure/building/`, with the
footprint the building declared, the height its floors give it and the ground floor's own doorways.
After that the arena generator places it like any other structure — and `ArenaLayoutGenerator` is not
changed by any of it, which is the test that the catalog boundary in section 3 is real. If placing an
exported building had needed a special case in the map generator, that would have been a hole in the
boundary worth reporting rather than working around.

**A row can say where it is walked into.** `CatalogEntry.Doorways` is a list of rectangles in the
entry's own space, and it is the one fact about a structure a generator cannot work out for itself:
how big a house is, is visible in its meshes, and which wall the door is in is not. A map used to
assume the middle of the two faces across the lane, which is true of a box with no door modelled into
it and false of everything else — the cover stage kept a clearance in front of a blank wall while it
stacked crates against the actual door. A row states them in one of two ways: `CatalogSync` reads
`DoorwayMarker` children out of an imported prefab — by name or by Unity tag, with a box collider for
the threshold or an empty transform for a metre-square one, and never measured into the piece's own
footprint — and a row bound by hand can state them directly.

**A generated building bakes its own markers, so it is read the same way as everything else.** The
rectangles come out of the ground floor's plan, which is the one thing about a generated building
that is known exactly; `BuildingExport` then puts a marker in the prefab for each of them and reads
the row back off the asset through `CatalogSync.DoorwaysIn`. Writing the row alone was not enough,
and the way it failed was invisible: delete an exported building's row and press *Sync from
Folders* — the ordinary way to get art the catalog has lost back into it — and the building came
back with no doorways, because the scan asks the asset and the asset had never been told. The prefab
looked right, the row looked right, and only the maps built from it were wrong. There is no second
kind of marker and no second reader: a generated building declares its doors exactly the way a house
somebody modelled does.

**Two doorways per structure, whatever the art says.** `ArenaLayoutGenerator.DoorwaysPerStructure` is
a rule of level design rather than a measurement: one way in is a dead end, three make a crossroads
with no walls worth holding. A row that declares more keeps the two furthest apart, a row that
declares one is topped up from the face further from it, and a row that declares none gets both off
its footprint as it always did. The declared rectangles go through the same quarter turn and
translation the footprint does, because a door that did not turn with the wall it is in would be a
door in a different wall.

**An exported thing is a leaf.** The prefab is baked art: no document, no floors, no seeds, and no
way to re-roll its second storey. The scene object it came from stays the editable source, and
re-exporting over the same path is how a change gets into the prefab. The `ArenaObjectRef`
components come off on the way out for exactly that reason — they name objects in a document that
no longer governs the thing carrying them — and so do `ArenaMap`, `ArenaBuilding` and
`WorldRealizer`, which a catalog prefab may be carrying and which would go looking for a document
that is not there.

**What is baked is the resolved list, not the generated one.** A hand edit is part of the map, so a
prefab exported from the generated objects would look right until you counted the crates. The
export takes the realised hierarchy, which is what `Resolve()` produced, which is the map as the
user sees it.

Its footprint and height are read from the document rather than measured off the meshes. Both are
what the contents were generated against: every object was held inside that footprint by a
constraint, and none was offered to a floor it would have stood through. Measuring the prefab
instead would report the extent of whatever the seed happened to place, so a sparse floor would
quietly shrink the building the arena thinks it is placing.

**The analysis stays two-dimensional.** To `MapAnalyzer` a placed building is one footprint and one
occluder; nobody stands on the upper floor. Extending visibility and connectivity to walkable
surfaces is named in FUTURE.md and is a much larger change than it sounds.

## 8. The ground, and how a building meets it

`TerrainField` gives every point of the playfield a height. It is built from the parameters like
`ArenaLayout` is, it is not stored in the document, and section 4 covers why it is hashed rather
than streamed. What is worth arguing about is what it does to the things standing on it.

**A structure gets a pad, not a sample.** A generated building is a stack of level storeys with
vertical walls between them; there is exactly one height it can be put at and no sense in which it
could follow a slope. Sampling the ground at its centre and standing it there would bury one corner
and leave the opposite one in mid-air on any ground worth calling uneven. So `BuildingGenerator`
cuts and fills the ground into a flat pad first — the footprint plus a metre of doorstep, held at
the mean of the ground under it, with a four-metre apron grading back to whatever the ground was
doing. The pad settles between the high and low ground it replaced, so a building neither perches on
a plinth nor sinks into a pit.

That levelling lives in `BuildingGenerator` although `ArenaLayoutGenerator` is what calls it,
because how a building meets the ground is a fact about the building. It is the one piece of the
building generator an arena calls into: a building document has no terrain of its own, since a
building is generated in its own space at ground level and only meets a heightfield when a map
places it.

**A pad beats an apron, whatever order they were graded in.** Two structures are kept two metres
apart and an apron reaches four, so without that rule a building close to another would tilt the
ground under its neighbour — on some seeds and not others, which is the worst way for a bug to
arrive. The spawn areas are graded first and for their own sake as well: a spawn on a slope gives
one team a look down it.

**A road gets a profile, not a pad.** The second thing graded into the field is a corridor: a
polyline held to a height at every sample, with the same smoothstep apron either side of it that a
pad has, measured to the line instead of to a rectangle. A road is the one thing on a map that is
neither level nor free to follow the ground exactly — held at one height it is a shelf, and over
sixty metres of map that leaves one end of it floating in the air and the other buried; laid straight
onto the ground it climbs faster than anything can drive up. So the ungraded ground is sampled along
the centreline and then clamped, by a forward sweep and then a backward one, until no stretch of it
exceeds `MaxRoadGradient`. Most of the time that clamps nothing: the router would not have laid a
step it could not grade in the first place, and what the sweep really does is carry the ends inward.

**The ends are pinned before anything is swept, and a junction is solved once.** A portal takes the
recorded pad height of the structure whose doorway it serves, so a path arrives level with the door
sill instead of a step below it. Everything else takes the mean of the ungraded ground over its own
disc, which is the same cut and fill a pad makes and settles a crossing between the high and the low
ground it stands on — except a junction standing on a trunk, which takes the height the trunk is
actually at where it passes. That exception is not a refinement: an artery cut into a bank runs well
off the natural ground, and a branch joining it at the ground's height would meet it at a step of
exactly that much, which is the failure solving junction heights was meant to prevent arriving by the
other door. With both ends fixed, each polyline is then an independent profile between them.

**The profile is sampled finer than the road is drawn.** `RoadRouter.Smooth` string-pulls, so a
straight run across a map is two points fifty metres apart — the right polyline to draw a road along
and the wrong one to grade against. A height at each end and a straight line between them is a ramp
that bulldozes every rise it crosses, and two roads running side by side over the same rise come out
at different heights because their vertices fell in different places. So `RoadProfile` is the same
line resampled at the placement grid's own cell. Keeping the two apart is what lets "this segment is
too steep to route" go on being a claim about the plan while "this ground climbs too fast" is a claim
about the profile.

**A road follows the road it braids into.** The router's cost decay exists to make two routes share
ground, so two roads over the same metre of map is the ordinary case; a road that sampled the natural
ground under a trunk it is running along would be graded to a different height over ground the trunk
had already claimed. Measured on the default map, that came out as a step of a metre and a half down
the length of a shared stretch. So a sample whose own carriageway overlaps one already swept takes
that carriageway's height. Overlapping rather than standing on: two roads that merely touch are the
same failure at half the width, because the ground is at full weight under both and the later one
wins outright with no blend between them at all.

**The precedence, in one place.** A pad beats a corridor — a building is a stack of level storeys and
a road may not tilt the ground under one, where a road crossing a pad has a doorstep's worth of blend
to give. A corridor beats a pad's apron, for the reason a pad does: a carriageway a neighbouring
building tilted is a road with a camber nobody asked for. A corridor beats a corridor's apron, so a
crossing is road rather than the average of two verges. And a solved junction height beats either
polyline, which is arranged by grading the junction discs last and letting the later corridor win.

**None of it touches a `TerrainData`.** `TerrainWriter` rebuilds the whole heightmap out of the field
on every realise, so a carve applied to the terrain asset is erased by the next regenerate or
deepened by every one. A feature graded into the field is a feature the field is a function of, which
is the only kind that survives — and it is why the road stage needed no change to the writer at all.

The same is true of the splat map, which is the other thing a `TerrainData` holds. `TerrainSplatWriter`
rebuilds the window the network reaches on every realise, so the surface is derived from the document
exactly as the heights are. See "The road surface is painted and its edges are placed" in section 5
for what painting into a binary asset costs and why the terrain is still not authoritative over
anything.

**A fence is planted in the ground; everything else is stood on it.** `Placement.AtQuarterTurn`
lifts a piece by its `BaseOffset` so the underside of the art lands on the surface, and every caller
adds the height of that surface on the way out — which is right for a crate and wrong for a fence.
Fence art is modelled with a foundation reaching below the pivot, and standing that on the ground is
a fence hovering a metre in the air with its own footings on show. So the two fence stages seat a
panel with `TerrainField.Planted`: the pivot goes exactly on the ground at the panel's own
coordinate, and whatever the art has below the pivot goes under it, which is what the foundation is
modelled for.

**A fence follows a slope by stepping, not by leaning.** One height per panel and no tilt: the
rotation is left exactly as the run laid it, a quarter turn about Y and nothing else. Pitching each
panel to the ground's normal is the obvious alternative and it is wrong in a way no height assertion
would catch — every post out of plumb, every join between two panels a wedge, and a gate frame no
longer square. Held upright and stepped, a run down a hillside is what a real fence is, and the
wedge of open ground each step opens under a panel is exactly what the foundation covers.

**The field decides where things stand; and the splat map is the one thing that decides what the
ground looks like.** The tool composes prefabs and does not author a mesh, so `TerrainWriter` hands
the numbers to a Unity `Terrain` — a component whose whole job is to render a grid of heights — and
that is the only shape this tool puts into a scene. It is not an exception to section 3; building a
ground mesh would be. `TerrainSplatWriter` is the same bargain about a different array on the same
component, and it does not extend one inch further: a road *surface* is painted because a terrain
already blends layers by weight, and a road's *edge* is placed as art because a ribbon of geometry
down a polyline is exactly the model section 3 forbids.
It does mean a map with relief and no terrain assigned is a map whose crates follow ground nothing
draws, which is why `TerrainAmplitude` defaults to zero: verticality is something you turn on once
there is a terrain to put it on, and until then every map is exactly the flat map this tool
generated before the field existed.

## 9. Assembly layout

```
Packages/com.pinchasvaknin.arenaforge/
  package.json
  Runtime/Core/       ArenaForge.Core.asmdef      noEngineReferences, Newtonsoft only
  Runtime/Unity/      ArenaForge.Unity.asmdef     → Core
  Editor/             ArenaForge.Editor.asmdef    → Core, Unity; Editor platform only
  Tests/EditMode/     ArenaForge.Tests.asmdef     → Core, Unity, Editor; NUnit; UNITY_INCLUDE_TESTS
  Samples~/ArenaDemo/ ArenaForge.Samples.asmdef   → Core, Unity, Input System
```

`Samples~/` holds the demo scene, its placeholder prefabs and the two scripts that drive it. The
tilde keeps it out of the asset database until Package Manager copies it into a project, which is
also what stops the demo from becoming something the tool depends on: nothing references that
assembly, and deleting the folder leaves the tool intact. It is the only part of the package that
needs URP and the Input System.

Tests ship inside the package rather than beside it, so a consumer can run them against their own
Unity version by adding the package to `testables` in their project manifest.

Unity 6 (6000.3.11f1 during development, `6000.0` declared as the minimum),
`com.unity.nuget.newtonsoft-json` as the only dependency.
