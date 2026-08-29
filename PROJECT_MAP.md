# PROJECT_MAP.md

For judging a proposed idea against what ArenaForge is: does it fit, where does it attach, what does
it break, what does it cost. Claims are tagged `[decided]` (explicit choice, recorded reason),
`[assumed]` (implied, never stated) or `[open]` (undecided), cited by file and section.

---

## 1. SNAPSHOT

ArenaForge generates small competitive arena maps in the Unity editor — 60 × 60 m by default, a
building in the middle, structures on the flanks, a fence, cover, two opposing spawns — from a seed,
parameters and a layer of manual edits, then measures the result against playability thresholds. A
map is never a saved scene: it is a document you can regenerate after hand-editing without losing
the edits (`ARCHITECTURE.md` §2). It never authors geometry; it composes existing prefabs. Audience:
a Unity level designer with an art pack, the tool doubling as a portfolio piece `[assumed]`.

**Build state: built, and green.** UPM package `com.pinchasvaknin.arenaforge` 0.1.0; Unity
`6000.0` minimum, built on 6000.3.11f1; sole dependency `com.unity.nuget.newtonsoft-json`. On disk:
~30,500 lines of runtime + editor C#, ~24,400 of tests, 658 EditMode cases in 47 files, all passing
(2026-08-29, 540 s). The first eleven commits are dated 2026-08-16 and end at *Package as UPM*;
everything after — building generator, terrain, roads, overlay, workspace, starter art — landed as
one baseline commit on 2026-08-29. **So the reasoning is in `CHANGELOG.md` `[Unreleased]` and the
source remarks, not in the commit sequence** `[decided]`. The art pack under `Assets/Models` is
gitignored, per the README's "no third-party assets are committed".

---

## 2. SPINE VS. APPLICATION

The spine survives a change of subject matter. Swapping the application is cheap; moving the spine
is not. Nothing below is unassigned.

**The spine** — all `[decided]`, all from `ARCHITECTURE.md`:

- **Three layers**, `Editor → Unity → Core`, Core a leaf, compiler-enforced.
- **Document = seed + parameters + overrides**; resolve = generate, then apply overrides.
- **Ids are readable generation paths** (`map/lane_mid/cover_03`); user objects under `user/`.
- **Determinism**: own RNG, `Fork(label)` per subsystem, FNV-1a, ordered iteration, `"R"` floats.
- **Art reached only by logical id and tag query**; Core never sees a prefab or path.
- **Placement is propose-and-judge**, and the *refusal* is kept, not reduced to a boolean.
- **Derived state is recomputed**: terrain, roads and GameObjects are functions of the document.
- **The document is authoritative, the scene derived**; capture diffs the scene back into overrides.
- **The document lives on the component as JSON** — one persistence path, undo-able as a string.
- **Engine-free property testing** over thousand-seed sweeps.
- **Two document kinds sharing one function** (`OverrideResolution`), not one generalised kind.
- **Constraints are an enum and a switch** — the mechanism, not the members.

**The application**:

- A two-team arena: lanes, two spawns, a centre building, flank structures, a fence. `[decided]`
- `ArenaLayout` = one rectangle in parallel lane bands. `[decided]`, named the first thing to fix.
- The stages: layout, building, fence, dressing, three road stages, cover, terrain. `[decided]`
- The thirteen constraint *members*. `[decided]`
- The seven metrics and thresholds, measured over seeds 1..1000 then given headroom. `[decided]`
- The tag vocabulary and the workspace folders spelling it. `[open]` — S1.
- Starter art from `ArenaAssetBuilder`; placeholder primitives in the sample. `[decided]`
- ~1 structure per 2,500 m², so a 400 m playfield is a town. `[decided]`

The 2D analysis is application, but load-bearing for the spine's affordability: 3D slows every sweep.

---

## 3. INVARIANTS

**I1 — Core references no engine type.** Keeps generation testable without a scene, which is what
makes thousand-seed sweeps affordable; violated, the suites go editor-bound and stop being run.
`noEngineReferences: true`; `AssemblyBoundaryTests`. **Hard.** `[decided]`

**I2 — Core's only precompiled reference is Newtonsoft.Json.** The same boundary applied to
dependencies. `overrideReferences: true`. **Hard.** `[decided]`

**I3 — Same seed and parameters ⇒ byte-identical output, anywhere.** Otherwise a reshuffled id
invalidates every edit and a failing seed cannot be reproduced — silently, on another machine.
Determinism tests. **Hard.** `[decided]`

**I4 — Ids are reproducible generation paths; `user/` is separate.** A hash changes whenever the
object does, so every override lands on nothing. **Hard.** `[decided]`

**I5 — An orphaned override is surfaced, never dropped.** `Resolve()` returns them; the editor
offers keep/discard. Dropping an edit silently is what would make the tool untrustworthy.
**Hard.** `[decided]`

