# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this package adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **`Pose.Bounds` replaces three copies of the same box.** The analyser's walkable set, the
  placement rules and now the edge snap each measured the world bounds a pose gives a footprint.
  The arithmetic is unchanged from the copies it replaces, corner for corner and comparison for
  comparison, and the full suite — every recorded digest and both thousand-seed sweeps — is
  unmoved by it.

- **A run closes with the longest piece that fits, so a catalog can carry length variants.**
  `WallRun.CloseGaps` read the shortest piece in the palette once and tiled every bare stretch with
  it; it now chooses per step, through `WallRun.LongestThatFits`. A folder holding a thirty-metre
  panel and a few shorter ones closes a nine-metre tail with a five, a two and a one where it used
  to seat a thirty-metre panel back over its neighbour.

  **What it buys is not coverage — the boundary already closed.** What it stops is the stacking.
  Over 160 maps at four sizes with the art-pack-scale catalog, panel laid over panel falls from
  4,192 m to 193 m, 95.4% less, and the deepest single lap from 15.3 m to 0.30 m — which is one
  panel thickness, the corner the pinwheel hands from one run to the next, and no longer a
  remainder at all. Coverage is identical to four decimal places on every size, which is the point:
  the fence was closing, and it was closing by wasting half a panel.

  **A palette of one length is untouched**, because the longest that fits is then the only piece
  there is. Every map generated from a catalog whose fence folder holds a single panel — which is
  every map this tool has generated so far — comes back byte for byte the same.

  **No draw is taken**, on the terms `Shortest` already set: the closing pass runs after the walk's
  draws are spent and ties break by catalog order, so a run cannot depend on how many picks missed.

  **The walk itself is unchanged and still picks at weighted random**, which is where the one cost
  lands: a catalog offering five lengths builds a sixty-metre edge out of about forty-five panels
  where a single-length catalog used eight. Weighting the long panel up does not recover it — a slot
  that draws a piece too long for the ground left retries, gives up, and hands that ground to the
  closing pass, which fills it greedily. Making the walk length-aware is the fix and is not done
  here; it would move every existing map.

  **One recorded baseline was re-recorded, deliberately.**
  `RoadFurnitureTests.UnfurnishedDigests` holds two hundred digests of a kerbed map, and
  `TestWorlds.KerbCatalog` files two lengths of kerbing on purpose — so it is exactly the sort of
  catalog this rule changes, and all two hundred moved. Every road and kerb property in the suite
  held across the change, so what moved is the map and not a guarantee; the array was re-recorded on
  the editor's own runtime, the way its remarks require, and those remarks now say why it moved.

### Added

- **Art somebody else modelled is imported as a prefab variant, fitted with a collider and corrected
  to size.** `ArenaAssetImport` beside `ArenaAssetBuilder` — beside rather than inside, because that
  file is the one place in the package that authors geometry and this one composes variants of art it
  did not make.

  **A variant, not a copy.** The source is left exactly as it was, so an art pack can be updated in
  place and the collider and the correction follow. A copy would double every mesh reference in the
  project and be a synchronisation problem for as long as both existed.

  **The size correction is for measurement error and nothing else.** A panel that measures 0.97 m was
  meant to be a metre, and a run tiled from it carries three centimetres of visible slack per piece.
  The default tolerance is five centimetres: larger than modelling slop, smaller than any deliberate
  size. Nothing in the number can tell a mis-measured 4.70 from a deliberate 2.30, and rounding the
  second is a 30 cm distortion — measured, it takes the lapping on a default map from 2.14 m to
  2.54 m — so the default corrects the first kind and declines the second. Everything declined is
  reported.

  **The correction goes on the prefab's children, and art modelled onto its root is refused.** See
  the fixed measurement convention below; a correction on the root would be invisible to the catalog
  and visible in the map.

- **A broken ring of wooden fencing round each spawn.** `SpawnEnclosure`, between the boundary and
  the exterior dressing, so a spawn reads as a base somebody holds rather than as a patch of ground
  with a marker on it. Wooden fencing and not the boundary's stone, because the two folders already
  mean different things: one is the edge of the world and the other is somebody's garden.

  **The ring is a square inside the spawn band, not the band's own outline.** A spawn area runs the
  full width of the map and reaches the playfield boundary on its outer side, so a ring on the band
  would put one run exactly where `PerimeterFence` has already tiled the world's edge and every
  panel of it would be refused. Held two panel thicknesses inside, which took the panels stood per
  pair of rings from 24 to 31.

  **Broken on purpose, one gate a side**, placed by this stage's own forked stream in the middle
  three fifths of each face — never in a corner, where it is hard to see and where two runs are
  already handing panels over. A gate is `PathWidth` across, the narrowest way through the map the
  generator lays anywhere. Over forty seeds a ring comes out **67.4% panel, 17.2% gate and 15.9%
  tail and corner handover**: an enclosure with ways out of it rather than a pen or a token.

  **A workspace with no wooden fence art generates the map it always did** — the palette is read
  before a draw is taken and an empty one returns before the stream is forked, which is why every
  recorded digest is unmoved.

- **A prefab dropped in from the Project window becomes a `user/` object.** `ArenaEditCapture`
  listens to `ObjectChangeEvents` while the tool window is open, on the same bargain the rest of the
  class is on. A drop whose prefab maps to a catalog row is snapped by the same rules a drag is —
  flush with what it landed beside, on the grid otherwise, standing on whatever is under it — and
  recorded as an `Add`.

  **Noted on the event and acted on at the next tick.** Adopting means destroying an object, adding
  an override and realising, which is a scene edit; making one from inside the notification that a
  scene edit happened is how a plugin re-enters itself. The tick is already where this class changes
  things.

  **The dropped instance is destroyed and the document makes its own.** The document is
  authoritative and the scene derived, so an adopted object has to be the realiser's — otherwise the
  next realise deletes it and puts an identical one in its place, which is the same thing happening
  later and less predictably. Both halves collapse into one undo step.

  **A prefab with no catalog row is left exactly as Unity dropped it.** A document names art by
  logical id, so there is nothing it could say about one; what is missing is a row, and Sync from
  Folders is how one is made.

### Fixed

- **The layout guides and their labels follow the ground.** `ArenaLayoutGuides` drew the playfield,
  the lane bands and the spawn areas at three fixed heights a few centimetres over zero, which on a
  map with relief buries them in a hill at one end and hangs them in the air at the other. They now
  sample `ArenaMap.Ground` — the *graded* field, roads cut into it — exactly as the placement
  verdict cells already did.

  **Cut into patches, sized from the terrain's own feature size.** A quarter of one rise or hollow,
  which is four samples across a bump: enough for a tinted band to read as lying on the ground.
  Capped at 32 a side so a four-hundred-metre playfield is not several thousand quads per repaint
  per scene view. **With no relief it is one patch and four corners**, which is pixel for pixel what
  it drew before.

- **A road drawn twice is drawn once.** `RoadNetwork.Merge` drops a segment that rides inside another
  segment's carriageway, after the routing and before the network is assembled. Over sixty seeds on
  flat ground that is 17.9 segments a map down to 12.8, and 311 m of polyline down to 268 m.

  The spanning tree joins every portal to every other, and some of its edges retrace an artery that
  is already there — two attachments on one trunk are joined through the trunk, and the tree does not
  know it. `ExtraEdgeCount` called that harmless because the route costs no new ground. True of the
  ground and false of everything stood along it: `RoadKerbs` and `RoadFurniture` both walk `Segments`,
  and the scene-view guides draw every one, so a retraced artery was a second run of kerbing, a
  second set of street furniture and a second line on screen.

  **It rides on one road, not on the network.** Two earlier rules were wrong and the suite caught
  both. Asking for nine tenths of a segment to be covered and arguing its ends must therefore be
  covered too: a tenth is enough to reach off an artery to a portal. Then testing the ends against
  everything kept so far: a segment half on one artery and half on another is covered by the pair and
  is the join between them, so dropping it took the network into two pieces on 21 seeds in 1000.
  Against a single road the argument holds — the segment goes where that road already goes, so it
  joins nothing that road does not, so removing it cannot separate anything.

  **It does not move the braiding figure**, which looks as though it should. `CorridorLength` counts
  the cells the network covers and a cell two segments cover is counted once, so what this removes
  was never in that total. What it removes is objects and lines.

  Two recorded baselines moved with it and were re-recorded on the editor's own runtime:
  `UnfurnishedDigests` on 22 seeds of 200, `KerblessDigests` on 1 of 200. Every road property held
  across the change — one piece, both spawns reached, nothing through a structure, braiding under its
  threshold — so what moved is the map and not a guarantee.

- **The road gradient limit was walling roads out of hilly maps, and the suite was calibrated
  around it.** `MaxRoadGradient` defaults to 0.6 where it was 0.25. Ground steeper than the limit is
  impassable to the router — `RoadNetwork.NaturalCost` — so the limit does not slow a route over
  rough ground, it shuts the ground to it. Over sixty seeds at twelve metres of relief, a quarter
  left the network spanning 50.9% of the map with 31 of the 60 confined to less than half, laying a
  tangle in whatever corner it could still reach. At 0.4 that is 88% and no failures; at 0.6, 91.7%,
  which is what flat ground gives.

  **The relief sweep moves from four metres to ten, and it is a stronger sweep than before.** Its own
  remarks recorded why it was capped at four: at six, seeds walled a spawn off behind a bank *no road
  may be graded up* — which was the quarter-gradient doing the walling. The sweep had been calibrated
  around the defect. At ten the limit refuses ground again, so the grading properties are asserted
  over ground that needs grading, which at four they no longer would be.

  **No threshold was weakened.** Braiding rose to 0.7254 at four metres of relief, over the 0.70
  limit — and then fell to 0.6739 at ten, under it. That fall is the finding: a confined router
  shares corridors because it has nowhere else to go, so the metric measures how boxed in the routing
  is as much as the cost decay it was written for. The old 0.6766 was flattered by the bug.
  `MaxCorridorShare` is untouched at 0.70, and the observation is now recorded beside it.

  **The report says when the two parameters are fighting.** A `report-caveat` line under the verdict,
  from `RoadNetwork.PassableShare` — measured by the router rather than guessed — naming both
  `Max Road Gradient` and `Terrain Amplitude` when a road can reach less than three quarters of the
  map. Beside it, and for the same reason, the report now says outright that it measured on flat
  ground whenever the amplitude is above zero: every metric here is a claim about a level map, and a
  verdict of "playable" that does not say so is claiming more than it checked.

- **Ground too steep for a road is dear to cross, and no longer shut.** `RoadNetwork.NaturalCost`
  returned `Impassable` above `MaxRoadGradient`. It now charges `ClimbDetour` cells of detour per
  whole limit of excess and lets the route through, so a road takes any way round a bank it can find
  and climbs one only when there is none. Raising the default from a quarter to six tenths, above,
  treated the symptom: the wall was still there and a spawn could still end up behind it.

  **A wall could cut a spawn off the map, and did.** The flood fill that numbers the open ground
  works on cells the router may enter, and the router only works inside the largest region it finds —
  so a bank right across a lane did not make a spawn expensive to reach, it put the spawn in a region
  of its own and left it off the network entirely. Over forty seeds on a hundred-metre field at
  twenty metres of relief and a feature size of twenty, a limit of a quarter stranded a spawn on
  **40 maps of 40** and six tenths on 3 of 40. Priced instead of forbidden it is **0 of 40 at every
  limit**, and the road laid per map at a quarter goes from **92.5 m to 371 m**.

  That is the map the user reported: seed 11162802767278075537 at those parameters had 29.8% of its
  ground open to the router, five segments, 80 m of road inside a box covering 8.3% of the playfield,
  and one spawn **68.5 m** from the nearest carriageway.

  **The limit still means exactly what it says, for the road.** The graded profile is never steeper
  than it — the grading holds that, and `NoGradedProfileIsSteeperThanTheMapAllows` still asserts it
  on every profile of every seed, untouched. What changed is the ground underneath, which the grading
  is there to cut.

  **What the climb costs is earthworks.** Over seeds 1..1000 on the relief sweep's own ground, 318 m
  of the 290,421 m laid crosses ground steeper than the limit — **0.109%**, on 156 seeds, no one of
  them over 1.88% of its own road — and the grading cuts up to 2.76 m to carry it there. Every one of
  those 318 m is a single one-metre step: 318 steps of 78,350, because the smoothing still tests
  every line it straightens or rounds and so will not lay a run *along* a bank.

  **One property was restated, and it is a weaker one.**
  `NoRoutedSegmentIsSteeperThanTheMapAllows` could not survive a router that deliberately climbs; it
  is now `RoadOverGroundTooSteepIsARareSingleStep`. The hard guarantee moved to the graded profile,
  which already held it, and what is left is bounded rather than absolute: no over-limit stretch
  longer than one cell, at most 0.5% of the road over the whole sweep and 5% on any one map, against
  0.109% and 1.88% measured. The sub-check that the sweep is laid over ground steep enough to be
  worth testing is unchanged — it now reads "charges" where it read "refuses".

  **A flat map is unmoved, cell for cell.** The slope term saturates at the limit and the climb takes
  over above it, so the cost is continuous through the limit and a cell within it prices exactly as it
  always did. Every recorded digest in the suite is on flat ground; none moved, and the full EditMode
  run is 696 of 696 in the editor.

  **The climb is capped at what going round everything costs**, `(cells across + cells down)` times a
  plain step. No detour on the grid is longer than that, so past it a steeper cell cannot change which
  way a route goes — and at the bottom of the parameter's range, where the excess is measured in
  hundreds of limits, an uncapped price would run a long route's total out of an `int`.

  **Note for anyone whose map still looks wrong: a default is not a migration.** `ArenaMap` is a
  MonoBehaviour and `_maxRoadGradient` is a serialised field, so a component created before the
  default changed still holds the value it was serialised with. That is why the earlier default
  change appeared to do nothing to an existing scene, and it is why this fix is in the router rather
  than in another number.

