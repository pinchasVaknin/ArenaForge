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

            if (_map == null)
            {
                return;
            }

            WorldDoc doc = _map.Document;
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
                RecordMove(doc, watched.StableId, current);
                watched.Base = current;
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

        static bool Near(CorePose a, CorePose b) =>
            Vec3.DistanceSquared(a.Position, b.Position) <= PositionEpsilon * PositionEpsilon &&
            Mathf.Abs(a.Scale - b.Scale) <= ScaleEpsilon &&
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
