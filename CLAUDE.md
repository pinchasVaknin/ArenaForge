# CLAUDE.md — standing rules for ArenaForge

Read `ARCHITECTURE.md` before starting work. These rules hold for every session and override any
habit or convention you would otherwise reach for.

## 1. Core never references the engine

`Runtime/Core/` must never reference `UnityEngine` or `UnityEditor`. This is enforced by
`"noEngineReferences": true` in `ArenaForge.Core.asmdef` — if you find yourself wanting to add one,
**stop and ask**. Do not remove the flag, do not move the code into the Unity assembly to dodge the
question, and do not add an interface in Core to be implemented in Unity as a workaround.

Core also sets `"overrideReferences": true` with `Newtonsoft.Json.dll` as its only precompiled
reference. Adding another Core dependency is the same kind of decision — stop and ask.

## 2. Determinism is a hard requirement

The same seed and the same parameters must produce byte-identical serialised output across runs and
across machines.

- Never use `System.Random` or `UnityEngine.Random`. Use the project's seeded RNG struct.
- Never iterate a `Dictionary` or `HashSet` where iteration order can affect output. Use a `List` or
  an explicitly ordered key sequence.
- Never rely on `GetHashCode` across runs — it is not stable. Use the project's FNV-1a hash.
- Fork the RNG per subsystem rather than sharing one stream, so one stage's draws cannot shift
  another's.

## 3. No speculative abstraction

Concrete implementations only. No interfaces, base classes, plugin points, event buses or
configuration hooks for needs that have not appeared yet. One adapter, one implementation. When a
second real use case shows up, refactor then — with the second case in hand, the right shape is
obvious; without it, it is a guess that has to be maintained.

A single-implementation interface with one caller is dead weight. Delete it.

## 4. Every session ends with the Test Runner green

Not "green except for a known failure". If a test cannot be made to pass, say so explicitly in the
session summary and explain why, rather than deleting it, skipping it, or loosening its assertion
quietly.

Thresholds in the validation suite are part of the deliverable. Do not weaken a threshold to make a
suite pass without saying so in the summary and explaining why the original threshold was wrong. A
metric failing across many seeds is a generator bug, not a test bug.

## 5. Build what was asked, and nothing else

Do not add features that were not requested. If a prompt seems to require something outside its
stated scope, **stop and ask** rather than inventing it. Ideas that are out of scope go in
`FUTURE.md`, not into the code.

## Conventions

- Non-US spelling in prose (`serialise`, `metre`); .NET API names stay as they are.
- Stable ids are readable generation paths (`map/lane_mid/cover_03`), never GUIDs or hashes.
  User-created objects live under the `user/` namespace.
- Public API gets XML doc comments. Internals get comments only where the reasoning is non-obvious.
