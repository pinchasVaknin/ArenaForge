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

The terrain does not change that and deliberately did not try to. `MapAnalyzer` measures a map as
though every point of it were at the same height, so with relief turned up, a sightline it reports
as clear may run through a rise and one it reports as blocked may go over a hollow. Making exposure
terrain-aware means sampling the heightfield along each ray, which is affordable; making
*connectivity* terrain-aware means deciding how steep a slope a player can climb, which is a game
design decision this tool has no business making on its own.

**Nothing draws the terrain in the tool's own surfaces.** `ArenaLayoutGuides` draws the playfield,
the lanes and the spawn areas as flat rectangles at y = 0, so with relief turned up the guides float
over or sink into the ground they describe. Draping them on the heightfield is a change to one
renderer and was not asked for. `ArenaRoadGuides` has the same gap and one more reason to care about
it: a carriageway is graded to a profile with real heights in it, so the flat polyline is wrong about
a road in a way the road itself knows the answer to — `RoadSegment.Profile` carries the height at
every point. Lifting the drawn line onto its own profile is a few lines and would make the roads read
correctly over relief; the junction discs would still be flat.

**The drawn road network is empty until the next realise.** `ArenaMap.Roads` is deliberately not
serialised — it is derived from the document exactly as the terrain is — so a domain reload empties
it and the overlay draws nothing until Generate, Regenerate or Load runs again, even though the
document that describes those roads is sitting right there. Rebuilding it once in `OnEnable` would
close the gap and is still nowhere near the repaint path, which is the thing the cache exists to stay
off. It was not done because "cached at regeneration" is what was asked for, and a lazy rebuild is a
second place the cache can be filled.

**A building has no terrain of its own.** Item mode generates a building at ground level in its own
space, which is right — it is exported as a prefab and the arena decides where it stands — but it
does mean the foundation levelling is only ever exercised by a map. A building placed by hand into a
scene with relief in it has to be put at the right height by hand.

The building generator does not change this, and deliberately did not try to. A generated building
now has three storeys of rooms, walls, doorways, furniture, a stairwell joining them and a roof over
the top, and `MapAnalyzer` still sees one footprint and one occluder, so a map that places an
exported one is measured exactly as a map with a hand-made building in it is. Every metric the
validation suite reports is a claim about the ground floor.

The shell makes that gap wider rather than narrower, which is worth being explicit about: there is
now an interior with rooms and sightlines through doorways, and nothing measures any of it. The
constraint set stays two-dimensional and so does the analysis — extending either to storeys is the
larger change described above, not a follow-on from the floor plan.

**A stairwell is one shaft, in one place, at one width.** `PlanStairwell` picks a single rectangle
for the whole building and every storey works around it, which is what makes the flights line up.
What it cannot do is turn: a switchback, a stairwell that moves as the plan narrows, or a second
flight at the other end of a long building would all need the shaft to be a per-storey decision
again, and the argument in ARCHITECTURE.md section 7 for why it is not is the same one that makes
those hard.

**The hole in a slab is still as coarse as the art pack's floor tile.** The opening is the *fewest*
whole tiles the flight can be made to need — it is anchored on a slab joint to get that, and the
flight stands flush in its low corner rather than adrift in the middle — so a flight whose footprint
is a whole number of tiles opens exactly its own size, which is what the generated starter set gives.
A pack whose sizes disagree still rounds up: a one-metre flight under a two-metre tile opens two
metres of floor. Trimming a tile means generating a mesh, which is the line ARCHITECTURE.md section 3 draws; a
`structure/floor/half` or an L-shaped landing piece in the catalog would close the rest from the art
side.

**Nothing checks that a flight actually lands.** A `structure/stairs` entry is selected for standing
under the floor height, and the generator assumes the art rises across its own footprint to its own
declared height. Art that is a half-flight, or that rises the other way, is placed exactly as
confidently. Measuring where a mesh's top surface is means reading geometry, which Core cannot do.

This is also why the doorway rule in ARCHITECTURE.md section 7 is absolute rather than clever. A
doorway that opened onto the *landing* — the end of the flight you can step off onto — would be fine
and is what a person would draw; but which end that is is a fact about the art, so the shaft is
treated as a solid boundary all the way round and no doorway opens into it at all. A convention for
which way a flight rises would let the landing edge be excepted, and it is the same missing
convention as the one under the decor entry below.

It is what stops the rail round the roof opening being a closed ring, too. The flanks are railed and
both ends are left open, because closing the head of the run would seal the roof off from the stairs
and closing the foot is the only one of the two that is worth doing. With the rise direction known,
the foot could be railed and the head left as the way through — and a square flight, which is
currently left unrailed because nothing says which of its sides are its flanks, could be railed like
any other.

