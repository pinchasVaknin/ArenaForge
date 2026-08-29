using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// Applies an override list to a generated object list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half of a document that is not about what was generated, and it is the same
    /// half in both document kinds: a map's edits and a building's edits are edits to a list of
    /// <see cref="PlacedObject"/>, and neither the lane a crate is in nor the floor a pillar is on
    /// changes what a Move means. It lived in <see cref="WorldDoc"/> while a map was the only
    /// document; <see cref="BuildingDoc"/> is the second caller, which is when it moved.
    /// </para>
    /// <para>
    /// Deliberately a static method over two lists rather than a base class the two documents
    /// derive from. There is nothing here to inherit — the documents share no state, no
    /// parameters and no schema, only this one function.
    /// </para>
    /// </remarks>
    static class OverrideResolution
    {
        /// <summary>
        /// Returns the objects that survive applying <paramref name="overrides"/> to
        /// <paramref name="generated"/>, together with any override that could not be applied.
        /// Neither input is mutated.
        /// </summary>
        /// <remarks>
        /// Overrides are applied in list order, so an edit is evaluated against the state the
        /// preceding edits left behind: a Move targeting something an earlier Delete removed is an
        /// orphan, not an error.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Two generated objects share a stable id, or an entry is null.</exception>
        public static ResolvedWorld Apply(
            IReadOnlyList<PlacedObject> generated, IReadOnlyList<EditOverride> overrides)
        {
            // Tombstones rather than list removals: deleting from the middle of a list would
            // invalidate every index in the lookup below. Nulls are compacted away at the end,
            // which preserves generation order for everything that survives.
            var objects = new List<PlacedObject>(generated);
            var indexById = new Dictionary<string, int>(objects.Count, StringComparer.Ordinal);

            for (int i = 0; i < objects.Count; i++)
            {
                PlacedObject placed = objects[i];
                if (placed == null)
                {
                    throw new InvalidOperationException($"Generated object at index {i} is null.");
                }

                if (indexById.ContainsKey(placed.StableId))
                {
                    throw new InvalidOperationException(
                        $"Two generated objects share the stable id '{placed.StableId}'. " +
                        "Stable ids must be unique for overrides to target them.");
                }

                indexById.Add(placed.StableId, i);
            }

            var orphaned = new List<EditOverride>();

            for (int i = 0; i < overrides.Count; i++)
            {
                EditOverride edit = overrides[i];
                if (edit == null)
                {
                    throw new InvalidOperationException($"Override at index {i} is null.");
                }

                if (edit.Op == OverrideOp.Add)
                {
                    if (indexById.ContainsKey(edit.TargetId))
                    {
                        // The user/ namespace is meant to make this impossible. If it happens
                        // anyway the edit still cannot be applied, so it surfaces like any other
                        // unapplicable override rather than overwriting a generated object.
                        orphaned.Add(edit);
                        continue;
                    }

                    objects.Add(edit.ToPlacedObject());
                    indexById.Add(edit.TargetId, objects.Count - 1);
                    continue;
                }

                if (!indexById.TryGetValue(edit.TargetId, out int target))
                {
                    orphaned.Add(edit);
                    continue;
                }

                switch (edit.Op)
                {
                    case OverrideOp.Move:
                        objects[target] = objects[target].WithPose(edit.Pose.Value);
                        break;
                    case OverrideOp.SwapAsset:
                        objects[target] = objects[target].WithLogicalId(edit.LogicalId);
                        break;
                    case OverrideOp.Delete:
                        objects[target] = null;
                        indexById.Remove(edit.TargetId);
                        break;
                }
            }

            var resolved = new List<PlacedObject>(objects.Count);
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null)
                {
                    resolved.Add(objects[i]);
                }
            }

            return new ResolvedWorld(resolved, orphaned);
        }
    }
}