- **The climb price ran away at a low limit, and the decay charged for the earthworks twice.**
  Two faults in the entry above, both found by measuring the map that prompted it rather than the
  sweep. Seed 11162802767278075537 at 100 x 100, amplitude 20, limit 0.25 laid **1013.8 m of
  polyline over 425 m of ground** — a spider of paths hugging the same gentle contours.

  **`Climb` saturates at twice the limit.** The excess was counted in units of the limit and left
  unbounded, so the detour a route would make to avoid one cell was however many limits over the
  limit that cell was: at a quarter over twenty metres of relief, nearly eight, which is a
  hundred-and-fifty-cell detour to avoid one metre of bank. On a hundred-metre map that reads as
  "go anywhere rather than cross", and the roads wandered accordingly — one path came out 290 m
  long. `ClimbDetour` now means the twenty cells its name claims. Ground past twice the limit is
  too steep to grade and telling one cliff from a worse one buys a longer road, not a better one.

  **And the climb is paid once, by the road that gets built there.** `RoadRouter.Lay` decayed a
  cell to a quarter of its cost but floored it at a quarter of the *pristine* cost — so a road over
  a bank, having cost ~2030 to build, still cost ~507 to reuse while fresh gentle ground cost 30.
  Joining an existing road was **seventeen times dearer than laying a new one beside it**, which
  defeats braiding exactly where the ground is worst and sends every route round the same way. The
  climb is the price of *building* over steep ground; once a road is there the next route finds a
  road, not a hillside, so it comes off the cell for good the first time one is laid across it.
  Cells within the limit have nothing to pay and are untouched, so a flat map is unmoved cell for
  cell and every recorded digest held.

  Together: **1013.8 m of polyline over 425 m of ground becomes 958.2 m over 359 m**, and union
  coverage — how much of the network retraces the rest of it — rises from 75.6% to 79.7%, which is
  braiding working rather than failing. **64.1% of the polyline now sits within 0.25 m of another
  centreline**: the routes share the ground. What is left is that each is still *drawn* full length,
  so kerbs and furniture are laid twice over one strip. `Merge` cannot see it — it asks whether a
  segment rides inside *one* other road and the answer is 0.0%, because these ride on a chain of
  several. Trimming each segment to the part that is not already road is the fix and is not done
  here; it reworks the assembly stage, and is recorded in FUTURE.md.

  **The figures in the entry above are superseded.** Re-measured over seeds 1..1000 of the relief
  sweep after both changes: road over the limit 0.137% where it was 0.109%, on 210 seeds where it
  was 156, worst seed 2.08% where it was 1.88%, steepest ground crossed 1.90 where it was 1.44. The
  road is a little readier to climb, which is the saturation doing its job. Every crossing is still
  exactly one metre-long step — 396 m over 396 steps — and both thresholds hold with room.

 All of them in what the scene
  view shows or writes, which is the half of the tool no property test looks at.

  - **The swap dropdown put itself back before the button could be pressed.** `RefreshSwap` runs on
    every editor update and wrote the field's value each time, so a choice was overwritten between
    making it and reaching `Swap` — which then swapped an object for itself and appeared to do
    nothing. The value is written only when the row starts describing a different object.
  - **The placement verdict stayed behind while the object moved.** It read the pose out of the
    document, and a drag does not reach the document until the object comes to rest — that is what
    makes one gesture one override. It reads the live transform now.
  - **An object added by hand got no verdict and cast no shadow.** `Selected` searched the generated
    list, where a user-added object is not: it lives as an `Add` override. `CoverPlacer.TryJudge`
    counted neighbours from the same list, so such an object was also not something to keep clear
    of. Both go through `Resolve()` now — the map as it stands rather than as it was generated.
  - **The verdict was drawn at a fixed height**, so on relief it was buried in a hill and hung in
    the air over a hollow. `ArenaMap.Ground` caches the field the last realise produced, beside
    `Roads` and out of the same call, and the cells are sampled a corner at a time so a quad on a
    slope lies along it. The lane bands and spawn areas are still drawn flat.
  - **A drop landed exactly where the mouse let go.** An edge in reach still wins; a drop with
    nothing near it now lands on the nearest cell. That is also what settles the disagreement
    between the two snaps — a drop that takes the grid is a drop the placement rules accept.
  - **`Clear` left the roads painted on the terrain.** `FlattenTerrain` resets heights and nothing
    reset the alphamap, so a cleared map kept a network drawn across an empty field.
    `TerrainSplatWriter.Clear` takes every texel back to its first layer — every texel, because
    clearing happens once the document has gone and there is no network left to ask where the road
    was.

- **`CatalogSync` measures a prefab in its own root space, so the root's own scale is not counted.**
  `Extent` composes `root.worldToLocalMatrix * of.localToWorldMatrix`, which cancels for a component
  on the root itself — while `WorldRealizer` instantiates that prefab and the root scale plainly does
  apply to what stands in the map. A row measured off a prefab with a scaled root therefore says one
  size while the map stands at another.

  This is **documented and pinned by a test rather than changed**:
  `ArenaAssetImportTests.TheMeasurementIgnoresTheRootsOwnScale`. Counting the root's scale would move
  every row measured off every prefab whose root is not at unit scale, and with it every recorded
  digest and both thousand-seed sweeps — a decision worth taking deliberately rather than as the side
  effect of an import feature. What it settles for now is where a size correction may live.

- **A dropped object is pulled flush with the edges it landed beside.** `EdgeSnap.TryFlush` in Core,
  called from `ArenaEditCapture` at the moment an instance comes to rest: a wall dropped with a
  seam after the one before it closes the seam, and one dropped a little sideways of it comes back
  in line rather than stepped.

  **Each neighbour decides which axis the run goes along** — whichever of the two the footprints
  share least of. There the offsets butt them together; across it they line the edges up. The
  obvious shape, letting every offer compete on both axes and taking the nearest edge, is wrong and
  the test that says so is `AWallOffsetOnBothAxesComesBackAsAContinuationOfTheRun`: a wall dropped
  slightly sideways is *nearer* to sitting alongside its neighbour than to continuing it, so nearest
  alone builds a staircase out of a straight run.

  **At rest, not during the drag**, so one gesture makes one override and nothing jumps under the
  cursor while it is still held. The transform is written along with the document — the two have to
  agree before the next tick, or the difference is recorded as a second edit — inside the undo group
  the move already opened, so one Ctrl+Z takes the whole gesture back.

  **Reach is half a cell of the map's own grid**, derived rather than picked: the distance inside
  which you plainly aimed at the neighbour and not at the gap beside it, and never far enough to
  pull an object out of the cell it was dropped in.

  **It can put an object off the placement grid, and the overlay says so.** Art whose footprint does
  not divide the cell cannot be both flush with its neighbour and on the grid. The tool snaps and
  lets the placement verdict report the conflict rather than choosing quietly; the art it ships is
  metre-based, where the two agree.

- **A dropped object is stood on what is under it.** `ArenaEditCapture.Standing`, in the same pass
  as the edge snap and after it: a crate dragged up onto a first-floor slab lands on the slab rather
  than at whatever height the mouse let go at, and one dragged off the end of it falls to the ground.
  The height is sampled where the object ends up rather than where it was dropped, because the
  horizontal snap can carry a piece off the slab it was let go over.

  **A standing surface is one the catalog says is one** — `BuildingGenerator.FloorTileTag`, plus the
  Unity terrain. A Unity layer is the usual way to ask this and would have put the answer in two
  places: the catalog, which already says what every piece of art is, and a layer assignment somebody
  has to remember to make on each prefab they import. So a table is not a standing surface and a
  crate dropped on one carries on down to the floor, which is the rule working rather than an
  exception to it.

  **The way back from a collider to its row is the prefab**, not the `ArenaObjectRef` the realiser
  attaches. A floor slab standing in the scene beside the map belongs to a building's document and
  not to the map's, and there is nothing in capture to resolve its id against — while the art it was
  made from is in the same catalog either way, which is what the question is actually about.

  **The ray starts at the top of the art and not at its pivot.** A drop that ends with the object
  half sunk into the slab is the ordinary case, that being where the mouse leaves it, and a ray fired
  from inside a slab passes under it and finds the ground instead. Triggers are ignored: a merged
  group carries one as a measurement of itself, and a measurement is not something anything stands
  on.

  **The conservative rule was rejected deliberately.** Snapping only an object that was already on
  the ground would never move anything unexpectedly, and would also make it impossible to lift a
  crate onto a second floor — which is an edit a person plainly means to make. So it falls as far as
  it has to, and the cost is that nothing can be left hanging in the air on purpose. That cost is in
  FUTURE.md.

  This is the first `Physics` query in the project.

- **`SwapAsset` has a control.** The op has resolved and round-tripped since the document model
  was written and nothing ever produced one. Now a dropdown and a `Swap`
  button on the overlay, beside the selection line: pick another catalog entry for the object you
  have clicked on and stand that instead, with the pose left exactly where it was.

  **What goes in the list is what the catalog says fits.** Every entry carrying *all* of the tags
  the object carries — a crate tagged `cover` and `cover/low` offers the other low cover and not a
  fence panel; a stone fence panel offers the other stone fence panels, which is the length variants
  the closing rule above was built for. Nothing in the editor decides what a sensible swap is; the
  tagging does, and it is the same answer `Catalog.Query` gives the placer. The row is hidden
  outright when there is nothing to choose between, which is most of the time.

  **Choosing the entry the generator picked removes the override** rather than writing one that says
  nothing. An override list that grew an entry per undone decision is a list whose count stops
  meaning "edits you have made".

  On the overlay rather than in the window, on the split in `ARCHITECTURE.md` section 6: it acts on
  what you have just clicked on. It is a row and not a panel, so no panel appears on both surfaces.

  The document rule is `ArenaForgeOverlay.ApplySwap`, internal and tested on its own — the grounds
  `InternalsVisibleTo` was already added for. Four tests: a swap writes one override and moves
  nothing; two swaps leave one; swapping back removes it; and a swap and a move on one object both
  hold, the pose from the move and the art from the swap.

- **The scene view says whether a prop may stand where it is, and which rule refuses it.** A
  `Placement` toggle on the overlay beside `Layout guides` and `Road network`, and
  `ArenaPlacementGuides` to draw it: the grid cells under a selected prop shaded green or red, with
  a chip naming the refusal — `NoOverlap(0.75)`, `MinDistanceFrom(structure, 1.5)` — rather than
  saying only that something is wrong. The constraint model has always answered with the first rule
  that rejected a candidate instead of with a boolean; this is the first thing that shows it.

  **Nothing is decided in the editor.** The verdict is `CoverPlacer.TryJudge`'s, under
  `CoverPlacer.Rules` — the same list `BuildConstraints` places cover by, extracted so it is stated
  once. Two copies would be two ideas of where a prop may stand, and the one on screen would be the
  one that drifted. `TryJudge` hands back the footprint it judged along with the verdict, so the
  cells shaded are the ground the answer was about rather than the same box measured again on the
  other side of the assembly boundary.

  **Only the cells the prop stands on**, not a legality field over the whole map: that is a query
  per cell over a few thousand cells on every repaint, and the question being asked is about where
  the prop is. Structures are skipped — a building is placed by another stage under other rules and
  would be refused here for standing near its neighbour.

  **A prop on a socket gets no verdict, and finding that out is what the property test was for.**
  `PlaceSocketProps` attaches a prop to a pose its parent declares, in a pass that never consults a
  constraint set, so in plan it sits inside its parent's own footprint. Asked under the ground rules
  the two refuse each other, and the first run of
  `ConstraintTests.EveryPieceOfCoverIsAcceptedWhereTheGeneratorPutIt` said so: cover the generator
  had placed came back rejected for `NoOverlap`. Without it the feature would have shipped drawing
  red on props that are exactly where they belong. `CoverPlacer.IsSocketProp` states the id shape
  once, and the id it writes is unchanged to the character, so no recorded digest moved.

- **The road network is drawn in the scene view, off a cache.** A `Road network` toggle on the
  overlay beside `Layout guides`, and `ArenaRoadGuides` beside `ArenaLayoutGuides` to draw it: the
  polyline the router found, arteries thick and branches thin at a ratio matching the widths they
  were laid at, through `Handles.DrawAAPolyLine` because it is the one primitive that takes a width.
  A junction is a disc at `RoadNetwork.JunctionRadius` — the radius it has and the one `RoadKerbs`
  cuts against — rather than a fixed sphere that would claim the same size on a sixty-metre arena
  and on a four-hundred-metre town. The three junction kinds are told apart by colour.

  **It never builds a network.** `ArenaMap.Roads` keeps the one the last realise produced —
  `ArenaLayoutGenerator.Terrain` already returns it, because the splat writer needs the same network
  the heights came from — and the drawing reads that or draws nothing. A repaint runs per scene view
  and per camera in it, so a gizmo callback that recomputed a routing sweep over the whole playfield
  would be re-finding the roads several times a frame while nothing about them had changed. That is
  why this is not a MonoBehaviour with an `OnDrawGizmos`. The cache is not serialised, on the same
  grounds the terrain is not, so a domain reload empties it and the next Generate, Regenerate or
  Load fills it again.

- **`RoadValidationTests`, one suite holding every road property.** `MapValidationTests` for the
  roads: one sweep, one `[Test]`, every property checked for every seed and all of them reported
  together with the worst seeds named. It replaces `RoadNetworkTests`, which is deleted, and takes
  the playability sweep out of `RoadPipelineTests`, which keeps only the density-zero properties.
  Net cost is unchanged — the same two thousand-seed sweeps as before, in one place.

  **Two sweeps, because the properties do not describe the same ground.** The relief sweep is seeds
  1..1000 at `roadDensity` 1 over four metres of ground with the fully furnished catalog, and
  carries the network's own properties — no carriageway through a structure footprint, no graded
  profile over `maxRoadGradient`, every portal within a centimetre of its door's sill, one connected
  region reaching both spawns, every metre of carriageway reserved, and nothing placed after the
  roads standing in a corridor. The default sweep is the same seeds on the default map with roads
  on, and carries the two properties stated against the shipped map: every doorway within one path
  width of a corridor, and `MapReport.IsPlayable` against `MapThresholds` unchanged. Determinism —
  two generations of seeds 1..200 serialising to the same bytes — is its own test, since it is the
  one property that cannot read a cached map. Braiding is a total, taken over the first two hundred:
  0.677 against a limit of 0.70.

### Documented

- **What "a road does not run through a building" actually guarantees**, measured rather than
  assumed. The router shuts a structure's foundation pad — the footprint with a metre of apron — and
  holds a smoothed line half a carriageway off it, but the route is cells and the nearest usable
  cell centre sits half a cell off shut ground, so the three readings come apart by exactly that.
  Over a thousand relief seeds the centreline never enters a footprint, the swept carriageway
  reaches into one on 392 seeds by at most 0.5 m, and the corridor rectangles clip one on 414 of
  3000 by the same. Only the first is promised; the apron is what absorbs the second and the
  conservatism of the third is what gives cover a box to be judged against. In `ARCHITECTURE.md`.

- **Why two road properties are stated on the default map rather than on the relief sweep.** Over
  four metres of ground, `CoverCoverage` puts seeds 869 and 921 under the threshold at 0.589 and
  0.573 — relief costing walkable floor, not roads costing cover — and the distance from a doorway
  threshold to the nearest corridor runs to 4.19 m against 1.0 m on the default map, because a
  portal is snapped to the nearest cell a road may use and unclimbable ground pushes it outwards. No
  portal is dropped in either case. Recorded in `REPORT.md` and in the tests' own remarks rather
  than absorbed into either number.