**The flight is flush in one corner of its opening, and which corner is arbitrary.** It is anchored
at the low corner, so any slack between the flight and the whole tiles above it lands at the high
end. With the rise direction unknown that is a coin toss: on art that rises the other way the slack
is at the top of the flight rather than at its foot, which is a step across a gap rather than a bit
of extra headroom over the bottom stair. Same missing convention as above.

Nothing guards that gap, so a flight rising towards the far end ends at a drop. The cure is in the
art — a flight whose run is a whole number of floor tiles leaves no slack at all, which is what the
generated starter flight is sized for — and the warning that would say so is not there.

**Nothing fences the interior stairwell.** A railing round the opening was built and then taken out
again: the only art a catalog has for the job is `structure/parapet`, which is sized to be seen from
across a roof, so a ring of it round a hole in a room is a wall through the middle of the floor.
Doing it properly needs a `structure/railing` tag and art meant for it — thin, waist high, a post
that reads at a metre — at which point the placement is the same run-and-gap arithmetic the roof
already does. Until then the hole is open, which is at least honest about what is there.

**A parapet corner is a lap rather than a mitre.** The four runs close the loop by each spanning its
whole side, so at every corner one run's end is buried in the middle of another and their top faces
are coplanar over a square the size of the parapet's own thickness. That is the deliberate trade —
see ARCHITECTURE.md section 7 — and on a roof it is a square somebody may well be standing on. A
corner piece in the catalog would close it properly, which is the same art-pack requirement the wall
junctions have.

**A parapet overhangs a roof its own length does not divide.** The pieces are counted up rather than
down, so a run is never short of its corner; the price is that a side which is not a whole number of
pieces sticks out past the roof by the remainder, split between the two ends of the run. Less than a
piece, and only on art whose parapet and floor tile disagree — the generated starter set's do not.
The alternative is the corner gap this replaced.

**Nothing walks on the roof, or on any floor above the first.** The stairs and the parapet make the
upper storeys and the roof somewhere a person can be, and `MapAnalyzer` still measures a placed
building as one footprint and one occluder at ground level. That gap is now wider than it was, and
closing it is the walkable-surfaces change described at the top of this file.

**Corridors are left empty rather than furnished sparsely.** A leaf that comes out one module wide
is circulation and gets nothing, which is right for a two-metre corridor and blunt for a wide one.
A density that fell off with narrowness rather than switching off at a threshold would be better and
is a tuning question, not a structural one.

**The slab and the walls can disagree about where a room ends.** The floor slab now tiles on its own
footprint rather than on the wall's module, which is what stops two slabs being laid over one patch
of ground — but it means an art pack whose floor tile is not as wide as its wall is long leaves the
slab joints out of step with the walls standing on them, and a strip of bare ground at the edge of
the storey where the last whole tile stops. Squaring the two automatically means scaling a prefab,
which this tool does not do — see ARCHITECTURE.md section 3. Matching the two sizes is a decision
for the art pack.

**Wall junctions still overlap horizontally.** Every run straddles its own centreline, so where two
cross, each one's end is buried in the middle of the other and no *vertical* faces are coplanar. The
top and bottom faces of the two are, over a square of about a tenth of a metre on a side. Both pairs
are now between a slab and the one above it and neither is ever seen, which the roof turned from an
argument into a fact. Closing it would still need a corner piece in the catalog — an art-pack
requirement rather than a generator change — and it was not worth inventing a tag for.

**Decor is furniture with no idea which way it faces.** A piece is drawn at a quarter turn from the
stream and then judged on whether it reaches a wall, so a sofa against the north wall is as likely to
face into it as away. Turning each piece to face the wall it is against is a small change and needs
one thing this tool does not have: a convention for which way a prefab's forward is. Guessing at one
would put every art pack that disagreed with it back to front.

**Decor and cover share one density.** `ContentDensity` sets both, at different baselines. A building
that wanted bare rooms with furniture round the edges, or a warehouse of crates and nothing else, has
no way to say so without a second parameter — which is a knob to add when somebody wants it, not
before.

**The exterior dressing has no density at all.** `ExteriorPlacer` tiles as much of a structure's
perimeter as the rules leave it and stands one heap per face, so how busy the outside of a building
is is decided by the art in the folder and by nothing the user can turn. That is right for a first
version — a hedge with a density knob is a hedge with holes in it at settings nobody asked for — but
a map that wanted planting round the building and none round the house has no way to say so. The
knob, when it exists, wants to be a share of the perimeter rather than a count.

