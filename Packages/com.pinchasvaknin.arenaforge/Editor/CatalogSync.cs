using System;
using System.Collections.Generic;
using System.Text;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// What one run of the catalog sync did.
    /// </summary>
    public readonly struct CatalogSyncResult
    {
        /// <summary>Creates a result.</summary>
        public CatalogSyncResult(
            int added, int updated, int kept, int untagged, int unmeasured, int fromArt = 0,
            int pruned = 0)
        {
            Added = added;
            Updated = updated;
            Kept = kept;
            Untagged = untagged;
            Unmeasured = unmeasured;
            FromArt = fromArt;
            Pruned = pruned;
        }

        /// <summary>Rows the scan created.</summary>
        public int Added { get; }

        /// <summary>Rows the scan overwrote.</summary>
        public int Updated { get; }

        /// <summary>Rows the catalog already had that the scan did not produce.</summary>
        public int Kept { get; }

        /// <summary>Prefabs skipped for sitting in the root rather than in a folder.</summary>
        public int Untagged { get; }

        /// <summary>Rows written with nothing at all to measure, so left at their defaults.</summary>
        public int Unmeasured { get; }

        /// <summary>Rows measured off the art itself, for want of a box collider to measure.</summary>
        /// <remarks>
        /// Worth reporting rather than doing quietly. A collider is a size somebody chose and a
        /// mesh is the size the art happens to be, so a row measured this way can be a little
        /// larger than the piece a person would have drawn a box round — and if a number in the
        /// catalog looks wrong, this is the first thing to know about it.
        /// </remarks>
        public int FromArt { get; }

        /// <summary>Rows removed for pointing at a prefab the project no longer has.</summary>
        /// <remarks>
        /// Reported rather than done quietly, because it is the one thing a sync does that takes
        /// something away. A number here means art was deleted or renamed outside the tool, which
        /// is worth knowing at the moment it is noticed rather than the next time a map comes out
        /// with a hole in it.
        /// </remarks>
        public int Pruned { get; }

        /// <summary>A one-line summary for a status line or the console.</summary>
        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append(Added).Append(" added, ").Append(Updated).Append(" updated");

            if (Kept > 0)
            {
                text.Append(", ").Append(Kept).Append(" left alone");
            }

            if (Pruned > 0)
            {
                text.Append(", ").Append(Pruned).Append(" pruned for having no prefab");
            }

            if (FromArt > 0)
            {
                text.Append(", ").Append(FromArt).Append(" measured off the art for want of a collider");
            }

            if (Unmeasured > 0)
            {
                text.Append(", ").Append(Unmeasured).Append(" with nothing to measure");
            }

            if (Untagged > 0)
            {
                text.Append(", ").Append(Untagged).Append(" skipped for not being in a folder");
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Builds catalog rows from a folder of prefabs: tags from the folders, footprint and height
    /// from the box colliders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A catalog is the one part of this tool that is pure data entry, and data entry is the part
    /// people stop doing. Everything a row needs is already stated somewhere in the project — a
    /// prefab in a <c>Walls</c> folder is a wall, and a prefab is already exactly as big as it is —
    /// so the sync reads those two statements rather than asking for them a second time.
    /// </para>
    /// <para>
    /// <strong>Size comes from a box collider where there is one and from the art where there is
    /// not</strong>, and never from nowhere. See <see cref="TryMeasure"/>: a row nothing measured
    /// keeps the one-metre-cube defaults, which is the wrong size for everything and is wrong
    /// silently.
    /// </para>
    /// <para>
    /// <strong>Folders name tags.</strong> The chain of folders under the root becomes a tag path,
    /// and every prefix of that path is a tag: a prefab in <c>Covers/Low</c> comes out tagged
    /// <c>cover</c> and <c>cover/low</c>, which is exactly the pair
    /// <see cref="ArenaForge.Core.CoverPlacer"/> queries for. A name this file does not recognise
    /// extends the path, which is what makes <c>Covers/Low/Wooden</c> mean something without this
    /// table having heard of wood; a name it does recognise takes one of the three roles in
    /// <see cref="FolderRole"/>.
    /// </para>
    /// <para>
    /// <strong>A folder says where a prop may be put, not what it is.</strong> That is why the
    /// tree is deep: <c>PropBuilding/Decor/Decoration</c> and
    /// <c>PropBuilding/Decor/Centerpieces</c> are both furniture, and they are two folders because
    /// one goes in a room's corners and the other in its middle. The tag path has to keep that
    /// depth or the distinction is lost on the way in, so <c>Decor</c> is a
    /// <see cref="FolderRole.Branch"/>: everything below it is read as its own name and spells
    /// <c>propbuilding/decor/…</c>, which is what keeps a room's furniture out of the queries the
    /// map's own art answers.
    /// </para>
    /// <para>
    /// The branch is what makes that safe; distinct folder names are what make it obvious. The
    /// middle-of-the-room folder was called <c>Covers</c> once, a level below the tactical
    /// <c>Props/Covers</c> the map is fought around, and the two were told apart by this table
    /// alone. Nothing about that was wrong and nothing about it was legible — so the folder is
    /// <c>Centerpieces</c> now, and <c>covers</c> means one thing in a workspace again.
    /// </para>
    /// <para>
    /// <strong>The folder is not the authority over the catalog; the project is.</strong> A
    /// catalog may hold rows this folder knows nothing about — an exported building, something
    /// typed in by hand, art filed somewhere else entirely — and a sync that removed a row for
    /// being absent from the folder it happened to scan would delete a person's work for having
    /// organised it differently. So a row whose prefab is still in the project is kept whatever
    /// the scan found.
    /// </para>
    /// <para>
    /// <strong>A row with no prefab left is pruned.</strong> That is not the same question. A
    /// deleted prefab leaves its row behind pointing at nothing, and a row pointing at nothing is
    /// worse than no row at all: it is still tagged, so every query still returns it and every
    /// weighted pick can still land on it, and what the generator does with the one it picked is
    /// reserve the ground, write the placement and realise nothing — a hole in the map at a spot
    /// something was chosen for. It is swept before the scan is merged in rather than after, so a
    /// dead row is never sitting beside the row that replaced it. See <see cref="Prune"/>.
    /// </para>
    /// <para>
    /// <strong>A prefab gets one row, however it was found.</strong> A row is matched by the
    /// prefab it points at first and by its logical id second, because a file that has been
    /// renamed or moved spells a different id and is the same piece of art — see
    /// <see cref="IndexOfRowBoundTo"/>. Matching on the id alone quietly doubled such a row and
    /// doubled its odds in every weighted pick with it.
    /// </para>
    /// </remarks>
    public static class CatalogSync
    {
        /// <summary>
        /// What a child of a prefab must be called, or tagged, to mark a way into it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The one thing about a structure a scan cannot work out. How big a house is, is a fact
        /// about its meshes; which wall the front door is in is a fact about the art nobody wrote
        /// down, and a map placing the house has to know it — see
        /// <see cref="ArenaForge.Core.CatalogEntry.Doorways"/>. So the person who made the prefab
        /// says where the doors are by putting a marker in it, in exactly the way they already say
        /// how big a piece is by putting a box collider on it.
        /// </para>
        /// <para>
        /// <strong>By name or by Unity tag, and by name first.</strong> A tag is the tidier answer
        /// and it is not available in every project: a tag has to exist in the tag manager before
        /// anything can carry it, and an art pack dropped into a project brings its prefabs without
        /// bringing that entry. A name is carried by the prefab itself. Both are accepted, matched
        /// case-insensitively, and a name only has to <em>start</em> with this — so a building with
        /// <c>DoorwayMarker_Front</c> and <c>DoorwayMarker_Back</c> in it declares two.
        /// </para>
        /// <para>
        /// A marker with a box collider on it declares the threshold that collider covers. One
        /// without — an empty transform, which is what the quickest way of marking a spot produces
        /// — declares a square <see cref="ArenaForge.Core.ArenaLayoutGenerator.DoorwayDepth"/> on a
        /// side, centred on it. Neither is measured into the piece's own footprint: a marker is an
        /// annotation, and a prefab that grew by half a metre because somebody said where its door
        /// was would be a measurement reporting on itself.
        /// </para>
        /// </remarks>
        public const string DoorwayMarkerName = "DoorwayMarker";

        /// <summary>What a recognised folder name does to the tag path being built.</summary>
        /// <remarks>
        /// Three roles rather than the one flag this started with, because the workspace layout
        /// grew a level that means something. A flag could say "this name is a kind" and nothing
        /// more, so a name in the table meant the same thing wherever it appeared — including
        /// inside <c>PropBuilding/Decor</c>, whose children are a vocabulary of their own.
        /// </remarks>
        internal enum FolderRole
        {
            /// <summary>
            /// Starts the tag path afresh, so an organising folder above — <c>Props</c>, a vendor
            /// name, an art pack version — does not end up as the first segment of every tag
            /// under it.
            /// </summary>
            Restart,

            /// <summary>
            /// Extends the path it finds, the way an unrecognised name does. For a name that
            /// qualifies the kind above it rather than naming one of its own: <c>Low</c> under
            /// <c>Covers</c> is <c>cover/low</c> and is nothing on its own.
            /// </summary>
            Extend,

            /// <summary>
            /// Extends the path, and reads every folder below it as a plain name whatever this
            /// table says about it.
            /// </summary>
            /// <remarks>
            /// <para>
            /// The role a folder takes when its children are its own vocabulary. What is filed
            /// under <c>PropBuilding/Decor</c> is a building's furniture, and it has nothing to do
            /// with the map art the same words name outside it — so the branch seals the table off
            /// below itself rather than every name inside it having to be spelled differently from
            /// the name it naturally has.
            /// </para>
            /// <para>
            /// A branch is the one role that also says what it means with nothing above it — see
            /// <see cref="Known.Alone"/>. <c>Decor</c> qualifies the kind it is filed under and is
            /// still a kind on its own, which <c>Low</c> is not.
            /// </para>
            /// </remarks>
            Branch,
        }

        /// <summary>Folder names this file knows, and the tag path each one means.</summary>
        /// <remarks>
        /// A closed table rather than a rule, because these are the tags the generator actually
        /// queries — see <see cref="ArenaForge.Core.ArenaLayoutGenerator"/> and
        /// <see cref="ArenaForge.Core.BuildingGenerator"/>. Every other folder name falls through
        /// to its own slug, so the table never has to grow to let a project organise itself.
        /// </remarks>
        static readonly Known[] KnownFolders =
        {
            new Known("floors", "structure/floor", FolderRole.Restart),
            new Known("floor", "structure/floor", FolderRole.Restart),
            new Known("walls", "structure/wall", FolderRole.Restart),
            new Known("wall", "structure/wall", FolderRole.Restart),
            new Known("doorways", "structure/doorway", FolderRole.Restart),
            new Known("doors", "structure/doorway", FolderRole.Restart),
            new Known("windows", "structure/window", FolderRole.Restart),
            new Known("window", "structure/window", FolderRole.Restart),
            new Known("stairs", "structure/stairs", FolderRole.Restart),
            new Known("staircases", "structure/stairs", FolderRole.Restart),
            new Known("parapets", "structure/parapet", FolderRole.Restart),
            new Known("parapet", "structure/parapet", FolderRole.Restart),
            new Known("buildings", "structure/building", FolderRole.Restart),
            new Known("houses", "structure/house", FolderRole.Restart),
            new Known("covers", "cover", FolderRole.Restart),
            new Known("cover", "cover", FolderRole.Restart),
            new Known("spawns", "spawn", FolderRole.Restart),
            new Known("markers", "spawn", FolderRole.Restart),
            new Known("props", "prop", FolderRole.Restart),
            new Known("propbuilding", "propbuilding", FolderRole.Restart),
            new Known("fence", "fence", FolderRole.Restart),
            new Known("fences", "fence", FolderRole.Restart),

            // Restarts like the fences do, so Props/Road/Kerb spells road/kerb rather than
            // prop/road/kerb — which is what RoadKerbs.KerbTag queries.
            new Known("road", "road", FolderRole.Restart),
            new Known("roads", "road", FolderRole.Restart),

            // Extends rather than restarts, so the same folder name reads as the building's decor
            // under PropBuilding and as the workspace's own decor directly under Props — which is
            // where a project that predates the deeper layout still has it. With nothing above it
            // to qualify it spells prop/decor, the tag it has always spelled.
            new Known("decor", "decor", FolderRole.Branch, "prop/decor"),

            new Known("low", "low", FolderRole.Extend),
            new Known("high", "high", FolderRole.Extend),
        };

        /// <summary>
        /// Scans <paramref name="rootFolder"/>, prunes the rows in <paramref name="asset"/> left
        /// pointing at nothing, and merges what the scan found into what is left.
        /// </summary>
        /// <param name="asset">The catalog to write into.</param>
        /// <param name="rootFolder">Project-relative folder to scan, for example <c>Assets/ArenaWorkspace</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="asset"/> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="rootFolder"/> is not a folder.</exception>
        public static CatalogSyncResult Sync(CatalogAsset asset, string rootFolder)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            List<CatalogAsset.Row> scanned = Scan(
                rootFolder, out int untagged, out int unmeasured, out int fromArt);
            var rows = new List<CatalogAsset.Row>(asset.Rows);

            // Before the merge, so a dead row cannot still be sitting there when the scan writes
            // the row that replaces it. Delete a prefab, make a new one under the same name, and
            // the row left holding nothing is not the row the scan produces — it is a second row
            // beside it, tagged and pickable and pointing at an asset that is gone.
            int pruned = Prune(rows);

            int added = 0;
            int updated = 0;

            for (int i = 0; i < scanned.Count; i++)
            {
                CatalogAsset.Row row = scanned[i];
                int existing = IndexOfRowBoundTo(rows, row.Prefab);

                if (existing < 0)
                {
                    existing = rows.FindIndex(
                        r => r != null && string.Equals(r.LogicalId, row.LogicalId, StringComparison.Ordinal));
                }

                if (existing < 0)
                {
                    rows.Add(row);
                    added++;
                    continue;
                }

                rows[existing] = Merge(rows[existing], row);
                updated++;
            }

            Undo.RegisterCompleteObjectUndo(asset, "ArenaForge: sync catalog");
            asset.SourceFolder = rootFolder;
            asset.SetRows(rows);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return new CatalogSyncResult(
                added, updated, rows.Count - added - updated, untagged, unmeasured, fromArt, pruned);
        }

        /// <summary>
        /// The index of the row already bound to <paramref name="prefab"/>, or -1 when no row is.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Asked before the logical id, because the reference is what a binding actually
        /// is.</strong> An id is spelled from a folder and a file name and both can change: rename
        /// <c>Crate.prefab</c> to <c>Barrel.prefab</c>, or drag it from <c>Covers/Low</c> to
        /// <c>Covers/High</c>, and the reference in the row follows the asset while the id the scan
        /// spells for it does not match any more. Matching on the id alone then wrote a second row
        /// for the same prefab and left the first one beside it — two rows, both live, both
        /// pickable, and the map twice as likely to choose that piece of art as the person asked
        /// for. The same row is updated instead.
        /// </para>
        /// <para>
        /// The rows have been pruned by the time this runs, so every reference left resolves to an
        /// asset and a null on either side can only mean a scan that found nothing.
        /// </para>
        /// </remarks>
        static int IndexOfRowBoundTo(List<CatalogAsset.Row> rows, GameObject prefab)
        {
            if (prefab == null)
            {
                return -1;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null && rows[i].Prefab == prefab)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Removes every row with no prefab left to place, and returns how many went.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The test is the row's own reference and not the folder that was scanned, which is what
        /// keeps this an orphan sweep rather than a mirror of one directory. A prefab that was
        /// deleted or moved outside the project leaves its row's reference resolving to null —
        /// that row is pruned. A prefab that merely lives somewhere the scan never looked resolves
        /// to the prefab it always did, and its row is left exactly where it is.
        /// </para>
        /// <para>
        /// A reference that resolves to something with no asset path goes too. It means a row was
        /// bound to an object in a scene rather than to an asset, which cannot be instantiated
        /// from a catalog and is the same hole in a map by a different route.
        /// </para>
        /// <para>
        /// <strong>Run before the scan is merged in, not after.</strong> A row holding nothing is
        /// dead whatever the scan is about to find, and leaving it in place until afterwards left
        /// it there to be matched — or worse, not matched: a prefab deleted and remade under the
        /// same name is a new asset, so the row that named the old one stayed behind while the
        /// scan wrote a fresh row beside it. Sweeping first means the merge only ever sees rows
        /// that still point at something. What it costs is the weight and the sockets somebody
        /// typed onto the dead row, which are facts about art the project no longer has.
        /// </para>
        /// <para>
        /// In place and walking downward, so removing one row cannot skip the next.
        /// </para>
        /// </remarks>
        static int Prune(List<CatalogAsset.Row> rows)
        {
            int pruned = 0;

            for (int i = rows.Count - 1; i >= 0; i--)
            {
                CatalogAsset.Row row = rows[i];
                if (row != null && row.Prefab != null &&
                    !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(row.Prefab)))
                {
                    continue;
                }

                if (row != null && !string.IsNullOrEmpty(row.LogicalId))
                {
                    Debug.LogWarning(
                        $"ArenaForge: pruned catalog row '{row.LogicalId}' — the prefab it named is " +
                        "no longer in this project.");
                }

                rows.RemoveAt(i);
                pruned++;
            }

            return pruned;
        }

        /// <summary>
        /// The rows <paramref name="rootFolder"/> describes, in logical-id order.
        /// </summary>
        /// <remarks>
        /// Sorted rather than left in whatever order the asset database enumerated, so two runs
        /// over the same folder produce the same catalog — the same reason
        /// <see cref="ArenaForge.Core.Catalog"/> sorts its entries.
        /// </remarks>
        /// <exception cref="InvalidOperationException"><paramref name="rootFolder"/> is not a folder.</exception>
        public static List<CatalogAsset.Row> Scan(string rootFolder) =>
            Scan(rootFolder, out _, out _, out _);

        static List<CatalogAsset.Row> Scan(
            string rootFolder, out int untagged, out int unmeasured, out int fromArt)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !AssetDatabase.IsValidFolder(rootFolder))
            {
                throw new InvalidOperationException(
                    $"'{rootFolder}' is not a folder in this project, so there is nothing to scan.");
            }

            string root = rootFolder.TrimEnd('/');
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
            var paths = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            paths.Sort(StringComparer.Ordinal);

            var rows = new List<CatalogAsset.Row>(paths.Count);
            untagged = 0;
            unmeasured = 0;
            fromArt = 0;

            for (int i = 0; i < paths.Count; i++)
            {
                string[] tags = TagsFor(root, paths[i], out string tagPath);
                if (tags.Length == 0)
                {
                    // Nothing said what this prefab is. A row with no tags is a row no query can
                    // ever return, so it is reported rather than written.
                    untagged++;
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null)
                {
                    continue;
                }

                var row = new CatalogAsset.Row
                {
                    LogicalId = tagPath + "/" + Slug(System.IO.Path.GetFileNameWithoutExtension(paths[i])),
                    Tags = tags,
                    Prefab = prefab,
                };

                if (TryMeasure(prefab, out Bounds bounds))
                {
                    Apply(row, bounds);
                    if (HasNoBoxCollider(prefab))
                    {
                        fromArt++;
                    }
                }
                else
                {
                    unmeasured++;
                }

                row.Doorways = DoorwaysIn(prefab);

                rows.Add(row);
            }

            rows.Sort((a, b) => string.CompareOrdinal(a.LogicalId, b.LogicalId));
            return rows;
        }

        /// <summary>
        /// The tags a prefab's folders give it, and the tag path they spell.
        /// </summary>
        public static string[] TagsFor(string rootFolder, string assetPath, out string tagPath)
        {
            tagPath = string.Empty;

            string root = rootFolder.TrimEnd('/');
            string folder = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? string.Empty;
            if (folder.Length <= root.Length)
            {
                return Array.Empty<string>();
            }

            string[] parts = folder.Substring(root.Length + 1).Split('/');
            var segments = new List<string>(parts.Length);

            // Set by a Branch folder and never cleared: everything below one is that branch's own
            // vocabulary, so the table stops being consulted for the rest of the path.
            bool branched = false;

            for (int i = 0; i < parts.Length; i++)
            {
                if (!branched && TryKnown(parts[i], out Known known))
                {
                    if (known.Role == FolderRole.Restart)
                    {
                        segments.Clear();
                    }

                    branched = known.Role == FolderRole.Branch;
                    segments.AddRange(known.PathFrom(segments.Count).Split('/'));
                    continue;
                }

                segments.Add(Slug(parts[i]));
            }

            if (segments.Count == 0)
            {
                return Array.Empty<string>();
            }

            // Every prefix, so a floor tile answers a query for 'structure' as well as one for
            // 'structure/floor'. That is how a hand-written catalog spells it, and the arena
            // generator's clearance rules are written against the short one.
            var tags = new string[segments.Count];
            var path = new StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0)
                {
                    path.Append('/');
                }

                path.Append(segments[i]);
                tags[i] = path.ToString();
            }

            tagPath = tags[tags.Length - 1];
            return tags;
        }

        /// <summary>
        /// Measures the box a prefab occupies in its own space: its box colliders if it has any,
        /// and the art itself if it has not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The box itself, where this once returned the smallest box <em>centred on the pivot</em>
        /// that held it. Centring was never a measurement — it was the shape a catalog row could
        /// express — and it errs large by twice however far the art is modelled off its pivot: a
        /// flight of stairs whose pivot sits at the foot of the run came back nearly twice as long
        /// as it is, and the stairwell opening cut from that was four times the floor the stairs
        /// actually cover.
        /// </para>
        /// <para>
        /// So a row states the offset as well as the size, and the generator puts each piece where
        /// its <em>art</em> goes rather than where its pivot goes. What a caller takes from this box
        /// is the footprint's size and centre on the ground plane, the height above the pivot, and
        /// how far the art hangs below it.
        /// </para>
        /// <para>
        /// <strong>Meshes are the fallback, and having none was a bug.</strong> Box colliders come
        /// first and always will: a collider is a size somebody chose, and a mesh is whatever the
        /// art happens to measure. But a bought art pack ships no box colliders and often no
        /// colliders at all — and a row this returned false for kept the defaults on
        /// <see cref="CatalogAsset.Row"/>, which are a one-metre cube. That is not a small error and
        /// it is not a visible one: every prop in the pack comes out the same size, every fence
        /// panel comes out square, and a square footprint makes
        /// <see cref="ArenaForge.Core.WallRun.QuarterTurnsAlong"/> a coin toss — so the fence round
        /// the map was tiled a metre at a time and turned across the line it was meant to run along.
        /// Measuring the art is worse than measuring a collider and enormously better than measuring
        /// nothing.
        /// </para>
        /// <para>
        /// Inactive children count, for both, because a prefab variant's switched-off alternates are
        /// still art the piece is made of, and a size that changed with what happened to be enabled
        /// would not be a measurement.
        /// </para>
        /// <para>
        /// <strong>The answer is in the space the art stands in, not in the root's own.</strong> A
        /// scale on the prefab's root counts, because <c>WorldRealizer</c> composes it rather than
        /// replacing it and the map stands the art at that size. A caller writing something back
        /// <em>under</em> that root — a <c>localPosition</c>, say — is in the other space and has to
        /// divide it out; <c>DoorwaySetup.Start</c> is the one that does.
        /// </para>
        /// </remarks>
        public static bool TryMeasure(GameObject prefab, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.one);

            if (prefab == null)
            {
                return false;
            }

            return TryMeasureColliders(prefab, ref bounds) || TryMeasureMeshes(prefab, ref bounds);
        }

        /// <summary>
        /// The box round every mesh a prefab renders, in the prefab's own space, ignoring whatever
        /// colliders it carries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Separate from <see cref="TryMeasure"/> rather than a flag on it, because the two answer
        /// different questions. <see cref="TryMeasure"/> asks how big a prefab is <em>said</em> to
        /// be, and a box somebody drew is a better answer than the art whenever there is one.
        /// <see cref="MergeToPrefab"/> asks how big the art is, because the box it is about to fit
        /// is the one that will be read back — and a measurement that included the pieces' own
        /// colliders would make that box a copy of whatever the artist happened to leave on them.
        /// </para>
        /// <para>
        /// <see cref="WrapObject.EncloseInABox"/> arranges for the same answer by stripping the
        /// colliders before it measures. A merge keeps them, so it has to ask instead.
        /// </para>
        /// </remarks>
        internal static bool TryMeasureArt(GameObject prefab, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.one);

            return prefab != null && TryMeasureMeshes(prefab, ref bounds);
        }

        /// <summary>
        /// The doorways a prefab declares, as rectangles in its own space, in hierarchy order.
        /// </summary>
        /// <remarks>
        /// In hierarchy order and not sorted, because the order is the person's: a prefab with a
        /// front door and a back door lists them the way they were put in, and a map that keeps
        /// only two of three keeps the two furthest apart by geometry rather than by position in a
        /// list. Sorting would be a second opinion about something nobody asked.
        /// </remarks>
        public static List<CatalogAsset.DoorwayRow> DoorwaysIn(GameObject prefab)
        {
            var doorways = new List<CatalogAsset.DoorwayRow>();
            if (prefab == null)
            {
                return doorways;
            }

            Transform[] children = prefab.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (!IsDoorwayMarker(children[i].gameObject))
                {
                    continue;
                }

                var extent = new Extent(prefab.transform);
                var box = children[i].GetComponent<BoxCollider>();

                if (box != null)
                {
                    extent.Add(children[i], box.center, box.size * 0.5f);
                }
                else
                {
                    // An empty marker: a threshold as wide as it is deep, centred on the transform.
                    // Nothing about the object says how wide the opening is, so what is declared is
                    // that a person walks through here rather than how much room they have.
                    float half = ArenaForge.Core.ArenaLayoutGenerator.DoorwayDepth * 0.5f;
                    extent.Add(children[i], Vector3.zero, new Vector3(half, half, half));
                }

                var bounds = new Bounds();
                if (!extent.TryClose(ref bounds))
                {
                    continue;
                }

                doorways.Add(new CatalogAsset.DoorwayRow
                {
                    Center = new Vector2(bounds.center.x, bounds.center.z),
                    Size = new Vector2(bounds.size.x, bounds.size.z),
                });
            }

            return doorways;
        }

        /// <summary>True if an object is one of the markers <see cref="DoorwayMarkerName"/> describes.</summary>
        /// <remarks>
        /// The Unity tag is only asked about when the project has defined it. <c>CompareTag</c>
        /// throws on a tag no tag manager knows, so asking unguarded would turn "this project has
        /// not defined the tag" into a failed sync.
        /// </remarks>
        static bool IsDoorwayMarker(GameObject candidate) =>
            candidate.name.StartsWith(DoorwayMarkerName, StringComparison.OrdinalIgnoreCase) ||
            (IsDefinedTag(DoorwayMarkerName) && candidate.CompareTag(DoorwayMarkerName));

        static bool IsDefinedTag(string tag)
        {
            string[] tags = UnityEditorInternal.InternalEditorUtility.tags;
            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True if the prefab carries no box collider, so any measurement came off its art.</summary>
        /// <remarks>
        /// <para>
        /// Asked separately rather than reported by <see cref="TryMeasure"/> through a second
        /// out-parameter, because only the scan's tally wants to know: it is a fact about how a row
        /// was arrived at rather than about the row.
        /// </para>
        /// <para>
        /// A doorway marker's box does not count, for the same reason
        /// <see cref="TryMeasureColliders"/> steps over it: it is an annotation and not art, so a
        /// house whose only box is the one somebody drew round its front door was measured off its
        /// meshes and would otherwise have been reported as though a person had sized it. Every
        /// exported building carries such a box now, which is what makes the difference visible.
        /// </para>
        /// </remarks>
        public static bool HasNoBoxCollider(GameObject prefab)
        {
            if (prefab == null)
            {
                return true;
            }

            BoxCollider[] boxes = prefab.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                if (!IsUnderADoorwayMarker(prefab.transform, boxes[i].transform))
                {
                    return false;
                }
            }

            return true;
        }

        static bool TryMeasureColliders(GameObject prefab, ref Bounds bounds)
        {
            BoxCollider[] boxes = prefab.GetComponentsInChildren<BoxCollider>(true);

            var extent = new Extent(prefab.transform);
            for (int i = 0; i < boxes.Length; i++)
            {
                if (IsUnderADoorwayMarker(prefab.transform, boxes[i].transform))
                {
                    continue;
                }

                extent.Add(boxes[i].transform, boxes[i].center, boxes[i].size * 0.5f);
            }

            return extent.TryClose(ref bounds);
        }

        /// <summary>
        /// True if a child is a doorway marker or sits inside one, so it is annotation rather than
        /// art.
        /// </summary>
        /// <remarks>
        /// Walked up to the prefab root rather than tested on the child alone, because a marker
        /// modelled as a marker with a box under it is the same statement made one level down —
        /// and a measurement that grew by whichever of the two the person happened to draw would
        /// not be a measurement.
        /// </remarks>
        static bool IsUnderADoorwayMarker(Transform root, Transform child)
        {
            for (Transform at = child; at != null && at != root.parent; at = at.parent)
            {
                if (IsDoorwayMarker(at.gameObject))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Measures every mesh the prefab renders, in the prefab's own space.
        /// </summary>
        /// <remarks>
        /// <see cref="MeshFilter.sharedMesh"/> rather than <see cref="Renderer.bounds"/>: a renderer
        /// on a prefab asset has never been in a scene, so its world bounds are not a number to
        /// trust. A mesh's own local bounds are a fact about the asset, and putting the eight corners
        /// of that box through the same transform chain the colliders use is the same measurement
        /// taken off a different source.
        /// </remarks>
        static bool TryMeasureMeshes(GameObject prefab, ref Bounds bounds)
        {
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);

            var extent = new Extent(prefab.transform);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null || mesh.vertexCount == 0 ||
                    IsUnderADoorwayMarker(prefab.transform, filters[i].transform))
                {
                    continue;
                }

                Bounds local = mesh.bounds;
                extent.Add(filters[i].transform, local.center, local.extents);
            }

            return extent.TryClose(ref bounds);
        }

        /// <summary>
        /// The box round a set of child boxes, accumulated in one root's space.
        /// </summary>
        /// <remarks>
        /// Eight corners through the transform chain rather than a centre and extents alone, because
        /// a child may be rotated inside the prefab and a rotated box's axis-aligned bounds are not
        /// its extents.
        /// </remarks>
        struct Extent
        {
            readonly Matrix4x4 _toRoot;
            readonly Vector3 _rootScale;
            Vector3 _min;
            Vector3 _max;
            bool _any;

            public Extent(Transform root)
            {
                _toRoot = root.worldToLocalMatrix;
                _rootScale = root.localScale;
                _min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                _max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                _any = false;
            }

            public void Add(Transform of, Vector3 centre, Vector3 extents)
            {
                Matrix4x4 matrix = _toRoot * of.localToWorldMatrix;

                for (int corner = 0; corner < 8; corner++)
                {
                    var local = new Vector3(
                        centre.x + ((corner & 1) == 0 ? -extents.x : extents.x),
                        centre.y + ((corner & 2) == 0 ? -extents.y : extents.y),
                        centre.z + ((corner & 4) == 0 ? -extents.z : extents.z));

                    Vector3 point = matrix.MultiplyPoint3x4(local);
                    _min = Vector3.Min(_min, point);
                    _max = Vector3.Max(_max, point);
                }

                _any = true;
            }

            public bool TryClose(ref Bounds bounds)
            {
                if (!_any)
                {
                    return false;
                }

                // Everything above was gathered in the root's own space, which is the one space the
                // root's scale is invisible in — worldToLocalMatrix carries its inverse and every
                // child's localToWorldMatrix carries it, so the two cancel and a prefab scaled to
                // twice its size measures the same as the prefab it was scaled from. Applying it
                // here is what makes the row say the size the map will stand the art at. See
                // WorldRealizer, which composes the same scale rather than overwriting it.
                var measured = new Bounds();
                measured.SetMinMax(
                    Vector3.Scale(_min, _rootScale), Vector3.Scale(_max, _rootScale));

                bounds = measured;
                return true;
            }
        }

        /// <summary>Writes what a measured box says into a row.</summary>
        /// <remarks>
        /// Internal rather than private because the merge tool writes a row for a prefab it has
        /// just created, and a row measured a second way would be a row that disagreed with the
        /// next sync of the same folder.
        /// </remarks>
        internal static void Apply(CatalogAsset.Row row, Bounds bounds)
        {
            row.FootprintSize = new Vector2(bounds.size.x, bounds.size.z);
            row.FootprintOffset = new Vector2(bounds.center.x, bounds.center.z);
            row.Height = Mathf.Max(0f, bounds.max.y);
            row.BaseOffset = Mathf.Max(0f, -bounds.min.y);
        }

        /// <summary>
        /// The row to write when the catalog already had one: the scan's answers, under the row's
        /// own name, with the hand work the scan has no opinion about.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The logical id is the row's, not the scan's.</strong> A row is matched by the
        /// prefab it points at as well as by its id — see <see cref="IndexOfRowBoundTo"/> — so the
        /// two can differ, and when they do it is because the file was renamed or moved under a
        /// row that has been in this catalog all along. An id is what a placement in a saved world
        /// document names, so renaming one to follow a file would break every map that already
        /// used it; the tags do follow the folder, because those are what a query asks about.
        /// </para>
        /// Weight and sockets survive a sync because nothing in a folder says what they should be.
        /// The measured numbers — footprint, its offset, height and base offset — survive only when
        /// there was nothing to measure at all, so a sync run after an art fix picks the fix up
        /// rather than preserving the stale number. A prefab that has only meshes is measurable now
        /// and was not before, so a size typed in by hand for one of those is replaced the next time
        /// the button is pressed. That is the rule doing what it says rather than an exception to
        /// it: a measurement beats a guess, and what the row held was a guess about art the sync
        /// could not read.
        /// </remarks>
        static CatalogAsset.Row Merge(CatalogAsset.Row existing, CatalogAsset.Row scanned)
        {
            if (existing == null)
            {
                return scanned;
            }

            scanned.LogicalId = existing.LogicalId;
            scanned.Weight = existing.Weight;
            scanned.Sockets = existing.Sockets;

            // The same rule the measurements follow, applied to the other thing a prefab can state
            // about itself: what the scan read wins, and what the row already held survives only
            // where the prefab said nothing at all. A generated building bakes its own markers on
            // the way out, so the scan reads its doorways off the asset like anything else; what
            // this keeps is a row somebody typed the rectangles into, and a building exported
            // before the markers were baked at all.
            if (scanned.Doorways == null || scanned.Doorways.Count == 0)
            {
                scanned.Doorways = existing.Doorways;
            }

            if (!TryMeasure(scanned.Prefab, out _))
            {
                scanned.FootprintSize = existing.FootprintSize;
                scanned.FootprintOffset = existing.FootprintOffset;
                scanned.Height = existing.Height;
                scanned.BaseOffset = existing.BaseOffset;
            }

            return scanned;
        }

        static bool TryKnown(string folder, out Known known)
        {
            string name = folder.Trim().ToLowerInvariant();
            for (int i = 0; i < KnownFolders.Length; i++)
            {
                if (string.Equals(KnownFolders[i].Folder, name, StringComparison.Ordinal))
                {
                    known = KnownFolders[i];
                    return true;
                }
            }

            known = default;
            return false;
        }

        /// <summary>Lower case, with anything that is not a letter, digit or dot as an underscore.</summary>
        static string Slug(string name)
        {
            var slug = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = char.ToLowerInvariant(name[i]);
                slug.Append(char.IsLetterOrDigit(c) || c == '.' ? c : '_');
            }

            return slug.ToString();
        }

        readonly struct Known
        {
            public Known(string folder, string tag, FolderRole role, string alone = null)
            {
                Folder = folder;
                Tag = tag;
                Role = role;
                Alone = alone;
            }

            public string Folder { get; }

            public string Tag { get; }

            /// <summary>What this name does to the path being built.</summary>
            public FolderRole Role { get; }

            /// <summary>
            /// The path this name means with nothing above it to qualify, or null when it means
            /// the same thing either way.
            /// </summary>
            /// <remarks>
            /// Only a <see cref="FolderRole.Branch"/> needs one, and only because it extends. A
            /// folder of decor at the top of an art pack is <c>prop/decor</c>, exactly as it was
            /// before the deeper layout existed; the same folder under <c>PropBuilding</c> is that
            /// building's decor and says so. A name that restarts already carries its whole path,
            /// and one that only qualifies — <c>Low</c>, <c>High</c> — has nothing to say alone.
            /// </remarks>
            public string Alone { get; }

            /// <summary>The path segments to append, given how much path is already down.</summary>
            public string PathFrom(int depth) => depth == 0 && Alone != null ? Alone : Tag;
        }
    }
}
