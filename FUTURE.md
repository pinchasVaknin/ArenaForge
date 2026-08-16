# FUTURE.md — things deliberately left out

Everything here was considered and left out on purpose. Some of it is scope that would have made the
tool worse before it made it better; some is a real limitation with a known shape. Nothing here is a
promise.

The rule this file exists to serve is in `CLAUDE.md`: ideas that are out of scope go here, not into
the code.

---

## Known limitations, with what fixing them would take

**The analysis is two-dimensional.** Occluders are rectangles on the XZ plane with a vertical span,
and exposure is measured at one eye height on the ground. A two-storey building is a single
footprint that blocks sightlines through it; nobody stands on its upper floor. Fixing this properly
means walkable *surfaces* rather than a walkable grid, and a visibility test that knows about floor
levels — a much larger change than it sounds, and one that would slow the thousand-seed suite by
whatever the third dimension costs.

**The playfield is a rectangle.** `ArenaLayout` divides one rectangle into parallel lane bands.
An L-shaped or asymmetric arena would need lanes to be a described route graph rather than a band
subdivision. That is the change most likely to be worth making first, because lane-band symmetry is
what makes different seeds feel more alike than they look.

**`SwapAsset` has no editor UI.** The override op exists, `Resolve()` applies it, and it round-trips
through JSON — but nothing in the tool window produces one. Swapping which prefab a slot uses is a
right-click on a realised object away; it was not asked for.

**Edit capture only runs while the tool window is open.** This is a deliberate trade rather than an
oversight — see ARCHITECTURE.md section 6 — but it does mean deleting a realised object with the
window closed goes unrecorded, and the object comes back on the next regeneration.

**Regeneration rebuilds every GameObject.** A map of a few hundred props rebuilds fast enough that
it has never been worth fixing, but an incremental realiser that diffed the resolved list against
the scene and touched only what changed would make regeneration feel instant on much larger maps.

**Play-mode edits are not captured.** `ArenaEditCapture` is editor-only. Moving a prop in play mode
changes a GameObject and nothing else, as it does in any Unity workflow.

**The sample needs URP and the Input System; the tool does not.** The demo's materials are URP and
its free-fly camera is written against the Input System, so importing the sample into a project with
neither will not compile. Declaring them as package dependencies would force a render pipeline on
everyone using the generator, which is worse. A sample built on primitives and a legacy camera would
avoid it.

---

## Scope that was considered and rejected

**A second engine adapter.** The Core/Unity boundary exists so generation is testable without an
engine, and portability is a consequence rather than the goal — ARCHITECTURE.md section 1 is
explicit about this. Building a second adapter now would mean guessing at the shape of the seam.
With a real second case in hand it would be obvious.

**An interchange format.** Same reasoning. `WorldDoc` JSON is already readable by anything that can
parse JSON; a format designed for exchange with a consumer that does not exist would be fiction.

**A rule DSL or plugin points for constraints.** The eight constraints are an enum and a switch. A
ninth is a change to one switch. An extension point would be a guess about a caller that does not
exist, and would have to be maintained either way.

**Weather, day/night, biomes, multi-map worlds.** All of it is content variety layered on a tool
whose interesting problem is placement and validation. A finished small tool beats an unfinished
large one.

**Runtime map generation for shipping games.** Generation *is* runtime code and does run in a build
— that is why it lives in `Runtime/` — but nothing here has been profiled or budgeted for a game
loop. Treat it as an authoring tool that happens not to be welded to the editor.

**A GUI for authoring catalogs beyond the inspector.** `CatalogAsset` is a list of rows in the
default inspector plus one export button. A dedicated catalog editor with prefab previews and
footprint gizmos would be nicer and is not the point of the project.