**The world boundary does not close completely.** `PerimeterFence` tiles all four edges of the
playfield, and over seeds 1..200 of the default map it stands on 97.9% of the perimeter with no gap
wider than 1.2 m — but not 100%, and it cannot be. Nothing in this tool rescales art (ARCHITECTURE.md
section 3), so the tail of a run is short by less than the shortest panel the catalog offers and each
corner by the thickest panel's thickness. Mixing shorter `WoodFence` panels into the stone is what
takes the tail from one panel to the shortest panel, and a catalog carrying a single length of stone
and nothing else leaves gaps that wide. Closing the last few percent means either a corner piece the
catalog does not have a folder for, or letting two panels overlap by a few centimetres — the first is
a change to the workspace layout and the second is art interpenetration, and neither was worth doing
without somebody asking.

**A stone fence is only ever a boundary.** The other things a stone run is for — a wall down the side
of a lane, a retaining wall at a level change — are placement questions nobody has answered, and the
answer is not "the same pass with a different rectangle": a wall inside the playfield has to be
permeable in a way the edge of the world must not be, which is the argument `ExteriorPlacer` makes
about gates.

**Buildings do not scale with the map's area, and houses do not spread along it.** The composition
rule counts houses by ground and caps them at the flanks; the building is one, always, because the
middle lane holds a single strong point and a second there would divide the ground rather than anchor
it. A map big enough to want two points of interest is really a map that wants more lanes, which is a
parameter it already has. Separately, two houses may both land on the same half of the long axis —
that is the whole of why `MaxExposureAsymmetry` has a longer tail than it used to (REPORT.md).
Spreading them means a rule about where a flank structure may sit *relative to another one*, which is
the first placement rule in this tool that would be about a pair rather than about a candidate.

**A fence is invisible to the analysis.** `MapAnalyzer` blocks the walkable grid with structures
only, so neither a garden fence nor the world boundary cuts a route or shows up in the connectivity
report — which is correct as far as it goes for the garden fence, since it is arranged so that it
never cuts one, and is a genuine gap for the boundary. Every metric is measured over a walkable grid
that runs to the edge of the playfield as though the fence were not there, so a map's reachable
fraction and cover coverage both count ground a player is now walled off from. The strip is a panel
deep and the effect is well inside the headroom these thresholds carry, but it is an approximation
rather than a decision. Blocking waist-high art in the walkable grid is a change to one call and a
decision about vaulting that this tool has no business making on its own.

**A wall must be shorter than its storey.** The slab has a thickness and the storey pitch has to
hold both, so a catalog whose `structure/wall` art is exactly the floor height gets a building with
no walls, quietly, exactly as a catalog with no wall art does. A warning when a catalog has wall art
that no floor can use would be worth having and is not there.

**A building's floor plan has no editor UI.** `FloorPlan` is public and `BuildingGenerator.PlanFloor`
returns the plan a storey was built on, but nothing draws it: to see which leaf is a room and which
is a corridor you have to look at the walls. Drawing the plan into the scene view in item mode would
be a small addition to `ArenaLayoutGuides` and is the same omission as the entry below.

**A building has no instrument panel.** Its parameters and its per-floor re-rolls live in the
scene-view overlay's item mode, and the values set once per project live on the `ArenaBuilding`
component's inspector. Nothing measures a building the way `MapAnalyzer` measures a map, so there is
nothing for a window panel to show — and inventing one before there is something to read in it
would be the mistake the two-surface split in ARCHITECTURE.md section 6 exists to avoid.

**The layout guides are map-only.** Item mode draws nothing into the scene view. Drawing the
building's footprint, its floor planes and its floor plan would help while arranging one, and it
would be a small addition to `ArenaLayoutGuides` — it was not asked for, so it is here rather than
in the code.

**An exported map is a snapshot, not a link.** `MapExport` bakes the realised map into a prefab and
that prefab has no way back: change the seed and the prefab is stale until it is exported again over
the same path. That is the same bargain an exported building makes and it is deliberate — the
alternative is a prefab that regenerates itself, which is a baked scene with a generator welded to
it, the thing ARCHITECTURE.md section 2 exists to avoid. What is missing is only the reminder: no
warning anywhere says the prefab no longer matches the map it came from.

**Buildings do not capture scene edits.** `ArenaEditCapture` watches an `ArenaMap`. A building's
override list round-trips and resolves like a map's, and the overlay reports how many edits are
live, but nothing writes one from a drag in the scene. The capture layer is not map-specific in any
deep way — it diffs realised instances against resolved poses — so this is a matter of giving it a
second target rather than a second implementation.