**I6 — Derived state is recomputed, not stored.** Otherwise the map is held twice, with two chances
to disagree. **Hard.** `[decided]`

**I7 — Compose prefabs; never author geometry or scale non-uniformly.** A per-axis scale is a schema
change to every pose in every document. Exception: `ArenaAssetBuilder`, Editor-side. **Hard.** `[decided]`

**I8 — No speculative abstraction; refactor on the second real case.** Single-implementation
interfaces are dead weight (`CLAUDE.md` rule 3). **Soft.** `[decided]`

**I9 — Sessions end green; thresholds are never quietly weakened.** A metric failing across many
seeds is a generator bug (rule 4). **Soft.** `[decided]`

---

## 4. COMPONENT INVENTORY

All `[built]`.

| Component | Responsibility | Changes that touch it |
|---|---|---|
| `Core/Math`, `Core/Rng` | Math types; seeded RNG, FNV-1a | almost nothing; a change here is an I3 event |
| `Core/Catalog` | Entries, tags, weighted tag query | a new tag vocabulary |
| `Core/World` | Two documents, params, overrides, resolution | a new parameter or override op |
| `Core/Serialisation` | `ArenaJson`; `kind` before version; v2, reads v1 | any schema change |
| `Core/Placement` | `ConstraintSet` (13 kinds), grid, Poisson disk, stats | a fourteenth rule; a new sampler |
| `Core/Generation` | Layout, building, plan, cover, dressing, fence, wall runs, terrain, roads | most new features |
| `Core/Analysis` | `MapAnalyzer` → `MapReport`: 7 metrics + exposure map | a new metric; a 3D model |
| `Runtime/Unity` | `ArenaMap`, `WorldRealizer`, `CatalogAsset`, terrain writers | a parameter that must reach the inspector |
| `Editor` | Window, Overlay, capture, catalog sync, workspace, guides, heatmap, exports | any UI change |
| `Tests/EditMode` | 47 suites, 658 cases, thousand-seed sweeps | every change |
| `Samples~/ArenaDemo` | Demo scene and prefabs; the only part needing URP | nothing depends on it |

```
Editor ──► Unity ──► Core ──► Newtonsoft.Json
   └────────────────►┘
Samples~ ──► Unity, Core, Input System        (nothing references Samples~)
```

---

## 5. DATA AND CONTROL FLOW

`ArenaParams` (seed inside the parameters, not beside them) + `Catalog`
→ `ArenaLayout.Build` (lane bands) and `TerrainField.Build` (hashed noise, not a stream)
→ spawn markers → structures, each cutting a level pad and declaring doorways
→ `PerimeterFence` — first: the boundary may not have a hole in it
→ `ExteriorPlacer` — dressing, placing round what the boundary left
→ `RoadNetwork.Build` — after doorways, before cover; **reserves ground, adds no object**
→ `roads.GradeInto(terrain)` → `RoadKerbs` → `RoadFurniture`
→ `CoverPlacer` — Poisson-disk candidates judged by `ConstraintSet`
→ **`WorldDoc`** → `Resolve()` → **`ResolvedWorld`** → `WorldRealizer` → GameObjects. Separately
`MapAnalyzer.Analyze` → **`MapReport`**, and the terrain writers → a `Terrain`. Stage order is
load-bearing and commented as such in `ArenaLayoutGenerator.Generate`. `[decided]`

**Formats.** `WorldDoc` JSON — v2, `kind: "world"`, reads and upgrades v1. `BuildingDoc` JSON — own
numbering, v1, same `kind` check. Catalog JSON — v1, from `CatalogAsset` (id, tags, footprint,
height, weight, sockets). Exported prefabs are snapshots with components stripped: a copy at a
moment, not a link. `[decided]`

---

## 6. EXTENSION POINTS

**E1 — Catalog rows and the art behind them.** Point `CatalogSync` at a folder tree; folders name
tags, colliders name sizes. Minutes. Heavily used. **Real.** `[decided]`

**E2 — A fourteenth constraint kind.** An enum member, a `case` label, a factory method. An hour.
Five of the thirteen arrived this way. **Real, with evidence.** `[decided]`

**E3 — A new override op.** A member plus a branch in `OverrideResolution`. Small in Core, larger in
UI. Four exist; `SwapAsset` has no UI. **Real, half-used.** `[decided]`

**E4 — A new pipeline stage.** A static `Place(...)` returning placements, inserted at a justified
point in `Generate`. Days, plus a property suite. The road layer arrived this way. **Real, with
evidence.** `[decided]`

**E5 — A new metric.** A field on `MapReport`, a threshold on `MapThresholds`, a default measured
over seeds 1..1000 first. A day plus the sweep. **Real.** `[decided]`