- **The road surface is painted into a terrain, by `TerrainSplatWriter`.** The companion to
  `TerrainWriter`, beside it in `Runtime/Unity`, taking the same `RoadNetwork` and a terrain layer
  index per road class. No geometry is authored: a `Terrain` already blends layers by weight and
  this hands it the weights, exactly as the height writer hands it a grid of numbers.

  **One buffer, one upload.** The whole network is rasterised into a single window and written with
  one `SetAlphamaps` over the dirty rect — the bounding box of `RoadNetwork.Corridors` with a texel
  of slack for the feathered edge. Painting a path at a time re-uploads the whole region per call
  and makes Unity re-derive the basemap each time, so a network of thirty branches would pay thirty
  full passes over ground that changes once.

  **Every touched texel is renormalised.** Unity divides an alphamap texel by its own total, so
  writing only the road channel over a ground channel already at one gives a road at fifty per cent
  with the field showing through it — the muddy-road look, which no amount of blending fixes. The
  other layers of a touched texel are scaled to what the road's coverage leaves them and the sum is
  one before Unity sees it.

  **It warns when the texel is coarser than a quarter of the narrowest road**, naming the texel
  size, that road's width and the alphamap resolution that would be enough. Below four texels across
  a carriageway paints as a dashed line and the information is simply not in the buffer. A warning
  rather than a refusal, because how much alphamap memory a project can afford is not the writer's
  decision.

  **It trades this package's "a map is a few kilobytes of readable JSON" promise, for the surface
  only.** A `TerrainData` is a binary asset. What keeps that honest is that the terrain stays
  derived: the whole dirty rect is rebuilt from the document's own network on every write, so a
  carve made by hand is replaced rather than compounded and a lost `TerrainData` costs a regenerate
  rather than a map. Noted in the type's own remarks as well as here.

  `ArenaMap` gains `arteryLayer` and `pathLayer`, both negative by default — which layer of somebody
  else's terrain material is road is a fact about their art, and painting into a guess would replace
  ground they meant to keep. A project that leaves them alone gets the scene it always got.

- **Kerbs and edging along the carriageways, by `RoadKerbs`.** Placed in Core and realised as
  ordinary objects, drawn from whatever a workspace files under `road/kerb` — a new `Props/Road/Kerb`
  folder, which `CatalogSync` now spells as a tag the way it spells `fence/StoneFence`.

  **It is `WallRun` with a different line in it, not a different mechanism.** The cursor already
  seats each piece where the last one stopped, already hands corners over rather than fighting for
  them, already closes what a walk left bare and already tests every piece through `ConstraintSet`.
  What it is given is a carriageway's polyline offset laterally by half the segment's own width, so
  a kerb's inner face lands exactly on the edge of the road and the corridors the cover stage places
  around are untouched.

  **Every piece is a `PlacedObject` with a readable id** — `map/road/artery_00/kerb_004` — so an
  override survives a regeneration. Three digits rather than two, for the reason the boundary's
  segments use three: an artery across a large map carries more than a hundred pieces a side.

  **Runs stop at the junction discs and wherever another carriageway covers them.** Two roads that
  meet would otherwise cross each other's edging at the one place a player reads the network as a
  network, and a branch braided into a trunk would lay a line of stone down the middle of it — one
  kerb in nine, measured over forty default seeds. The cut is made against each side's own kerb line
  rather than against the road's centre, because a road running along one side of another leaves the
  far one alone; asking about the centreline costs a third of all the kerbing.

  **A workspace with nothing in the kerb folder gets no kerbs**, takes no draw and writes no
  statistics — the bargain `PerimeterFence` makes with an empty stone fence folder. `RoadKerbTests`
  holds seeds 1..200 of the default map *with roads on it* to the bytes they serialised to in a build
  with no kerb stage in the pipeline.

- **The ground under a road is graded, inside `TerrainField`.** A `Corridor` is the second graded
  feature beside `Foundation`: a polyline held to a height at every sample, with the same smoothstep
  apron a pad has, measured to the line instead of to a rectangle. It is recorded in a list and
  replayed in order for the reason the pads are — a field rebuilt from a document has to come back
  identical, and neither the network nor its profile is in the document either.

  **`TerrainWriter` needed no change at all, and that is the point.** It rebuilds the whole heightmap
  out of the field on every realise, so a carve applied to a `TerrainData` is erased by the next
  regenerate or deepened by every one. The authoritative ground is `TerrainField.HeightAt` and this
  is graded into it.

  **A profile rather than a plateau.** A building is held at one height because it is a stack of
  level storeys; a road held at its start height is a shelf, and over the length of a map that leaves
  one end floating and the other buried. So the ungraded ground is sampled along the centreline and
  clamped by a forward sweep and then a backward one until no stretch exceeds `maxRoadGradient` —
  which most of the time clamps nothing, because the router would not have laid a step it could not
  grade.

  **Sampled finer than the road is drawn.** `RoadSegment.Profile` is its own polyline, resampled at
  the placement grid's cell, because `Smooth` string-pulls and a straight run across a map is two
  points fifty metres apart. Grading between those is a ramp through every rise it crosses, and it
  put two roads over the same ground at heights a metre and a half apart. The plan is untouched, so
  what it means for a routed segment to be too steep is still a claim about the plan.

  **Both ends pinned before anything is swept.** A portal takes the `foundation_height` of the
  structure whose doorway it serves, so a path arrives level with the sill; a junction on a trunk
  takes the height that trunk is at where it passes; everything else takes the mean ungraded ground
  over its own disc. And a sample whose carriageway overlaps a road already swept takes that road's
  height, because braiding is what the router's decay exists to produce and two roads over one metre
  of map is the ordinary case.

  **Precedence:** a pad beats a corridor, a corridor beats a pad's apron, a corridor beats a
  corridor's apron, and a solved junction height beats either polyline — the last arranged by grading
  the junction discs after the roads and letting the later corridor win.

  `ArenaLayoutGenerator.Terrain` now replays the roads as well as the pads, and
  `TerrainBeforeRoads` is the ground they were laid over — what `RoadNetwork.Build` has to be handed,
  and what anything measuring the ground a route was chosen against has to measure. A `roadDensity`
  of zero grades nothing, so every map that already exists is untouched.

- **The roads are in the pipeline, and they reserve ground.** `RoadNetwork.Build` now runs inside
  `ArenaLayoutGenerator.Generate`, after the structures, the boundary and the dressing are committed
  and before `CoverPlacer` scatters anything. It has to be there and nowhere else: a network is laid
  to reach the doorways the structures declared, so it cannot run before them, and what it produces
  is ground a crate may not stand in, so it cannot run after the crates. Computed afterwards — which
  is where it sat while nothing consumed it — it left cover standing in the carriageway on a large
  share of seeds.

  **`RoadNetwork.Corridors` is the carriageway as rectangles**, because every rule in `ConstraintSet`
  is already stated in terms of rectangles and a swept polygon is not something any of them can be
  asked about. One axis-aligned `Rect2` per stretch of centreline, grown by half the carriageway and
  overlapping at the joints exactly as the polyline's segments do. A stretch is not always a whole
  segment: the smoothing string-pulls, so a segment is routinely tens of metres long and the box
  round a diagonal one that length is a square of that side — a third of the default playfield for a
  single segment, measured. Cut so that no piece strays more than a quarter of a carriageway off its
  own box's long axis, an axis-aligned run stays whole and the network's reservation falls from 1.56
  times the ground the carriageways cover to 1.30.

  **Cover keeps off them by the rule that was already there.** `ConstraintKind.OffReservedPath` is
  what a building's floor keeps its walkways with, and its sentence — a footprint may not stand in
  ground something else has claimed to walk on — is the same sentence outdoors, so the corridors go
  to `ConstraintSet.AddReservedPath` and the cover placer names the rule unchanged. No `OffRoad`, and
  no distance either: an outdoor verge looked like the thing that rule's unused `Value` was waiting
  for and measured as the opposite. Cover snaps to the placement grid, so a verge of even half a
  metre moves a prop a whole cell further from the road and the road's own ground stops being covered
  from beside it — 47 seeds in a thousand under the cover threshold at half a metre and 102 at one,
  against none without a verge at all.

  **Cover also prefers to stand beside a road, and that is what makes one contested.** The positions
  the sampler offers within a metre of a carriageway are tried before the rest, so a lane fills along
  its road first and fills the remainder afterwards. A preference rather than a rule, because a hard
  "within so far of a road" rejects the far corners of a lane outright and the lane comes out with a
  third of the cover it asked for. It is not decoration: reserving a fifth of the map as carriageway
  costs cover coverage, and over seeds 1..1000 of the default map the sweep runs down to 0.567 with
  ten seeds under the 0.6 threshold without the preference and 0.602 to 0.771 with a mean of 0.700
  and none under it with it. The roadless map on the same seeds is 0.620 to 0.776, mean 0.712, so the
  worst seed clears the threshold by 0.002 where the roadless map clears it by 0.020 — a real cost,
  reported rather than tuned away, and the reason `MapThresholds` was not touched.

  **A network is now a function of the finished document rather than of the moment it was laid.** The
  router prefers sheltered ground to open ground, so what is standing on the map when it runs decides
  where the roads go — and it runs before the cover does. Cover is therefore left out of that measure
  entirely, which is what makes `RoadNetwork.Build` over a saved document give back the network that
  document's own cover was placed around.

  **`roadDensity` of zero is byte for byte the map it always was.** No road, no corridor, no
  preference, and a rule with nothing to reject. `RoadPipelineTests` holds seeds 1..200 of the default
  map to digests recorded from a build with no road stage in the pipeline at all, so nothing under
  test can agree with them by construction; `RoadNetworkTests` adds that no piece of cover stands in a
  corridor and that every metre of carriageway is inside one, over its thousand-seed sweep.

- **A road network, as Core data.** `RoadNetwork.Build` takes the parameters, the layout, the ground
  and the placements already committed, and returns junctions and classified polylines — arteries
  down the lane gaps, paths from every doorway and both spawns. It is a pure function of its inputs
  and is never serialised, on the same terms as `ArenaLayout` and `TerrainField`: a document carries
  the seed and the placements, and the network is rebuilt on demand from them. Nothing places an
  object, grades the ground or touches a scene, and `roadDensity` of zero still returns nothing.

  **The nodes come out of the document, not the scene.** A doorway is already a rectangle in a
  structure's metadata, written by the stage that placed it, and that is what is read — the same
  fact the cover placer and the validator read, with one reader of the format rather than two.

  **Braiding is the point of the design.** Every route quarters the cost of the cells it used, and
  halves the cost of the ground within half a carriageway of them, so the next route joins the road
  that is there instead of laying a second one alongside it. The two are deliberately different
  numbers: priced the same, the ground beside a road is as cheap as the road, and two paths from
  neighbouring doors run parallel and touching for their whole length — which is a spider and not a
  network. Across a thousand default seeds the finished network covers 0.677 of what the same routes
  would cover laid one at a time on untouched ground; with the decay removed the same sweep is
  0.796, and `RoadNetworkTests` holds the total under 0.70.

  Flat A* over one reused `int[]`: integer costs throughout, an octile heuristic in the same units,
  the frontier popped by `(f, then h, then cell index)` rather than by whatever order the heap
  happens to leave, and the score array cleared with a generation stamp. No stage draw is taken, so
  adding it moves no placement on any seed that already exists.

- **Four road parameters.** `ArenaParams` gains `roadDensity`, `arteryWidth`, `pathWidth` and
  `maxRoadGradient` — a multiplier on how many redundant loop connections a network lays over the
  route it needs, the trunk and branch carriageway widths in metres, and the steepest rise-over-run
  a road may be graded to. What the four deliberately do not reach — bridges, the ravines they would
  need, road classes past the two, and deliberate dead ends — is recorded in FUTURE.md.

  `roadDensity` defaults to zero for the reason `terrainAmplitude` does: any other default would
  change the output of every map that already exists, in a build where nothing renders the result.

  **No schema version was bumped.** All four are optional fields with defaults, so a document
  written before them carries none of them and comes back meaning exactly what it meant — a density
  of zero, which is no roads. `ArenaJson`'s version check exists to stop a file being quietly
  misread, and there is nothing here for an old file to be misread as; a bump would have rewritten
  the version line of every document in exchange for no change in meaning. Same call as the one
  `terrainAmplitude` and `verticalScale` got, for the same reason.

- **A merged group keeps its pieces' collision and gets a trigger box round the lot.** A table with
  four chairs pulled up to it fills perhaps a third of the rectangle it stands in, so the obvious
  answer — one solid `BoxCollider` on the merged root — walls off the other two thirds. A player
  cannot walk between the chairs, cannot step into the gap at the end of the table, and cannot see
  why not.

  **Merge to Prefab** now takes nothing off the pieces. Each was already finished art carrying
  collision somebody chose, and a chair goes on blocking exactly where the chair is. What the merge
  adds is a single `BoxCollider` on the root at the bounds of every mesh in the group, with
  `isTrigger` set — because the footprint still has to be stated somewhere. `CatalogSync.TryMeasure`
  reads box colliders where a prefab has any, and the clearance the generator keeps round a placed
  piece is that rectangle, so a group of mesh-collidered parts with no box on it anywhere is a group
  whose size nothing says. A trigger is how a collider answers that question without also being a
  wall.

  It is measured off the art rather than off the pieces' own colliders, through the new
  `CatalogSync.TryMeasureArt`. The children keep theirs, so a measurement that read them would make
  the root box a copy of whatever the artist left on the parts rather than of the group. Where a
  piece's collider matches its art the two are the same box; where somebody drew one larger than the
  art, the row records the larger of the two, and the next **Sync from Folders** reads back the same
  number either way.

  This is the opposite bargain from **Wrap Object**, which strips a raw import's colliders and fits
  one solid box, and it is the opposite input: a wrap is a model straight out of a pack, a merge is
  an arrangement of pieces that are already right. A group that renders nothing — markers, empties —
  gets no box at all, for the reason a wrap round nothing does not.

- **A merged group's art that arrived with no collider is given one.** Half a bought art pack ships
  that way: the pieces render and stop nothing. Preserving the pieces' collision is what the merge
  above was asked to do, and on art like that there was none to preserve — so a group made of it came
  out as scenery a player walks straight through, and the root's trigger did not cover for it, because
  a trigger is a measurement and blocks nothing by design.

  Every mesh in the group with no solid collider over it now gets a `BoxCollider` at its own bounds, on
  its own object. Its own bounds and not the group's: a box round the whole arrangement repeated once
  per piece is the invisible wall the root trigger exists to avoid, drawn several times over.

  **The question asked is whether the mesh is blocked, not whether it holds the component.** The walk
  goes up to the merged root, because **Wrap Object** makes exactly the arrangement that tells those
  two apart — one solid box on a wrapper with the art's own colliders stripped from under it. A pass
  that asked each mesh about itself would put a box back on every part the wrap deliberately cleared, a
  dozen colliders inside a box that already covers them, on a prop the generator stamps thirty of round
  a map. Triggers are not an answer either way, which is what keeps the pass independent of the root's
  own box.