**The playfield is a rectangle.** `ArenaLayout` divides one rectangle into parallel lane bands.
An L-shaped or asymmetric arena would need lanes to be a described route graph rather than a band
subdivision. That is the change most likely to be worth making first, because lane-band symmetry is
what makes different seeds feel more alike than they look.

**`SwapAsset` swaps one object at a time, and only for art the catalog says fits.** The overlay
offers the entries carrying all of the selected object's tags, which is the right list for changing
a crate for another crate or a fence panel for a longer one. What it cannot do is swap a selection
of objects at once, or offer art the tagging does not already group together — filing a piece under
a second tag is how you say two things are interchangeable, and that is the catalog's job rather
than the dropdown's.

**A catalog row does not count the scale on its prefab's root.** `CatalogSync` measures in the
prefab's own root space, so a component on the root is measured through an identity matrix and the
root's own scale cancels out — while `WorldRealizer` instantiates the prefab and that scale applies
to what stands in the map. Art whose root is not at unit scale therefore gets a row that disagrees
with the object. `ArenaAssetImportTests.TheMeasurementIgnoresTheRootsOwnScale` pins the behaviour so
it cannot change by accident. Fixing it is a one-line change to `Extent` and a very wide one in
effect: every row measured off such a prefab moves, and with it every recorded digest and both
thousand-seed sweeps. Worth doing deliberately, with the baselines re-recorded in the editor, and
not as the side effect of something else.

**Edge snapping and the placement grid can disagree, and nothing reconciles them.** A dropped
object is pulled flush with its neighbour's edge; `ConstraintKind.OnGrid` wants its pivot on the
map's cell grid. Art whose footprint divides the cell satisfies both — which is all of the art this
tool writes, deliberately, since section 3 of ARCHITECTURE.md sizes it in whole metres. Art that
does not can be flush or on the grid and not both, and the snap picks flush. The overlay's
placement verdict then shows the object as refused by `OnGrid`, which is true and is the honest
thing to show; what is missing is any way to say which of the two you would rather have. A modifier
key while dragging, or a per-catalog preference, would cover it and neither is there.

**Edit capture only runs while the tool window is open.** This is a deliberate trade rather than an
oversight — see ARCHITECTURE.md section 6 — but it does mean deleting a realised object with the
window closed goes unrecorded, and the object comes back on the next regeneration.

**Regeneration rebuilds every GameObject.** A map of a few hundred props rebuilds fast enough that
it has never been worth fixing, but an incremental realiser that diffed the resolved list against
the scene and touched only what changed would make regeneration feel instant on much larger maps.

**Play-mode edits are not captured.** `ArenaEditCapture` is editor-only. Moving a prop in play mode
changes a GameObject and nothing else, as it does in any Unity workflow.

**Closing a run still costs a doubled panel, but only as deep as the shortest piece in the folder.**
A run is tiled from indivisible pieces and nothing here stretches art, so the only way to reach the
end of a line is to seat one last panel backwards from it, over the panel before — see
`WallRun.CloseGaps`. What changed is which panel: the pass now takes the longest piece that fits
what is left of the stretch rather than the shortest in the palette, so the overlap is bounded by
the shortest piece on the *shelf* and a folder that carries one closes almost exactly. A folder of
one 2.5 m panel still laps by up to 2.5 m of coplanar faces, and that is a z-fight the tool cares
about everywhere else. The remaining fix is the art: file a short panel beside the long one. The
alternative — an along-run scale on `Pose` — is a schema change and a break with "art is placed,
never rescaled", and is rejected below.

**A catalog of many lengths builds a run out of many more pieces than it needs to.** The closing
pass chooses by length; the walk that lays the body of the run still picks at weighted random, so a
fence folder holding five lengths tiles a sixty-metre edge with about forty-five panels where a
folder holding one tiles it with eight. Weighting the long panel up does not recover it: a slot that
draws a piece too long for the ground left retries, gives up, and hands that ground to the closing
pass, which fills it greedily. The fix is to make the walk length-aware the way the closing pass now
is, and it was left out here because it moves the output of every map that already exists, where the
closing rule moves only maps whose folders hold more than one length.

**Nothing ships any interior cover, and a building cannot be generated without some.**
`BuildingGenerator.FloorContents` now queries `propbuilding/decor/interiorcovers` and nothing else —
the dumpster in the sitting room came from its querying `CoverPlacer.CoverTag`, and the folder that
fixed it is `Props/PropBuilding/Decor/InteriorCovers`. The scaffold creates it and leaves it empty,
which is the honest thing to do: a project cannot be asked to guess which of its crates were meant
for indoors, so nothing is moved into it and a catalog that has not filed anything there is refused
with a message naming the folder.

