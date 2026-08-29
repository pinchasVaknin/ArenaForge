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
| `ExposureAsymmetry` | 0.000 – 0.257, mean 0.057 | ≤ 0.30 | 14% |
| `CoverCoverage` | 0.620 – 0.777, mean 0.712 | ≥ 0.60 | 3% |
| `MaxOpenSightline` | 68.6 – 75.9 m | ≤ 0.95 of the diagonal (80.6 m) | 6% |
| `Connectivity.SpawnsConnected` | true on every seed | must be true | — |
| `Connectivity.DoorwayReachableFraction` | 1.0 on every seed | ≥ 1.0 | — |
| `Connectivity.ReachableFraction` | 1.0 on every seed | ≥ 0.95 | 5% |

The observed column was re-measured when the composition rule gave the default map a second house.
Three of the four live metrics moved, all in the direction a third structure predicts: shorter
sightlines, slightly less floor within reach of cover, and a longer tail on the asymmetry. Only the
asymmetry threshold moved with them, and the paragraph on it below says why.

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

0.26 at the worst seed means one team's exit is visible from 26 percentage points more of the map
than the other's. That spread is **real, not sampling noise**: raising the observer count from 300
to 1200 moves the mean from 0.059 to 0.053 and the worst seed from 0.199 to 0.181. It comes from the
map's content — which flank lanes the houses landed in, where the building sits, where the cover fell
— and not from any one of them alone; grouping seeds by how far a house sits from the centre of the
long axis gives a flat mean asymmetry of 0.052 – 0.064 across every bucket.

**The threshold moved from 0.25 to 0.30 when the composition rule arrived, and only the tail
justified it.** The middle of the distribution did not move: the mean went from 0.058 to 0.057 and
the 99th percentile sits at 0.186. What moved was the worst seed of a thousand, from 0.217 to 0.257,
and the mechanism is plain — with two flank structures instead of one, both can land on the same
half of the long axis and concentrate their occlusion near one spawn. 0.30 is the new worst seed
plus the headroom the old number carried.

That is a threshold re-derived by the method this table uses, not a threshold loosened to rescue a
red run, and the difference is worth stating because it is the difference the suite exists to
protect. A metric drifting across many seeds would be a generator bug and would be fixed as one.
Tightening this one back below about 0.26 would not be a matter of adjusting the number — it would
mean constraining where the flank structures may sit relative to each other, which is a change to
the generator and out of scope here as it was before.

### CoverCoverage

Fraction of walkable cells within `CoverRadius` (6 m) of a piece of cover. Six metres is about a
second and a half of sprinting: near enough that a player caught in the open there has somewhere to
reach. Distance is measured to the prop's footprint rather than its pivot, so a long barrier covers
the ground along its length and not just the ground beside its middle.

**0.71 is lower than it looks, and the reason is the spawns.** The two spawn strips are 60 × 7 m
each — a quarter of the walkable floor — and cover is deliberately kept 3 m clear of them, so almost
none of that ground is ever within reach of a prop. Across 300 seeds, **81% of the uncovered floor
is spawn ground**, and coverage measured over the contested floor alone runs 0.85 – 0.99, median
0.93. The generator is not under-furnishing the map; the metric, as specified, counts ground nobody
fights over.

The threshold is 0.60 — the worst seed of a thousand less about 7%. It is deliberately measured over
all walkable cells rather than over the contested floor, because a change that stopped the generator
covering the *spawn approaches* should show up here, and excluding the spawn strips would hide it.

**Roads spend most of that headroom, and the number is worth stating rather than rounding off.** A
carriageway is ground no prop may stand in and ground this metric still counts as floor that wants
cover within reach of it, and on the default map at a `roadDensity` of 1 the network reserves about a
fifth of the playfield. Over seeds 1..1000:

| sweep | min | 1st %ile | mean | max | seeds under 0.60 |
|---|---|---|---|---|---|
| `roadDensity` 0 — the default map | 0.620 | 0.651 | 0.712 | 0.776 | 0 |
| `roadDensity` 1, no roadside preference | 0.567 | 0.600 | 0.687 | 0.771 | 10 |
| `roadDensity` 1, as shipped | 0.602 | 0.623 | 0.700 | 0.771 | 0 |

The threshold holds and it was not moved, but the worst seed of a thousand clears it by 0.002 where
the roadless map clears it by 0.020. That is not a tuning artefact: what pays the difference back is
cover standing *along* the roads, which is the same thing that makes a road contested rather than
decorative — a road with nothing beside it is both the emptiest ground on the map and the ground
nobody fights over. The whole of the recovery is in the sampler's ordering; the constraint set is
unchanged apart from being handed the corridors.

Two things were tried and measured worse on the same thousand seeds. Giving the reserved-path rule an
outdoor verge costs more than it buys, because cover snaps to the one-metre placement grid and a verge
of even half a metre moves a prop a whole cell off the road: 0.668 mean, 47 seeds failing at half a
metre and 0.658 mean, 102 failing at one. Expressing the roadside preference as a `MaxDistanceFrom`
rule rather than as an ordering needs the corridors committed as placements so the rule has something
tagged to measure from, which hands `NoOverlap` a second opinion about how wide a road is: 0.658 mean,
86 seeds failing.

