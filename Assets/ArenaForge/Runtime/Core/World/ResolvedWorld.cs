using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// The outcome of applying a document's overrides to its generated objects.
    /// </summary>
    /// <remarks>
    /// <see cref="OrphanedOverrides"/> is why this is a result type rather than a plain list.
    /// Regeneration can stop producing the object an edit points at, and silently discarding that
    /// edit is the one failure mode that would make the tool untrustworthy. The orphans come back
    /// to the caller, which shows them and lets the user decide.
    /// </remarks>
    public sealed class ResolvedWorld
    {
        /// <summary>The objects that should exist in the scene, in generation order with additions appended.</summary>
        public IReadOnlyList<PlacedObject> Objects { get; }

        /// <summary>
        /// Overrides that could not be applied — their target no longer exists, or an Add collided
        /// with an id already in use.
        /// </summary>
        public IReadOnlyList<EditOverride> OrphanedOverrides { get; }

        /// <summary>Creates a resolution result.</summary>
        public ResolvedWorld(IReadOnlyList<PlacedObject> objects, IReadOnlyList<EditOverride> orphanedOverrides)
        {
            Objects = objects;
            OrphanedOverrides = orphanedOverrides;
        }
    }
}