- **The Wrap Object tool fits the collision box as well as the pivot.** A model arrives from a pack
  with whatever colliders the artist left on it — a mesh collider per part, thirty of them, or nothing
  at all — and neither is a size the generator can use or a cost a map wants stamped thirty times over.
  Wrapping now strips every collider under the parent, measures every mesh, and puts a single
  `BoxCollider` on the parent at exactly those bounds.

  Stripping happens *before* measuring, and that order is the whole of why the box is trustworthy.
  `CatalogSync.TryMeasure` reads box colliders where a prefab has any and meshes where it has none, so
  a stripped model answers about its art — and the box written from that answer is precisely what the
  next **Sync from Folders** reads back into the row. The rectangle the placement rules keep clear and
  the shape a player walks into are one measurement rather than two that can drift apart.

  It is refitted after every turn and once more on the way into the prefab. A quarter turn swaps a
  box's two horizontal sides, and a collider left at its old size sits at right angles to the art
  inside it — perfect in the scene view, and what a player walks into. A wrapper round something with
  no mesh in it — an empty, a light, a marker — gets no box at all, because a default one-metre cube
  round nothing is a collider standing in an empty room.

- **A structure can say where it is walked into, and a map believes it.** `CatalogEntry.Doorways` is
  a list of rectangles in the entry's own space, and it is the one fact about a structure a generator
  cannot work out for itself: how big a house is, is visible in its meshes, and which wall the door is
  in is not. A map assumed the middle of the two faces across the lane, which is true of a box with no
  door modelled into it and false of everything else — the cover stage kept a clearance in front of a
  blank wall while it stacked crates against the actual door.

  A row states them two ways. **Sync from Folders** reads `DoorwayMarker` children out of an imported
  prefab — matched by name or by Unity tag, case-insensitively and by prefix, so
  `DoorwayMarker_Front` and `DoorwayMarker_Back` declare two — taking the threshold from a box
  collider where the marker has one and a metre square where it is an empty transform. A marker is
  never measured into the piece's own footprint: a prefab that grew half a metre because somebody
  said where its door was would be a measurement reporting on itself. And an exported building writes
  its own out of the plan it was generated from, which no marker could have said.

  The declared rectangles go through the same quarter turn and translation the footprint does. A door
  that did not turn with the wall it is in would be a door in a different wall.

- **Two ways into every building, far apart, and never adjacent.** The ground floor's shell now has
  two modules taken out of it rather than one. A building with a single door is a cul-de-sac: the only
  way out is the way you came, so the room behind the door is where a fight ends rather than ground
  either side can move through. The second way in is asked of the *opposite* wall first and the two
  walls beside it after that, and wherever it lands it has to be half the shell's diagonal from the
  first — a share of the diagonal rather than a distance, so the rule means the same thing on a shell
  of any size. Two doors in adjacent modules are one wide door; two near a shared corner are a door
  with a corner in it.

  `ArenaLayoutGenerator.DoorwaysPerStructure` holds the map's half of the same rule. A row that
  declares more than two openings keeps the two furthest apart, one that declares a single opening is
  topped up from the face further from it, and one that declares none gets both off its footprint as
  it always did.

- **Furniture is turned by the wall behind it.** A quarter turn drawn at random is the right answer
  for a crate lying in a lane and the wrong one for everything with a front: a sofa is modelled facing
  its own positive Z, so a sofa turned at random faces the wall three times out of four — which is not
  a room somebody furnished, it is a room somebody dropped furniture into. `WallFacing` is the rule,
  and a rotation is the one thing no constraint could ever have caught: every rule in `ConstraintSet`
  is a statement about a footprint, and a footprint is the same rectangle whichever way the sofa in it
  faces.

  The corner pass takes its turn from the corner the piece lands in and draws nothing. The scatter
  still draws one and
  overrides it only for a piece that landed near a wall, so a crate in the middle of a floor keeps the
  turn its stream gave it — a room where everything faced inwards would be a ring of furniture rather
  than a room. The facing is decided from the piece's pivot rather than its footprint, because a
  footprint is the rectangle a piece occupies *after* being turned: an oblong drawn lying along one
  wall stands end-on to another once turned, and the wall it now backs onto is no longer the one it
  was nearest.

- **`Props/PropBuilding/Decor/Corners`, for furniture that is modelled as a corner.** An L-shaped sofa
  has a back along negative Z and a second back along negative X, so it belongs in a true geometric
  corner and in exactly one of the four turns there. The corner pass draws from this folder alongside
  `Decoration`; the scatter does not, which is what stops an L-sofa being dropped into the middle of a
  floor with half of it hanging over open ground. Created by **Setup Complete Workspace**, with
  nothing moved into it.

- **Wrap Object.** `Tools/ArenaForge/Wrap Object`, and the same entry on the hierarchy's context
  menu. Everything this tool places is turned on the assumption that an artist modelled the front of
  a piece down its own positive Z — `WallFacing` says so, and says nothing can check it — so a model
  imported facing sideways is stood against a wall sideways on every seed, and no work in the
  placement code can tell that from a piece meant to face that way. The fix belongs in the art, and
  it used to mean making an empty by hand, dragging the model into it, zeroing the child, guessing at
  ninety degrees, looking, guessing again, and remembering to save.

  Select the model and press **Wrap Selection**: it goes under a new empty at the origin, with the
  empty's axes left alone. Three buttons turn the *model* inside it — −90°, 180°, +90° — while the
  parent goes on pointing world-forward, which is the whole point: what the generator turns is the
  prefab root. The scene view is the preview, because the turns are applied to the real object.
  **Save as Prefab** writes the wrapper anywhere in the project. No catalog row is written; a wrapped
  model is art on its way into a folder, and what a folder means is **Sync from Folders**' question.

- **Doorway Setup.** `Tools/ArenaForge/Doorway Setup`, and the same entry on the hierarchy's context
  menu. Where a house is walked into is the one thing about it nobody can measure — how big it is, is
  visible in its meshes, and which wall the door is in is not — so a `DoorwayMarker` child is how the
  person who made the model says so. Declaring one meant making a child, naming it exactly right,
  adding a collider, remembering the trigger box, and then finding the row by hand.

  **Add Doorway** puts a correctly named marker with a trigger box on the middle of the model's
  ground floor and selects it, so the next thing that happens is dragging it onto a wall with Unity's
  own handles. Markers are numbered, so a house declares a front door and a back door. **Save and
  Update Catalog** applies them to the prefab and writes what they measure into the row bound to that
  prefab — both halves, because either alone is a trap: applied and not written is a house that
  declares its doors to nobody, since the generator reads the catalog and never the prefab; written
  and not applied is a row describing markers that vanish when the scene closes. The row is found by
  the prefab it points at rather than by name, and a prefab with no row is named rather than guessed
  at. What the tool makes is an ordinary marker, so a house set up here and one set up by hand are
  read the same way.

- **Merge to Prefab.** `Tools/ArenaForge/Merge to Prefab`, and the same entry on the hierarchy's
  context menu: select a desk, a monitor and a chair arranged in a scene, and the tool parents them
  under a new empty at the average of their pivots, saves that as a prefab anywhere in the project,
  and adds the catalog row. The folder is browsed with the editor's own panel — `Decor/Centerpieces`
  and `Decor/InteriorCovers` are shortcut buttons beside it rather than the only two answers, which
  is what a dropdown of the folders the workspace happened to create had made them. A project keeps
  art where it keeps art, and the folder table still decides what a row is tagged: a folder that
  spells no tags is refused by name when the merge runs, rather than being unreachable.

  The row it writes is the row a sync would have written: same tags off the same folder table, same
  logical id, same measurement. Pressing **Sync from Folders** afterwards finds the new prefab and
  changes nothing, which is the whole claim — the tool saves four steps rather than adding a fifth
  kind of thing to the project. A folder that spells no tags is refused before anything is created,
  rather than written as a row no query can return.

- **`Props/PropBuilding/Decor/InteriorCovers`, and `Props/Covers` reserved for the map** — a room's
  floor scatter has a folder of its own, queried by `BuildingGenerator.InteriorCoverTag` and by
  nothing else. It drew from the root `Covers/` folder before, which sounds like economy — cover is
  cover, and a crate is a crate wherever it stands — and is not: what a workspace files there is the
  tactical furniture of an open arena, so the query returned dumpsters, concrete barriers and
  sandbags to be strewn through somebody's living room. `Props/Covers` is now strictly
  `CoverPlacer`'s and a room's floor is strictly `InteriorCovers`.

  The folder is created by **Setup Complete Workspace** and nothing is moved into it: a project
  cannot be asked to guess which of its crates were meant for indoors. A catalog with the folder
  still empty is refused with a message naming it, rather than building rooms with nothing in them —
  the same bargain the shell art has always made, in the one place a building has to have something.

- **A gap in the run in front of every doorway, as a rectangle rather than a rejection.** The
  `ContinueAround` line is now tiled against gaps computed before the walk begins — one across the
  approach to each doorway the structure declares — exactly as the yard fence opens its gates. The
  clearance rule kept the declared approaches clear already; what changes is that the break is now a
  property of the pass rather than a rule that happened to fire, which is the treatment a fence that
  may never close already gets and for the same reason: a doorway a hedge has grown across is a
  building with no way in.


- **A minimum composition, enforced rather than hoped for** — a map's structure counts are now
  derived from its ground and then *demanded*. One building anchors the middle lane; houses are
  counted one per 1800 m² of playfield and capped at the number of flank lanes, so the default
  60 × 60 m map is a building in the middle and **a house on each flank**, on every seed rather than
  on most. `ArenaLayoutGenerator.RequiredHouses` is the rule, and it is public so a tool or a test
  can ask what a size is going to produce before producing it.

  What makes it a rule is what happens when a structure will not fit where it belongs. Each one is
  offered a list of places in preference order — the building gets its narrow window on the centre
  of the map, then the whole run between the spawns, then every other lane; a house gets its own
  flank, then every other lane — and only when all of them refuse does generation fail, with a
  message naming the tag, the count and the ground it was asked to fit them on. The outcome ruled
  out is the quiet one: a document with a single hut on a field satisfies every property the
  validation suite asserts and is not a map.

- **World boundaries** — a `PerimeterFence` stage tiling the art a workspace files in
  `Props/fence/StoneFence` along all four edges of the playfield, so an arena has a physical
  boundary rather than a coordinate one. It runs after the exterior dressing and before the cover,
  and hands its segments to the cover stage with the rest of the anchored placements.

  The four runs face **inward**, which is the one thing this stage does differently from every other
  run in the tool: a hedge is laid on the outside of the wall it hugs, and a boundary on the inside
  of the line it marks, because the line is the edge of the world and there is no outside. Each
  segment is therefore seated flush from the playfield's own edge and grows inward by its own
  thickness, so a thin panel sits exactly as flush as a thick one.

  Two rules rather than the dressing's four, and the two that are missing are missing on purpose. A
  spawn area spans the full width of the map at its own end, so `ClearOfSpawn` would delete the two
  edges a player actually runs at; and a hole in a hedge is a feature where a hole in a world
  boundary is a way out of the level, so `NotBlockingDoorway` is not asked either. What the boundary
  does yield to is art already standing on the edge — a building flush against the playfield is the
  boundary along its own wall.

- **Mixed boundary fencing** — `fence/WoodFence` panels shorter than every `fence/StoneFence` one
  are offered alongside the stone. A run tiled from a single length of panel is short by up to that
  length at the end of each face, and only a shorter piece closes it; wood longer than the shortest
  stone is left out, because it would not shorten a tail and would put a garden fence in the middle
  of a stone wall. A workspace with only wood in it still gets a boundary. One with neither gets
  none and draws nothing, so a project that has never filled the fence folders generates the map it
  always generated, down to the byte.

  Measured over seeds 1..200 of the default map, the boundary stands on 99.5% of the perimeter, and
  what is missing from that figure is not missing from the map: it is the four corners, each credited
  by a per-edge measurement to the run that turns there rather than the run that owns it. Runs reach
  the ends of their own lines and corners are closed — see the two entries under **Fixed** about the
  pinwheel and the closing panel. Mixing the shorter wood in still matters for a different reason: it
  is what keeps a stone run from doubling up on itself by a whole 2.5 m panel to close.

- **Exterior dressing** — a stage between the structures and the cover, drawing the art a workspace
  files in `Props/PropBuilding/Decor/OutDecor` and `Props/fence/WoodFence` and putting it round the
  outside of the buildings the map has just placed. Three folders, three passes, because a folder in
  this tool says where a prop may go rather than what it is.

  `OutDecor/ContinueAround` is tiled: a cursor walks each face of a structure's footprint, seats the
  drawn piece flush against the wall, and advances by that piece's own length, so a bed of planting
  reads as one line running round the building rather than as a row of separate bushes. It is the
  only placement stage in the tool with no grid and no sampler in it, and deliberately — two
  independently sampled positions are never flush, and a metre grid rounds the joins off. Nine pieces
  in ten come out with another flush against them; what breaks a run is always a rule, and the rules
  that break one are the clearance in front of a doorway, the heap the cluster pass stood there
  first, and the edge of the playfield.

  `OutDecor/UniqueGroup` is the opposite claim about the same strip of ground: a handful of pieces
  drawn round one anchor, at most one heap per face, so a stack of barrels reads as somebody's stack
  of barrels. The heaps go down before the planting, because a heap somewhere specific is a feature
  and a run of hedge is the filler between features. A heap whose anchor turns out to be inside a
  doorway's approach tries two more spots along the same wall before giving that wall up.

- **House perimeter fences** — `fence/WoodFence` tiled round a ring two and a half metres out from a
  house's footprint, and **it can never close**. The gaps are rectangles computed before a single
  segment is drawn — one straight out from each of the house's own doorways, plus one more drawn on
  the ring — and the tiling skips any segment that would stand in one, so a yard is permeable however
  the draws fall rather than permeable because a rule happened to reject the right piece. Leaving it
  to `NotBlockingDoorway` would not have worked: that clearance reaches two metres out from the wall
  and the fence stands half a metre beyond it. The narrowest opening measured over five hundred seeds
  is seven and a half metres, and a spawn can walk to the front door on every one of them.

  The dressing is handed to the cover stage, which places around it exactly as it places around the
  structures themselves — the anchored stage first, the free one after. A catalog with none of this
  art places nothing and draws nothing, so a project that has never filled the exterior folders
  generates the map it always generated, down to the byte.