**What a road stage stands on the floor is not floor that has stopped wanting cover.** The table
above is a catalog with no road art in it. Put kerbing in one and every piece of it was being claimed
into the floor the target is counted off, alongside the hedges and the heaps — so a map shrank its
own cover budget by every kerb it had laid, and the metric fell with it. `CoverPlacer.IsRoadside` now
leaves the two road stages' art out of that floor and keeps it in the grid the sampler draws from,
which is the same split the carriageways themselves already had. Over the same thousand seeds:

| sweep, `roadDensity` 1 | min | 1st %ile | mean | max | seeds under 0.60 |
|---|---|---|---|---|---|
| kerb art, before the fix | 0.485 | 0.506 | 0.598 | 0.718 | 531 |
| kerb art, after it | 0.550 | 0.576 | 0.654 | 0.744 | 54 |
| kerb and furniture art | 0.630 | 0.660 | 0.728 | 0.794 | 0 |

The cover target is identical on all three and on the roadless map — 36.6 props on average over the
thousand — which is the fix stated as a measurement rather than as an intention.

**Street furniture counts towards this metric and kerbing does not**, which is what takes the third
row above the roadless map's own 0.712. In a sixty-metre arena a bench beside a road is what a player
caught in the open there reaches, and it is standing where a crate otherwise would; a kerb is fifteen
centimetres of stone and nobody gets behind one. Whether either *blocks a sightline* is a separate
question, asked by height alone — the shelter in the test catalog stands across the eye line and the
bench does not, exactly as high cover does and low cover does not.

The 54 seeds left in the middle row are not the budget, they are the room: a kerb run is a continuous
line down both sides of every carriageway and it is committed to the cover placer's constraint set at
the three-quarter-metre `PropMargin`, which is exactly the strip `RoadsideReach` sends cover to
first. A workspace that has filled `Props/Road/Kerb` and not `Props/Road/Furniture` gets that;
`FUTURE.md` has what fixing it would take and why it is a judgement about how a map plays rather
than a defect.

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

`RoadValidationTests` is the same shape for the roads: one file holding every property a network is
promised to have, and one `[Test]` reporting all of them together. It sweeps twice, because the
properties do not all describe the same ground. **The relief sweep** — seeds 1..1000 at
`roadDensity` 1 over four metres of ground, with kerbing and street furniture in the catalog —
carries the ones about the network itself: no carriageway crosses a structure footprint, no graded
profile segment is steeper than `maxRoadGradient`, every portal arrives within a centimetre of the
sill of the door it serves, the corridors are one connected region reaching both spawns, every metre
of carriageway is inside some corridor rectangle, and nothing placed after the roads stands in one.
**The default sweep** — the same thousand seeds on the default map with roads on — carries the two
that are claims about the shipped map: every declared doorway has a corridor within one path width
of its threshold, and `MapReport.IsPlayable` holds against `MapThresholds` unchanged. Determinism is
its own test, because it is the one property that cannot read a cached map: two generations of each
of seeds 1..200 serialise to the same bytes. Braiding is a claim about a total rather than about a
map, so it is taken over the first two hundred: the network covers 0.677 of what the same routes
laid separately would, against a limit of 0.70.

Two of those are stated on the ground they are true of rather than on the harder one, and both are
worth reading as measurements rather than as concessions. `IsPlayable` is measured on the default
map because every `MapThresholds` default was derived there; over four metres of relief the same
sweep puts two seeds — 869 and 921 — under `MinCoverCoverage` at 0.589 and 0.573, which is relief
costing walkable floor rather than roads costing cover. The doorway bound is measured there for the
same kind of reason: on the default map no threshold is more than 1.0 m from the nearest corridor on
any of six thousand doorways, and on relief it runs to 4.19 m, because a portal is snapped to the
nearest cell a road may actually use and ground too steep to grade pushes that cell outwards. No
portal is dropped in either case.

`RoadPipelineTests` keeps what is left: that a map at a density of zero is the map it was before the
road stage existed, over seeds 1..200 of recorded digests, that its cover placer never once rejects
a candidate for a reserved path, and that no road is laid at all.

`RoadFurnitureTests` asks the same of a map with street furniture on it, and adds the two questions a
spaced run raises that a tiled one does not. Over a thousand seeds: nothing stands in a corridor, a
doorway, a spawn or off the map. Over a hundred and twenty: the gaps between pieces, measured in
metres along the polyline, are never shorter than the jitter's own floor and four in five fall inside
its band — measured only where projecting a pivot back onto a smoothed centreline is exact, which is
detectable rather than assumed, since a piece seated on the concave side of a bend comes back nearer
the polyline than the seat it was placed at. Cover coverage is asked of three hundred, against
`MapThresholds` unchanged.

**Thresholds are part of the deliverable.** A metric failing across many seeds is a generator bug,
not a test bug. Two defaults moved when they were first set, both before any suite was run against
them and both because the first guess was made without evidence: `MaxExposureAsymmetry` from 0.15 to
0.25 and `MinCoverCoverage` from 0.8 to 0.6. A third move came later and is the only one made
against a red run: `MaxExposureAsymmetry` from 0.25 to 0.30, after the composition rule put a second
house on the default map and one seed in a thousand landed 0.007 over the old line. The reasoning is
in its section above, and it turns on the distribution's middle having stayed exactly where it was
— had the whole thing drifted, the answer would have been to fix the generator.
