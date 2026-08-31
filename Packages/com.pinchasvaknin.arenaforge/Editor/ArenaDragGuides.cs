using System;
using System.Collections.Generic;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;
using CorePose = ArenaForge.Core.Pose;
using Object = UnityEngine.Object;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Shows where a prefab being dragged over the scene view would land, and whether it may stand
    /// there, while the mouse button is still down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The verdict is worth nothing after the drop.</strong> A person dragging a crate into
    /// an arena is deciding where to put it, and the two things they need while deciding are where
    /// it will actually go — which is not where the cursor is, because an edge in reach pulls it —
    /// and whether the rules will allow it. <see cref="ArenaDropWatch"/> answers both, correctly,
    /// one tick <em>after</em> the decision has been made; by then the answer is a fact about
    /// something that has already happened and the person is looking at the object rather than at
    /// the guides. So this asks the same questions during the drag and draws the same answer.
    /// </para>
    /// <para>
    /// <strong>It computes nothing of its own.</strong> Where the piece would land is
    /// <see cref="ArenaEditCapture.Preview"/> — the same edge snap, grid fallback and drop the
    /// adoption applies on the way in — and whether it may stand there is
    /// <see cref="ArenaPlacementGuides.DrawPending"/>, which goes through
    /// <c>CoverPlacer.TryJudge</c> like every other verdict in the editor. A preview worked out
    /// separately would be a second opinion about where a drop lands, and the one on screen would
    /// be the one that drifted.
    /// </para>
    /// <para>
    /// <strong>Nothing here consumes the event.</strong> The drop itself stays Unity's: it
    /// instantiates the prefab, and <see cref="ArenaDropWatch"/> adopts it once it comes to rest.
    /// Taking <see cref="EventType.DragPerform"/> would mean re-implementing prefab instantiation,
    /// undo and the drop's own parenting to draw a preview — and would put the delivery half of the
    /// feature back on a path no test can drive, which is the whole reason the watch works by
    /// looking rather than by listening.
    /// </para>
    /// <para>
    /// <strong>The pose is remembered between events because the drawing cannot happen on
    /// them.</strong> <c>Handles</c> only draws on <see cref="EventType.Repaint"/>, and a drag
    /// arrives as <see cref="EventType.DragUpdated"/>, so what the update works out is kept and the
    /// repaint that follows draws it. The scene view is asked to repaint on every update, which is
    /// what makes it move with the cursor rather than lag a frame behind it.
    /// </para>
    /// <para>
    /// <strong>One map or none</strong>, on the same terms as <see cref="ArenaDropWatch"/>: a
    /// dragged object belongs to a map, and with two loaded there is no way to tell which. It also
    /// runs whether or not the tool window is open, because it changes nothing — drawing a verdict
    /// is not an edit, and a drag aimed at an arena is a drag aimed at an arena.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    static class ArenaDragGuides
    {
        /// <summary>
        /// Stable id the previewed placement is given, under the namespace user objects live in.
        /// </summary>
        /// <remarks>
        /// It has to have one — <c>CoverPlacer.TryJudge</c> leaves the subject out of the committed
        /// set by id — and it has to be one nothing in a document can be called, or the drag would
        /// hide a real object from its own verdict. Under <c>user/</c> for the reason
        /// <see cref="ArenaEditCapture.UserIdPrefix"/> exists, and never written anywhere: the
        /// object this names is destroyed at the end of the drag it was invented for.
        /// </remarks>
        internal const string PreviewId = ArenaEditCapture.UserIdPrefix + "drag_preview";

        /// <summary>
        /// How far the cursor has to travel before the answer is worked out again, in metres.
        /// </summary>
        /// <remarks>
        /// A millimetre, which is a fact about the arithmetic rather than a throttle: a snap reads
        /// the whole resolved document and fires a physics ray, and the editor re-sends
        /// <see cref="EventType.DragUpdated"/> while the mouse is standing still. Below this the
        /// answer cannot have changed, so the last one is drawn again.
        /// </remarks>
        const float Restep = 1e-3f;

        /// <summary>The map the drag in progress is aimed at, or null when there is not exactly one.</summary>
        static ArenaMap _map;

        /// <summary>Whether the drag in progress has already been asked which map it is over.</summary>
        /// <remarks>
        /// Once per drag rather than once per event. Finding the sole map walks every root of every
        /// loaded scene, and the editor sends <see cref="EventType.DragUpdated"/> continuously while
        /// the button is down — but which scenes are open cannot change in the middle of a drag, so
        /// the answer cannot either.
        /// </remarks>
        static bool _looked;

        /// <summary>What the last <see cref="EventType.DragUpdated"/> worked out, for the repaint.</summary>
        static PlacedObject _pending;

        /// <summary>What that answer was worked out for, so a still cursor is not asked twice.</summary>
        static string _lastId;
        static Vec3 _lastAt;

        static ArenaDragGuides()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        static void OnSceneGui(SceneView view)
        {
            Event current = Event.current;

            switch (current.type)
            {
                case EventType.DragUpdated:
                    Track(current);

                    // Repainted from here rather than waiting for the next idle one, or the guides
                    // would trail the cursor by however long the editor took to decide it was worth
                    // redrawing. The event is left alone: Unity's own drag handling has to see it.
                    if (_pending != null && view != null)
                    {
                        view.Repaint();
                    }

                    break;

                case EventType.Repaint:
                    if (_pending != null)
                    {
                        ArenaPlacementGuides.DrawPending(_map, _pending);
                    }

                    break;

                // The drag is over either way, and which way it went is not this one's business:
                // a dropped prefab is ArenaDropWatch's, and a cancelled one is nobody's. MouseDown
                // is the backstop: no drag is in progress while somebody is clicking, so a drag that
                // somehow ended without saying so cannot leave its guides on screen for long.
                case EventType.DragPerform:
                case EventType.DragExited:
                case EventType.MouseDown:
                    Forget();
                    break;
            }
        }

        /// <summary>Works out what the drag under the cursor would put down, and where.</summary>
        static void Track(Event current)
        {
            if (!_looked)
            {
                _map = ArenaDropWatch.SoleMap();
                _looked = true;
            }

            string logicalId = LogicalIdOf(_map, DragAndDrop.objectReferences,
                DragAndDrop.GetGenericData(ArenaForgeWindow.DragKey));

            if (_map == null || logicalId == null ||
                !TryGroundPoint(_map, current.mousePosition, out Vec3 at))
            {
                _pending = null;
                _lastId = null;
                return;
            }

            if (_pending != null && _lastId == logicalId && Near(_lastAt, at))
            {
                return;
            }

            _lastId = logicalId;
            _lastAt = at;
            _pending = Pending(_map, logicalId, at);
        }

        static bool Near(Vec3 a, Vec3 b) =>
            Mathf.Abs(a.X - b.X) < Restep && Mathf.Abs(a.Z - b.Z) < Restep;

        static void Forget()
        {
            _map = null;
            _looked = false;
            _pending = null;
            _lastId = null;
        }

        /// <summary>
        /// The placement a drag of <paramref name="logicalId"/> released at <paramref name="at"/>
        /// would make, snapped exactly as the drop will snap it. Null if the map cannot answer.
        /// </summary>
        /// <remarks>
        /// Internal so the suite can ask the question without a scene view, which is the only half
        /// of this that a headless run can drive — see
        /// <c>EditorWorkflowTests</c>. The drawing is a repaint and the repaint needs an editor
        /// somebody is looking at.
        /// </remarks>
        internal static PlacedObject Pending(ArenaMap map, string logicalId, Vec3 at)
        {
            if (map == null || !map.HasDocument || string.IsNullOrEmpty(logicalId))
            {
                return null;
            }

            CatalogAsset asset = map.Realizer != null ? map.Realizer.Catalog : null;
            if (asset == null)
            {
                return null;
            }

            WorldDoc doc;
            CatalogEntry entry;
            try
            {
                doc = map.Document;
                entry = asset.ToCatalog().Find(logicalId);
            }
            catch (Exception error) when (error is ArgumentException ||
                                          error is InvalidOperationException ||
                                          error is UnsupportedSchemaVersionException)
            {
                return null;
            }

            if (entry == null)
            {
                return null;
            }

            CorePose pose = new ArenaEditCapture(map).Preview(doc, entry, CorePose.At(at));

            return new PlacedObject(
                PreviewId, logicalId, pose, Tags(entry), new Dictionary<string, string>());
        }

        /// <summary>
        /// Which catalog row a drag is carrying: the tool window's own, or a prefab from the
        /// Project window bound to one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two sources because there are two ways to start this drag and they are the same gesture
        /// to the person making it. The catalog panel drags a logical id outright — see
        /// <c>ArenaForgeWindow</c> — and the Project window drags the prefab, which is bound to a
        /// row through the catalog asset. Anything else is a drag that has nothing to do with a
        /// map, and draws nothing.
        /// </para>
        /// <para>
        /// The <em>nearest prefab asset root</em> rather than the object dragged, because dropping
        /// a prefab is dropping the whole prefab however deep into it the Project window's selection
        /// reached.
        /// </para>
        /// </remarks>
        internal static string LogicalIdOf(ArenaMap map, Object[] dragged, object generic)
        {
            if (generic is string carried && !string.IsNullOrEmpty(carried))
            {
                return carried;
            }

            CatalogAsset asset = map != null && map.Realizer != null ? map.Realizer.Catalog : null;
            if (asset == null || dragged == null)
            {
                return null;
            }

            for (int i = 0; i < dragged.Length; i++)
            {
                if (!(dragged[i] is GameObject candidate) ||
                    !PrefabUtility.IsPartOfPrefabAsset(candidate))
                {
                    continue;
                }

                GameObject prefab = candidate.transform.root.gameObject;
                IReadOnlyList<CatalogAsset.Row> rows = asset.Rows;

                for (int r = 0; r < rows.Count; r++)
                {
                    if (rows[r].Prefab == prefab || rows[r].Prefab == candidate)
                    {
                        return rows[r].LogicalId;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Where a scene-view mouse position lands on the map's ground plane, in the realisation
        /// root's local space — the space document poses are expressed in.
        /// </summary>
        /// <remarks>
        /// The plane and not the terrain, because the height is settled afterwards and by something
        /// that knows more: <see cref="ArenaEditCapture.Preview"/> drops the piece onto whatever
        /// surface is under where it ends up, which is not the same place the cursor is over once an
        /// edge has pulled it sideways.
        /// </remarks>
        static bool TryGroundPoint(ArenaMap map, Vector2 mousePosition, out Vec3 local)
        {
            local = default;

            Transform root = map.Realizer != null ? map.Realizer.Root : null;
            if (root == null)
            {
                return false;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            var ground = new Plane(root.up, root.position);

            if (!ground.Raycast(ray, out float distance))
            {
                return false;
            }

            local = CoreConvert.ToCore(root.InverseTransformPoint(ray.GetPoint(distance)));
            return true;
        }

        static string[] Tags(CatalogEntry entry)
        {
            var tags = new string[entry.Tags.Count];
            for (int i = 0; i < tags.Length; i++)
            {
                tags[i] = entry.Tags[i];
            }

            return tags;
        }
    }
}
