using System.Collections.Generic;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;
using CorePose = ArenaForge.Core.Pose;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Watches the realised map and turns what the user does to it into overrides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dragging a crate in the scene view moves a GameObject, and a GameObject is not the map — the
    /// document is. This is the piece that closes that loop: it compares every realised instance
    /// against the pose the document resolved it to, and records the difference as a
    /// <see cref="OverrideOp.Move"/>. Without it the crate goes back where the generator wanted it
    /// the next time the map is regenerated, which is the failure the whole project exists to avoid.
    /// </para>
    /// <para>
    /// It runs while the tool window is open and not while it is closed. Capture mutates the user's
    /// document, and a global editor hook doing that quietly in every scene that happens to contain
    /// a map is a worse bargain than a tool that only watches while it is on screen.
    /// </para>
    /// <para>
    /// An edit is recorded once the instance stops moving rather than on every tick of a drag, which
    /// keeps one gesture to one override and one undo step instead of ten a second of both.
    /// </para>
    /// </remarks>
    sealed class ArenaEditCapture
    {
        /// <summary>Position difference, in metres, below which an instance counts as unmoved.</summary>
        const float PositionEpsilon = 1e-3f;

        /// <summary>Rotation difference, as a quaternion dot product, below which a rotation counts as unchanged.</summary>
        const float RotationEpsilon = 1e-5f;

        /// <summary>Scale difference below which a scale counts as unchanged.</summary>
        const float ScaleEpsilon = 1e-3f;

        /// <summary>Namespace user-added objects live in, so their ids can never collide with generated ones.</summary>
        public const string UserIdPrefix = "user/";

        sealed class Watched
        {
            public string StableId;
            public Transform Instance;
            public CorePose Base;
            public CorePose Last;
        }

        readonly List<Watched> _watched = new List<Watched>();
        readonly ArenaMap _map;

        WorldDoc _builtFrom;
        int _cleanUndoGroup;

        /// <summary>Starts watching a map. Call <see cref="Rebuild"/> once it has been realised.</summary>
        public ArenaEditCapture(ArenaMap map)
        {
            _map = map;
            _cleanUndoGroup = Undo.GetCurrentGroup();
        }

        /// <summary>
        /// Re-reads the realised scene and the document it came from. Call after realising, after an
        /// undo, and after anything else that can change which instances exist.
        /// </summary>
        public void Rebuild()
        {
            _watched.Clear();
            _cleanUndoGroup = Undo.GetCurrentGroup();
            _builtFrom = null;

            if (_map == null)
            {
                return;
            }

            WorldDoc doc = _map.Document;
            _builtFrom = doc;
            if (doc == null)
            {
                return;
            }

            var poseById = new Dictionary<string, CorePose>(System.StringComparer.Ordinal);
            IReadOnlyList<PlacedObject> resolved = doc.Resolve().Objects;
            for (int i = 0; i < resolved.Count; i++)
            {
                poseById[resolved[i].StableId] = resolved[i].Pose;
            }

            // Driven by what is actually in the scene, not by what the document says should be:
            // an object whose logical id the catalog binds no prefab to was never instantiated, and
            // must not be mistaken for one the user deleted.
            var refs = _map.Realizer.Root.GetComponentsInChildren<ArenaObjectRef>(true);
            for (int i = 0; i < refs.Length; i++)
            {
                if (!poseById.TryGetValue(refs[i].StableId, out CorePose pose))
                {
                    continue;
                }

                CorePose current = CoreConvert.ToCorePose(refs[i].transform);
                _watched.Add(new Watched
                {
                    StableId = refs[i].StableId,
                    Instance = refs[i].transform,
                    Base = pose,
                    Last = current,
                });
            }
        }

        /// <summary>
        /// Looks for moved and deleted instances and records them. Returns true if the document
        /// changed.
        /// </summary>
        public bool Tick()
        {
            if (_map == null || !_map.HasDocument)
            {
                return false;
            }

            WorldDoc doc = _map.Document;

            if (!ReferenceEquals(doc, _builtFrom))
            {
                // The document was replaced by something other than a scene edit — the scene-view
                // overlay generating, or a load. The instances this was watching went with it, and
                // reading their absence as deletions would turn someone else's regeneration into a
                // list of overrides the user never made.
                Rebuild();
                return true;
            }

            bool registered = false;
            bool settled = true;

            for (int i = 0; i < _watched.Count; i++)
            {
                Watched watched = _watched[i];

                if (watched.Instance == null)
                {
                    Register(ref registered, $"ArenaForge: delete {watched.StableId}");
                    RecordDelete(doc, watched.StableId);
                    _watched.RemoveAt(i--);
                    continue;
                }

                CorePose current = CoreConvert.ToCorePose(watched.Instance);

                if (!Near(current, watched.Last))
                {
                    // Still under the mouse. Wait for it to come to rest so one drag makes one
                    // override rather than one per tick.
                    watched.Last = current;
                    settled = false;
                    continue;
                }

                if (Near(current, watched.Base))
                {
                    continue;
                }

                Register(ref registered, $"ArenaForge: move {watched.StableId}");

                // Snapped at rest rather than during the drag: one gesture makes one override, and
                // an object that jumped under the cursor while it was still being held would be
                // fighting the hand holding it.
                current = Snapped(doc, watched, current);

                RecordMove(doc, watched.StableId, current);
                watched.Base = current;
                watched.Last = current;
            }

            if (registered)
            {
                Commit(doc);
                return true;
            }

            if (settled)
            {
                // Nothing is pending, so any undo step after this point belongs to the next edit
                // and can be collapsed into it — including the transform move Unity itself records
                // when the drag begins.
                _cleanUndoGroup = Undo.GetCurrentGroup();
            }

            return false;
        }

        /// <summary>
        /// Records an object the generator did not produce, at a pose, and realises the result.
        /// </summary>
        public void RecordAdd(string logicalId, CorePose pose, IReadOnlyList<string> tags)
        {
            WorldDoc doc = _map.Document;
            if (doc == null)
            {
                return;
            }

            string stableId = NextUserId(doc, logicalId);
            int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(_map, $"ArenaForge: add {stableId}");

            doc.Overrides.Add(EditOverride.Add(stableId, logicalId, pose, ToArray(tags)));
            _map.SetDocument(doc);
            EditorUtility.SetDirty(_map);
            _map.Realize();

            Undo.SetCurrentGroupName($"ArenaForge: add {stableId}");
            Undo.CollapseUndoOperations(group);
            Rebuild();
        }

        /// <summary>
        /// Takes one override back out of the document and realises the result.
        /// </summary>
        public void Revert(EditOverride edit)
        {
            WorldDoc doc = _map.Document;
            if (doc == null)
            {
                return;
            }

            int group = Undo.GetCurrentGroup();
            string name = $"ArenaForge: revert {edit.Op} {edit.TargetId}";
            Undo.RegisterCompleteObjectUndo(_map, name);

            doc.Overrides.Remove(edit);
            _map.SetDocument(doc);
            EditorUtility.SetDirty(_map);
            _map.Realize();

            Undo.SetCurrentGroupName(name);
            Undo.CollapseUndoOperations(group);
            Rebuild();
        }

        /// <summary>
        /// The stable id a new user-added object should take: the logical id's last segment under
        /// the <c>user/</c> namespace, numbered so it cannot collide with anything already there.
        /// </summary>
        public static string NextUserId(WorldDoc doc, string logicalId)
        {
            string leaf = logicalId;
            int slash = logicalId.LastIndexOf('/');
            if (slash >= 0 && slash < logicalId.Length - 1)
            {
                leaf = logicalId.Substring(slash + 1);
            }

            var taken = new HashSet<string>(System.StringComparer.Ordinal);
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                taken.Add(doc.GeneratedObjects[i].StableId);
            }

            for (int i = 0; i < doc.Overrides.Count; i++)
            {
                taken.Add(doc.Overrides[i].TargetId);
            }

            for (int index = 0; ; index++)
            {
                string candidate = $"{UserIdPrefix}{leaf}_{index:00}";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        /// <summary>
        /// Replaces the pose an object is placed at, upserting the override that carries it.
        /// </summary>
        /// <remarks>
        /// A user-added object has its own <see cref="OverrideOp.Add"/> updated in place rather than
        /// acquiring a Move on top of it, so one object the user put down stays one row in the
        /// override list however many times they nudge it.
        /// </remarks>
        internal static void RecordMove(WorldDoc doc, string stableId, CorePose pose)
        {
            for (int i = 0; i < doc.Overrides.Count; i++)
            {
                EditOverride existing = doc.Overrides[i];
                if (!string.Equals(existing.TargetId, stableId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (existing.Op == OverrideOp.Add)
                {
                    doc.Overrides[i] = EditOverride.Add(
                        stableId, existing.LogicalId, pose, ToArray(existing.Tags), null);
                    return;
                }

                if (existing.Op == OverrideOp.Move)
                {
                    doc.Overrides[i] = EditOverride.Move(stableId, pose);
                    return;
                }
            }

            doc.Overrides.Add(EditOverride.Move(stableId, pose));
        }

        /// <summary>
        /// Records that an object is gone.
        /// </summary>
        /// <remarks>
        /// Deleting something the user added removes the Add that produced it rather than stacking a
        /// Delete on top; deleting something the generator produced drops any Move it had picked up,
        /// because the pose of an object that is not there is not information.
        /// </remarks>
        internal static void RecordDelete(WorldDoc doc, string stableId)
        {
            bool wasAdded = false;

            for (int i = doc.Overrides.Count - 1; i >= 0; i--)
            {
                EditOverride existing = doc.Overrides[i];
                if (!string.Equals(existing.TargetId, stableId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                wasAdded |= existing.Op == OverrideOp.Add;
                doc.Overrides.RemoveAt(i);
            }

            if (!wasAdded)
            {
                doc.Overrides.Add(EditOverride.Delete(stableId));
            }
        }

        // The vertical scale is compared on the same slack as the uniform one rather than exactly,
        // because a stretched wall reads back out of a Unity transform as a division of what went
        // into it — and an instance nobody touched must not come back as an edit.
        static bool Near(CorePose a, CorePose b) =>
            Vec3.DistanceSquared(a.Position, b.Position) <= PositionEpsilon * PositionEpsilon &&
            Mathf.Abs(a.Scale - b.Scale) <= ScaleEpsilon &&
            Mathf.Abs(a.VerticalScale - b.VerticalScale) <= ScaleEpsilon &&
            1f - Mathf.Abs(Dot(a.Rotation, b.Rotation)) <= RotationEpsilon;

        static float Dot(Quat a, Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        static string[] ToArray(IReadOnlyList<string> tags)
        {
            if (tags == null || tags.Count == 0)
            {
                return null;
            }

            var copy = new string[tags.Count];
            for (int i = 0; i < tags.Count; i++)
            {
                copy[i] = tags[i];
            }

            return copy;
        }

        /// <summary>
        /// Pulls a dropped object flush with the edges it came to rest beside, stands it on what is
        /// under it, and returns the pose it ends up at.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The arithmetic is <see cref="EdgeSnap.TryFlush"/>; what this adds is what it is measured
        /// against. Everything else the resolved map holds, minus the object being moved and minus
        /// props standing on sockets — a bottle on a crate offers an edge that is not on the ground
        /// and is not one a wall should line up with.
        /// </para>
        /// <para>
        /// The transform is written as well as the document, because the two have to agree before
        /// the next tick reads them: a document snapped and an instance left where the mouse
        /// dropped it is a difference the next pass would faithfully record as a second edit.
        /// Inside the undo group <see cref="Register"/> just opened, so the snap and the move come
        /// back together on one Ctrl+Z along with Unity's own transform entry.
        /// </para>
        /// <para>
        /// <strong>An edge in reach wins, and the grid is the fallback.</strong> A drop with nothing
        /// near it lands on the nearest cell rather than exactly where the mouse let go, so nudging
        /// something a few centimetres left and right does not leave it a few centimetres off. A
        /// drop beside a neighbour lines up with the neighbour instead, because that is what the
        /// user was aiming at.
        /// </para>
        /// <para>
        /// <strong>An edge snap can still leave an object off the grid</strong>, and the overlay's
        /// placement verdict will say so. Art whose footprint does not divide the cell cannot be
        /// both flush with its neighbour and on the grid; the tool shows the conflict rather than
        /// choosing for you. The art it ships is metre-based, where the two agree.
        /// </para>
        /// <para>
        /// <strong>The height is settled separately and afterwards</strong>, by <see cref="Standing"/>
        /// — the two snaps do not compete, because one decides where on the ground the object goes
        /// and the other decides which ground that is.
        /// </para>
        /// </remarks>
        CorePose Snapped(WorldDoc doc, Watched watched, CorePose current)
        {
            CatalogAsset asset = _map.Realizer != null ? _map.Realizer.Catalog : null;
            if (asset == null)
            {
                return current;
            }

            Catalog catalog = asset.ToCatalog();
            CatalogEntry entry = EntryOf(catalog, doc, watched.StableId);
            if (entry == null)
            {
                return current;
            }

            var neighbours = new List<Rect2>();
            ResolvedWorld resolved = doc.Resolve();

            for (int i = 0; i < resolved.Objects.Count; i++)
            {
                PlacedObject other = resolved.Objects[i];
                if (other.StableId == watched.StableId || CoverPlacer.IsSocketProp(other.StableId))
                {
                    continue;
                }

                CatalogEntry row = catalog.Find(other.LogicalId);
                if (row != null)
                {
                    neighbours.Add(other.Pose.Bounds(row.Footprint));
                }
            }

            ArenaGrid grid = ArenaLayout.Build(_map.BuildParams()).Grid;

            // An edge in reach wins; the grid is what a drop falls back on. That order is the whole
            // of how the two snaps are reconciled: lining a wall up with the wall beside it is a
            // thing the user aimed at, and the grid is what they get when they aimed at open floor.
            bool flush = EdgeSnap.TryFlush(
                current.Bounds(entry.Footprint), neighbours, SnapReach(), out Vec2 offset);

            Vec2 placed = flush
                ? new Vec2(current.Position.X + offset.X, current.Position.Z + offset.Y)
                : grid.Snap(new Vec2(current.Position.X, current.Position.Z));

            // Downwards after sideways, and sampled where the object ends up rather than where the
            // mouse let go: the horizontal snap can carry a piece off the slab it was dropped over,
            // and the height that matters is the height under where it lands.
            float standing = Standing(asset, entry, watched, placed, current);

            if (Near(placed.X, current.Position.X) &&
                Near(placed.Y, current.Position.Z) &&
                Near(standing, current.Position.Y))
            {
                return current;
            }

            var snapped = new CorePose(
                new Vec3(placed.X, standing, placed.Y),
                current.Rotation,
                current.Scale,
                current.VerticalScale);

            Undo.RecordObject(watched.Instance, "ArenaForge: snap");
            watched.Instance.localPosition = CoreConvert.ToUnity(snapped.Position);

            return snapped;
        }

        /// <summary>
        /// The height a dropped object comes to rest at: the first standing surface under it, plus
        /// whatever the art reaches below its own pivot. Its current height if there is none.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>A standing surface is one the catalog says is one.</strong> Floor slabs carry
        /// <see cref="BuildingGenerator.FloorTileTag"/>, and the Unity terrain is the other. A
        /// Unity layer is the usual way to ask a question like this and would have put the answer
        /// in two places — the catalog, which already says what every piece of art is, and a layer
        /// assignment somebody has to remember to make on each prefab they import.
        /// </para>
        /// <para>
        /// <strong>A table is not a standing surface</strong>, so a crate dropped onto one carries
        /// on down to the floor beneath it. That is the same rule rather than an exception to it:
        /// the catalog decides, and what the catalog calls a floor is what a crate stands on.
        /// </para>
        /// <para>
        /// The ray starts at the top of the art and not at its pivot, because a drop that ends with
        /// the object half sunk into the slab is the ordinary case — that is where the mouse leaves
        /// it — and a ray fired from inside a slab passes under it and finds the ground instead.
        /// Triggers are ignored: <see cref="MergeToPrefab"/> puts one round a merged group as a
        /// measurement of it, and a measurement is not something anything stands on.
        /// </para>
        /// <para>
        /// <strong>It falls as far as it has to.</strong> An object let go in mid-air lands on what
        /// is under it rather than hanging where the mouse dropped it, which is what this is for.
        /// The cost is that nothing can be left in the air on purpose; see FUTURE.md.
        /// </para>
        /// </remarks>
        float Standing(
            CatalogAsset asset, CatalogEntry entry, Watched watched, Vec2 at, CorePose current)
        {
            Transform space = _map.Realizer.Root;

            // The factor the realiser wrote onto Y, which is what the art's reach above and below
            // its own pivot is multiplied by.
            float vertical = current.Scale * current.VerticalScale;

            Vector3 from = space.TransformPoint(
                new Vector3(at.X, current.Position.Y + entry.Height * vertical, at.Y));

            // The instance was moved by writing a transform rather than by simulating, so without
            // this the physics scene is still answering for where it used to be.
            Physics.SyncTransforms();

            RaycastHit[] hits = Physics.RaycastAll(
                from,
                Vector3.down,
                Mathf.Infinity,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            bool found = false;
            float nearest = 0f;
            float surface = 0f;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];

                if (hit.transform.IsChildOf(watched.Instance) ||
                    !IsStandingSurface(asset, hit.collider))
                {
                    continue;
                }

                if (found && hit.distance >= nearest)
                {
                    continue;
                }

                found = true;
                nearest = hit.distance;
                surface = space.InverseTransformPoint(hit.point).y;
            }

            return found ? surface + entry.BaseOffset * vertical : current.Position.Y;
        }

        /// <summary>Whether something the ray hit is a surface an object may stand on.</summary>
        /// <remarks>
        /// The way back from a collider to a catalog row is the prefab it is an instance of, rather
        /// than the <see cref="ArenaObjectRef"/> the realiser attaches. A floor slab standing in the
        /// scene beside the map belongs to a building's document and not to the map's, and there is
        /// nothing here to resolve its id against — whereas the art it was made from is in the same
        /// catalog either way, and the art is what the question is about.
        /// </remarks>
        static bool IsStandingSurface(CatalogAsset asset, Collider collider)
        {
            if (collider is TerrainCollider)
            {
                return true;
            }

            GameObject instance = PrefabUtility.GetNearestPrefabInstanceRoot(collider.gameObject);
            if (instance == null)
            {
                return false;
            }

            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource<GameObject>(instance);
            if (prefab == null)
            {
                return false;
            }

            IReadOnlyList<CatalogAsset.Row> rows = asset.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Prefab != prefab)
                {
                    continue;
                }

                string[] tags = rows[i].Tags;
                for (int t = 0; tags != null && t < tags.Length; t++)
                {
                    if (tags[t] == BuildingGenerator.FloorTileTag)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Two coordinates the same to within a tenth of a millimetre.</summary>
        /// <remarks>
        /// So a snap that moved nothing writes nothing. Without it every drop that was already where
        /// it belonged would register a transform write and an override, and holding an object still
        /// would look like editing it.
        /// </remarks>
        static bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-4f;

        /// <summary>How far an edge may pull, in metres: half a cell of the map's own grid.</summary>
        /// <remarks>
        /// Derived rather than picked. The grid is the quantum this map is arranged on, so half a
        /// cell is the distance inside which you have plainly aimed at the neighbour rather than at
        /// the gap beside it — and it cannot pull an object past the cell it was dropped in.
        /// </remarks>
        float SnapReach() => ArenaLayout.Build(_map.BuildParams()).Grid.CellSize * 0.5f;

        static CatalogEntry EntryOf(Catalog catalog, WorldDoc doc, string stableId)
        {
            for (int i = 0; i < doc.GeneratedObjects.Count; i++)
            {
                if (doc.GeneratedObjects[i].StableId == stableId)
                {
                    return catalog.Find(doc.GeneratedObjects[i].LogicalId);
                }
            }

            for (int i = 0; i < doc.Overrides.Count; i++)
            {
                EditOverride edit = doc.Overrides[i];
                if (edit.Op == OverrideOp.Add && edit.TargetId == stableId)
                {
                    return catalog.Find(edit.LogicalId);
                }
            }

            return null;
        }

        void Register(ref bool registered, string name)
        {
            if (registered)
            {
                return;
            }

            registered = true;
            Undo.RegisterCompleteObjectUndo(_map, name);
            Undo.SetCurrentGroupName(name);
        }

        void Commit(WorldDoc doc)
        {
            _map.SetDocument(doc);
            EditorUtility.SetDirty(_map);
            Undo.CollapseUndoOperations(_cleanUndoGroup);
            _cleanUndoGroup = Undo.GetCurrentGroup();
        }
    }
}
