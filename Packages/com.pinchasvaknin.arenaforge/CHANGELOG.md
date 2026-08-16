# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — 2026-08-16

First release.

### Added

- **Core data model** — engine-free math types, a PCG32 RNG with per-subsystem `Fork`, FNV-1a
  stable hashing, a tag-queried catalog, and a `WorldDoc` of seed, parameters, generated objects and
  edit overrides. `Resolve()` applies the overrides and returns anything it could not apply as
  `OrphanedOverrides` rather than dropping it. Newtonsoft round-trip serialisation at round-trip
  float precision.
- **Arena layout generator** — lane bands, two opposing spawns, a building in the middle lane and a
  house on a flank, each snapped to the grid and carrying its doorways as world-space rectangles.
- **Constraint-based cover placement** — eight concrete constraints, Poisson-disk candidate
  sampling over the cells a placement grid still has open, weighted catalog selection at a
  configurable low-to-high ratio, and props attached to catalog sockets tagged `prop_surface`.
  Rejections are counted by constraint and recorded in the document.
- **Validation and analysis** — `MapAnalyzer` measures spawn separation, exposure asymmetry, cover
  coverage, longest open sightline and connectivity, each against a configurable threshold, plus an
  `ExposureMap` of every walkable cell. Visibility is segment-versus-rectangle arithmetic in Core,
  not physics raycasts.
- **Unity adapter** — `CatalogAsset` binding logical ids to prefabs with a JSON export,
  `WorldRealizer` instantiating a resolved document under one root, and `ArenaMap` holding a scene's
  parameters and document.
- **Editor tool** — a UI Toolkit window for generating, regenerating, saving and loading; a live
  validation panel; an exposure heatmap as a texture and as a scene-view overlay; scene edits
  captured as overrides; an override list with per-row revert; orphaned overrides surfaced with
  keep and discard actions; and every mutation grouped into one named Undo step.

[0.1.0]: https://github.com/pinchasVaknin/ArenaForge/releases/tag/v0.1.0
