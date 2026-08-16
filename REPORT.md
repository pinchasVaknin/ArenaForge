# ArenaForge — map validation

A generated map can satisfy every placement rule and still be unplayable. Nothing in "this crate is
on the grid, inside its lane, clear of the doorway" says the map has a sightline down its whole
length, a spawn overlooked from the other team's roof, or a corner of floor walled off from
everything else. Those are properties of the finished map, and the only way to know them is to
measure it.

`MapAnalyzer.Analyze` measures a resolved world document — generated objects with any hand edits
applied — and returns a `MapReport`: seven metrics, each beside the threshold it has to meet, plus
the exposure of every walkable cell.

All of it is in `Runtime/Core/Analysis/`, engine-free. Visibility is segment-versus-rectangle
arithmetic, not `Physics.Raycast`. A raycast would need a loaded scene, a collider on every prefab
and the editor's frame loop, and the thousand-seed suite below would not be affordable at any of
those prices.

---

## The occlusion model

Every object contributes an **oriented rectangle** on the XZ plane, and it blocks a sightline only
if the vertical span it occupies contains the eye height — `EyeHeight`, 1.6 m by default.

For anything standing on the ground that reduces to "taller than eye height", which is the rule
that separates cover you can shoot over from cover you cannot: a 1.8 m concrete barrier occludes, a
1.0 m crate does not. Stating it as a *span* rather than as a height also gets a crate sitting on a
socket a metre up right — its own height is a metre, but the metre it occupies is the one the eye
line runs through, so it occludes and the identical crate on the floor beside it does not.

The rectangle is oriented rather than axis-aligned because cover can be rotated off the quarter
turns, and the box around a barrier turned 45° is nearly four times its area. A model built on those
boxes would report a map far more sheltered than it plays.

## The visibility sweep

- **Walkable cells** are the playfield grid minus every cell a structure footprint touches.
  Structures only: cover is something you walk round or vault, and deleting the cell under every
  crate would report a well-furnished lane as a broken one.
- **Observers** are `ObserverSamples` walkable cells — 300 by default — drawn by partial
  Fisher–Yates from the seeded stream forked at `analysis/observers`, so the sample holds no
  duplicates and is the same sample on every run of a seed.
- **Exposure** of a cell is the fraction of those observers with an unobstructed segment to it.

A sample rather than every cell, because full pairwise visibility on a 60 m arena is about thirteen
million segment tests. Three hundred observers is roughly a tenth of the floor and settles each
cell's fraction to within a couple of percent — finer than any threshold here cares about — for a
tenth of the work. On the default map the sweep is 3,432 cells × 300 observers ≈ 1.0 M segments,
each tested against the ~28 occluders a finished map carries, and it runs in about 50 ms.

---

## The metrics, and where each threshold came from

Every default below was set from the measured spread of seeds 1..1000 on a default 60 × 60 m map
with the sample catalog, then given headroom. None was loosened to make a suite pass; the two that
moved from their first guesses are called out explicitly at the end.

| Metric | Observed over seeds 1..1000 | Threshold | Headroom |
|---|---|---|---|
| `SpawnSeparation` | 53.0 m on every seed | ≥ 0.70 of the long axis (42.0 m) | 26% |
| `ExposureAsymmetry` | 0.000 – 0.217, mean 0.058 | ≤ 0.25 | 15% |
| `CoverCoverage` | 0.644 – 0.790, mean 0.730 | ≥ 0.60 | 7% |
| `MaxOpenSightline` | 68.8 – 77.3 m | ≤ 0.95 of the diagonal (80.6 m) | 4% |
| `Connectivity.SpawnsConnected` | true on every seed | must be true | — |
| `Connectivity.DoorwayReachableFraction` | 1.0 on every seed | ≥ 1.0 | — |
| `Connectivity.ReachableFraction` | 1.0 on every seed | ≥ 0.95 | 5% |

### SpawnSeparation

Distance between the two spawn area centres. Two spawns close together means both teams arrive in
the same fight on the same second, every round.

The threshold is a **fraction of the playfield's long axis**, not a distance, so it means the same
thing on a 40 m map and a 90 m one. 0.70 is the figure the layout suite has held the generator to
since the arena layout was built; the layout puts the spawns at opposite ends, so it lands at 0.88
and never varies with the seed. This metric is a structural guard rather than a live one — it fires
only if the layout arithmetic breaks.

### ExposureAsymmetry

The gap between the mean exposure of the floor within `SpawnAnalysisRadius` (10 m) of spawn A and
the same figure for spawn B. Ten metres is about the first few seconds out of a spawn — the stretch
where being seen before you can react is the difference between a fair start and a spawn trap.

0.22 at the worst seed means one team's exit is visible from 22 percentage points more of the map
than the other's. That spread is **real, not sampling noise**: raising the observer count from 300
to 1200 moves the mean from 0.059 to 0.053 and the worst seed from 0.199 to 0.181. It comes from the
map's content — which flank lane the house landed in, where the building sits, where the cover fell
— and not from any one of them alone; grouping seeds by how far the house sits from the centre of
the long axis gives a flat mean asymmetry of 0.052 – 0.064 across every bucket.

0.25 is the worst seed of a thousand plus 15%. Tightening it below about 0.22 would not be a matter
of adjusting the number — it would mean constraining where the flank structure may sit, which is a
change to the generator and out of scope here.

### CoverCoverage