- **Room centrepieces** — a third interior pass, drawing the art a workspace files in
  `Props/PropBuilding/Decor/Covers` and standing it in the middle of a room. The new `InCentre`
  constraint is the exact negation of `AgainstWall` over the same predicate — a footprint neither of
  the region's axes can reach — with the walkway width as its clearance, and the pass offers the open
  cells in order of how near they are to the centre of the floor, so a sofa lands as close to the
  middle as anything will fit rather than wherever a sampler happened to find room.

  It runs first, before the cover it is meant to be the focus of. A stage that ran last would be
  competing for the middle of the room with whatever the sampler had already dropped there, and
  losing about as often as not. It has its own fork of the floor's seed and returns before it draws
  or commits anything when the catalog has no such art, so a project without it builds the building
  it always built, byte for byte.

  The strips a plan reserves between a room's doors run through exactly the floor a centrepiece
  wants, and the strips win: a room whose middle is spoken for gets its table beside the route, or
  gets none.

- **Navigable interior paths** — a floor plan now reserves the route as well as the opening.
  `PlanRoom.Paths` is a chain of 1.2-metre strips joining each of a room's doorways to the next, and
  on to the stairwell when the shaft is in that room, and the new `OffReservedPath` constraint
  refuses anything proposed onto one. Keeping a doorway clear never said anything about the floor
  between it and the next doorway, so a crate could sit across the only route between a room's two
  doors and every existing property still passed.

  A chain rather than a star through the middle of the room or a route between every pair: a chain
  joins all of the doors and does it with one fewer leg, where a star would reserve the exact middle
  — which is where a room's cover is supposed to stand. Each leg leaves its doorway perpendicular to
  its own wall before it turns, so what is reserved is the floor you walk on rather than a strip
  hugging the wall beside it. A room with one door and no shaft reserves nothing: one door is not a
  passageway, and the clearance already in front of it is what makes it usable.

  The strips are geometry rather than draws — where a walkway runs is a fact about where the
  partition put the doors — so this stage takes nothing from any stream and adding it moved no
  placement in any building that already existed by shifting one.

- **Scene-view overlay** — the controls used while arranging a map, in the scene view with the map:
  the bound `ArenaMap` with a field to retarget it, the seed and a re-roll, Generate, Regenerate and
  Clear, a live count of active overrides, and the stable id of the selected realised object with
  whether an override currently holds it in place. It works with the tool window closed, and it
  deliberately carries none of the window's panels.
- **Layout guides** — the playfield, each labelled lane band and the two spawn areas drawn into the
  scene view, from the component's current parameters rather than from the document, so they follow
  a change to the lane count or the playfield size without regenerating.
- **Building generator** — a multi-storey building as a second document kind: `BuildingDoc`,
  `BuildingParams` and `BuildingGenerator`, with `ArenaBuilding` holding one in a scene and
  `WorldRealizer` realising it exactly as it realises a map. Each floor is filled from the catalog
  by tag query under the existing `ConstraintSet` and placement grid.
- **Per-floor seeds and re-rolls** — every floor stores its own seed, derived from the building seed
  by `Fork` on the first generation and rewritten one at a time after that. Re-rolling floor 2
  leaves floors 1 and 3 byte-identical, and so does adding a storey on top.
- **Building export** — bakes a realised building into a prefab and binds it into the catalog under
  `structure/building/`, with the footprint the building declared and the height its floors give it.
  The arena generator then places it like any other structure, unchanged. An exported building is a
  leaf: baked art, no longer re-rollable by floor.
- **Item mode in the overlay** — map or item, with the building parameters, a re-roll per floor and
  the export button in item mode.
- **Building floor plans** — `FloorPlan` divides each storey by binary space partition into rooms
  and corridors, and the generator builds the shell from catalog art tagged `structure/floor`,
  `structure/wall` and `structure/doorway`: a tiled slab, a wall run per cut with one module of it
  taken out as a doorway, and one entrance in the ground floor's outside wall. Contents are then
  distributed per room instead of over the whole storey, corridors are left clear, and doorways are
  kept clear by the existing `NotBlockingDoorway` rule. Every cut carries a doorway, so a floor is
  connected by construction. A catalog with none of that art still builds — the storey comes back
  as one undivided room, exactly as before.
- **Stairs** — a new `structure/stairs` tag and a stairwell that lines up vertically. The shaft is
  picked once for the whole building by `BuildingGenerator.PlanStairwell`, from the building seed
  rather than from any floor's, so every storey's partition works around the same rectangle and
  re-rolling a floor rearranges its rooms without moving the stairs. One flight per storey, and the
  slab of every storey above the ground — the roof included — has the tiles over the shaft left out,
  so a flight opens into the floor above rather than into a ceiling. No cut may cross the shaft, and
  a run picks a doorway module clear of it whenever it has one.
- **Roof and parapet** — the top storey is capped with a final slab of its own floor art, and a new
  `structure/parapet` tag is tiled round the four edges of it, so a rooftop is enclosed rather than
  open at the lip. There are stairs onto the roof for the same reason: a fence round somewhere
  nobody can reach is a fence round a field. A building's declared `Height` now counts the roof, so
  an exported one binds into the catalog at the height it actually stands.
- **Interior decor** — a new `prop/decor` tag for plants, sofas and desks, placed as a second pass
  per room under three new placement rules: `AgainstWall`, `InCorner` and `NearDoorway`. Corners are
  filled first, then the walls; decor keeps out of the doorways under the existing
  `NotBlockingDoorway` rule and off the stairs and the cover under `NoOverlap`. It draws from its
  own fork of the floor's seed, so it moved nothing that was already there.
- **Map size in the overlay** — the Map tab now carries a Map size field beside the seed, defaulting
  to 60 × 60, so resizing the arena is the same gesture as resizing a building's footprint in the
  Item tab. It writes through the same component the window does, and both re-read it.
- **Workspace and sync know the new tags** — `Props/Stairs`, `Props/Parapets` and `Props/Decor` are
  scaffolded by the workspace setup, and `CatalogSync` reads `structure/stairs`, `structure/parapet`
  and `prop/decor` out of those folder names.
- **Map export** — `MapExport` bakes a realised map into a prefab carrying no ArenaForge components
  at all, ready for a project that does not have the package. It bakes the resolved map, so hand
  edits are in it.
- **Save, Load and Export in the overlay's Map tab** — the same file panels the tool window has,
  now where your hands are. Both surfaces run them through `MapOperations`.
- **Terrain elevation** — `TerrainField` gives the playfield an organic height at every point, from
  three octaves of value noise over the seed. Spawn markers, structures and cover all take their
  height from it. Two new parameters, `terrainAmplitude` and `terrainFeatureSize`, on the component,
  in the tool window and in the document. Relief defaults to **zero**, so a map generated without
  touching it is the flat map this tool produced before: the field decides where things stand, not
  what the ground looks like, so turning it up without a terrain to draw would leave the props
  following ground nothing renders.
- **Foundation levelling** — `BuildingGenerator.LevelFoundation` cuts and fills the ground into a
  flat pad under a structure's footprint plus a metre of doorstep, held at the mean of the ground it
  replaced, with a four-metre apron grading back. A structure never stands on a slant. Spawn areas
  are levelled the same way and first, so both ends of a map are flat. A pad beats another pad's
  apron, so two buildings near each other cannot tilt the ground under one another.
- **Terrain in the scene** — `TerrainWriter` writes the field into a Unity `Terrain` assigned on the
  `ArenaMap`, sizing and positioning it to cover the playfield. No mesh is authored: a terrain is a
  component that renders a grid of heights, and this hands it the heights.
- **Catalog sync from folders** — a source folder and a **Sync from Folders** button on
  `CatalogAsset`. It scans the folder recursively, reads tags out of the folder names — `Walls` →
  `structure/wall`, `Covers/Low` → `cover` and `cover/low` — and reads the footprint and height off
  the prefab's box colliders. It adds and updates, and prunes only the rows whose prefab the project
  no longer has; weight and sockets survive a re-sync.
- **`Tools ▸ ArenaForge ▸ Setup Complete Workspace`** — one menu item that scaffolds
  `ArenaWorkspace`, files the demo art into it and writes the starter art, in that order. It
  replaces the separate **Create Workspace** and **Generate Starter Assets** items, which were
  useless on their own: a workspace of empty folders syncs to an empty catalog, and art with
  nowhere to go cannot be filed.

  The layout is `Buildings`, `Catalog`, `Meshes` and `Props`, with `Covers/Low`, `Covers/High`,
  `Decor`, `Doorways`, `Floors`, `Houses`, `Parapets`, `Spawns`, `Stairs` and `Walls` under `Props`
  — the names the sync recognises — and a fresh catalog pointed at the root. Somewhere to work that
  a package update will not overwrite. Every folder is checked with `AssetDatabase.IsValidFolder`
  before it is created, because `CreateFolder` does not refuse a name that is taken and quietly
  makes `Props 1` beside `Props`.

- **Demo migration** — `DemoMigration` walks the sample's art into the workspace: each prefab into
  the folder that names its tag, so `structure_wall_panel_2m` lands in `Props/Walls` and the next
  sync reads `structure/wall` off the folder; the materials and meshes they are built from into
  `Meshes`, so the prop folders hold nothing but props. `AssetDatabase.MoveAsset` keeps every GUID,
  so the demo scene and `DemoCatalog` follow the move rather than breaking.

  It knows a closed list of exact names — a rule over prefixes would reach into a project's own art
  — so demo art that was never imported, renamed, deleted or already filed is simply not found, and
  nothing else in the setup stops. A destination that is already taken is left alone and reported,
  never overwritten.

- **Starter art** — `ArenaAssetBuilder` writes three prefabs into the workspace as the last step of
  the setup, with real meshes, exactly fitted colliders and sizes the generator can lay out
  without slack: a 2 × 3 m staircase that climbs a whole 3 m storey in twelve steps, a 1 m × 0.1 m
  roof fence a metre high, and a 1 m floor tile 0.2 m thick. They land in the folders the catalog
  sync reads, so a sync straight afterwards binds them to `structure/stairs`, `structure/parapet`
  and `structure/floor` with nothing to type in. It is the one place in the package that authors
  geometry, and it is a tool that writes an asset rather than a generator that builds a scene —
  nothing in `Runtime/` knows it exists.

  The sizes matter more than the modelling: a metre floor tile is what lets a 2 × 3 m flight open
  exactly 2 × 3 m of floor instead of rounding up to a coarser grid, the flight is two metres wide
  because that is how wide the two-metre wall module leaves a corridor, and a metre of fence divides
  a roof of metre tiles so the parapet ring closes with no overhang.

- **Windows** — a new `structure/window` tag, a generated `MyWindow_Gen` prefab and a
  `Props/Windows` folder in the workspace to keep it in. A window is a wall module with a hole in
  the upper half of it — colliders included, so you can see and shoot through it — and it is
  swapped into a module a wall run had already been tiled with rather than laid out separately,
  which is why it measures exactly what the demo pack's wall measures.

  Where they go is a reading of the plan rather than a draw: **outside runs only**, and **one to a
  room on each side of the building that room reaches, in the middle of that room's stretch of it**.
  Glazing whatever will take a window gives a wall of glass every two metres; spacing them by
  counting modules instead spaces them evenly against a wall that is not evenly divided, so where a
  window lands has nothing to do with the room behind it. Three modules are refused outright and a
  room that wanted one of them goes without: the ends of a run, which are the corners of the
  building and are shared with the run coming the other way; the doorway; and a module next to a
  window already placed. On the sample building that is four to six windows in eighteen perimeter
  modules.

  A catalog that gains window art gets windows in the building it already had rather than a
  differently partitioned one — the art is picked from a fork, so no draw here moves anything after
  it — and a catalog without any builds exactly what it built before, byte for byte.

- **Street furniture along the verges, by `RoadFurniture`.** Lamp posts, benches, bins and shelters
  from the art a workspace files under `Props/Road/Furniture`, stood on the ground behind the
  kerbing. It is the last anchored stage, after `RoadKerbs` and before the cover, and it hands its
  pieces to the cover placer with the rest of the anchored art.

  **Spaced by distance along the road, not by position along the polyline.** A carriageway's
  centreline is smoothed and string-pulled, so its vertices crowd round the bends and stand tens of
  metres apart on the straights; stepping that list bunches props on the curves and stretches them on
  the straights whatever spacing is asked for. Each polyline therefore gets an arc-length table once
  — cumulative distance to each of its own points, binary-searched and interpolated inside the piece,
  which is exact because a polyline is straight between its points — and every position is a distance
  in metres against it.

  **The events go down first and a jittered spacing fills the rest.** Evenly spaced lamp posts are
  the loudest signal a level was generated, so the pieces that carry the meaning are placed at the
  junction corners, the portal approaches, the road-class transitions and the apexes of the bends,
  and the stretches between them are filled at nine metres give or take a third, off this stage's own
  `Rng.Fork("road_furniture")` stream. A junction answers three of the four kinds of event at once,
  because in this network a road's class only changes where two roads meet.

  **A bend is measured off its own chord rather than through an angle** — how far the road runs off
  the straight line between the points four metres either side of it, compared against the depth an
  arc of a sixteen-metre radius reaches. Comparing lengths is arithmetic; turning a polyline into an
  angle is trigonometry, which is not bit-identical across runtimes. The rotation is the run's own
  tangent quantised to the nearest `YawStep` through `WallRun.Face.Along`, for the same reason.

  **Height from `TerrainField.HeightAt`, with `Placement.AtYawStep` adding the entry's own
  `BaseOffset`** — never a raycast, which would answer with whatever colliders a scene happened to
  have and would put the result outside the document. Every candidate goes through `ConstraintSet`:
  `InsidePlayfield`, `ClearOfSpawn`, `NotBlockingDoorway`, `OffReservedPath`, `NoOverlap`. A bench in
  a doorway is the same bug as a crate in one.

  Ids are `map/road/artery_00/furniture_03`, so an override survives a regeneration. A workspace with
  nothing in the folder takes no draw and places no object, and `RoadFurnitureTests` holds seeds
  1..200 of the kerbed default map to the bytes they serialised to in a build with no furniture stage
  in the pipeline.

- **`Props/Road/Furniture`** joins the workspace layout, beside `Props/Road/Kerb`. Two folders,
  because a kerb is tiled end to end along the edge of a carriageway and a lamp post stands on the
  verge a spacing apart; a lamp post drawn into a kerb run would be tiled into a line of lamp posts
  touching end to end.

### Changed