**E6 — A third document kind.** The two share only `OverrideResolution`, so a third means a third
serialisation path. **Real but costly.** `[assumed]`

**E7 — A second engine adapter.** No seam exists; refused (X3). **Aspirational.** `[decided]`

No rule DSL, plugin registry, event bus or controller between the surfaces. The absence is the
design (`CLAUDE.md` rule 3).

---

## 7. OUT OF SCOPE

All from `FUTURE.md`, "Scope that was considered and rejected", all `[decided]`. Dates are not
recorded per item; stages are, loosely — `[open]` where none is given.

**X1 — Non-uniform scaling to close a fence run.** Cut on I7: a 43% squash of modelled stonework and
a schema change to every pose. Stage: when the boundary was made to close at every map size.
*Technical.*

**X2 — A rule DSL or constraint plugin points.** A ninth rule is one switch; an extension point is a
guess about a caller that does not exist. *Technical.*

**X3 — A second engine adapter** and **X4 — an interchange format.** Portability is a *consequence*
of the Core boundary, not its goal. Stage: from the start. *Technical.*

**X5 — Weather, day/night, biomes, multi-map worlds.** Content variety on a tool whose problem is
placement and validation. *Portfolio focus.*

**X6 — Runtime generation for shipping games.** It runs in a build; nothing is profiled for a game
loop. *Scope.*

**X7 — A catalog GUI beyond the inspector.** Nicer, not the point. *Scope.*

**X8 — A catalog sync that deletes.** An authoritative folder would delete exported buildings and
hand-bound rows sharing the file. *Technical.*

**X9 — A renderer-bounds fallback for footprints.** Would fold shadow-casting flourishes into a
footprint. *Technical.*

**X10 — Non-rectangular footprints, and exact kerb mitres.** A box errs safely for rules that keep
things apart; a mitre needs a transcendental inside a deterministic placement. *Technical.*

---

## 8. SOFT SPOTS AND OPEN QUESTIONS

**S1 — The tag vocabulary is unsettled.** `prop/decor` and `propbuilding/decor/decoration` are two
live tags for the same art, the deeper matched at full depth so it misses the exterior folders;
`Props/fence` is lowercase among PascalCase siblings. Any new stage inherits it. `[open]`

**S2 — The analysis is 2D and terrain-blind.** Occluders are XZ rectangles with a vertical span and
exposure is one eye height on flat ground, so every metric is a claim about the ground floor of a
level map. Fixing it needs walkable *surfaces*. `[decided]` limitation, `[open]` fix.

**S3 — Lanes are parallel bands in a rectangle.** Named the thing to fix first: that symmetry is why
seeds feel more alike than they look. It needs lanes to become a route graph, which puts it at the
spine boundary. `[decided]` as a weakness.

**S4 — Two threshold numbers in three documents.** `docs/loop.md` prints `ExposureAsymmetry at most
0.25`; `REPORT.md` and `MapThresholds.cs` say 0.30. **Conflict** — trust `MapThresholds.cs`.

**S5 — Verification is manual.** No CI configuration exists, so I1, I3 and I9 rest on a suite that
nothing runs automatically; it is run headlessly by hand. `[open]`.

---

## 9. IDEA EVALUATION PROTOCOL

The procedure a future session follows when handed a new idea, reproduced verbatim from Prompt B.

1. RESTATEMENT: Restate the idea in one paragraph, in your own words, including what you take to be its underlying motivation. If any part of the idea is ambiguous, state your reading explicitly and flag it.
2. TIER: Does this idea change the spine, the application, or both? Name the specific spine items it touches.
3. INVARIANT CHECK: Go through the invariants and report which the idea respects, which it strains, and which it breaks. For each break: is it hard or soft, what concretely stops working, and is there a variant of the idea that avoids the break.
4. ATTACHMENT: Which extension points does the idea use? What new components would be needed? Which existing components would need modification rather than extension?
5. COLLISIONS: Which existing decisions does the idea contradict? Does it revive anything from the out-of-scope list?
6. COST: Estimate the work in build sessions, and state what it displaces from the current plan. Separate the cost of the idea itself from the cost of the refactoring it forces.
7. PORTFOLIO IMPACT: Does the idea make the demonstration sharper, or merely larger? Would a reviewer read it as focus or as scope creep?
8. VERDICT: One of: adopt as proposed / adopt with modifications / reject. Refactoring Note: The developer is explicitly open to deep architectural refactoring and rewriting core components if it serves the new direction. Do not reject an idea purely because it breaks the spine. Instead, detail exactly what structural changes and architectural rewrites will be necessary to make it work.
9. ALTERNATIVE: If the idea addresses a real underlying need but does so badly, propose the better way to address that same need.
