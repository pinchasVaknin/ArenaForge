using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Draws a map's road network — carriageways and junctions — into the scene view.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A renderer for a Core result and nothing else, on the same terms
    /// <see cref="ArenaLayoutGuides"/> is. What it draws is <see cref="ArenaMap.Roads"/>, the
    /// network kept from the last time the map was realised; if that is null it draws nothing.
    /// <strong>It never builds one.</strong> A network is a routing sweep over the whole playfield,
    /// and a repaint runs per scene view and per camera in it — putting the sweep behind this would
    /// be paying for the roads to be found again several times a frame while nothing about them had
    /// changed.
    /// </para>
    /// <para>
    /// Drawn in the map's own space rather than the realisation root's, for the reason the guides
    /// are: they are the same space, and asking for the root would create it, which is not
    /// something a repaint should do to a map that has never been generated.
    /// </para>
    /// <para>
    /// <strong>The line is the polyline the router found, at the width it was laid at.</strong> A
    /// carriageway is a strip and not a wire, so an artery and a branch are drawn at thicknesses
    /// that stand in the same ratio as the widths they were laid at, through
    /// <see cref="Handles.DrawAAPolyLine(float, Vector3[])"/> — which is the one primitive here
    /// that takes a thickness in screen pixels, so a road stays legible zoomed out and does not
    /// swell into a slab zoomed in. Drawing the actual quad would be drawing the road surface,
    /// which the terrain's splat map already does far better than a handle can.
    /// </para>
    /// <para>
    /// <strong>A junction is drawn at the radius it actually has.</strong>
    /// <see cref="RoadNetwork.JunctionRadius"/> is half the widest carriageway on the map and is
    /// the disc the kerbing is cut against, so a fixed-size sphere would be a picture of something
    /// that is not on the map — it would read as the same junction on a sixty-metre arena and on a
    /// four-hundred-metre town, where the second one's are twice the size. The three kinds are
    /// coloured apart rather than sized apart, because what makes a portal a portal is what it is
    /// attached to and not how big it is.
    /// </para>
    /// </remarks>
    static class ArenaRoadGuides
    {
        // Above the layout guides, so a road drawn over a lane band reads as being on top of it
        // rather than fighting with it. The junctions sit above the carriageways for the same
        // reason: a junction is where roads meet, so it is drawn over the roads it joins.
        const float CarriagewayHeight = 0.10f;
        const float JunctionHeight = 0.11f;

        /// <summary>How thick an artery is drawn, in screen pixels.</summary>
        /// <remarks>
        /// Six against the branches' three, which is the ratio the default map's own widths stand
        /// in — a four-metre artery and a two-metre path. Screen pixels rather than metres because
        /// that is what <see cref="Handles.DrawAAPolyLine(float, Vector3[])"/> takes, and it is the
        /// right unit for this: the network is being read as a diagram of where the roads go, and a
        /// diagram that thinned to nothing as you pulled back would stop being one.
        /// </remarks>
        const float ArteryThickness = 6f;

        /// <summary>How thick a branch is drawn, in screen pixels.</summary>
        const float BranchThickness = 3f;

        /// <summary>How many segments a junction disc is drawn from.</summary>
        /// <remarks>
        /// Twenty-four, which is a degree and a half of error against a true circle at the radius
        /// these are drawn at and costs nothing. It is also <see cref="YawStep.Count"/>, and that
        /// is a coincidence rather than a reason.
        /// </remarks>
        const int DiscSegments = 24;

        static readonly Color ArteryLine = new Color(1f, 0.45f, 0.15f, 0.95f);
        static readonly Color BranchLine = new Color(1f, 0.65f, 0.30f, 0.8f);

        // The three junction kinds, told apart by colour rather than by size. A portal is a door,
        // an attachment is where a branch meets its trunk, and a terminal is a spawn.
        static readonly Color PortalDisc = new Color(0.35f, 0.85f, 1f, 0.85f);
        static readonly Color AttachmentDisc = new Color(1f, 0.8f, 0.35f, 0.75f);
        static readonly Color TerminalDisc = new Color(0.35f, 0.9f, 0.5f, 0.85f);

        static readonly Vector3[] Disc = new Vector3[DiscSegments + 1];

        static Vector3[] _line = new Vector3[64];

        /// <summary>
        /// Draws the network a map is holding. Does nothing if it is not holding one, which is the
        /// case for a map with no roads and for one that has not been realised since the assembly
        /// was loaded.
        /// </summary>
        public static void Draw(ArenaMap map)
        {
            if (map == null)
            {
                return;
            }

            RoadNetwork roads = map.Roads;
            if (roads == null || roads.IsEmpty)
            {
                return;
            }

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            CompareFunction previousZTest = Handles.zTest;

            Handles.matrix = map.transform.localToWorldMatrix;
            Handles.zTest = CompareFunction.LessEqual;

            for (int i = 0; i < roads.Segments.Count; i++)
            {
                DrawCarriageway(roads.Segments[i]);
            }

            for (int i = 0; i < roads.Junctions.Count; i++)
            {
                DrawJunction(roads.Junctions[i], roads.JunctionRadius);
            }

            Handles.zTest = previousZTest;
            Handles.color = previousColor;
            Handles.matrix = previousMatrix;
        }

        // One polyline per segment rather than one call per pair of points: DrawAAPolyLine joins
        // its own corners, and a segment drawn as a run of separate lines has a notch at every bend
        // where the two ends of the join meet at an angle.
        static void DrawCarriageway(RoadSegment segment)
        {
            int count = segment.Points.Count;
            if (count < 2)
            {
                return;
            }

            if (_line.Length < count)
            {
                _line = new Vector3[Mathf.NextPowerOfTwo(count)];
            }

            for (int i = 0; i < count; i++)
            {
                Vec2 at = segment.Points[i];
                _line[i] = new Vector3(at.X, CarriagewayHeight, at.Y);
            }

            bool artery = segment.Class == RoadClass.Artery;
            Handles.color = artery ? ArteryLine : BranchLine;

            // The array is longer than the polyline whenever it has been grown for a longer one, so
            // the count is passed rather than the array — the overload that takes only an array
            // would draw the stale tail of the last segment as well.
            Handles.DrawAAPolyLine(
                artery ? ArteryThickness : BranchThickness, count, _line);
        }

        static void DrawJunction(RoadJunction junction, float radius)
        {
            Handles.color = Colour(junction.Kind);

            for (int i = 0; i <= DiscSegments; i++)
            {
                float angle = i * (2f * Mathf.PI / DiscSegments);
                Disc[i] = new Vector3(
                    junction.Position.X + (Mathf.Cos(angle) * radius),
                    JunctionHeight,
                    junction.Position.Y + (Mathf.Sin(angle) * radius));
            }

            Handles.DrawAAPolyLine(2f, Disc);
        }

        static Color Colour(RoadJunctionKind kind)
        {
            switch (kind)
            {
                case RoadJunctionKind.Portal:
                    return PortalDisc;
                case RoadJunctionKind.Terminal:
                    return TerminalDisc;
                default:
                    return AttachmentDisc;
            }
        }
    }
}