The loose end is that neither the demo sample nor the starter art has a piece for it, so **Generate
Building** against a freshly imported demo reports that message rather than building. Fixing it means
either a demo catalog row or a generated starter box, and both are decisions about what art this
tool authors. `ArenaAssetBuilder` writes four pieces — stairs, parapet, floor tile, window — and they
are the ones a shell could not be built without on any project's own art. A fifth is arguable now
that a floor's contents are one of them too.

**A room's floor scatter places nothing when the interior cover art is bigger than the rooms.**
`FillRoom` insets the room by the wall thickness and the wall margin, then insets it again by the
smallest piece on offer, and gives up when what is left has no width. Against the project's own art
that is every room: the workspace's `InteriorCovers` folder holds a four-metre sofa and a
three-metre couch, and a partitioned floor of a twenty-metre building has rooms about three and a
half metres across. Nothing is reported — a room with no floor left for a crate is a legitimate
outcome of the same arithmetic — so the rooms come out furnished by the corner and centre passes
alone, and the scatter looks as though it is not running.

Neither half of that is obviously wrong on its own. The art really is too big for the rooms, and the
margin really is what keeps a crate off the wall. What is missing is that nobody is told: a stage
that was asked for eight objects and placed none has something to say, and `PlacementStats` already
carries the vocabulary for saying it. A per-room count of "asked for, no floor to offer" in the
building's metadata, surfaced in the window beside the other tallies, would turn a silent nothing
into a sentence naming the folder — the same treatment the missing-shell-art case already gets.

**The sample needs URP and the Input System; the tool does not.** The demo's materials are URP and
its free-fly camera is written against the Input System, so importing the sample into a project with
neither will not compile. Declaring them as package dependencies would force a render pipeline on
everyone using the generator, which is worse. A sample built on primitives and a legacy camera would
avoid it.

**Nothing bridges anything, and the ground cannot make a gap worth bridging.** `TerrainField` sums
three octaves of value noise and smooths between lattice points, so every height it produces is
continuous with the ones beside it. It makes rises and hollows and it cannot make a ravine, a cutting
or a dry riverbed, because there is no discontinuity anywhere in the function to make one out of — and
a bridge over ground like that is a bridge over a dip, which is a road on a slight embankment and not
worth the art. Ravines come first, then, and not as a fourth octave: no number of octaves of a smooth
sum puts a cliff in it. It wants a second shape laid over the noise — a channel with its own course,
width and depth, carved after the ground is summed — which is a change to what `TerrainField` is
rather than to how it is tuned.

The half worth arguing about now is where the decision to cross lives, because the obvious build is
the wrong one. That build lays the road first and then scans along it for a gap — a raycast down at
intervals while drawing the path, dropping a bridge wherever the ground falls away — and it fails the
way every post-hoc pass fails: by the time the path is being drawn the route has already been chosen
as though the gap were not there, so the bridge lands wherever the road happened to cross rather than
where a bridge belongs. The crossing belongs in the router's cost function instead. A cell too steep
to grade under `MaxRoadGradient` is not impassable, it is expensive — priced at what a crossing
costs — and tagged as crossed when a route takes it anyway. Then the router weighs going round
against paying for a bridge on the same terms it weighs everything else, the narrow throat of a
ravine is cheaper than its wide middle without anything having to say so, and what comes out is a
bridge where a surveyor would have put one. Nothing has to be scanned for afterwards because nothing
was decided too early.

**There are two road classes, and the parameters say so out loud.** `ArteryWidth` and `PathWidth` are
a trunk and a branch, which is the smallest hierarchy that reads as one: a network of a single width
is a maze, and two widths are a way through with the ways off it. What is missing is everything on
either side — a boulevard wide enough to be its own firing lane, a service alley behind a row of
buildings, a track that is dirt where the artery is surfaced. Each is a width plus rules about what it
may connect to and what may face onto it, and at three the widths stop being two named fields and
want to be a table; at that point a road class is a catalog row rather than a property on
`ArenaParams`. Which of those it becomes is obvious with the third case in hand and a guess without
it, which is CLAUDE.md rule 3 exactly.

**Every road goes somewhere, and some of the best ones do not.** The network as planned connects
things, and `RoadDensity` only lays redundant loops over the top, so the one shape it cannot produce
is a road that deliberately stops: a cul-de-sac, or a courtyard with buildings on three sides of it.
Both are dead ends that are the destination rather than a failure to reach one, and both are what
makes a block read as somewhere people live rather than as ground to be crossed. Note that this is
the opposite claim from the only thing the tool currently says about dead ends —
`ArenaLayoutGenerator.DoorwaysPerStructure` is two because one way into a building makes it a dead
end — and the two are not in conflict so much as at different scales: a room you can be cornered in
is a design failure, a street you can be cornered in is a fight worth having. Building them means the
router growing a notion of a terminus it is allowed to leave unconnected, which is a rule against the
grain of every other stage here, all of which are trying to join things up; and it means the
structures around a cul-de-sac being placed *with* it rather than merely near it, which is the same
pairwise placement rule the flank houses are still waiting on above.

