# Contributing to ArenaForge

Thanks for looking. This file is short on ceremony and long on constraints, because ArenaForge has a
few rules that are unusual enough that a well-meant patch can break them without the author ever
noticing. None of them are style preferences — each one is load-bearing, and each has its reasoning
written down.

**Read [`ARCHITECTURE.md`](ARCHITECTURE.md) before starting.** It is long, and it records why each
decision was made, usually with the measurement that settled it. Most obvious improvements are
already discussed there and rejected for a reason. Arguing against that reasoning is welcome;
arguing around it wastes your time and mine.

---

## The four rules

### 1. Core never references the engine

`Runtime/Core/` must not reference `UnityEngine` or `UnityEditor`. This is enforced by
`"noEngineReferences": true` in `ArenaForge.Core.asmdef`, so it is a compile error rather than a
review note. Core also sets `"overrideReferences": true` with `Newtonsoft.Json.dll` as its only
precompiled reference, so it cannot quietly acquire a second dependency either.

If you find yourself needing one, **open an issue and ask** before writing the patch. Do not remove
the flag, do not move the code into the Unity assembly to dodge the question, and do not add an
interface in Core to be implemented in Unity as a workaround — that is the same change wearing a
hat.

The boundary exists so generation logic is testable without an engine, without a scene, and without
the editor loop. The thousand-seed property sweep is only affordable because a map can be generated
and analysed as plain data. Portability is a consequence of the boundary, not its purpose: there is
no second engine adapter and no plan for one.

### 2. Determinism is a hard requirement

The same seed and the same parameters must produce byte-identical serialised output, across runs and
across machines. Concretely:

- **Never** use `System.Random` or `UnityEngine.Random`. Use `Rng` — PCG32, seeded, in
  `Runtime/Core/Rng/`.
- **Never** rely on `GetHashCode` for anything persisted or generated from. It is randomised per
  process. Use `StableHash` (FNV-1a 64-bit).
- **Never** iterate a `Dictionary` or `HashSet` where iteration order can reach the output. Use a
  `List` or an explicitly ordered key sequence. Serialised maps use `SortedDictionary` with
  `StringComparer.Ordinal` for exactly this reason.
- **Fork the RNG per subsystem** — `rng.Fork("cover/lane_mid")`. A fork derives from the stream's
  *seed* and the label, not from its current position, so forking consumes no draw and adding a draw
  to an earlier stage cannot reshuffle a later one.
- **Avoid runtime trigonometry in generation decisions.** `sin`, `cos` and `atan2` are not
  guaranteed bit-identical across runtimes. `QuarterTurn` and `YawStep` are literal quaternion
  tables; `YawStep.Nearest` sweeps the table rather than dividing an `atan2`; road gradient is rise
  over run rather than an angle; the road router costs are integers so two near-equal routes cannot
  be ordered by the last bit of a float sum.

If your change is meant to be **output-neutral** — a refactor, a spatial index, a file split — say so
in the pull request and prove it: identical serialised documents over a sample of seeds, before and
after. "The tests still pass" is not the same claim.

### 3. No speculative abstraction

Concrete implementations only. No interfaces, base classes, plugin points, event buses or
configuration hooks for needs that have not appeared yet. One adapter, one implementation.

A single-implementation interface with one caller is dead weight — delete it. When a second real use
case shows up, refactor *then*: with the second case in hand the right shape is obvious; without it,
it is a guess somebody has to maintain.

Two examples of the rule working, so it does not read as dogma. `ConstraintKind` is a closed enum of
thirteen rules evaluated by one switch — when interior decor arrived and needed to know where a wall
was, that switch was the obvious place to change, and adding a fourteenth rule stayed a two-line job.
And `OverrideResolution` lived inside `WorldDoc` until `BuildingDoc` became a genuine second caller,
at which point it moved out — as a static method over two lists, not as a base class, because the two
documents share one function and no state.

### 4. Every change ends with the Test Runner green

Not "green except for a known failure". If a test cannot be made to pass, say so in the pull request
and explain why, rather than deleting it, skipping it, or quietly loosening its assertion.

**The thresholds in `MapThresholds` are part of the deliverable.** They were derived from the
measured spread of seeds 1..1000 with headroom on top, and every derivation is recorded in
[`REPORT.md`](REPORT.md). Do not weaken one to get a green run. A metric failing across many seeds is
a generator bug, not a test bug — and if you genuinely believe a threshold was wrong, that is a pull
request of its own, with the new distribution measured and `REPORT.md` updated in the same change.

---

## Running the tests

**A local run is the validation gate on this project.** There is a CI workflow in
`.github/workflows/tests.yml`, but it is dormant — Unity no longer permits a Personal seat to be
activated in CI, so nothing on GitHub can start an editor for us. The workflow header explains the
detail and how to switch it on if that ever changes. Until then, "the tests pass" means you ran them,
so please actually run them before opening a pull request, and say in the description that you did.

