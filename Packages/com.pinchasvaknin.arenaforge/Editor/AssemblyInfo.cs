using System.Runtime.CompilerServices;

// The edit-capture bookkeeping and the exposure ramp are internals with real rules to get wrong —
// which override an edit upserts, which one a delete removes, where a user/ id comes from — and
// those rules are worth testing without making them part of the tool's surface.
[assembly: InternalsVisibleTo("ArenaForge.Tests")]
