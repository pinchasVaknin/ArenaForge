using System;
using System.Collections.Generic;
using ArenaForge.Core;
using UnityEngine;
using CorePose = ArenaForge.Core.Pose;

namespace ArenaForge.Unity
{
    /// <summary>
    /// Binds the logical asset ids Core places to the prefabs Unity instantiates.
    /// </summary>
    /// <remarks>
    /// This asset is the only place the two halves of an art entry meet. Core gets the engine-free
    /// half — id, tags, footprint, height, weight — through <see cref="ToCatalog"/> or the JSON the
    /// inspector's export button writes, and never learns that a prefab exists. Swapping the art
    /// pack is then a matter of repointing prefab fields here; no world document changes, because
    /// documents refer to <c>cover/low/crate_wood_01</c> and not to a GUID.
    /// </remarks>
    [CreateAssetMenu(menuName = "ArenaForge/Catalog", fileName = "ArenaCatalog")]
    public sealed class CatalogAsset : ScriptableObject
    {
        /// <summary>
        /// A place on a piece of art where a smaller prop may be put — the top of a crate, a
        /// window ledge.
        /// </summary>
        /// <remarks>
        /// A position and no rotation, because a surface is a place rather than a direction: the
        /// prop that lands on it inherits the parent's own rotation and nothing else.
        /// </remarks>
        [Serializable]
        public sealed class SocketRow
        {
            /// <summary>Socket name, unique within its row. Forms part of a child object's stable id.</summary>
            public string Name;

            /// <summary>What may attach here — <c>prop_surface</c> is the one the generator looks for.</summary>
            public string[] Tags;

            /// <summary>Where the socket sits in the prefab's local space, in metres.</summary>
            public Vector3 LocalPosition;
        }

        /// <summary>
        /// A place on a structure where a player walks in from outside.
        /// </summary>
        /// <remarks>
        /// A rectangle rather than a point, because what the generator does with one is keep the
        /// ground in front of it clear and walk to it: both are questions about an area. Measured
        /// in the prefab's own space, so the row is measured once and a map turns it a quarter at a
        /// time.
        /// </remarks>
        [Serializable]
        public sealed class DoorwayRow
        {
            /// <summary>Where the threshold's centre sits relative to the pivot, in metres.</summary>
            public Vector2 Center;

            /// <summary>How wide the opening is and how deep the threshold is, in metres.</summary>
            public Vector2 Size = Vector2.one;
        }

        /// <summary>One piece of art: what Core knows about it, and what to instantiate for it.</summary>
        [Serializable]
        public sealed class Row
        {
            /// <summary>Readable logical id, for example <c>cover/low/crate_wood_01</c>.</summary>
            public string LogicalId;

            /// <summary>Tags the generator selects this entry by.</summary>
            public string[] Tags;

            /// <summary>Ground footprint in metres.</summary>
            public Vector2 FootprintSize = Vector2.one;

            /// <summary>
            /// Where the footprint's centre sits relative to the pivot, in metres on the ground
            /// plane. Zero for art modelled around its pivot.
            /// </summary>
            /// <remarks>
            /// Without this a row can only describe a footprint centred on the pivot, which for art
            /// modelled off to one side means declaring a rectangle twice the size of the real one:
            /// a flight of stairs pivoted at the foot of its run measured nearly twice its length,
            /// and everything downstream — the stairwell opening most visibly — was cut to the
            /// declared size rather than the real one.
            /// </remarks>
            public Vector2 FootprintOffset;

            /// <summary>Height above the pivot in metres.</summary>
            public float Height = 1f;

            /// <summary>
            /// How far the art reaches below its pivot in metres — zero for a prefab modelled on
            /// its base, half its height for one modelled around its centre, and negative for one
            /// whose art starts above its own pivot.
            /// </summary>
            /// <remarks>
            /// The generator stands a piece by lifting it this far, so art whose pivot is not on
            /// its base ends up resting on the floor rather than sunk into it — or hanging over it,
            /// which is what a negative figure lowers the pivot to prevent. The sync measures it
            /// from the box colliders; a row bound by hand can say it here.
            /// </remarks>
            public float BaseOffset;

            /// <summary>Relative likelihood of being chosen by a weighted pick. Must be positive.</summary>
            public float Weight = 1f;

            /// <summary>Attachment points this piece of art offers.</summary>
            public List<SocketRow> Sockets = new List<SocketRow>();

            /// <summary>
            /// Where this piece of art can be walked into. Empty for everything that is not a
            /// structure.
            /// </summary>
            /// <remarks>
            /// The one fact about a building the generator cannot measure: how big a house is, is
            /// visible in its meshes, and which wall the door is in is not. The sync reads these
            /// off <c>DoorwayMarker</c> children in the prefab, an exported building writes its own
            /// out of the plan it was generated from, and a row bound by hand can say it here.
            /// </remarks>
            public List<DoorwayRow> Doorways = new List<DoorwayRow>();

