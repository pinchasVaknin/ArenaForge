using System;
using System.Collections.Generic;

namespace ArenaForge.Core
{
    /// <summary>
    /// Laying art end to end along the sides of a rectangle: the four faces of one, and the cursor
    /// that walks a face seating pieces against it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Nothing here snaps to a grid</strong>, which is what separates a run from every other
    /// kind of placement in the tool. What makes a hedge look like a hedge, and a fence like a
    /// fence, is that each piece starts exactly where the last one stopped; two independently
    /// sampled positions are never flush, and a grid a metre across rounds the joins off into a row
    /// of separate objects. So a run is laid out from the side it belongs to, exactly as
    /// <see cref="BuildingGenerator"/> tiles a wall run, and the constraint set is what says whether
    /// each piece is legal.
    /// </para>
    /// <para>
    /// It lives beside the two stages that walk one rather than inside either, because they walk it
    /// round different things for different reasons — <see cref="ExteriorPlacer"/> round a building
    /// it is dressing, <see cref="PerimeterFence"/> round the playfield it is closing — and the
    /// cursor is the only part they share. Everything either stage decides for itself is a parameter
    /// here: which art, which gaps, which rules, where a piece sits across the run, and what to do
    /// with one that was accepted. Nothing is committed by this type; the accepting caller owns
    /// that, because only it knows what the piece is called and what the piece belongs to.
    /// </para>
    /// </remarks>
    public static class WallRun
    {
        /// <summary>Faces a rectangle has, which is how many runs go round one.</summary>
        public const int FaceCount = 4;

        /// <summary>Different catalog entries tried in one slot of a run before it is left empty.</summary>
        const int EntriesPerSlot = 4;

        /// <summary>
        /// How much clear ground two runs of a ring leave between them where they hand a corner
        /// over, in metres.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two runs meeting at a corner are meant to touch exactly, and two rectangles that merely
        /// touch do not overlap — but the two edges are arrived at by different arithmetic. One is a
        /// rectangle's own bound plus a thickness; the other is a cursor position turned into a
        /// pivot and back into a bound, three additions later. In single precision over a
        /// sixty-metre map those land about a hundredth of a millimetre apart, and which side of
        /// exact they land on decides whether the run that turns the corner keeps its first panel or
        /// has it refused for overlapping — so a corner closed on one seed is open on the next for
        /// no reason anybody can see.
        /// </para>
        /// <para>
        /// A millimetre is a hundred times the error and a hundredth of the thinnest art this tool
        /// places. Nothing can be seen through it and nothing can stand in it; what it buys is that
        /// the handover is decided by the geometry rather than by the last bit of a float.
        /// </para>
        /// </remarks>
        public const float CornerSlack = 1e-3f;

        /// <summary>How much of a run may be left uncovered and still count as reaching the end.</summary>
        /// <remarks>
        /// A cursor that advanced by four floats' worth of panel lengths does not land exactly on
        /// the end of the run even when the panels divide it exactly, and a tenth of a millimetre of
        /// bare ground is not a gap anybody can see or stand in. Sized to be smaller than any art and
        /// larger than the arithmetic.
        /// </remarks>
        /// <remarks>
        /// Public because a caller sizing a run to its own art has to stay under it: ground left
        /// bare by less than this is ground <see cref="CloseGaps"/> will not try to fill, and a
        /// caller that leaves more gets a whole panel laid across a sliver. See
        /// <c>SpawnEnclosure.FitSlack</c>.
        /// </remarks>
        public const float EndTolerance = 1e-4f;

        /// <summary>Where a piece sits across a run, given the face and the art's rotated footprint.</summary>
        public delegate float Seat(Face face, Rect2 local);

        /// <summary>
        /// What a caller does with a piece the rules accepted: commit it, name it, and file it in a
        /// document. <paramref name="index"/> counts pieces stood up across the whole run, so a
        /// caller numbering stable ids gets one sequence round all four faces.
        /// </summary>
        public delegate void Accept(Placement candidate, int index);

        /// <summary>Flush against the outside of the face — what hugging a wall means.</summary>
        public static readonly Seat Flush = (face, local) => face.Against(local, 0f);

        /// <summary>Straddling the face's own line — what describing a line means.</summary>
        public static readonly Seat Astride = (face, local) => face.Astride(local);