**A road is graded into the ground and nothing on the ground is re-seated over it.** The corridors go
down between the anchored stages and the cover, so the cover reads the graded height as it is placed
and the boundary fence and the yard dressing do not — they were seated before the roads existed. On a
map with relief a hedge whose ground a road has since cut can end up a few centimetres off it. The
honest fix is not re-seating those stages afterwards, which would change ids and poses for a reason
nobody asked for; it is the same shape as the bridge problem below, where a decision made too early
is corrected too late. What it wants is the road network laid before the anchored stages and the
doorway rectangles it routes to available before the structures are dressed, which is a bigger
reordering than this one and worth doing when something actually draws a road.

**The steepest ground on a map is now the rim of a pad a road runs past.** Precedence says a corridor
beats a pad's apron, which is what stops a building tilting a carriageway — and the cost is that the
apron is discarded over the strip the road covers, so the pad's edge meets the road's surface with no
blend between them. Measured over a hundred default seeds at an amplitude of 4, the steepest slope on
the ground goes from 1.08 before the roads to 2.53 after, and the excess is at pad rims: away from
them the worst is 2.05 and three cells in ten thousand exceed a slope of 1. Read generously it is a
retaining edge where a road passes a platform cut into a hillside, which is what a real one has;
read plainly it is a step nobody chose the height of. Fixing it means the corridor and the pad
agreeing on a shared edge rather than one overriding the other, which is a third rule between two
features that currently need none.

**Nothing draws a road.** The network reserves ground and biases the cover along it, and a player
looking at the map sees a strip nobody built on rather than a street. Surfacing it is a tiling
problem rather than a routing one — a carriageway is a polyline with a width, which is what
`WallRun` already walks along a rectangle's side — but it wants art nothing in the starter workspace
has: a road piece, a junction piece, and something to do with the joint where a four-metre artery
meets a two-metre path. Until there is, the corridors are worth more as a reservation than as
geometry, which is why the stage emits rectangles and stops.

**A corridor rectangle occasionally clips the corner of a building the carriageway itself misses.**
The reservation is axis-aligned boxes and the road is a swept strip, so at the outside of a bend the
box reaches about four tenths of a half-carriageway past the strip — under a metre for an artery.
The router holds a road clear of a structure's *pad*, which is its footprint plus a metre of
doorstep, so that overhang mostly lands inside the doorstep and reaches the footprint itself on about
one structure in five — 657 of them over the thousand seeds of the default map at a road density of
one. It costs nothing today: everything the reservation is shown to is placed after the road, and
cover was never going to stand against a wall there anyway. It would start to matter the moment
something is drawn from these rectangles, and the fix then is not finer subdivision — the overhang is
a corner term that no amount of cutting removes — but clipping each rectangle against the pads the
router was already told to avoid.

**The serialised text of a document is not byte-identical across runtimes, though the document is.**
Newtonsoft renders a quarter turn as `0.70710677` under .NET and `0.707106769` under the runtime the
editor runs on: the same float, two shortest-round-trip conventions. Determinism as section 4 of
ARCHITECTURE.md states it — same seed, same bytes, any machine — holds within a runtime and is what
every suite here asserts, and `RoadPipelineTests` digests the document's values rather than its text
for exactly this reason. Making the text itself portable means writing floats through a formatter
this project owns rather than through the serialiser's, which is a change to every document ever
written in exchange for a guarantee nothing currently needs.

**A map with kerbing on it is not the same document under .NET as under the editor's runtime, and
that one is not just the text.** Measured while recording the baseline for `RoadFurnitureTests`: the
kerbed default map at a road density of 1 digests to `e75a5d81470c8e8f` on seed 1 under .NET 8 and
`e8dd4ae3ca7166a8` under Mono, and every one of two hundred seeds differs. Same source, same seed,
two answers — which is the guarantee in section 4 of ARCHITECTURE.md, not a rendering convention.
It was invisible until now because nothing digested a *kerbed* map: `RoadKerbTests` holds the
kerb**less** map to its bytes, and that one agrees on both runtimes to the digit.

