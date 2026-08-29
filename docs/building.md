# The building generator

Four frames, captured headlessly from the demo scene with the demo catalog — the same placeholder
cubes every other figure in `docs/` uses. Pale blue blocks are walls and floor slabs, tan frames are
doorways, orange and olive blocks are low cover and slate blocks are high cover.

| Frame | What it shows |
|---|---|
| `building-1-generated.png` | Seed `20260816`, footprint 12 × 10 m, three storeys 3 m apart: 220 objects, 9 m tall, 209 of them shell. The tan opening on the ground floor is the way in. |
| `building-3-shell.png` | The same building with the two outside walls nearest the camera taken away, so all three storeys are visible at once. |
| `building-4-floorplan.png` | The ground floor from above, on its own: two rooms with a crate each, four corridors, and a doorway in every wall between them. |
| `building-2-placed.png` | Seed `20260816` of the demo map, generated after that building was exported. The plan in the middle lane is the exported prefab seen from above; the tan block is the house, the blue squares are the two spawns. 58 objects, playable on all seven metrics. |

Floor seeds `10024618897969338620`, `10027488623318401655`, `10026532048202047310` — each a `Fork`
of the building seed, each stored in the document.

## Reading the first three frames

**A floor is a plan before it is a pile of crates.** `FloorPlan` cuts the storey in two along its
longer axis, at a line drawn from that floor's own stream, and cuts each half again until a region is
too small to be worth dividing. The leaves are the rooms; every cut becomes a wall with one module
taken out of it for a doorway. That last part is why the plan in the fourth frame is connected
without anything having checked: a binary space partition has exactly one path between any two
leaves, and putting a door in every cut opens all of them.

**A corridor is a leaf that came out one module wide** and at least twice as long — the shape a cut
leaves when it shaves a strip off the side of a region rather than halving it. The four unfurnished
strips in the plan are those. They are left clear on purpose: circulation you have to climb over is
not circulation.

**Nothing in the shell is authored.** The walls, the slab and the doorway frames are all catalog art,
selected by the tags `structure/wall`, `structure/floor` and `structure/doorway`, and tiled along the
plan's runs at the size the catalog declares. Poses carry a uniform scale and nothing is stretched to
fit, which is why the partition is laid out in modules of the wall's own length rather than in metres
— a cut that missed a module boundary would leave a run that cannot be built.

Point it at a catalog with none of that art and it still builds: the storey comes back as one
undivided room with contents in it, which is exactly what a catalog of crates and barriers produced
before the shell existed. The shell is art the catalog either has or does not, not a feature with a
switch.

**There are no stairs, and the entrance is on the ground floor only.** Each storey is partitioned on
its own, so what this is is three floor plans stacked rather than one building you can walk up. A
doorway in the outside wall of floor 2 would open onto a drop. `FUTURE.md` says what adding a
stairwell would cost, and it is more than it sounds: it is the first thing in the building generator
that would make two floors' seeds talk to each other.

## Reading the last frame

The exported building is placed by `ArenaLayoutGenerator` with nothing in `ArenaLayoutGenerator`
having changed. It reaches the map generator through the catalog and nothing else: the export writes
a prefab and a `CatalogAsset` row tagged `structure` and `structure/building`, and the middle-lane
structure slot selects it by tag exactly as it selects a hand-made building.

That is the test of the catalog boundary rather than a convenience. If placing an exported building
had needed a special case in the map generator, the boundary would have a hole in it.

The report on that map:

```
playable (0 failing)
  SpawnSeparation         53   at least 42     pass
  ExposureAsymmetry    0.078   at most 0.25    pass
  CoverCoverage        0.748   at least 0.6    pass
  MaxOpenSightline    71.847   at most 80.61   pass
  SpawnsConnected          1   at least 1      pass
  DoorwaysReachable        1   at least 1      pass
  ReachableFraction        1   at least 0.95   pass
```

To `MapAnalyzer` that building is one footprint and one occluder — its declared 12 × 10 m and its
floors' 9 m. There are now rooms and sightlines inside it and nothing measures any of them; nobody
stands on its upper storeys, and every metric above is a claim about the ground outside. The shell
widened that gap rather than closing it. The analysis staying two-dimensional is a known limit, not
an oversight; `FUTURE.md` says what fixing it would take.

## Recording the per-floor re-roll

The frame this folder does not have is the one that shows the feature best: re-rolling floor 2 and
watching floors 1 and 3 not move. That needs a running editor.

1. Add an **ArenaForge → Arena Building** component to an empty object, point its `World Realizer`
   at the demo catalog, and switch the scene-view overlay to **Item**.
2. Press **Generate**.
3. Press **↻** beside *Floor 2*. Only the middle storey changes — its rooms and its walls as well as
   its contents, because the plan is drawn from the same stored seed the contents are.

Floors 1 and 3 are not regenerated and then found to match — they are not regenerated at all. Each
floor's seed is written into the document, so a re-roll rewrites one number and the other floors
serialise to the bytes they already had. `BuildingGenerationTests` asserts exactly that, on the
serialised form rather than on object counts, and `BuildingStructureTests` asserts it again on the
walls.