- **A road no longer makes a map want less cover.** The floor a lane's cover target is counted off
  already left the carriageways in — a player moves through a road, so a lane with one down it wants
  exactly the cover it wanted without one — but the art the road stages stand on that floor was being
  claimed into it along with the dressing round a building. So a map shrank its own cover target by
  every kerb it had laid. Measured over seeds 1..1000 of the default map at a road density of 1,
  cover coverage ran to a mean of 0.598 with kerbing in the catalog against 0.700 without, and 531 of
  the thousand fell under the 0.6 threshold where none had.

  `CoverPlacer.IsRoadside` names the two stages, and the road-side art is left out of the grid the
  *target* is counted from and kept in the grid the *sampler* draws from — nothing may be placed
  inside a bench. It is not the claim the dressing makes: a hedge and a heap of barrels are placed to
  be in the way and the floor they cover has become the yard, where a kerb is a line on the ground
  and a lamp post is a post.

  This changes every map generated from a catalog with kerb or furniture art in it. No baseline
  covered one, and the two that predate the change — the roadless digests in `RoadPipelineTests` and
  the kerbless ones in `RoadKerbTests` — are maps with neither, and both still hold to the byte.

- **`MapAnalyzer` counts street furniture towards the floor being within reach of cover.** In a
  sixty-metre arena a bench beside a road is not decoration: a player caught in the open there
  reaches it, and it is standing where a crate otherwise would. Kerbing does not count and needs no
  exclusion to not count — it carries no cover tag, and nobody takes cover behind fifteen centimetres
  of stone.

  That is only the count. Whether either blocks a sightline is a separate question, asked of every
  object on the map by `Occluder.TryCreate` and answered by height alone: a shelter stands across the
  eye line and a bench does not, exactly as high cover does and low cover does not.

  Together with the target fix, a road-dense map with furniture on it runs 0.630 to 0.794 over seeds
  1..1000, mean 0.728, with none under the threshold — against 0.700 for the same map with no road
  art at all. `MinCoverCoverage` is unchanged at 0.6.

- **`WallRun.Face` can describe a line at any angle**, through `WallRun.Face.Along`. The four sides
  of a rectangle are untouched — same arithmetic, same quarter turns, byte-identical output, which
  the roadless digests in `RoadPipelineTests` hold every tiled run in the tool to.

  An oblique run differs in two ways, both forced by the placement model. A piece is turned to the
  nearest of the twenty-four `YawStep`s rather than to a quarter turn, because the line is where the
  router put it and it is the art that has to round; and the run's pieces are judged against
  everything on the map *except that run*, because a footprint is an axis-aligned box and two boxes
  round diagonal art laid end to end always overlap, so a flush run would have every second piece
  refused for standing in the one before it. It is the exemption `CloseGaps` already needed for the
  tail of a run, applied to the whole of one.

- **`YawStep.Nearest`** picks the step closest to a direction by comparing against the table's own
  literals rather than by dividing an `atan2` by fifteen. A direction on the boundary between two
  steps would otherwise be decided by the last ulp of a runtime's trigonometry, and two machines
  would generate two different maps.

- **`ArenaLayoutGenerator.Terrain`** gains an overload that hands back the `RoadNetwork` it graded,
  so a caller painting the carriageways gets exactly the network the heights came from rather than
  rebuilding a second one over the finished ground.

- **The router's shelter measure now skips kerbs as well as cover.** A network is a function of the
  document, and a kerb is placed *from* a network — so counting one would route the next rebuild
  round the edging the last one laid, and the reservation the cover was placed against would move
  under it. `RoadNetwork.IsCover` becomes `IsPlacedAfterTheRoads`, which is what the rule always was.

- **How many structures a map holds is decided by the ground rather than by a rule in a file.** The
  composition was one building for the middle lane and one house per 1800 m² *capped at the number of
  flank lanes*, so a 60 × 60 m map and a 400 × 400 m map both came out with the same three structures
  — the larger one being three buildings and 158,000 m² of grass.

  The run between the two spawns, across the lane bands, is now divided into layout cells of about
  `MetresPerStructure` (2500 m², a fifty-metre square) on both axes, and every cell is offered a
  structure. Fifty metres is the size of the arena this tool was built for: the default map comes out
  at one cell per lane — the three structures it has always had, on the map every threshold in this
  project was measured against — so the density that map has is the density every larger map is tiled
  at. A 40 × 40 m map has room for one or two, a 100 × 100 m for about three, a 200 × 200 m for nine,
  and a 400 × 400 m for about three dozen.

  One cell is still a demand: the anchor, a building in the middle lane, tried in its own cell, then
  the whole middle lane, then every other lane, and named in an exception when none of them will hold
  it. Every other cell is an offer — it takes a structure if one will stand there and stays empty if
  none will, because "as many as the ground will hold" is not a number anything can be held to in
  advance.

  `ArenaLayoutGenerator.RequiredBuildings`, `RequiredHouses` and `MetresPerHouse` are gone, replaced
  by `MetresPerStructure` and `StructureCells(ArenaParams)` — how many cells the ground divides into,
  which is the ceiling rather than the count.

- **Large against small is a size budget rather than a slot with a name on it.** Every cell but the
  anchor draws from one weighted list — everything tagged `structure/building` or `structure/house` —
  filtered to what fits `StructureDensity × ¼ × the cell's own area`. A cell with room for a
  two-storey building may come up with one or with a hut; a cell with room for neither takes the
  smallest structure in the catalog.

  What is measured against that budget is the structure **and its yard**, since a house is fenced
  `ExteriorPlacer.YardMargin` out from its own walls and that ground is taken too. Measuring the walls
  alone let the two-storey building into a sixty-metre flank cell that could hold its walls and
  nothing else, three times over, and the floor that was left came out measurably worse covered: the
  1000-seed sweep in `MapValidationTests` put its worst seed at 0.56 against a threshold of 0.6. With
  the yard counted, a sixty-metre flank cell affords the small house and not the building, and the
  sweep is back at its documented 0.62 to 0.78, mean 0.71.

- **Two structures keep two yards' worth of ground between them.** `StructureClearance` was two
  metres flat and is now `2 × ExteriorPlacer.YardMargin`. At three structures on sixty metres the
  difference was invisible; at three dozen it is the normal case, and any closer has two houses'
  garden fences threaded through each other.

- **The boundary fence goes down first of the three anchored stages.** It ran after the dressing and
  yielded to whatever it found, on the reading that a building flush against the playfield is the
  boundary along its own wall. That reading holds while boundary art is a couple of metres long and
  collapses at the length real stone comes in — see the fence entry below. `ExteriorPlacer.Place` now
  takes the boundary and places around it, and `PerimeterFence.Place` no longer takes the dressing.

- **A room's furniture is kept clear of the stairs rather than merely off them.** A storey gave up
  exactly the tiles the stairwell opening covers and not a centimetre more, so a crate could stand
  flush against the bottom step and a room's scatter could close in on all four sides of the shaft —
  a floor above reached sideways, through a gap. `BuildingGenerator.Stairwell.Landing` is the opening
  grown by `StairClearance`, which is twice `DoorwayClearance`, and it is what all three of a room's
  content passes are furnished against. A flight of stairs is a doorway to the storey above, and it is
  approached, turned onto and passed by rather than walked through in one direction.

  It is a separate rectangle from the shaft rather than a wider shaft: the shaft is what the floor
  gives up — the tiles the opening costs, the ground no cut may cross, the hole in the slab — and
  widening it would move walls and take floor out from under the storey above.

- **No cut may leave a room narrower than 2.5 m.** The space partition measured a minimum in whole
  modules, which meant four metres on one art pack and a metre and a half on another; and one module
  is what produced the leaves nobody could use — a two-metre strip with a room id and a door at each
  end. `FloorPlan.MinRoomSide` is stated in metres and converted per floor, and it is checked
  *before* a split: the two regions a cut would leave are measured, and a cut that would leave either
  of them under the minimum is not made at all — the region stays one larger, usable room.

  A floor on the common two-metre module therefore comes out all rooms, where it used to come out
  part corridors. That is the minimum doing its job rather than the corridor rule going away: on an
  art pack whose module is longer than the minimum a narrow leaf is still circulation and is still
  left clear. The recursion terminates for the reason it always did — every split strictly shrinks
  both halves — and a footprint too small to split at all comes back as one room.

- **The world boundary is stone, and only stone.** `PerimeterFence` drew from `fence/StoneFence` and
  mixed in any `fence/WoodFence` panel shorter than the shortest stone one, to shorten the tail of a
  run. That bought a metre of tidiness and sold the distinction the two folders exist for: stone is
  the edge of the level and wood is somebody's garden, which is what `ExteriorPlacer` fences a yard
  with, and a garden panel spliced into the wall round the world reads as a hole somebody patched. A
  workspace with nothing in `Props/fence/StoneFence` now gets **no** boundary rather than a boundary
  made of garden panels — which is the answer that says what to file.

- **The boundary ring stands dead level.** Every panel took the height of the ground under it, which
  is how a garden fence is built and is not how the edge of a level is built: the wall stepped down
  every slope it crossed, so its top edge went up and down and every step was a ledge to be got over.
  The whole ring now takes one height — the highest ground anywhere along the path it runs, sampled
  before a single segment goes down — and the deep footings the stone art carries bury themselves in
  the low ground. What it costs is a wall that stands taller than a panel over a hollow, which is the
  right way for the edge of the world to fail. A house's yard fence still steps, because that is
  exactly the thing that ought to.

  The structures have graded their pads in by then, so a building against the edge of the playfield
  raises the ring with it: the pad is ground now, and a boundary the pad rose through would be a
  boundary you could walk over from the doorstep.

- **A building's declared doorways are the ground floor's shell and nothing else.** Where a person
  may walk between two rooms is a fact about a floor, and every cut of every storey carries one;
  where they may walk in from the map outside is a fact about the building. The document now writes
  the four outside runs of the ground floor, and `BuildingExport` carries them into the catalog row.

- **A `UniqueGroup` heap is seated flush against the wall, and takes that stretch of wall with it.**
  It was drawn between five centimetres and 1.2 m out, so that `NoOverlap` with a margin could accept
  it — and five centimetres of open ground behind a heap is exactly the width of a bush. The heap now
  touches the wall at zero clearance, the same seating the continuous run uses, and the cluster pass
  drops its own margin to zero to match.

  Flush alone would not have been enough. A heap is three separate pieces, so the ground beside a
  barrel and between two of them overlaps nothing, and a run seated flush tiled straight into it —
  planting threaded through somebody's stack of barrels and out the far side, two lines of dressing
  against a wall the art says has one. So a heap reserves the ground it covers, carried back to the
  wall, and the run treats that as a gap decided before its walk: it stops before the heap and picks
  up after it. Heaps are also held to the face they were given, art and all, so two of them cannot
  meet round a corner and become one heap bent through ninety degrees.

- **Fences are planted in the ground rather than stood on it.** `Placement.AtQuarterTurn` lifts a
  piece by its `BaseOffset` so the underside of the art lands on the surface, which is right for a
  crate and wrong for a fence: fence art is modelled with a foundation reaching below the pivot, and
  standing that on the ground is a fence hovering a metre in the air with its own footings on show.
  Both fence stages — the map's boundary and the house's yard — now seat a panel with the new
  `TerrainField.Planted`, which puts the pivot exactly on the ground at the panel's own coordinate
  and lets whatever is modelled below it go under.

  **The rotation is left alone, and that is the point.** One height per panel, a quarter turn about
  Y and nothing else, so a run down a slope steps like a staircase rather than leaning: posts stay
  plumb, joins stay square, and the wedge of open ground each step opens is exactly what a deep
  foundation is modelled to cover. Pitching each panel to the ground's normal is the obvious
  alternative and is wrong in a way no height check would catch. Everything else outside — the
  planting, the heaps, the cover — still stands *on* the ground and is unchanged.

- **The default map now carries three structures where it carried two**, which moves every map the
  generator produces. Documents saved from an earlier version still load and still resolve; they
  simply describe the map that version generated.

- **A run no longer ends because its draws were unlucky.** `WallRun` — the cursor the exterior
  dressing and the world boundary both walk — opens a slot only when the shortest piece on offer
  fits in it, so a slot where all four draws overshot was never a slot the art could not fill. It
  used to end the run there, leaving a hole up to a whole panel wide at the end of every face; it
  now stands the shortest piece, which takes no draw and so leaves the stream reading exactly as it
  did. This makes "what breaks a run is always a rule" true rather than nearly true, and it is what
  took the boundary's widest gap from 2.7 m to 1.2 m. Hedges get the same treatment, and get
  slightly longer.

- **`MapThresholds.MaxExposureAsymmetry` is 0.30, re-derived from 0.25.** The middle of the
  distribution did not move when the second house arrived — mean 0.058 before and 0.057 after, 99th
  percentile 0.186 — but the worst seed of a thousand went from 0.217 to 0.257, because two flank
  structures can both land on the same half of the long axis where one could not. The number is the
  new worst seed plus the headroom the old one carried, on the terms REPORT.md sets every threshold.
  The alternative is constraining where a flank structure may sit, which REPORT.md rules out on
  purpose. `MinCoverCoverage` and `MaxOpenSightlineFraction` are unchanged and still clear their
  bounds; a third structure narrows the cover margin (worst seed 0.644 to 0.620 against a 0.60
  threshold) and widens the sightline one (worst 77.3 m to 75.9 m against 80.6 m).

- **`CoverPlacer.Place` takes everything anchored, not only the exterior dressing.** The parameter is
  now called `anchored` and carries the boundary segments alongside the dressing round each
  structure. Same rule, one more thing to keep out of.

- **`CoverPlacer.Place` takes the exterior dressing as well as the structures**, and takes the
  structures as the new `MapStructure` — the placed object and the exact footprint it was committed
  from, paired at the moment of placement. Cover has to keep out of a hedge as it keeps out of a
  building, and the stages that place *around* a structure need both halves of it: a `PlacedObject`
  carries the stable id, the lane and the doorways but only a pose, and recovering a footprint from a
  pose means rotating four corners through a quaternion rather than the exact axis swap the placement
  actually used.

  The dressing is claimed on the sampler's grid at the sampler's own margin rather than at a
  structure's clearance. A building keeps a metre and a half of floor clear because it is a thing a
  fight is fought round; a bush growing against its wall is a thing you walk past.

- **Doorway rectangles are read back by `ArenaLayoutGenerator.Doorways`**, beside the code that
  writes them, where `CoverPlacer` had its own private copy of the parsing. Two stages need them now,
  and two readers of one metadata format is one of them eventually reading it wrong.