The shape of the cause is a last-ulp comparison deciding a discrete choice. `YawStep.Nearest` picks
a step by sweeping the table and keeping the largest `turned.X * direction.X + turned.Z * direction.Y`
— and a JIT that contracts that into a fused multiply-add rounds once where one that does not rounds
twice. For a direction sitting between two steps the two roundings pick two different steps, and a
kerb turned one step further round is a different piece in a different place, which moves everything
the run lays after it. The type's own remarks already worry about exactly this and removed the
`atan2` that used to cause it; what is left is the comparison itself. Nothing before the oblique runs
arrived was choosing anything off a dot product, which is why the kerbless map is unaffected.

Fixing it means deciding what the tie-break is *for* — comparing against a widened `double`, or
carrying an explicit epsilon and a documented winner, or quantising the direction before the sweep.
All three are cheap; what is not cheap is being confident the choice is the only place this bites,
which means auditing every float comparison that decides between discrete outcomes rather than
between "near enough". Until then the practical rule is the one this baseline follows: record a
digest on the runtime the suite runs on, and treat the offline harness as a fast check rather than
an oracle.

**Kerbing still displaces cover, and on a road-dense map that is worth 54 seeds in a thousand.** The
cover *budget* no longer shrinks because a road stage stood something on the floor — that is
`CoverPlacer.IsRoadside`, and it took a kerbed map at a road density of 1 from 531 seeds under the
0.6 cover-coverage threshold to 54. What is left is not the budget but the room: a kerb run is a
continuous line down both sides of every carriageway, it is committed into the cover placer's
constraint set at the `PropMargin` of three quarters of a metre, and that is exactly the strip
`CoverPlacer.RoadsideReach` sends cover to first. So the preference that makes a road worth fighting
over is fighting the edging that makes it look like a road, and on the worst seeds the placer runs
out of legal roadside positions inside its attempt budget. Measured over seeds 1..1000 of the default
map: 0.550 to 0.744, mean 0.654, against 0.700 with no kerb art in the catalog.

Fixing it means deciding whether a crate may sit flush against a kerb — a kerb is fifteen centimetres
of stone and a three-quarter-metre walking margin from one is a clearance nothing asked for — which
is a judgement about how a map plays rather than a defect in the code. The cheap version is a smaller
overlap margin against roadside art specifically; the honest version is asking what `PropMargin` is
for and whether it should be one number for everything a prop can be near. Neither is in the scope of
the stage that found it. Note that a map with street furniture on it does not have the problem at
all — furniture counts towards the cover a stretch of floor needs, so a furnished road-dense map runs
0.630 to 0.794 with none under the threshold — so this bites exactly the workspace that has filled
`Props/Road/Kerb` and not `Props/Road/Furniture`.

---

## Scope that was considered and rejected

**Stretching a fence panel to close a run exactly.** The obvious alternative to seating a closing
panel backwards over the one before it is to scale the last panel along the run until it fits the
remainder exactly — no doubling, no overlap. It was considered when the boundary was made to close
on every map size and rejected on the rule in ARCHITECTURE.md section 3: `Pose` carries a *uniform*
scale on purpose, because this tool composes prefabs rather than authoring geometry and squashing an
art asset out of proportion is a defect rather than a placement decision. Fitting a run this way
needs a per-axis scale, which is a schema change to every pose in every document, and on the art it
would actually run against — a thirty-metre stone panel closing a seventeen-metre stretch — it is
not a slight stretch but a 43% squash of visibly modelled stonework. A post standing in front of a
post is cheaper.

The one case it would genuinely buy something is the case that is still open: a stretch shorter than
the shortest panel, with something standing at both ends of it, which no seating can fill. That is
rare enough now — structures are held clear of the boundary strip and the dressing places around it
— that it did not justify the change.

**A uniform scale inside a declared tolerance was built to close that case, measured, and taken back
out.** The rule stretched the last piece of a stretch to fill the remainder exactly, bounded by a
per-row tolerance in metres so the 43% squash above stayed impossible. Over 120 maps at three sizes
it fired **not once** on a 0.97 m panel — the case it was justified by — and where it did fire it
saved between nothing and three per cent: a remainder is only ever near a piece's own length by
coincidence, and once length variants are on the shelf the lapping that is left is the corner
handover rather than a remainder at all. The code was reverted rather than shipped. What the
measurement pointed at instead is that art measured at 0.97 m is a fault in the asset, and correcting
it once at import — `ArenaAssetImport` — takes the same map's lapping from 1.96 m to 1.20 m, which is
the corner floor, meaning none left.

