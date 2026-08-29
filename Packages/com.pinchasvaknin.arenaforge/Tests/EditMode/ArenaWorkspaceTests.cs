using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ArenaForge.Core;
using ArenaForge.Editor;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The workspace scaffold: somewhere to keep a project's own art and catalog that is not the
    /// sample the package ships.
    /// </summary>
    public sealed class ArenaWorkspaceTests
    {
        const string Root = "Assets/ArenaForgeWorkspaceTests";

        /// <summary>
        /// The workspace layout, written out rather than read off <see cref="ArenaWorkspace.Folders"/>.
        /// </summary>
        /// <remarks>
        /// A test that iterated the array it is checking would pass whatever the array said, which
        /// is a test of nothing. This is the map the tool promises, so dropping a folder from it —
        /// or gaining one — has to be a deliberate edit in two places.
        /// </remarks>
        static readonly string[] Map =
        {
            "Buildings",
            "Catalog",
            "Meshes",
            "Props",
            "Props/Covers",
            "Props/Covers/High",
            "Props/Covers/Low",
            "Props/Houses",
            "Props/PropBuilding",
            "Props/PropBuilding/Decor",
            "Props/PropBuilding/Decor/Centerpieces",
            "Props/PropBuilding/Decor/Corners",
            "Props/PropBuilding/Decor/Decoration",
            "Props/PropBuilding/Decor/InteriorCovers",
            "Props/PropBuilding/Decor/OutDecor",
            "Props/PropBuilding/Decor/OutDecor/ContinueAround",
            "Props/PropBuilding/Decor/OutDecor/UniqueGroup",
            "Props/PropBuilding/Doorways",
            "Props/PropBuilding/Floors",
            "Props/PropBuilding/Parapets",
            "Props/PropBuilding/Stairs",
            "Props/PropBuilding/Walls",
            "Props/PropBuilding/Windows",
            "Props/Road",
            "Props/Road/Furniture",
            "Props/Road/Kerb",
            "Props/Spawns",
            "Props/fence",
            "Props/fence/StoneFence",
            "Props/fence/WoodFence",
        };

        /// <summary>
        /// The tag path each prop folder spells, written out for the same reason <see cref="Map"/>
        /// is.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The folder tree and the tag table are two halves of one statement and they are kept in
        /// two files, so this is where they are held against each other. A folder that stopped
        /// spelling what it used to spell would move every prefab in it to a tag nothing queries,
        /// and the catalog would look untouched while it happened — there is no row to inspect,
        /// only a query that comes back empty.
        /// </para>
        /// <para>
        /// The depths are the point of the layout. <c>propbuilding/decor/centerpieces</c> and
        /// <c>cover</c> are two different things — the sofa a room is arranged around and the crate
        /// a firefight is fought behind — and a table that collapsed either into the other would put
        /// a sideboard where the firefight is. The two were filed in two folders both called
        /// <c>Covers</c> once, a level apart, told apart by the branch rule alone; they are named
        /// apart now as well.
        /// </para>
        /// </remarks>
        static readonly (string Folder, string Tag)[] TagPaths =
        {
            ("Buildings", "structure/building"),
            ("Props/Covers", "cover"),
            ("Props/Covers/High", "cover/high"),
            ("Props/Covers/Low", "cover/low"),
            ("Props/Houses", "structure/house"),
            ("Props/PropBuilding", "propbuilding"),
            ("Props/PropBuilding/Decor", "propbuilding/decor"),
            ("Props/PropBuilding/Decor/Centerpieces", "propbuilding/decor/centerpieces"),
            ("Props/PropBuilding/Decor/Corners", "propbuilding/decor/corners"),
            ("Props/PropBuilding/Decor/Decoration", "propbuilding/decor/decoration"),
            ("Props/PropBuilding/Decor/InteriorCovers", "propbuilding/decor/interiorcovers"),
            ("Props/PropBuilding/Decor/OutDecor", "propbuilding/decor/outdecor"),
            ("Props/PropBuilding/Decor/OutDecor/ContinueAround", "propbuilding/decor/outdecor/continuearound"),
            ("Props/PropBuilding/Decor/OutDecor/UniqueGroup", "propbuilding/decor/outdecor/uniquegroup"),
            ("Props/PropBuilding/Doorways", "structure/doorway"),
            ("Props/PropBuilding/Floors", "structure/floor"),
            ("Props/PropBuilding/Parapets", "structure/parapet"),
            ("Props/PropBuilding/Stairs", "structure/stairs"),
            ("Props/PropBuilding/Walls", "structure/wall"),
            ("Props/PropBuilding/Windows", "structure/window"),
            ("Props/Road", "road"),
            ("Props/Road/Furniture", "road/furniture"),
            ("Props/Road/Kerb", "road/kerb"),
            ("Props/Spawns", "spawn"),
            ("Props/fence", "fence"),
            ("Props/fence/StoneFence", "fence/stonefence"),
            ("Props/fence/WoodFence", "fence/woodfence"),
        };

        [SetUp]
        [TearDown]
        public void Clean()
        {
            if (AssetDatabase.IsValidFolder(Root))
            {
                AssetDatabase.DeleteAsset(Root);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void AWorkspaceHasItsFoldersAndAnEmptyCatalog()
        {
            CatalogAsset catalog = ArenaWorkspace.Create(Root);

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Rows, Is.Empty, "a new workspace starts with nothing in it");
            Assert.That(catalog.SourceFolder, Is.EqualTo(Root),
                "the catalog knows which folder to sync from");

            for (int i = 0; i < ArenaWorkspace.Folders.Length; i++)
            {
                string folder = Root + "/" + ArenaWorkspace.Folders[i];
                Assert.That(AssetDatabase.IsValidFolder(folder), Is.True, folder);
            }

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CatalogAsset>(
                    $"{Root}/{ArenaWorkspace.CatalogFolder}/{ArenaWorkspace.CatalogName}.asset"),
                Is.Not.Null);
        }

        /// <remarks>
        /// The layout is what a project files its art by and what the sync reads tags out of, so
        /// it is a promise rather than an implementation detail. Compared as a whole set, because
        /// a folder that appeared uninvited is as much a change to the map as one that went
        /// missing.
        /// </remarks>
        [Test]
        public void AWorkspaceIsExactlyTheFolderMap()
        {
            ArenaWorkspace.Create(Root);

            var expected = new List<string>(Map);
            expected.Sort(StringComparer.Ordinal);

            Assert.That(FoldersUnder(Root), Is.EqualTo(expected));
        }

        /// <remarks>
        /// <see cref="AssetDatabase.CreateFolder"/> does not refuse a name that is taken — it
        /// makes <c>Props 1</c> beside <c>Props</c>. So the second run of a scaffold that did not
        /// check first leaves a project with two of every folder, half its art in each, and a sync
        /// that reads one of them.
        /// </remarks>
        [Test]
        public void CreatingAWorkspaceTwiceDuplicatesNoFolder()
        {
            ArenaWorkspace.Create(Root);
            ArenaWorkspace.Create(Root);

            var expected = new List<string>(Map);
            expected.Sort(StringComparer.Ordinal);

            Assert.That(FoldersUnder(Root), Is.EqualTo(expected));
        }

        /// <remarks>
        /// Running it twice is what a person does when they cannot remember whether they ran it,
        /// so the second run must not be the one that empties the catalog.
        /// </remarks>
        [Test]
        public void CreatingAWorkspaceTwiceKeepsTheCatalogItAlreadyHad()
        {
            CatalogAsset first = ArenaWorkspace.Create(Root);
            first.SetRows(new[]
            {
                new CatalogAsset.Row { LogicalId = "cover/low/crate", Weight = 1f },
            });
            EditorUtility.SetDirty(first);
            AssetDatabase.SaveAssets();

            CatalogAsset second = ArenaWorkspace.Create(Root);

            Assert.That(second, Is.Not.Null);
            Assert.That(second.Rows.Count, Is.EqualTo(1), "the second run emptied the catalog");
            Assert.That(second.Rows[0].LogicalId, Is.EqualTo("cover/low/crate"));
        }

        /// <remarks>
        /// Every folder under <c>Props</c> is one the sync recognises, so a workspace and a sync
        /// fit together without anyone having to learn the tag table. A scaffold whose folders
        /// produced a tag other than the one they promise would be filing art under a name
        /// nothing looks up.
        /// </remarks>
        [Test]
        public void EveryWorkspaceFolderSpellsTheTagPathItPromises()
        {
            ArenaWorkspace.Create(Root);

            for (int i = 0; i < TagPaths.Length; i++)
            {
                (string folder, string tag) = TagPaths[i];
                CatalogSync.TagsFor(Root, $"{Root}/{folder}/thing.prefab", out string spelled);

                Assert.That(spelled, Is.EqualTo(tag), folder);
            }
        }

        /// <remarks>
        /// The other half of the same statement, and the half a table of expected strings cannot
        /// make on its own: a folder listed in <see cref="TagPaths"/> that the scaffold no longer
        /// creates would still pass the test above, because <see cref="CatalogSync.TagsFor"/> reads
        /// a path rather than the disk.
        /// </remarks>
        [Test]
        public void EveryFolderThatHoldsArtHasATagPathOfItsOwn()
        {
            var tagged = new List<string>();
            for (int i = 0; i < TagPaths.Length; i++)
            {
                tagged.Add(TagPaths[i].Folder);
            }

            for (int i = 0; i < ArenaWorkspace.Folders.Length; i++)
            {
                string folder = ArenaWorkspace.Folders[i];
                if (folder == ArenaWorkspace.CatalogFolder ||
                    folder == ArenaWorkspace.MeshFolder ||
                    folder == "Props")
                {
                    // Filing rather than tagging: the catalog is not art, the mesh folder holds
                    // the materials and meshes that art is made of, and Props is the organising
                    // folder the recognised names below it restart the tag path from.
                    continue;
                }

                Assert.That(tagged, Does.Contain(folder), $"{folder} is scaffolded and unspoken for");
            }
        }

        /// <remarks>
        /// <para>
        /// <c>Props/Covers</c> is the tactical cover a firefight is fought around and
        /// <c>CoverPlacer</c> queries it by name; <c>PropBuilding/Decor/Centerpieces</c> is the piece
        /// that stands in the middle of a room. The second folder was called <c>Covers</c> too,
        /// which is the case the tag table's branch rule was rebuilt for — a rule that read every
        /// folder of that name as the first would have put furniture in the query the map is built
        /// from.
        /// </para>
        /// <para>
        /// It is still asserted from both ends after the rename, because the branch rule is what
        /// makes it true: everything under <c>Decor</c> is read as that branch's own vocabulary, so
        /// nothing filed there can answer a map query whatever a project decides to call its
        /// folders.
        /// </para>
        /// </remarks>
        [Test]
        public void DecorCentrepiecesAreNotTheCoverAFirefightIsFoughtAround()
        {
            string[] decor = CatalogSync.TagsFor(
                Root, $"{Root}/Props/PropBuilding/Decor/Centerpieces/sideboard.prefab", out _);

            Assert.That(decor, Does.Not.Contain(CoverPlacer.CoverTag));
            Assert.That(decor, Does.Contain("propbuilding/decor/centerpieces"));

            string[] tactical = CatalogSync.TagsFor(
                Root, $"{Root}/Props/Covers/Low/crate.prefab", out _);

            Assert.That(tactical, Does.Contain(CoverPlacer.CoverTag));
            Assert.That(tactical, Does.Contain(CoverPlacer.LowCoverTag));
        }

        /// <remarks>
        /// <para>
        /// The same statement about the third thing that goes inside a building. A room's floor
        /// scatter drew from <c>Props/Covers</c> once, which put dumpsters and concrete barriers in
        /// living rooms — so the clutter a room is fought through has a folder of its own, and the
        /// two must not answer each other's query in either direction.
        /// </para>
        /// <para>
        /// <c>InteriorCovers</c> spells its tag through the branch rule and not through the tag
        /// table, which is what makes the name safe: <c>Covers</c> is a name the table knows and
        /// restarts the path from, and everything under <c>Decor</c> is read as that branch's own
        /// vocabulary whatever the table says about it.
        /// </para>
        /// </remarks>
        [Test]
        public void InteriorCoversAreNotTheCoverAFirefightIsFoughtAround()
        {
            string[] indoors = CatalogSync.TagsFor(
                Root, $"{Root}/Props/PropBuilding/Decor/InteriorCovers/boxes.prefab", out _);

            Assert.That(indoors, Does.Contain(BuildingGenerator.InteriorCoverTag));
            Assert.That(indoors, Does.Not.Contain(CoverPlacer.CoverTag));
            Assert.That(indoors, Does.Not.Contain(CoverPlacer.LowCoverTag));

            string[] outdoors = CatalogSync.TagsFor(
                Root, $"{Root}/Props/Covers/Low/dumpster.prefab", out _);

            Assert.That(outdoors, Does.Not.Contain(BuildingGenerator.InteriorCoverTag));
        }

        // --- moving a project off the layout it was on ------------------------------------------

        /// <remarks>
        /// A workspace is a folder of somebody's own art, so a new layout that only applied to new
        /// workspaces would leave every project that already had one filing into folders the sync
        /// no longer reads. The move keeps the asset's GUID, which is what makes the catalog rows
        /// and scene references pointing at it follow rather than break.
        /// </remarks>
        [Test]
        public void ArtFiledUnderTheOldLayoutIsMovedToItsNewFolder()
        {
            ArenaWorkspace.Create(Root);

            string wall = WritePrefab("Props/Walls", "Panel");
            string decor = WritePrefab("Props/Decor", "Sideboard");
            WritePrefab("Props/Walls/Brick", "Header");

            string wallGuid = AssetDatabase.AssetPathToGUID(wall);

            Assert.That(ArenaWorkspace.Relocate(Root), Is.EqualTo(3));

            Assert.That(AssetDatabase.IsValidFolder($"{Root}/Props/Walls"), Is.False,
                "the emptied folder was left behind");
            Assert.That(AssetDatabase.IsValidFolder($"{Root}/Props/Decor"), Is.False);

            string moved = $"{Root}/Props/PropBuilding/Walls/Panel.prefab";
            Assert.That(Exists(moved), Is.True);
            Assert.That(AssetDatabase.AssetPathToGUID(moved), Is.EqualTo(wallGuid),
                "the move rewrote the asset rather than moving it");

            Assert.That(Exists($"{Root}/Props/PropBuilding/Decor/Decoration/Sideboard.prefab"), Is.True);
            Assert.That(Exists($"{Root}/Props/PropBuilding/Walls/Brick/Header.prefab"), Is.True,
                "a project's own subfolder was flattened");
        }

        /// <remarks>
        /// The rename that made the two <c>Covers</c> folders one word apart, which is a relocation
        /// rather than a reorganisation: the same art, placed by the same pass, under a name it
        /// cannot be confused by. It has to be on the table rather than left to the person, because
        /// the tag the sync reads out of the folder is the tag
        /// <see cref="BuildingGenerator.CentrepieceTag"/> queries — a workspace left on the old name
        /// syncs to rows nothing asks for, and what that looks like is rooms that stopped being
        /// furnished.
        /// </remarks>
        [Test]
        public void ACentrepieceFiledUnderTheOldCoversNameIsMovedToCenterpieces()
        {
            ArenaWorkspace.Create(Root);
            string sofa = WritePrefab("Props/PropBuilding/Decor/Covers", "Sofa");
            string guid = AssetDatabase.AssetPathToGUID(sofa);

            Assert.That(ArenaWorkspace.Relocate(Root), Is.EqualTo(1));

            string moved = $"{Root}/Props/PropBuilding/Decor/Centerpieces/Sofa.prefab";
            Assert.That(Exists(moved), Is.True);
            Assert.That(AssetDatabase.AssetPathToGUID(moved), Is.EqualTo(guid),
                "the move rewrote the asset rather than moving it");
            Assert.That(AssetDatabase.IsValidFolder($"{Root}/Props/PropBuilding/Decor/Covers"), Is.False,
                "the emptied folder was left behind");

            Assert.That(
                CatalogSync.TagsFor(Root, moved, out _),
                Does.Contain(BuildingGenerator.CentrepieceTag),
                "the moved art does not answer the query it was moved for");
        }

        /// <remarks>
        /// Running it twice is what a person does when they cannot remember whether they ran it.
        /// The second run has nothing to find, which is the same case as a workspace that was
        /// never on the old layout at all.
        /// </remarks>
        [Test]
        public void RelocatingTwiceMovesNothingTheSecondTime()
        {
            ArenaWorkspace.Create(Root);
            WritePrefab("Props/Walls", "Panel");

            Assert.That(ArenaWorkspace.Relocate(Root), Is.EqualTo(1));
            Assert.That(ArenaWorkspace.Relocate(Root), Is.Zero);
            Assert.That(Exists($"{Root}/Props/PropBuilding/Walls/Panel.prefab"), Is.True);
        }

        /// <remarks>
        /// The destination is already taken, so the move would have to overwrite a project's own
        /// art to go through. It does not: the asset stays where it is with a warning, and the
        /// folder it is in survives with it rather than being taken away underneath it.
        /// </remarks>
        [Test]
        public void ArtIsLeftWhereItIsRatherThanOverwritingWhatIsAlreadyThere()
        {
            ArenaWorkspace.Create(Root);
            WritePrefab("Props/Walls", "Panel");
            WritePrefab("Props/PropBuilding/Walls", "Panel");

            LogAssert.Expect(LogType.Warning, new Regex("is already taken"));

            Assert.That(ArenaWorkspace.Relocate(Root), Is.Zero);
            Assert.That(Exists($"{Root}/Props/Walls/Panel.prefab"), Is.True,
                "the art was moved over something that was already there");
            Assert.That(AssetDatabase.IsValidFolder($"{Root}/Props/Walls"), Is.True,
                "a folder that still had art in it was deleted");
        }

        /// <summary>Writes an empty prefab into a folder of the workspace and returns its path.</summary>
        static string WritePrefab(string folder, string name)
        {
            ArenaWorkspace.EnsureFolder($"{Root}/{folder}");
            string path = $"{Root}/{folder}/{name}.prefab";

            var placeholder = new GameObject(name);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(placeholder, path);
            }
            finally
            {
                Object.DestroyImmediate(placeholder);
            }

            return path;
        }

        static bool Exists(string path) => AssetDatabase.LoadMainAssetAtPath(path) != null;

        [Test]
        public void AWorkspaceOutsideAssetsIsRefused()
        {
            Assert.Throws<ArgumentException>(() => ArenaWorkspace.Create("Packages/somewhere"));
            Assert.Throws<ArgumentException>(() => ArenaWorkspace.Create(" "));
            Assert.Throws<ArgumentException>(() => ArenaWorkspace.Relocate("Packages/somewhere"));
            Assert.Throws<ArgumentException>(() => ArenaWorkspace.Relocate(" "));
        }

        /// <summary>Every folder under <paramref name="root"/>, relative to it and in path order.</summary>
        static List<string> FoldersUnder(string root)
        {
            var found = new List<string>();
            var pending = new List<string> { root };

            for (int i = 0; i < pending.Count; i++)
            {
                string[] children = AssetDatabase.GetSubFolders(pending[i]);
                for (int c = 0; c < children.Length; c++)
                {
                    found.Add(children[c].Substring(root.Length + 1));
                    pending.Add(children[c]);
                }
            }

            found.Sort(StringComparer.Ordinal);
            return found;
        }
    }
}
