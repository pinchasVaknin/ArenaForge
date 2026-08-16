# ArenaForge — Architecture

ArenaForge is a Unity editor tool that procedurally generates small Call-of-Duty-style arena maps:
roughly 60×60 metres, a two-storey building, a small house, scattered cover, two opposing spawns.

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

---

## 5. Placement is a search under named rules

Cover is not placed, it is *proposed* and then judged. A candidate — a logical id and a pose — is
tested against a `ConstraintSet`, which answers with either an acceptance or the first rule that
refused it. Nothing reaches a world document without having been accepted.

The rules are a closed set of eight: inside the playfield, within a lane, on the grid, no overlap
within a margin, a minimum and a maximum distance from anything carrying a tag, not blocking a
doorway, clear of a spawn. They are an enum and a switch, not an interface with eight
implementations and not a rule language. Every one of them is known here, Core is the only thing
that evaluates them, and a ninth would be a change to one switch. An extension point would be a
guess about a caller that does not exist.

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

## 7. Assembly layout

```
Assets/ArenaForge/
  Runtime/Core/     ArenaForge.Core.asmdef      noEngineReferences, Newtonsoft only
  Runtime/Unity/    ArenaForge.Unity.asmdef     → Core
  Editor/           ArenaForge.Editor.asmdef    → Core, Unity; Editor platform only
  Tests/EditMode/   ArenaForge.Tests.asmdef     → Core, Unity; NUnit; UNITY_INCLUDE_TESTS
  Samples/          ArenaForge.Samples.asmdef   → Core, Unity, Input System
```

`Samples/` holds the demo scene and the two scripts that drive it. It is a fifth assembly rather than
loose scripts so the demo cannot become something the tool depends on: nothing references it, and
deleting the folder leaves the tool intact.

Unity 6 (6000.3.11f1), URP, `com.unity.nuget.newtonsoft-json`.