**Length variants are the answer this entry was reaching for.** A folder holding the same wall at
several lengths, and a closing pass that takes the longest that fits, gets what stretching wanted —
a tail closed to within a short piece — without a per-axis scale, without a schema change, and
without squashing anything. The 43% squash above is the measure of how far the wrong answer had to
go; the right one is a second row in the catalog.

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
default inspector, a folder to sync from and two buttons. A dedicated catalog editor with prefab
previews and footprint gizmos would be nicer and is not the point of the project.

**A catalog sync that deletes.** The sync adds and updates; a row whose prefab has left the folder
stays. Making the folder authoritative would be a smaller mental model and would also delete the
exported buildings and hand-bound rows that share the file, so the rule is that the sync owns what
it produced and nothing else. A "rows with no prefab" warning in the inspector would cover the gap
and is not there.

**The sync reads box colliders and nothing else.** Art without a collider comes back at the default
one-metre cube and has to be measured by hand. Falling back to the renderer bounds would cover most
of those cases, and would silently include a prop's shadow-casting flourishes in its footprint,
which is worse than a number the user knows is a guess.

That now costs a second number rather than one: a row without a collider gets a default height *and*
a base offset of zero, so an uncollidered prefab modelled around its centre is stood half inside the
floor exactly as everything was before `BaseOffset` existed. The fix is the same one — measure it by
hand, in the row — and the warning that would make it obvious is the missing piece.

**A footprint is a box, so art that is not one is still measured as though it were.** A row now says
where the art sits as well as how big it is, in all three directions, which is what an opening cut to
fit needs. What it still cannot say is any shape but a rectangle: an L-shaped landing or a spiral
flight is measured by the box around it, and everything kept clear of it is kept clear of the box.
That is the right direction to err for rules whose job is to keep things out of each other, and it
is why the stairwell reserves a rectangle rather than a silhouette.

**The road layer indices are on the map component, not in the tool window.** `ArenaMap.ArteryLayer`
and `ArenaMap.PathLayer` are two ints in the inspector, both off by default, and picking them means
counting the layers on a terrain material by hand. A pair of dropdowns in the tool window reading the
terrain's own `terrainLayers` would make it obvious; it is a widget, and the writer works without it.

**A kerb clips the inside of a sharp bend of its own carriageway.** A run on the concave side of a
corner is nearer the far arm of its own polyline than half a carriageway, so the last piece before a
bend reaches into the road it is edging — nine kerbs in a thousand over forty default seeds, all but
two of them under 40 cm in. Cutting it exactly means a mitre setback computed through a tangent, and
a transcendental in the middle of a deterministic placement is a byte-identical guarantee traded for
a corner nobody has complained about. Shutting each run against its own polyline instead is the other
route, and its collinear stretches sit exactly on the boundary of the test, which is the kind of tie
`WallRun.CornerSlack` exists because nobody wants to decide with a float.

**Nothing can be left hanging in the air.** A dropped object falls to the first standing surface
under it, however far down that is, so a lamp meant to hang from a ceiling or a walkway meant to
span a gap comes back to the floor the moment it is nudged. The alternative that was considered and
rejected is snapping only what was already on the ground, which costs the case the feature is for —
lifting a crate onto a second floor. What would cover both is a way to say *this one is airborne*,
either a modifier held during the drag or a flag on the override, and neither is there.

**The same ground is drawn as several road segments.** The routes merge — on a hundred-metre map at
twenty metres of relief, 64.1% of the network's polyline sits within a quarter of a metre of another
centreline, and the corridor total counts that ground once. What does not merge is the *drawing*:
958 m of polyline over 359 m of ground, each route still a full-length segment of its own. `RoadKerbs`
and `RoadFurniture` walk `Segments`, so a shared strip gets two runs of kerbing and two sets of
street furniture, and the scene-view guides draw it twice.

`RoadNetwork.Merge` cannot see it. It asks whether a segment rides inside *one* other road, which is
0.0% of the length here, because these ride on a chain of three or four in turn. Asking against the
union instead was tried in an earlier pass and reverted: a segment covered by two roads can be the
join between them, and dropping it split the network on 21 seeds in a thousand.

The fix is to trim rather than drop — cut each segment down to the runs that are not already road,
which cannot disconnect anything because the kept piece still touches whatever covered it. What
makes it a piece of work rather than a patch: profiles are computed by `Sweep` before the merge and
would have to be sliced with the polyline, ids gain a level (`road/path_07/part_00`), `Sweep`'s
artery-and-branch index ranges move, and four validation properties are stated in terms of whole
segments — including the one that holds a portal's profile end level with its door sill. Every
recorded digest moves with it.

The property that would have caught this and does not exist: **the polylines cover each piece of
ground about once.** Polyline over corridor is 1.35 on flat ground and 2.67 on the map that prompted
this.