            /// <summary>What to instantiate, posed by its pivot and centred on the footprint.</summary>
            public GameObject Prefab;
        }

        [SerializeField]
        List<Row> _rows = new List<Row>();

        [SerializeField]
        [Tooltip("Project-relative folder the inspector's Sync from Folders button scans.")]
        string _sourceFolder = string.Empty;

        /// <summary>The rows this asset declares, in inspector order.</summary>
        public IReadOnlyList<Row> Rows => _rows;

        /// <summary>
        /// Project-relative folder the inspector's sync button scans for prefabs, or empty.
        /// </summary>
        /// <remarks>
        /// A path rather than a folder object, because a <c>DefaultAsset</c> is a
        /// <c>UnityEditor</c> type and this asset lives in the runtime assembly. The inspector
        /// draws it as a folder field and writes the path back, so what the user sees is a folder
        /// either way.
        /// </remarks>
        public string SourceFolder
        {
            get => _sourceFolder;
            set => _sourceFolder = value ?? string.Empty;
        }

        /// <summary>
        /// Replaces every row. For tooling that builds a catalog in code — the inspector is the
        /// normal way to author one.
        /// </summary>
        public void SetRows(IReadOnlyList<Row> rows)
        {
            _rows = rows != null ? new List<Row>(rows) : new List<Row>();
        }

        /// <summary>
        /// Builds the Core catalog these rows describe. The result is sorted by logical id, so the
        /// order rows happen to sit in the inspector cannot reach a generated map.
        /// </summary>
        /// <exception cref="InvalidOperationException">A row is incomplete or duplicated.</exception>
        public Catalog ToCatalog()
        {
            var entries = new CatalogEntry[_rows.Count];
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.LogicalId))
                {
                    throw new InvalidOperationException(
                        $"Catalog '{name}' row {i} has no logical id.");
                }

                if (!(row.Weight > 0f))
                {
                    throw new InvalidOperationException(
                        $"Catalog '{name}' row '{row.LogicalId}' has a weight of {row.Weight}; it must be positive.");
                }

                entries[i] = new CatalogEntry(
                    row.LogicalId,
                    row.Tags ?? Array.Empty<string>(),
                    Rect2.FromCenterSize(
                        new Vec2(row.FootprintOffset.x, row.FootprintOffset.y),
                        new Vec2(row.FootprintSize.x, row.FootprintSize.y)),
                    row.Height,
                    row.Weight,
                    ToSockets(row),
                    row.BaseOffset,
                    ToDoorways(row));
            }

            try
            {
                return new Catalog(entries);
            }
            catch (ArgumentException error)
            {
                throw new InvalidOperationException($"Catalog '{name}' is not valid: {error.Message}", error);
            }
        }

        /// <summary>The doorways a row declares, as rectangles in the prefab's own space.</summary>
        /// <remarks>
        /// A doorway of no size is dropped rather than passed on. It is what an empty element added
        /// in the inspector and never filled in comes out as, and a rectangle of no area is one
        /// nothing can be inside and no walkable cell can land in — a doorway the validator would
        /// report as unreachable for ever.
        /// </remarks>
        static Rect2[] ToDoorways(Row row)
        {
            if (row.Doorways == null || row.Doorways.Count == 0)
            {
                return null;
            }

            var doorways = new List<Rect2>(row.Doorways.Count);
            for (int i = 0; i < row.Doorways.Count; i++)
            {
                DoorwayRow doorway = row.Doorways[i];
                if (doorway == null || doorway.Size.x <= 0f || doorway.Size.y <= 0f)
                {
                    continue;
                }

                doorways.Add(Rect2.FromCenterSize(
                    new Vec2(doorway.Center.x, doorway.Center.y),
                    new Vec2(doorway.Size.x, doorway.Size.y)));
            }

            return doorways.Count > 0 ? doorways.ToArray() : null;
        }

        static CatalogSocket[] ToSockets(Row row)
        {
            if (row.Sockets == null || row.Sockets.Count == 0)
            {
                return null;
            }

            var sockets = new CatalogSocket[row.Sockets.Count];
            for (int i = 0; i < row.Sockets.Count; i++)
            {
                SocketRow socket = row.Sockets[i];
                if (socket == null || string.IsNullOrWhiteSpace(socket.Name))
                {
                    throw new InvalidOperationException(
                        $"Row '{row.LogicalId}' has a socket with no name.");
                }

                sockets[i] = new CatalogSocket(
                    socket.Name,
                    socket.Tags ?? Array.Empty<string>(),
                    CorePose.At(new Vec3(
                        socket.LocalPosition.x, socket.LocalPosition.y, socket.LocalPosition.z)));
            }

            return sockets;
        }

        /// <summary>
        /// The prefab bound to a logical id, or null if this catalog does not carry one.
        /// </summary>
        public GameObject PrefabFor(string logicalId)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null && string.Equals(_rows[i].LogicalId, logicalId, StringComparison.Ordinal))
                {
                    return _rows[i].Prefab;
                }
            }

            return null;
        }
    }
}