        /// <summary>What a run with no gaps decided in advance is tiled against.</summary>
        public static readonly Rect2[] NoGates = Array.Empty<Rect2>();

        /// <summary>The four faces of a rectangle, starting at the low Z one and going clockwise.</summary>
        public static List<Face> Faces(Rect2 rect) => new List<Face>(FaceCount)
        {
            new Face(true, -1, rect.MinZ, rect.MinX, rect.MaxX),
            new Face(false, 1, rect.MaxX, rect.MinZ, rect.MaxZ),
            new Face(true, 1, rect.MaxZ, rect.MinX, rect.MaxX),
            new Face(false, -1, rect.MinX, rect.MinZ, rect.MaxZ),
        };

        /// <summary>
        /// The entries a run can be tiled from: the ones that measure something on both axes.
        /// </summary>
        /// <remarks>
        /// A catalog row's footprint stays at its default when nothing about a prefab could be
        /// measured, and a piece with no length is a cursor that never advances. Filtered here
        /// rather than guarded against in the walk, so the walk can say what it means.
        ///
        /// It is a weaker guard than it looks. A default footprint is a one-metre <em>square</em>,
        /// which measures something on both axes and passes this — and then has no long axis for
        /// <see cref="QuarterTurnsAlong"/> to lay along the run, so a run tiled from it is a row of
        /// unit cubes turned whichever way the tie fell. Nothing in a run can tell that from art
        /// that really is square; what can is the sync, and it now measures the meshes when there
        /// are no colliders rather than leaving the row at its defaults.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
        public static IReadOnlyList<CatalogEntry> Tileable(IReadOnlyList<CatalogEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var tileable = new List<CatalogEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Footprint.Width > 0f && entries[i].Footprint.Depth > 0f)
                {
                    tileable.Add(entries[i]);
                }
            }

