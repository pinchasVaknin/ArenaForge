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

A `WorldDoc` holds a schema version, a seed, a parameters object, the generated object list, and an
override list. Resolving it means running the generator and then applying overrides on top.

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

## 5. Assembly layout

```
Assets/ArenaForge/
  Runtime/Core/     ArenaForge.Core.asmdef      noEngineReferences, Newtonsoft only
  Runtime/Unity/    ArenaForge.Unity.asmdef     → Core
  Editor/           ArenaForge.Editor.asmdef    → Core, Unity; Editor platform only
  Tests/EditMode/   ArenaForge.Tests.asmdef     → Core, Unity; NUnit; UNITY_INCLUDE_TESTS
```

Unity 6 (6000.3.11f1), URP, `com.unity.nuget.newtonsoft-json`.
