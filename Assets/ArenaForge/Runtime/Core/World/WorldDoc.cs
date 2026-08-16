using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ArenaForge.Core
{
    /// <summary>
    /// A map: a seed, the parameters it was generated with, what the generator produced, and the
    /// manual edits layered on top.
    /// </summary>
    /// <remarks>
    /// This is the whole persistence story. A map is not a saved scene — it is this document,
    /// a few kilobytes of readable JSON, from which the scene is rebuilt on demand. See
    /// ARCHITECTURE.md section 2 for why.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class WorldDoc
    {
        /// <summary>Schema revision written into world JSON.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Schema revision this document was written with.</summary>
        [JsonProperty("schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>
        /// Generator settings the generated objects were produced with, seed included.
        /// </summary>
        [JsonProperty("parameters", Order = 1)]
        public ArenaParams Parameters { get; set; } = new ArenaParams();

        /// <summary>What the generator produced, in generation order.</summary>
        [JsonProperty("generatedObjects", Order = 2)]
        public List<PlacedObject> GeneratedObjects { get; } = new List<PlacedObject>();

        /// <summary>Manual edits, applied in list order.</summary>
        [JsonProperty("overrides", Order = 3)]
        public List<EditOverride> Overrides { get; } = new List<EditOverride>();

        /// <summary>
        /// Applies the overrides to the generated objects and returns the result, together with
        /// any override that could not be applied.
        /// </summary>
        /// <remarks>
        /// Overrides are applied in list order, so an edit is evaluated against the state the
        /// preceding edits left behind: a Move targeting something an earlier Delete removed is an
        /// orphan, not an error. The document itself is not mutated.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Two generated objects share a stable id.</exception>
        public ResolvedWorld Resolve()
        {
            // Tombstones rather than list removals: deleting from the middle of a list would
            // invalidate every index in the lookup below. Nulls are compacted away at the end,
            // which preserves generation order for everything that survives.
            var objects = new List<PlacedObject>(GeneratedObjects);
            var indexById = new Dictionary<string, int>(objects.Count, StringComparer.Ordinal);

            for (int i = 0; i < objects.Count; i++)
            {
                PlacedObject generated = objects[i];
                if (generated == null)
                {
                    throw new InvalidOperationException($"Generated object at index {i} is null.");
                }

                if (indexById.ContainsKey(generated.StableId))
                {
                    throw new InvalidOperationException(
                        $"Two generated objects share the stable id '{generated.StableId}'. " +
                        "Stable ids must be unique for overrides to target them.");
                }

                indexById.Add(generated.StableId, i);
            }

            var orphaned = new List<EditOverride>();

            for (int i = 0; i < Overrides.Count; i++)
            {
                EditOverride edit = Overrides[i];
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