            return tileable;
        }

        /// <summary>The shortest run any one of these entries covers, laid along its long axis.</summary>
        public static float ShortestRun(IReadOnlyList<CatalogEntry> entries)
        {
            float shortest = float.MaxValue;
            for (int i = 0; i < entries.Count; i++)
            {
                shortest = MathF.Min(
                    shortest, MathF.Max(entries[i].Footprint.Width, entries[i].Footprint.Depth));
            }

            return shortest;
        }

        /// <summary>How thick the thickest of these entries is across its own run.</summary>
        public static float ThickestSegment(IReadOnlyList<CatalogEntry> entries)
        {
            float thickest = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                thickest = MathF.Max(
                    thickest, MathF.Min(entries[i].Footprint.Width, entries[i].Footprint.Depth));
            }

            return thickest;
        }

        /// <summary>How far to turn a piece so its long axis lies along the run.</summary>
        public static int QuarterTurnsAlong(CatalogEntry entry, bool alongX) =>
            (entry.Footprint.Width >= entry.Footprint.Depth) == alongX ? 0 : 1;

        /// <summary>
        /// Walks a face laying pieces end to end, and returns how many it stood up.
        /// </summary>
        /// <param name="face">The side to walk, already shortened at both ends if it shares corners.</param>
        /// <param name="entries">The art to draw from, weighted.</param>
        /// <param name="gates">Rectangles no piece may stand in, decided before the walk began.</param>
        /// <param name="constraints">The rules a piece has to satisfy, over everything already down.</param>
        /// <param name="stats">The tally rejections are recorded into.</param>
        /// <param name="seat">
        /// Where a piece sits across the run: <see cref="Flush"/> outside the face for art that hugs
        /// a wall, <see cref="Astride"/> for art that describes a line.
        /// </param>
        /// <param name="first">What to number the first piece this face stands up.</param>
        /// <param name="accept">What to do with a piece the rules accepted.</param>
        /// <param name="stream">The caller's draw stream, advanced in place.</param>
        /// <remarks>
        /// <para>
        /// The cursor advances by the piece's own length whether or not the piece was stood up, so a
        /// rejected slot is a gap in the run rather than a shuffle of everything after it. That is
        /// what keeps a run reading as one line with holes in it, which is what a hedge round a
        /// doorway looks like.
        /// </para>
        /// <para>
        /// <strong>Then the run goes back and closes what it left.</strong> Pieces are indivisible
        /// and nothing here rescales art, so a walk from the low end stops short of the far end by a
        /// remainder — up to a whole panel of bare ground, which round a map is four gaps in the
        /// boundary and round a yard is four open corners. A slot the rules refused leaves the same
        /// bare ground in the middle of the run, and the longer the art the worse it is: the common
        /// stone pack tiles a sixty-metre map two panels to a side, so one refusal there is half an
        /// edge of the level missing. Both are the same fault — ground nothing stood on because of
        /// where the cursor happened to land — so <see cref="CloseGaps"/> takes them together, and
        /// tries one piece seated <em>backwards</em> from the high end of each stretch the walk left
        /// bare, then one seated forwards from its low end. A fence post standing in front of
        /// another fence post is the cheapest thing a run can be short of; a hole a player walks
        /// through is the most expensive.
        /// </para>
        /// <para>
        /// Those closing pieces are judged against everything on the map <em>except this run</em> —
        /// see <see cref="ConstraintSet.Evaluate(Placement, int)"/>. Overlapping the run's own
        /// pieces is the whole point of them and overlapping a building is still not allowed, so a
        /// stretch that is bare because something is standing in it stays bare. That is the one gap
        /// this leaves and it is the one that is meant to be there.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">An argument other than <paramref name="face"/> is null.</exception>
        public static int Tile(
            Face face,
            IReadOnlyList<CatalogEntry> entries,
            IReadOnlyList<Rect2> gates,
            ConstraintSet constraints,
            PlacementStats stats,
            Seat seat,
            int first,
            Accept accept,
            ref Rng stream)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (gates == null)
            {
                throw new ArgumentNullException(nameof(gates));
            }

            if (constraints == null)
            {
                throw new ArgumentNullException(nameof(constraints));
            }

            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            if (seat == null)
            {
                throw new ArgumentNullException(nameof(seat));
            }

            if (accept == null)
            {
                throw new ArgumentNullException(nameof(accept));
            }

            // A run holds no more pieces than it does of the shortest piece on offer, which is what
            // bounds the walk without a magic number in it: every step advances the cursor by at
            // least this much.
            float shortest = ShortestRun(entries);
            if (!(shortest > 0f) || face.Length < shortest)
            {
                return 0;
            }

            // Read before a piece of this run is committed, so the closing pieces below can be told
            // to ignore everything the run itself stood up.
            int before = constraints.Committed.Count;

            int slots = (int)MathF.Floor(face.Length / shortest) + 1;
            float cursor = face.Min;
            int stood = 0;

            // Where each piece the walk stood up landed, in walk order. The cursor only ever
            // advances, so this comes out sorted and the stretches between its entries are exactly
            // the ground the run left bare.
            var laid = new List<Span>(slots + 1);

            for (int slot = 0; slot < slots && cursor + shortest <= face.Max; slot++)
            {
                if (TryFill(
                        face, ref cursor, entries, gates, constraints, stats, seat, first + stood,
                        accept, laid, before, ref stream))
                {
                    stood++;
                }
            }

            if (stood > 0)
            {
                stood += CloseGaps(
                    face, entries, gates, constraints, stats, seat, first + stood, accept, before,
                    laid);
            }

            return stood;
        }

        /// <summary>
        /// Fills in every stretch of the run the walk left bare, and returns how many pieces that
        /// took.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The shortest piece on offer, because it is the one that doubles up on the least of the
        /// run — and no draw is taken for any of this, so a stream reads the same whether a face
        /// divided evenly or not. Ties break by catalog order, as <see cref="Shortest"/> does and
        /// for the same reason.
        /// </para>
        /// <para>
        /// <strong>Backwards from the high end of the stretch, then forwards from its low end.</strong>
        /// A stretch is not always one piece wide and not always clear. It is wider than a piece
        /// whenever the walk stopped early — a slot refused for overlapping its own predecessor by
        /// a rounding error costs the rest of the face, because the cursor never gets going again —
        /// so the backward pass keeps laying pieces down the stretch until it is closed or one is
        /// refused. And a stretch with something standing part way along it, a building's corner
        /// poking into the line, refuses everything seated from the end nearer the obstruction; the
        /// forward pass then fills the far side of it. A stretch that refuses both ends at once is
        /// one nothing could have stood in, and it stays open.
        /// </para>
        /// <para>
        /// Only reached when the run stood something up. A face that is one long gate, or one the
        /// rules refused outright, is meant to be empty, and closing it would put a single panel in
        /// the middle of nowhere.
        /// </para>
        /// </remarks>
        static int CloseGaps(
            Face face,
            IReadOnlyList<CatalogEntry> entries,
            IReadOnlyList<Rect2> gates,
            ConstraintSet constraints,
            PlacementStats stats,
            Seat seat,
            int index,
            Accept accept,
            int before,
            List<Span> laid)
        {
            // The piece a stretch shorter than any of the art falls back to, read once because it
            // is a fact about the palette rather than about a stretch.
            CatalogEntry shortest = Shortest(entries);

            int closed = 0;
            float covered = face.Min;

            for (int i = 0; i <= laid.Count; i++)
            {
                // One stretch per step: the ground between what is covered so far and the next
                // piece the walk laid, and after the last of them the ground up to the end of the
                // run. That last stretch is the remainder every tiled run ends with.
                float upTo = i < laid.Count ? laid[i].From : face.Max;
                closed += FillStretch(
                    face, entries, shortest, covered, upTo, gates, constraints,
                    stats, seat, index + closed, accept, before);

                if (i < laid.Count)
                {
                    covered = MathF.Max(covered, laid[i].To);
                }
            }

            return closed;
        }

        /// <summary>
        /// Lays pieces into one bare stretch of a run, and returns how many stood up.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two passes over the same ground, and each stops at the first refusal rather than
        /// stepping over it: a refusal means something is standing there, and the ground beyond it
        /// is what the other pass is for. The step is a whole piece each time, so each pass runs at
        /// most as many times as the stretch holds pieces and the walk terminates on the geometry
        /// rather than on a counter.
        /// </para>
        /// <para>
        /// <strong>The piece is chosen per step rather than once for the pass</strong>, because
        /// what is left of a stretch shrinks as it is filled — see
        /// <see cref="LongestThatFits"/>. A palette holding one length behaves exactly as it did
        /// before that choice existed: the longest that fits is the only one there is.
        /// </para>
        /// </remarks>
        static int FillStretch(
            Face face,
            IReadOnlyList<CatalogEntry> entries,
            CatalogEntry shortest,
            float from,
            float to,
            IReadOnlyList<Rect2> gates,
            ConstraintSet constraints,
            PlacementStats stats,
            Seat seat,
            int index,
            Accept accept,
            int before)
        {
            int closed = 0;

            // Backwards, so the piece that closes the far end of the stretch lands exactly on it
            // and the doubling up happens at the near end, over ground the run already covers.
            float high = to;
            while (high > from + EndTolerance)
            {
                CatalogEntry entry = LongestThatFits(face, entries, high - from, shortest);
                int steps = face.Steps(entry);
                Rect2 local = face.Local(entry);
                float length = face.AlongSize(local);

                if (!TrySeat(
                        face, entry, steps, local, MathF.Max(face.Min, high - length), gates,
                        constraints, stats, seat, index + closed, accept, before))
                {
                    break;
                }

                closed++;
                high -= length;
            }

            // Forwards over whatever is left of the stretch, which is only ever reached when the
            // backward pass ran into something.
            float low = from;
            while (high > low + EndTolerance)
            {
                CatalogEntry entry = LongestThatFits(face, entries, high - low, shortest);
                int steps = face.Steps(entry);
                Rect2 local = face.Local(entry);
                float length = face.AlongSize(local);

                if (!TrySeat(
                        face, entry, steps, local, MathF.Min(low, face.Max - length), gates,
                        constraints, stats, seat, index + closed, accept, before))
                {
                    break;
                }

                closed++;
                low += length;
            }

            return closed;
        }

        /// <summary>
        /// Stands one closing piece with its low edge at <paramref name="at"/> along the run, and
        /// returns whether it stood up.
        /// </summary>
        /// <remarks>
        /// Held inside the run at both ends, because a piece seated backwards from a stretch
        /// shorter than itself would otherwise reach back past <see cref="Face.Min"/> and one
        /// seated forwards from a stretch at the end of the run would reach past
        /// <see cref="Face.Max"/> — and a boundary panel sticking out of the corner of the map is a
        /// different defect from the one being fixed.
        /// </remarks>
        static bool TrySeat(
            Face face,
            CatalogEntry entry,
            int steps,
            Rect2 local,
            float at,
            IReadOnlyList<Rect2> gates,
            ConstraintSet constraints,
            PlacementStats stats,
            Seat seat,
            int index,
            Accept accept,
            int before)
        {
            float length = face.AlongSize(local);
            if (at < face.Min - EndTolerance || at + length > face.Max + EndTolerance)
            {
                return false;
            }

            Placement candidate = Placement.AtYawStep(
                entry, face.Point(face.From(local, at), seat(face, local)), steps);

            if (StandsInAGap(gates, candidate.Footprint))
            {
                return false;
            }

            ConstraintResult result = constraints.Evaluate(candidate, before);
            stats.Record(result);

            if (!result.IsOk)
            {
                return false;
            }

            accept(candidate, index);
            return true;
        }

        /// <summary>
        /// Fills one slot of a run, advancing the cursor past whatever art was drawn for it. Returns
        /// whether a piece was stood up.
        /// </summary>
        /// <remarks>
        /// The last resort is the shortest piece on offer rather than the end of the run, and that
        /// is what makes "a run only breaks where a rule broke it" true rather than nearly true. A
        /// slot is opened only when the shortest piece fits in it — see <see cref="Tile"/> — so a
        /// slot where every draw overshot is a slot the draws were unlucky in, not one the art
        /// cannot fill. Ending the run there would leave a hole up to a whole panel wide at the end
        /// of every face, and a hole a player can read as "the fence ran out" is worse than one they
        /// can read as "the door is there".
        /// </remarks>
        static bool TryFill(
            Face face,
            ref float cursor,
            IReadOnlyList<CatalogEntry> entries,
            IReadOnlyList<Rect2> gates,
            ConstraintSet constraints,
            PlacementStats stats,
            Seat seat,
            int index,
            Accept accept,
            List<Span> laid,
            int before,
            ref Rng stream)
        {
            for (int attempt = 0; attempt < EntriesPerSlot; attempt++)
            {
                CatalogEntry entry = stream.WeightedPick(entries, e => e.Weight);
                Fill fill = TryStand(
                    face, ref cursor, entry, gates, constraints, stats, seat, index, accept, laid,
                    before);

                if (fill != Fill.TooLong)
                {
                    return fill == Fill.Stood;
                }
            }

            // Every draw overshot. The shortest piece is the one the slot was opened for, and it
            // takes no draw, so the stream reads the same whether or not this line was reached.
            return TryStand(
                    face, ref cursor, Shortest(entries), gates, constraints, stats, seat, index,
                    accept, laid, before)
                == Fill.Stood;
        }

        /// <summary>
        /// Proposes one piece in the slot the cursor is at. A piece too long for what is left of the
        /// run leaves the cursor where it was and the slot open; anything else consumes the slot.
        /// </summary>
        static Fill TryStand(
            Face face,
            ref float cursor,
            CatalogEntry entry,
            IReadOnlyList<Rect2> gates,
            ConstraintSet constraints,
            PlacementStats stats,
            Seat seat,
            int index,
            Accept accept,
            List<Span> laid,
            int before)
        {
            int steps = face.Steps(entry);
            Rect2 local = face.Local(entry);
            float length = face.AlongSize(local);

            if (cursor + length > face.Max)
            {
                return Fill.TooLong;
            }

            Placement candidate = Placement.AtYawStep(
                entry, face.Point(face.From(local, cursor), seat(face, local)), steps);

            float from = cursor;
            cursor += length;

            if (StandsInAGap(gates, candidate.Footprint))
            {
                // A gap that was decided before the tiling began. No rule was asked, so this is not
                // a rejection and the statistics do not report one.
                return Fill.Skipped;
            }

            // An oblique run doubles up on itself by construction: its footprints are the boxes
            // round turned art, and two of those laid end to end always overlap. See Face.Along.
            ConstraintResult result = face.IsOblique
                ? constraints.Evaluate(candidate, before)
                : constraints.Evaluate(candidate);

            stats.Record(result);

            if (!result.IsOk)
            {
                return Fill.Skipped;
            }

            accept(candidate, index);
            laid.Add(new Span(from, cursor));
            return Fill.Stood;
        }

        /// <summary>A stretch of a run one piece stands on, in the run's own along coordinate.</summary>
        readonly struct Span
        {
            public Span(float from, float to)
            {
                From = from;
                To = to;
            }

            /// <summary>Where the piece's low edge sits along the run.</summary>
            public float From { get; }

            /// <summary>Where its high edge sits.</summary>
            public float To { get; }
        }

        /// <summary>What became of one piece proposed in a slot of a run.</summary>
        enum Fill
        {
            /// <summary>Too long for what is left of the run. The cursor did not move.</summary>
            TooLong,

            /// <summary>The slot was consumed and left empty: it fell in a gap, or a rule refused it.</summary>
            Skipped,

            /// <summary>Stood up and handed to the caller.</summary>
            Stood,
        }

        /// <summary>
        /// The entry that covers the least run, which is the one a slot was opened for.
        /// </summary>
        /// <remarks>
        /// Ties break by catalog order rather than by weight, so the piece a tail is closed with is
        /// a function of the catalog alone — this is reached after the draws are spent, and a
        /// tie-break that consulted the stream would make the run depend on how many of them missed.
        /// </remarks>
        static CatalogEntry Shortest(IReadOnlyList<CatalogEntry> entries)
        {
            CatalogEntry shortest = entries[0];
            float length = MathF.Max(shortest.Footprint.Width, shortest.Footprint.Depth);

            for (int i = 1; i < entries.Count; i++)
            {
                float candidate = MathF.Max(entries[i].Footprint.Width, entries[i].Footprint.Depth);
                if (candidate < length)
                {
                    shortest = entries[i];
                    length = candidate;
                }
            }

            return shortest;
        }

        /// <summary>
        /// The entry covering the most run that still fits in <paramref name="stretch"/>, or
        /// <paramref name="fallback"/> when none of them does.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What length variants in a catalog are for. A folder holding a one-metre, a two-metre and
        /// a five-metre panel closes a five-metre stretch with one piece where a pass that knew
        /// only the shortest laid five of them, and the remainder no piece divides is then the only
        /// place anything is still doubled over its neighbour.
        /// </para>
        /// <para>
        /// <strong>This is not the mixing <see cref="PerimeterFence"/> rejected.</strong> That
        /// spliced a garden panel into the wall round the world — one folder's art closing another
        /// folder's run, which reads as a gap somebody patched. This chooses inside the run's own
        /// palette, so every piece is the art the stage was pointed at and a longer one is the same
        /// wall in a longer piece.
        /// </para>
        /// <para>
        /// Ties break by catalog order and no draw is taken, on the same terms as
        /// <see cref="Shortest"/>: this is reached after the draws are spent, and a tie-break that
        /// consulted the stream would make the run depend on how many of them missed.
        /// </para>
        /// <para>
        /// Measured through the face rather than off the footprint, so the length compared is the
        /// one the caller then steps by. <see cref="EndTolerance"/> of slack, so a piece exactly as
        /// long as the stretch it is being fitted to counts as fitting.
        /// </para>
        /// </remarks>
        static CatalogEntry LongestThatFits(
            Face face, IReadOnlyList<CatalogEntry> entries, float stretch, CatalogEntry fallback)
        {
            CatalogEntry longest = null;
            float length = 0f;

            for (int i = 0; i < entries.Count; i++)
            {
                float candidate = face.AlongSize(face.Local(entries[i]));

                if (candidate <= stretch + EndTolerance && candidate > length)
                {
                    longest = entries[i];
                    length = candidate;
                }
            }

            return longest ?? fallback;
        }

        /// <summary>True if the footprint stands in any of the gaps.</summary>
        static bool StandsInAGap(IReadOnlyList<Rect2> gates, Rect2 footprint)
        {
            for (int i = 0; i < gates.Count; i++)
            {
                if (footprint.Overlaps(gates[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One side of a rectangle, seen as a run to lay art along: which way it runs, which way is
        /// out of the rectangle, where the side sits, and how far it goes.
        /// </summary>
        /// <remarks>
        /// The four faces of a footprint differ only in an axis and a sign, and writing each pass
        /// out twice — once for the runs along X and once for those along Z — is how a placement
        /// stage acquires two nearly identical halves that then drift apart. Naming the run's own
        /// axes "along" and "cross" lets one walk serve all four, which is the trick
        /// <see cref="ArenaLayout"/> plays with its lanes.
        /// </remarks>
        public readonly struct Face
        {
            /// <summary>Where the run starts, on an oblique face. Unused on a rectangle's side.</summary>
            readonly Vec2 _origin;

            /// <summary>Which way the run goes, as a unit vector, on an oblique face.</summary>
            readonly Vec2 _along;

            /// <summary>Which way is across it, on an oblique face. <see cref="Outward"/> signs it.</summary>
            readonly Vec2 _cross;

            /// <summary>The run's own yaw as a <see cref="YawStep"/>. Zero on a rectangle's side.</summary>
            readonly int _steps;

            /// <summary>Creates a face from its axis, its outward sign, its line and its extent.</summary>
            public Face(bool alongX, int outward, float line, float min, float max)
            {
                AlongX = alongX;
                Outward = outward;
                Line = line;
                Min = min;
                Max = max;

                IsOblique = false;
                _origin = default;
                _along = default;
                _cross = default;
                _steps = 0;
            }

            Face(Vec2 origin, Vec2 along, Vec2 cross, int steps, int outward, float length)
            {
                // The run's own frame, so every measurement below reads as it does on a face that
                // happens to lie along world X: along is this face's X and across it is this
                // face's Z. Only Point knows the difference, because only Point leaves the frame.
                AlongX = true;
                Outward = outward;
                Line = 0f;
                Min = 0f;
                Max = length;

                IsOblique = true;
                _origin = origin;
                _along = along;
                _cross = cross;
                _steps = steps;
            }

            /// <summary>
            /// A run along a line at any angle, from one point to another, with art laid on the
            /// side <paramref name="outward"/> names.
            /// </summary>
            /// <remarks>
            /// <para>
            /// <strong>The line stays exactly where it was given and the art is what rounds.</strong>
            /// A piece is turned to the nearest of the twenty-four <see cref="YawStep"/>s, so a run
            /// at forty degrees is tiled from pieces stood at forty-five — each one centred on the
            /// line at the point the cursor reached, skewed across it by at most seven and a half
            /// degrees. Snapping the line instead would move the thing being described, which for a
            /// road is the one thing that may not move: the carriageway was routed, graded and
            /// reserved before a kerb was thought about.
            /// </para>
            /// <para>
            /// <strong>An oblique run's pieces are boxes round turned art, and they overlap where
            /// the art does not.</strong> A footprint is a <see cref="Rect2"/> and every rule in
            /// <see cref="ConstraintSet"/> is stated over one, so a piece at forty-five degrees is
            /// judged by the square round it — which for anything longer than it is wide is far
            /// bigger than the piece. Two of them laid end to end always overlap, whatever the art,
            /// so a run seated flush would have every second piece refused for standing in the one
            /// before it. That is why <see cref="Tile"/> judges an oblique run's pieces against
            /// everything on the map <em>except that run</em> — see <see cref="IsOblique"/>. It is
            /// the exemption <see cref="CloseGaps"/> already needed, applied to the whole walk for
            /// a reason that is about the box rather than about the tail.
            /// </para>
            /// </remarks>
            /// <param name="from">Where the run starts.</param>
            /// <param name="to">Where it ends. A run of no length lays nothing.</param>
            /// <param name="outward">Which side art is laid on: 1 to the left of the line, -1 to the right.</param>
            public static Face Along(Vec2 from, Vec2 to, int outward)
            {
                Vec2 span = to - from;
                float length = span.Length;
                if (!(length > 0f))
                {
                    // No direction to lay anything along. A zero-length face tiles nothing, because
                    // no piece of art is shorter than it.
                    return new Face(from, new Vec2(1f, 0f), new Vec2(0f, 1f), 0, outward, 0f);
                }

                Vec2 along = span / length;

                // The run's own Z axis, which is where a quarter turn sends it: a yaw that carries
                // X to 'along' carries Z to this.
                var cross = new Vec2(-along.Y, along.X);

                return new Face(from, along, cross, YawStep.Nearest(along), outward, length);
            }

            /// <summary>
            /// True when the run lies at an angle rather than along a world axis, so its pieces are
            /// judged against everything except the run itself.
            /// </summary>
            /// <remarks>See <see cref="Along"/> for why the exemption is needed and what it costs.</remarks>
            public bool IsOblique { get; }

            /// <summary>
            /// True when the run goes along world X, so the cross axis is world Z.
            /// </summary>
            /// <remarks>
            /// An oblique face reports true, because on one this describes the run's own frame
            /// rather than the world's: along is its X and across it is its Z. Nothing outside this
            /// type builds or reads one — <see cref="WallFacing.AwayFrom"/> and the like are asked
            /// only about the sides of rectangles, where the two frames are the same.
            /// </remarks>
            public bool AlongX { get; }

            /// <summary>Which way is out of the rectangle on the cross axis: 1 or -1.</summary>
            public int Outward { get; }

            /// <summary>Where the face sits on the cross axis.</summary>
            public float Line { get; }

            /// <summary>The low end of the run, on the along axis.</summary>
            public float Min { get; }

            /// <summary>The high end of the run.</summary>
            public float Max { get; }

            /// <summary>How far the run goes.</summary>
            public float Length => Max - Min;

            /// <summary>A world point from a position along the run and one across it.</summary>
            /// <remarks>
            /// The one measurement that leaves the run's own frame, which is why it is the one that
            /// has to know whether the frame is the world's. On a rectangle's side the two
            /// coordinates are already world ones.
            /// </remarks>
            public Vec2 Point(float along, float cross) => IsOblique
                ? new Vec2(
                    _origin.X + _along.X * along + _cross.X * cross,
                    _origin.Y + _along.Y * along + _cross.Y * cross)
                : AlongX ? new Vec2(along, cross) : new Vec2(cross, along);

            /// <summary>
            /// The footprint a piece of this art covers in the run's own frame, turned so its long
            /// axis lies along the run.
            /// </summary>
            public Rect2 Local(CatalogEntry entry) =>
                QuarterTurn.Rotate(entry.Footprint, QuarterTurnsAlong(entry, AlongX));

            /// <summary>How far to turn a piece of this art so it lies along the run.</summary>
            /// <remarks>
            /// The run's own yaw plus the quarter turn that puts the art's long axis on the run's
            /// X — which on a rectangle's side is the whole of it, because such a run has no yaw of
            /// its own. The four exact quarter turns are steps 0, 6, 12 and 18 of the finer table
            /// and are bit-identical to <see cref="QuarterTurn.Rotation"/>, so a run along an axis
            /// places exactly what it placed before this measurement existed.
            /// </remarks>
            public int Steps(CatalogEntry entry) => YawStep.Normalize(
                _steps + YawStep.Count / QuarterTurn.Count * QuarterTurnsAlong(entry, AlongX));

            /// <summary>How far a rotated footprint reaches along the run.</summary>
            public float AlongSize(Rect2 local) => AlongX ? local.Width : local.Depth;

            /// <summary>
            /// Where a pivot goes so the footprint starts at <paramref name="at"/> along the run.
            /// </summary>
            public float From(Rect2 local, float at) => at - (AlongX ? local.MinX : local.MinZ);

            /// <summary>Where a pivot goes so the footprint is centred on <paramref name="at"/>.</summary>
            public float Centred(Rect2 local, float at) =>
                at - (AlongX ? local.Center.X : local.Center.Y);

            /// <summary>
            /// Where a pivot goes so the footprint stands outside the face, <paramref name="gap"/>
            /// metres clear of it.
            /// </summary>
            public float Against(Rect2 local, float gap) => Outward > 0
                ? Line + gap - (AlongX ? local.MinZ : local.MinX)
                : Line - gap - (AlongX ? local.MaxZ : local.MaxX);

            /// <summary>Where a pivot goes so the footprint straddles the face's line.</summary>
            public float Astride(Rect2 local) => Line - (AlongX ? local.Center.Y : local.Center.X);

            /// <summary>
            /// A copy of the run slid along its own axis by <paramref name="by"/> metres, both ends
            /// together.
            /// </summary>
            /// <remarks>
            /// What closes the corners of a ring. Four runs round a rectangle meet twice each, and
            /// if every one of them reaches both of its own corners the two that meet there overlap
            /// — which a no-overlap rule turns into a rejection and a hole. Sliding each run by half
            /// the thickest piece hands every corner to exactly one of the two runs that reach it:
            /// the ring closes all the way round, and no two runs ask for the same ground.
            /// </remarks>
            public Face Shifted(float by) => IsOblique
                ? new Face(
                    Point(by, 0f), _along, _cross, _steps, Outward, Length)
                : new Face(AlongX, Outward, Line, Min + by, Max + by);

            /// <inheritdoc />
            public override string ToString() => IsOblique
                ? $"{_origin}..{Point(Max, 0f)} facing {Outward}"
                : $"{(AlongX ? "x" : "z")} {Min}..{Max} at {Line} facing {Outward}";
        }
    }
}
