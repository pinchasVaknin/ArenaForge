# The generate / edit / regenerate loop

The four frames in this folder are the loop the tool exists for, captured from the demo scene
(`Samples~/ArenaDemo/DemoArena.unity` in the package; `Assets/Samples/ArenaForge/0.1.0/Arena Demo/`
once imported) looking straight down at the 60 × 60 m playfield.
Blue squares are the two spawns, grey is the two-storey building, tan is the house, and the small
orange and olive blocks are low and high cover.

| Frame | What it shows |
|---|---|
| `loop-1-generated.png` | Seed `20260816`, generated: 58 objects, no manual edits. `map/lane_mid/cover_02` sits at `(4, 0, -8)`. |
| `loop-2-edited.png` | The same map after that one prop was dragged to `(4, 0, -20)`. One `Move` override; everything else untouched. |
| `loop-3-regenerated.png` | Regenerated on seed `4815162342`, keeping the edits. Every structure and every other prop has moved; `map/lane_mid/cover_02` is still at `(4, 0, -20)`, and no override was orphaned. |
| `loop-4-exposure.png` | The exposure heatmap of the regenerated map — cold blue is sheltered, hot red is seen from everywhere, dark cells are the ground a structure stands on. |

Frame 3 is worth a second look: the prop kept its hand-placed *pose*, but the regeneration chose a
different catalog entry for that slot, so it comes back as a different piece of cover in the same
place. A `Move` override carries a pose, not an asset — swapping the art is a separate
`SwapAsset` override.

The report on the regenerated map:

```
playable (0 failing)
  SpawnSeparation         53   at least 42   pass
  ExposureAsymmetry    0.008   at most 0.25  pass
  CoverCoverage          0.7   at least 0.6  pass
  MaxOpenSightline    72.422   at most 80.61 pass
  SpawnsConnected          1   at least 1    pass
  DoorwaysReachable        1   at least 1    pass
  ReachableFraction        1   at least 0.95 pass
```

## Recording the animated version

These are stills because they were captured headlessly. The animated capture the README wants —
the window on one side, the scene view on the other, a prop dragged and the seed changed — has to
be recorded from a running editor:

1. Import the **Arena Demo** sample from Package Manager, open its `DemoArena.unity`, and open
   `Window → ArenaForge → Arena Forge`. The window binds to the `ArenaForge Demo Map` object.
2. Press **Generate**, then drag a crate a few metres in the scene view. Watch the override count
   go to 1 and the validation panel re-measure.
3. Press the **↻** button beside the seed, then **Regenerate (keep edits)**.
4. Press Ctrl+Z a few times to walk the whole thing back.

Save the recording here as `loop.gif`.