- **Interior decor goes only into the corners**, one piece per corner, where it filled the corners
  and then carried on along the walls. `Props/PropBuilding/Decor/Decoration` is the small stuff — a
  plant, a bin, a lamp — and a room reads as furnished with one of them in the corner and as
  cluttered with a row of them down a wall; the folder split has said so since the deeper workspace
  layout arrived, and the pass now says it too.

  The four corners are enumerated and walked in an order the room's own stream draws, rather than
  left to `InCorner` alone. That rule asks whether a piece has two walls within reach of it and says
  nothing about *which* two, so on its own it would let a room heap its whole allowance into one
  corner and count every piece as correctly placed.

  A catalog with decor art in it therefore furnishes a room differently than it did before, and a
  room whose density asks for more than four pieces gets four.

- **The workspace folder tree is semantic.** A folder now says where a prop may be put rather than
  what it is, which is the only question the placement stages ask. The shell folders moved under
  `Props/PropBuilding`, and `Decor` under them split four ways: `Decoration` for a room's corners,
  `Decor/Covers` for its middle, and `OutDecor/ContinueAround` against `OutDecor/UniqueGroup` for
  exterior dressing that tiles without a gap against dressing that lands in clusters. `Props/fence`
  arrived beside them with `WoodFence` and `StoneFence`. Tactical `Props/Covers` stayed where it
  was, deliberately: it is what a firefight is fought around and `CoverPlacer` queries it by name.

  The placement logic that tells a corner piece from a centre piece is the two entries above:
  `Decoration` goes into the corners and `Decor/Covers` into the middle. The two exterior folders
  are catalogued, tagged and still waiting.

- **`CatalogSync` reads nested depths.** A recognised folder name now takes one of three roles
  rather than carrying a single restart flag. `PropBuilding` restarts, `Decor` branches — it extends
  the path it finds and every folder below it is read as its own name — so
  `Props/PropBuilding/Decor/OutDecor/UniqueGroup` spells `propbuilding/decor/outdecor/uniquegroup`
  and the `Covers` inside the decor tree spells `propbuilding/decor/covers` instead of being
  mistaken for tactical cover. The shell folders still spell `structure/wall` and the rest wherever
  they are filed, and a `Decor` folder with nothing above it still spells `prop/decor`.

- **A workspace already on the old layout is moved to the new one.** `ArenaWorkspace.Relocate` walks
  a closed table of the folders earlier versions created and moves what it finds, whole trees at a
  time, so a project's own `Walls/Brick` survives as `PropBuilding/Walls/Brick`. It moves rather
  than copies, so GUIDs — and every catalog row and scene reference bound to them — follow. A
  destination that is already taken is a warning and not an overwrite, and a source folder is
  deleted only once it is genuinely empty. Idempotent, like the rest of the setup.

- **The interior decor pass asks for both spellings.** It queries `prop/decor` and
  `propbuilding/decor/decoration`, so decor relocated into the deeper tree keeps being placed. The
  deeper name is matched at full depth rather than at `propbuilding/decor`, which is the parent of
  the exterior folders too — what goes round the outside of a building is not what stands in the
  corner of a room.

- **The stairwell opening in the roof is railed along its flanks.** It is the one hole in a surface
  that is fenced all the way round otherwise, and what is under it is a flight dropping most of a
  storey — so this is the one place a ring of `structure/parapet` is worth standing round an
  opening: on a roof it is the art doing the job it was sized for, which is why the same ring
  *inside* the building was taken out again. The ends of the run are left open, because one of them
  is the way off the flight and nothing this tool can read says which — a fence across both would be
  a fence between the roof and the only way down from it. A flight as wide as it runs gets no rail
  at all rather than a rail across the head of its run, and a run that would stand inside the ring
  round the edge of the roof is left out: what a person would fall over there is the edge, and the
  edge is already fenced.

- **The interior stairwell is no longer railed.** The railing added in the previous round could only
  be built from `structure/parapet`, which is sized to be seen from across a roof — so a ring of it
  round a hole in a room came out as a wall through the middle of the floor. The opening is left
  open until there is art meant for the job; see FUTURE.md.

### Fixed

- **A generated building forgot where its doors were the moment its catalog row was lost.** The
  export wrote the ground floor's doorways into the row and nowhere else, so the prefab itself said
  nothing about them. Remove that row and press **Sync from Folders** — the ordinary way to get art
  the catalog has lost back into it — and the building came back with a doorway count of zero,
  because the scan asks the asset and the asset had never been told. Nothing reported it: the prefab
  was right, the row looked right, and only the maps built from it were wrong, with the cover stage
  keeping a clearance in front of a blank wall while it stacked crates against the actual door.

  `BuildingExport` now bakes the doorways into the prefab as `DoorwayMarker` children — a named
  child with a trigger box the size of the opening, exactly what the **Doorway Setup** tool makes
  for a house somebody modelled — and reads the row back off the saved asset through
  `CatalogSync.DoorwaysIn`. There is no second kind of marker and no second reader, so a generated
  building is self-sufficient: it declares its doors the way every other prefab does, and it survives
  being re-scanned by a catalog that has never heard of it. Markers are still never measured into the
  piece's own footprint, in the sync's tally as well as its measurement.

- **A prefab could end up with two rows in the same catalog.** Rows were matched to a scan by logical
  id alone, and an id is spelled from a folder and a file name — so renaming `Crate.prefab`, or
  dragging it from `Covers/Low` to `Covers/High`, spelled an id that matched nothing and added a
  second row for a prefab that already had one. Nothing pruned it either, because the reference in
  the old row follows the asset: both rows were live, both were tagged, and the piece was twice as
  likely to be picked as anybody asked for. A row is now matched by the prefab it points at first and
  by its id second, and the row that survives keeps its own id — a saved world document names the ids
  it placed, so renaming one to follow a file would quietly break every map already built from it.
  The tags do follow the folder, because those are what a query asks about.

- **A sync could leave a dead row sitting beside the row that replaced it.** The sweep that prunes
  rows pointing at nothing ran *after* the scan was merged in. Delete a prefab and make a new one
  under the same name and the new one is a new asset with a new guid, so the row naming the old one
  was not the row the scan produced — it was a second row beside it, still tagged and still pickable,
  and what the generator does with a row it cannot instantiate is reserve the ground, write the
  placement and realise nothing. The sweep now runs first, so the merge only ever sees rows that
  still point at something. It costs the weight and the sockets typed onto the dead row, which are
  facts about art the project no longer has.

- **The boundary fence could stand across a building's front door.** Holding structures one
  panel-thickness inside the playfield so the fence tiles behind them — which is what closed the
  gaps in the edge of the map — put the fence directly in front of any declared doorway in the wall
  it tiled past. Measured against the workspace's own catalog, 6 of 120 declared doorways on an
  80 × 80 m map had a stone panel inside their approach, and the default map reproduces it on seed 6.

  The boundary is not the stage that gives way: `NotBlockingDoorway` is absent from it deliberately,
  because a hole in the edge of the world is a way out of the level. The structure gives way instead.
  `ArenaLayoutGenerator.DoorsCanBeReached` refuses a placement whose declared doorway does not have
  `CoverPlacer.DoorwayClearance` of ground between it and the fence — a door a metre from the world
  boundary is unusable whether or not anything stands in front of it. Four quarter turns are tried at
  each position, so a building turns its doors along the lane rather than losing its cell: measured
  over eight map sizes and twenty seeds each, the structure counts are unchanged and no declared
  doorway on any of them is crowded by anything.

  The hedges, heaps, yard fencing and cover were never at fault and are unchanged — all four place
  under `NotBlockingDoorway` and always did.

- **Nothing in the suite had ever generated a map from a structure that declares its own doorways.**
  Every catalog in `TestWorlds` passed `null` for `CatalogEntry.Doorways`, so every doorway any suite
  had ever measured was one the generator *derived* from a footprint — the middle of the two faces
  across the lane. A row that states its own openings takes a different path entirely, which is how a
  fence across a front door stayed green. `TestWorlds.DoorwayDeclaringCatalog` declares a door in each
  structure's two facing walls, modelled the way the art pack's `DoorwayMarker`s measure, and two new
  properties sweep map sizes against it: nothing placed outside a structure may crowd a declared
  doorway, and a yard fence must leave the way out of one open.

- **The fence round the map came out with holes in it, and on the project's own art with whole sides
  missing.** Measured across seeds of the workspace catalog, the default 60 × 60 m map stood on 74%
  of its perimeter and an 80 × 80 m map on 49% — a complete edge of the level open. Three separate
  causes, each invisible until the art is the size real art is: the pack's stone panel is thirty
  metres long, so one panel refused is thirty metres of open level, where the sample catalog's
  two-and-a-half-metre panel makes every one of these faults a rounding error.

  - *The dressing got there first.* A lamp post standing against a building's outward wall reached
    into the strip the fence needed, and the boundary — which ran second — refused a panel and lost
    half a side. The boundary now runs first and the dressing places around it.
  - *A run only closed its tail.* `WallRun` seated one last piece backwards from the end of a face,
    which covers the remainder and nothing else: a slot the rules refused left bare ground in the
    middle of the run, and once the cursor was within a panel of the end it never got going again.
    `WallRun.CloseGaps` now goes back over **every** stretch the walk left bare, laying pieces
    backwards from each stretch's far end until it is closed or one is refused, then forwards from
    the near end to fill past whatever refused it.
  - *A structure could stand on the ground the fence needed.* A ten-metre building flush against a
    seventy-five-metre edge leaves seventeen metres either side of itself and neither will take a
    thirty-metre panel, so ten metres of wall cost forty-five metres of open level. Structures are now
    held one panel-thickness inside the playfield — `ArenaLayoutGenerator.Buildable`, measured off the
    catalog and zero when the catalog has no stone in it — and the fence tiles behind them.

  Every playfield from 40 × 40 to 400 × 400 m now stands on its whole perimeter bar the four corners
  each run hands to the run that turns there, which the per-edge measurement credits to that other
  edge. `WorldBoundaryTests` sweeps six map sizes against a catalog built at the art pack's own scale
  — a thirty-metre panel and a house that nearly spans a lane band — which is the shape no catalog of
  small tidy art reproduces.

- **A cell asking for "a structure" could be handed a wall panel.** Every piece of structural art in a
  workspace carries the `structure` tag, because wall panels, floor tiles, door frames and flights of
  stairs are what a *building* is built out of. The query for a map's own structures is now that tag
  **with** `structure/building` or `structure/house`, so a two-metre wall panel is no longer stood in
  a lane and called a house — which is what the pack's own catalog produced.

- **A map that could not fit its houses threw instead of building what it could.** With the workspace
  catalog — eighteen-metre houses on sixty-metre maps — the old composition rule demanded two houses
  the ground could not hold and failed generation outright on two thirds of seeds. Only the anchor is
  demanded now, so a map too tight for more comes out with what fits.

- **Furniture stood against a wall outdoors faced the wall.** The heaps `ExteriorPlacer` piles
  against the outside of a building took their quarter turn from the stream, which was the right
  answer when the art in `OutDecor/UniqueGroup` was barrels and the wrong one the moment somebody
  filed a cabinet there: three of every four pieces had their doors in the plaster. The turn now
  comes from the face the heap was given, through `WallFacing.AwayFrom` — back to the wall, front to
  the ground the player walks past on. Indoors the same rule has to *find* the wall, because a piece
  is scattered onto a floor and which of the four is behind it is a measurement; out here the wall is
  handed in, so there is nothing to measure, no reach to be within and no draw to override.

- **A room's decor was turned into the corner it was offered rather than the one it stands in.** How
  far from a corner a piece may be offered is sized off the largest piece of decor in the catalog, so
  one big piece widens the offer for every piece — and art modelled well off its own pivot, which is
  what an imported model is before somebody fixes it, widens it to the whole floor. The pass then
  stood a closet in the far corner carrying the near corner's turn: back to open floor, face in the
  wall. Against the project's own art that was two pieces in five. The turn is now taken from where
  the piece actually lands.

- **A building whose document declared no doorways exported a row with none.** `BuildingExport` read
  the openings out of the document's metadata and wrote an empty list when there was nothing to read
  — no message, no complaint. A building generated before the openings were ever written down and
  still sitting in a scene is exactly that document, and a single storey is the quickest such
  building to have made. What it produced was a house the map placed by guessing at the middle of two
  faces while the cover stage stacked crates against the real front door, and nothing on the way
  through said so. Floor zero's doorways now reach the catalog whatever the metadata says: the ground
  floor is laid out again from the seed that storey already holds — `BuildingGenerator.PlanFloor`,
  the same plan the shell in the prefab was tiled from — and `BuildingGenerator.ExteriorDoorways`
  reads the openings out of it, which is the call the generation itself makes. One implementation,
  two callers. A catalog that can no longer lay the floor out is a building with no declared doorways
  rather than a failed export, which is the bargain the metadata route already made.

- **A window could stand in an interior doorway.** A cut ends buried in the outside wall it runs up
  to, so a cut two modules long with its door in the end module puts that door inside the outside
  wall — and the window rule only knew about the doorway in its *own* run, so it would glaze the
  module the interior door was standing in. The rule now refuses any module a doorway on the floor
  reaches, measured as rectangles rather than as module indices, which belong to different runs.

- **A deleted prefab left a row behind that the generator went on choosing.** The catalog sync only
  added and updated, so a row whose prefab had been deleted or moved out of the project stayed in the
  catalog pointing at nothing — still tagged, so every query still returned it and every weighted pick
  could still land on it. What the generator did with the one it picked was reserve the ground, write
  the placement and realise nothing: a hole in the map at a spot something was deliberately chosen
  for, recorded in the document as a perfectly ordinary object. **Sync from Folders** now prunes
  those rows, counts them in its summary and names each one in the console.

  The test is the row's own prefab reference, not the folder that was scanned, so this stays an
  orphan sweep rather than a mirror of one directory: a row whose prefab lives somewhere the scan
  never looked is left exactly where it is. An exported building, a row bound by hand, art filed
  outside the workspace — all still survive a sync, which is the promise that made the sync
  add-only in the first place.

- **Every prop in a bought art pack was a one-metre cube.** The catalog sync measured box colliders
  and nothing else, and an art pack ships neither box colliders nor, usually, colliders at all — so
  every row it wrote for one kept the defaults on `CatalogAsset.Row`, which are 1 × 1 × 1. Nothing
  reported it as wrong because nothing was wrong with the row; it simply described a piece of art
  that does not exist. The sync now falls back to the meshes the prefab renders, transformed into the
  prefab's own space exactly as the colliders are, and says in its summary how many rows it had to
  measure that way. Box colliders still come first — a collider is a size somebody chose and a mesh
  is whatever the art happens to be — so a catalog that already measured is byte-identical.

  This is the bug under the two below it. A fence panel measured as a square has no long axis, so
  `WallRun.QuarterTurnsAlong` could not tell which way to turn it and the boundary came out turned
  **across** the line it was meant to run along; and a cursor stepping one metre at a time along art
  that is metres long tiled panels through each other and out past the corners.