Fraction of walkable cells within `CoverRadius` (6 m) of a piece of cover. Six metres is about a
second and a half of sprinting: near enough that a player caught in the open there has somewhere to
reach. Distance is measured to the prop's footprint rather than its pivot, so a long barrier covers
the ground along its length and not just the ground beside its middle.

**0.73 is lower than it looks, and the reason is the spawns.** The two spawn strips are 60 × 7 m
each — a quarter of the walkable floor — and cover is deliberately kept 3 m clear of them, so almost
none of that ground is ever within reach of a prop. Across 300 seeds, **81% of the uncovered floor
is spawn ground**, and coverage measured over the contested floor alone runs 0.85 – 0.99, median
0.93. The generator is not under-furnishing the map; the metric, as specified, counts ground nobody
fights over.

The threshold is 0.60 — the worst seed of a thousand less about 7%. It is deliberately measured over
all walkable cells rather than over the contested floor, because a change that stopped the generator
covering the *spawn approaches* should show up here, and excluding the spawn strips would hide it.

### MaxOpenSightline

The longest unobstructed segment found between two walkable cells, in metres. A map with a 60 m
uninterrupted line is a map with a rifle lane down it.

Measured over the observer-to-cell segments the sweep already tests, which is every walkable cell
against 300 spread-out positions, rather than over all 5.9 M pairs. Same reason the exposure map
samples: the exhaustive answer costs a thousand times more and would land within a metre of this
one.

The threshold is 0.95 of the playfield diagonal — a fraction again, so it survives a change of
playfield size. **This is the weakest metric of the seven, and it is worth naming why.** The
generator anchors the middle lane and one flank, which leaves the map's corner-to-corner diagonal
open on essentially every seed; the ceiling for a 60 × 60 m map is 83.4 m between cell centres and
the observed values sit at 69 – 77 m, so the threshold at 80.6 m catches only the worst few percent
of what the generator can already produce. It is a regression guard — it would fire if the
structures stopped blocking the long lanes — rather than a statement that these maps have no long
sightlines. They do. Breaking the diagonal would need a third structure or a corner treatment, which
is a generator change, not a threshold.

### Connectivity

A four-connected flood fill from spawn A over the walkable cells, reporting three things: whether
spawn B is reachable, how many of the structures' declared doorways are reachable, and what fraction
of the floor is. Four-connected, not eight: two rooms touching at a single grid corner are two
rooms, and treating a diagonal pinch as a route would report a map as connected across a gap nobody
can walk through.

All three come out perfect on every seed, which is expected — the structures are small relative to
their lanes and the lane gaps run the length of the map, so there is nothing for a flood fill to get
stuck behind. That does not make the metric idle. A walled-off pocket of floor is beautifully
sheltered and perfectly covered; connectivity is the only one of the seven that would notice it, and
it is the metric most likely to start failing if structure placement is ever made denser.

The reachable-floor threshold is 0.95 rather than 1.0 to leave room for a cell or two sealed into a
corner by a structure flush against the playfield edge — a cosmetic defect, not a broken map.
Doorways and spawn-to-spawn are all-or-nothing: a door you cannot reach is a door that is not there.

### PlacementStats

Not a threshold — the cover placer's own tally of what it attempted, accepted and which constraint
turned the rest away, read back out of the document metadata. A lane that came out with half the
cover it asked for looks the same as one that was never asked for much; `cover_rejected_no_overlap:
157` is the difference between "this map is saturated" and "the catalog has nothing small enough".

---

## The heatmap

`ExposureMap.ToGrayscale()` returns a `float[x, z]` over the whole playfield grid: the raw exposure
fraction for walkable cells, `NaN` for the cells a structure stands on. Not rescaled to the map's own
range, so two maps rendered from it are comparable — a sheltered map looks cold rather than looking
like an ordinary map with the contrast turned up.

The ramp it is meant to be read through runs cold to hot, sheltered to exposed, interpolated
linearly between five stops:

| Value | Colour | Reading |
|---|---|---|
| 0.00 | deep blue | dead ground, seen from nowhere |
| 0.25 | cyan | sheltered, worth holding |
| 0.50 | green | ordinary lane floor |
| 0.75 | amber | crossed under observation |
| 1.00 | red | open ground, seen from everywhere |

Core returns numbers and stops. Turning them into pixels is the editor's job — a colour type in Core
would be the first engine concept across the boundary.

---

## The property suite

`MapValidationTests` generates and measures seeds 1..1000 and asserts, for every one of them: spawn
B is reachable from spawn A; every declared doorway is reachable; the reachable floor fraction is
above threshold; `ExposureAsymmetry` is below threshold; `CoverCoverage` is above threshold; and the
report passes on all seven metrics.

One test rather than six. The sweep is what costs, and running it six times to assert six things
about the same thousand maps would buy nothing but five more minutes; every property is still
checked for every seed and all of them are reported together. Seeds are independent — the catalog is
immutable and both the generator and the analyser keep their state in locals — so `Parallel.For`
does the thousand across the machine's cores. When something fails, the message names the worst
seeds by value and dumps the full report for the worst one, so a bad map can be loaded in the editor
and looked at rather than guessed about.

**Thresholds are part of the deliverable.** A metric failing across many seeds is a generator bug,
not a test bug. Two defaults moved during this session, both before any suite was run against them
and both because the first guess was made without evidence: `MaxExposureAsymmetry` from 0.15 to 0.25
and `MinCoverCoverage` from 0.8 to 0.6. The reasoning for both is in their sections above; neither
was changed to rescue a red run.