**In the editor:** Window → General → Test Runner → EditMode → Run All. 736 tests.

**Headless**, which is the same suite without the editor UI in the way:

```bash
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode \
          -testResults results.xml -logFile run.log
```

On Windows, `Unity.exe` is a GUI-subsystem binary: launched from PowerShell with `&` it returns
instantly with an empty exit code while the editor is still running, which looks exactly like a
crashed run. Use `Start-Process -Wait`:

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe"
Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  "-batchmode", "-projectPath", ".", "-runTests", "-testPlatform", "EditMode",
  "-testResults", "results.xml", "-logFile", "run.log")
```

The exit code is 0 when everything passed and 2 when something failed, but read `results.xml` rather
than trusting the code — it is NUnit XML, and the `<test-run>` element carries `total`, `passed` and
`failed` as attributes.

A full run is about ten minutes on a normal machine. Three things worth knowing:

- **Do not run anything CPU-heavy alongside it.** `MapValidationTests` is a thousand-seed
  `Parallel.For` sweep under NUnit's 180-second per-test timeout. It finishes comfortably with the
  machine to itself and times out when starved — which reads exactly like a generator regression and
  is not one. Re-run clean before believing a failure.
- **A running editor holds the project lock**, so a batch run fails while one is open.
- **Do not edit source while a run is in flight.** A domain reload part-way through invalidates the
  results, and the run that comes back is not a run of either version.

### The `Slow` category, and the short run

Three fixtures are categorised `Slow` — `RoadFurnitureTests`, `MapCompositionTests` and
`RoadKerbTests`. They are the three most expensive suites in the project and nothing else.

| | Tests | Measured |
|---|---|---|
| `-testCategory "!Slow"` | 700 | ~5½ min |
| everything | 736 | ~10 min |

The thousand-seed validation sweep is deliberately **not** in that category. It costs 57 seconds and
it is the one guarantee that should never be deferred, so the short run keeps it.

Use the short run while iterating; **run the full suite before you push or open a pull request.**
Since CI cannot run it for us, the full suite is only ever as reliable as the last person who
remembered to run it.

One trap, recorded because it cost an afternoon: excluding fixtures with `-testFilter` and a
negative-lookahead regex does **not** work. Unity accepts the pattern, ignores it, and runs all 736 —
so a run you believe is filtered is quietly the full one. Only `-testCategory` honours `!`.

---

## House conventions

- **Non-US spelling in prose and comments** — `serialise`, `metre`, `colour`. .NET API names stay as
  they are.
- **Stable ids are readable generation paths** — `map/lane_mid/cover_03`, never GUIDs or content
  hashes. A path is reproducible from the same seed and survives a regeneration that did not disturb
  its branch; a hash changes whenever the object does, which is precisely wrong for something whose
  job is to keep pointing at the same object. User-created objects live under `user/`.
- **Public API gets XML doc comments.** Internals get comments only where the reasoning is
  non-obvious — and the house style is that a comment explains *why*, ideally with the measurement
  that settled it. Look at `ArenaParams.MaxRoadGradient` or `PlacementConstraint.OffReservedPath` for
  the register. A comment restating what the line does is noise.
- **Commit messages are unprefixed sentences** describing what the change does, in the project's own
  voice — "Route the roads round a fence somebody stood by hand". No `feat:` / `fix:` prefixes.

### Adding a parameter

An `ArenaParams` property lives in four places, and forgetting one used to fail silently:

1. The property itself, with its `[JsonProperty(Order = …)]`
2. `ArenaParams.Clone()`
3. `ArenaMap`'s serialised field, plus `BuildParams()` and `ApplyParams()`
4. The tool window UI

The first three are now guarded by `ParameterMirrorTests`, which enumerates the parameters by
reflection and fails naming the one you missed. The fourth is not, so it is still on you.

---

## Scope

**Build what was asked, and nothing else.** If a change seems to require something outside its stated
scope, raise it rather than inventing it. Ideas that are out of scope go in
[`FUTURE.md`](FUTURE.md) — which is a register of things deliberately left out, each with the
reasoning that left them out. Check it before proposing a feature: if what you want is listed, it was
considered, and the useful conversation starts from the recorded reasoning.

Things that are settled and not open for a drive-by patch: a second engine adapter, authoring
geometry in code (the tool composes prefabs — a coarse slab opening is answered by a new catalog row,
from the art side), a constraint plugin system, a query language for the catalog, and merging
`WorldDoc` with `BuildingDoc`.

---

## Pull requests

Keep them focused — one concern per pull request. In the description, say:

- what changed and why,
- whether the output is expected to move (and if not, how you proved it did not),
- anything you tried that did not work, which is often the most useful part.

If you are fixing a bug, a failing test that demonstrates it, added first, is worth more than the fix
that follows.

Questions and design discussions are welcome as issues before any code is written — especially for
anything that touches rules 1 to 3, where the right time to disagree is before the patch exists.