- **A fence that stopped a panel short of every corner.** The four runs round a rectangle each
  shortened themselves at both ends so that neither would stand in ground the other could occupy —
  which kept them apart by leaving a square of open ground at all four corners, on the map's own
  boundary and round every yard. They now go round like a pinwheel: each run takes the corner at one
  of its ends and gives up the one at the other, so every corner is claimed exactly once and the ring
  closes. The handover carries `WallRun.CornerSlack` — a millimetre — because two runs meeting
  exactly is otherwise a thing two different float sums have to agree on to the last bit, and which
  way that fell decided whether a corner had a panel on it.

  Measured over seeds 1..200 of the default map: the boundary now stands on 99.5% of the perimeter
  against 97.2% before, and the 0.5% is exactly the four corners, each credited by the measurement to
  the run that turns there rather than the run that owns it. Every corner of every playfield is
  closed on every seed. Round the yards, 1371 of 1600 corners are closed where the old geometry
  closed none of them; the rest are corners a gate reaches.

- **A run of fence that never reached the end of its line.** Panels are indivisible and nothing in
  this tool stretches art, so tiling from one end always stopped short by a remainder — up to a whole
  panel of bare ground at the end of every face. `WallRun` now seats one last panel *backwards* from
  the end of the run, so its far end lands exactly on the line and it doubles up on the panel before
  it. A fence post standing in front of another fence post is the cheapest thing a run can be short
  of; a hole a player walks through is the most expensive.

  The closing panel is judged against everything on the map except the run it is closing — see
  `ConstraintSet.Evaluate(Placement, int)`. Overlapping its own tail is the point of it; overlapping
  a building is still refused, and a closing panel that would stand in a gate is not placed at all.
  It is drawn from the shortest art on offer, which is both the least it can double up by and a piece
  no draw is taken for, so the streams read the same as before and a run that divided its line evenly
  is unchanged.

- **A building with no walls in it at all.** `BuildingParams.FloorHeight` defaulted to a round 3 m.
  Every piece of wall art this package ships — the demo pack's panel and door frame, and the window
  `ArenaAssetBuilder` writes to match them — is 2.9 m tall, and the starter floor tile beside them is
  0.2 m thick, which leaves 2.8 m of headroom. So all three were refused, and a storey with no wall
  art is not a storey with slightly wrong walls: with no module to lay a partition out in it is one
  undivided room with no envelope at all — bare slabs, contents standing under a ceiling held up by
  nothing, and not a word in the document to say why. The default is 3.1 m, which is the pitch the
  art in the box was drawn for. A building whose floor height was set by hand is unaffected.

- **`Props/PropBuilding/Decor/Covers` is now `Props/PropBuilding/Decor/Centerpieces`**, and the tag
  it spells is `propbuilding/decor/centerpieces`. Nothing was broken: `Decor` is a branch in the
  sync's folder table, so what was filed under it never could answer a query for the tactical `cover`
  the map is fought around. But two folders one level apart with the same name on them, told apart by
  a rule in a file nobody filing art is reading, is a trap standing open — and a catalog row reading
  `propbuilding/decor/covers` does not say at a glance which kind of cover it means. `ArenaWorkspace`
  moves the old folder to the new one, GUIDs and subfolders intact, the next time the workspace is
  set up.

- **A staircase you had to crawl up.** The generated flight was solid: every step reached down to
  the floor, so the run was a wedge with a flat underside. A building stacks its flights one above
  another in the same shaft, and that underside is the ceiling of the flight below — flat at the
  height of the storey, which left three metres of headroom over the bottom of the run and none at
  all over the top of it. Every step is now a tread of its own thickness with a riser closing the
  step under it, so the flight is open underneath and every tread of the one below has a constant
  2.6 m over it. The tread needs the riser: a tread is thinner than its step is high, so treads on
  their own would be a flight of slabs floating clear of one another.

- **Walls and parapets hanging over the stairwell.** A flight anchored flush with the edge of the
  slab took the whole ring of tiles out from under the outside wall standing on that edge — and a
  wall stands on *top* of its storey's slab, so what was left was a wall floating a slab's thickness
  clear of the storey below, with daylight running along the outside of the building at that floor
  line. The roof's parapet did the same thing a storey higher. A flight is now held off the strip of
  slab the shell stands on: half a wall thickness, or a parapet's whole depth, whichever is wider.
  The anchor snaps to a tile joint, so on the common art pack that costs the outermost ring of
  tiles and nothing else. A building too small to hold a flight clear of its own walls keeps the
  stairwell and gives up the strip, because a storey with no way up is worse.

- **A drop where the top of the stairs should be.** The hole in a slab was cut to the rectangle the
  stairwell was *planned* against — whole tiles of the largest floor tile in the catalog — rather
  than to the ground the flight actually covers, and every storey then cut that same rectangle
  whatever tile it had drawn. A workspace holding both the demo's 2 m slab and the starter's 1 m
  tile therefore opened 2 × 4 m for a 2 × 3 m flight on every floor, and the spare metre landed at
  the top of the run: you climbed the flight and stepped off the last tread into the storey below.
  Each storey now cuts its own grid to the flight itself, so a metre tile opens exactly the ground
  the stairs stand on; where a coarser tile cannot reach the edge of the opening, the strip it had
  to give up is floored again from the finest tile the catalog has, laid flush with the top of the
  slab. A catalog with one floor tile lays its slab exactly as it did before.
- **A stairwell opening four times the floor the stairs cover.** A catalog row could say how big a
  piece of art was but not where it sat, so a footprint had to be declared centred on the pivot —
  which for a flight of stairs pivoted at the foot of its run means declaring it nearly twice as
  long as it is. The opening was then cut to the declared size: a 1 × 3 m flight measured 1 × 5.5 m
  and opened 2 × 6 m of floor, with the stairs filling a quarter of it. Rows now carry a
  `FootprintOffset` beside the size, the sync measures the collider box as it stands instead of
  growing it to centre, and the shell is laid out by where each piece's art goes rather than by
  where its pivot happens to be. The same flight now opens 2 × 4 m. Art modelled around its pivot is
  unaffected, since its offset is zero.
- **A prefab whose pivot is not on its base stood half inside the floor.** A catalog row said how
  far its art reached *above* the pivot and nothing about how far it reached below, so every piece
  modelled around its own centre — every Unity primitive, and a great deal of furniture — was sunk
  half its own depth into whatever it was standing on. Rows now carry a `BaseOffset`, the sync
  measures it off the same box colliders it already read, and `Placement` stands a piece on it. Art
  modelled on its base is unaffected, since its offset is zero. A piece is also selected for
  headroom by its whole standing height now rather than by the half above its pivot, so a sofa
  modelled around its centre is no longer offered a floor it would reach through.
- **The parapet left a gap at all four corners of the roof**, twice. The first version stopped the
  runs along Z half a piece short of the runs along X so that no two would lap, which closed nothing
  and opened four visible holes. The second spanned each run across its whole side but measured that
  side from a rectangle already inset by half the parapet's thickness *and* rounded the piece count
  down, so both losses landed back at the corners. A run is now inset only **across** its travel —
  which is what keeps its outer face off the drop — spans the full side of the roof, and counts its
  pieces **up**, so the four runs close by burying each end in the middle of the next with nothing
  left over. A side the art does not divide overhangs by less than a piece instead of falling short.
- **The parapet was lined up with the walls rather than with the roof.** It is tiled round the roof
  slab's own edge now: the slab is what a person stands on and falls off, and where its edge is comes
  from the tiles rather than from the wall centrelines, which had the ring sitting half a wall
  thickness outboard of the floor it was fencing.
- **The stairs floated in the middle of a hole twice their size.** The opening was cut as "every
  slab tile the flight laps", and a flight at an arbitrary position laps one tile more on each axis
  than it covers — a 1 × 5.5 m flight opened 4 × 8 m of floor. The flight is now anchored on a slab
  joint and the shaft is the smallest whole-tile rectangle that holds it, so the same flight opens
  2 × 6 m and stands flush against the floor at the edge of it. The room contents and the partition
  now keep off the whole opening rather than off the flight, since the difference between the two
  is a drop.
- **Doorways opened into the stairwell.** A run whose every module was against the shaft used to
  take one anyway, on the grounds that an awkward door beat a sealed room. Above the ground floor
  that door is a hole in the floor with a frame round it. It is now forbidden outright: a cut that
  cannot place a doorway clear of the shaft is not made at all, so the region stays one room and
  nothing is sealed off, and the entrance walks round to another side if its own has no clear
  module. Every cut that is made still carries a doorway, so a floor is still connected by
  construction.
- **Z-fighting across the whole generated building shell.** Three separate causes, each of which put
  two surfaces at the same depth:
  - A storey is now a stack rather than a plane. The slab is laid at the floor's own height and
    everything else stands on top of it, so a crate is no longer sunk a slab-thickness into the
    floor, and a wall no longer reaches into the slab above with its top face in that slab's plane —
    which was a coplanar pair running the whole length of every wall in the building. The headroom a
    wall or a crate is selected against is now the floor height less the slab's thickness, so the
    demo's wall art is 2.9 m under a 3 m storey.
  - Every wall run now straddles its own centreline, the outside walls included, and the module
    count comes off the footprint less one wall thickness so the shell still fits inside it. A run
    that arrives at another ends half a thickness inside it instead of flush with its outer face, so
    no two overlapping walls have a vertical face in the same plane. Corners and T-junctions were
    each producing two flush faces before.
  - The floor slab tiles on the floor tile's own footprint, centred in the plan, rather than on the
    wall's module. An art pack whose floor is not exactly as wide as its wall is long now leaves a
    strip of bare ground instead of overlapping tiles with coplanar top faces.
- The demo's floor slab prefab hung *below* its pivot while its catalog row declared a height above
  it, which put its top face in the same plane as the demo scene's ground across every ground-floor
  building. Its mesh now sits above the pivot, as the catalog says it does.

### Changed

- The undo-grouped whole-map operation moved out of the tool window into `MapOperations`, now that
  the overlay performs it too, and is generic over the component being rebuilt so a building goes
  through the same runner. Behaviour is unchanged: one named undo step on success, a revert to the
  group it started from on failure.
- `ArenaEditCapture` notices when the document is replaced by something other than a scene edit —
  the overlay regenerating, or a load — and re-reads it instead of recording the instances that went
  with it as deletions.
- `ConstraintSet` can be built over a plain bounded region as well as an arena, which is what a
  building's floor is. `WithinLane` and `ClearOfSpawn` throw over one rather than being quietly
  satisfied — a storey has no lanes and no spawns, and a rule that can never reject anything would
  report zero rejections as though it had passed.
- `WorldDoc.Resolve` and `BuildingDoc.Resolve` share `OverrideResolution`, extracted now that there
  is a second document to apply an override list to.
- Saving a prefab and stripping the components that only mean something to a document moved out of
  `BuildingExport` into `PrefabBake`, now that a map is baked the same way. It also strips
  `ArenaMap`, `ArenaBuilding` and `WorldRealizer`, which a catalog prefab may be carrying.
- A building's contents are now identified per room —
  `building/floor_02/room_01/cover_00` rather than `building/floor_02/cover_00` — and the density
  is measured against a room's clear floor rather than against the positions a pivot may occupy.
  The two are nearly the same over a lane and nothing like each other in a four-metre room.
- Building metadata gained `plan_rooms`, `plan_corridors` and `structure_objects`.
- `FloorPlan.Bounds` is now the rectangle the walls' *centrelines* enclose rather than their outer
  faces; `FloorPlan.OuterBounds` is the old meaning. A building fits one fewer module across a
  footprint that was an exact multiple of the module, because the outside walls now need half a
  thickness outside the rectangle they tile.
- **A building regenerated on the same seed comes out differently.** The module count changed with
  the line above, and the shell now draws its floor tile before its wall so the slab's thickness is
  known when the wall is chosen. Saved documents load and resolve exactly as before — the objects
  are in the file — but pressing Generate again on an old seed rebuilds a different building, and
  overrides recorded against the old one will orphan. Re-export any baked building prefabs.
- `CoverPlacer.Place` takes the terrain the structures' foundations have been graded into.
- Structures and spawn markers carry `foundation` and `foundation_height` metadata, which is what
  `ArenaLayoutGenerator.Terrain(doc)` replays to rebuild the ground a saved map was placed on.

### Added

- **Grouped catalog inspector** — `CatalogAsset` no longer draws as one flat list of every row.
  Rows are filed under the first segment of their logical id — `structure (5 items)`,
  `cover (23 items)`, `prop (14 items)` — one foldout each, with each row labelled by its own id
  rather than by `Element 12`. Sync and Export stay at the top. Rows can still be added and removed,
  and every edit goes through `SerializedProperty`, so undo and the dirty flag work exactly as
  before. It is a view only: the asset still stores one flat list in one order.

### Fixed

- **A wall that misses its storey by a rounding error no longer empties the building.** A floor
  height of 3.1 m on a 0.2 m slab leaves a headroom a ten-millionth of a metre under 2.9 in single
  precision, so the 2.9 m wall an art pack ships for that storey was refused — and a catalog with no
  usable wall art builds every storey as one undivided room, which looks exactly like a catalog with
  no walls in it. Art within `BuildingGenerator.HeadroomTolerance` (5 cm) of fitting is now
  admitted.
- **Wall runs are fitted to the ceiling, so there is no gap along the top of them.** A wall or
  doorway is stretched or squashed along its own Y by the ratio between what the catalog says it
  measures and what the storey actually leaves, so its top face meets the underside of the slab
  above exactly and what the tolerance admitted never stands into it. The lift that stands a piece
  on its own base is scaled with it, so art modelled around its centre still rests on the floor.

  Nothing else is rescaled: crates, decor, slabs, flights and parapets are placed at the size the
  catalog declares, as they always were. A piece the tolerance admits and nothing stretches may
  stand up to 5 cm into the slab above it.

### Changed

- `Pose` carries a `VerticalScale` beside its uniform `Scale`, and `WorldRealizer` writes it into
  the instance's `localScale.y`. `CoreConvert.ToCorePose` reads it back, so a wall the user stretches
  by hand is captured as an edit rather than ignored.

### Schema

- World documents are written at **schema version 2**, which adds a `kind` field so a building
  cannot be silently read as a map. Version 1 files still load, and are upgraded on the way in, so
  a v1 file loaded and saved again is a whole v2 document. `BuildingDoc` starts its own numbering
  at 1.
- `ArenaParams` gained `terrainAmplitude` and `terrainFeatureSize`. Both have defaults, so an older
  file loads and generates exactly as it did — the amplitude default is zero, which is a flat map.
- A pose gained an optional `verticalScale`, written only when it is not 1. Every document this tool
  has already produced is therefore byte-identical to what it was, and reads back unstretched — no
  version was bumped, because a file with no such field means the same thing it always did.

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
