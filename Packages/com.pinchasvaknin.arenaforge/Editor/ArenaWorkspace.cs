using System;
using System.Collections.Generic;
using System.IO;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// What one run of the workspace setup did.
    /// </summary>
    public readonly struct WorkspaceSetupResult
    {
        /// <summary>Creates a result.</summary>
        public WorkspaceSetupResult(
            CatalogAsset catalog,
            int relocated,
            DemoMigrationResult migration,
            IReadOnlyList<GameObject> starterArt)
        {
            Catalog = catalog;
            Relocated = relocated;
            Migration = migration;
            StarterArt = starterArt;
        }

        /// <summary>The workspace's catalog, new or already there.</summary>
        public CatalogAsset Catalog { get; }

        /// <summary>Assets an earlier layout had filed that were moved to their new folders.</summary>
        public int Relocated { get; }

        /// <summary>What the demo migration moved.</summary>
        public DemoMigrationResult Migration { get; }

        /// <summary>The starter prefabs, written last.</summary>
        public IReadOnlyList<GameObject> StarterArt { get; }

        /// <summary>A one-line summary for a status line or the console.</summary>
        public override string ToString()
        {
            string relocated = Relocated > 0 ? $"{Relocated} assets relocated, " : string.Empty;
            return $"{relocated}{Migration}, {StarterArt.Count} starter prefabs written";
        }
    }

    /// <summary>
    /// Creates the folder layout a project keeps its own art and catalog in, files the demo art
    /// into it, and writes the starter art on the end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The demo that ships with the package is a sample: it is copied into a project by the
    /// Package Manager, it is overwritten when the sample is re-imported, and its catalog is
    /// written to be read rather than edited. Every project that starts by adding rows to
    /// <c>DemoCatalog</c> loses them the first time it updates the package, so the tool offers
    /// somewhere else to work before anyone has to find that out.
    /// </para>
    /// <para>
    /// The subfolders are not decoration. <see cref="CatalogSync"/> reads tags out of folder
    /// names, so this layout is the same statement as a hand-written catalog would be — a prefab
    /// dropped into <c>Props/Covers/Low</c> and synced comes back tagged <c>cover</c> and
    /// <c>cover/low</c>, which is what the cover placer queries for. A workspace of three empty
    /// folders would scaffold the filing and leave the part that does the work undone.
    /// </para>
    /// <para>
    /// <strong>It is one menu item because it is one job.</strong> Scaffolding the folders,
    /// filing the demo art into them and writing the starter art were three commands, and the
    /// first two are useless on their own: a workspace of empty folders syncs to an empty catalog,
    /// and art with nowhere to go cannot be filed. What a person wants from the Tools menu is a
    /// workspace with something in it. Every step is idempotent, so the item can be run again
    /// whenever it is not clear whether it was run before — which is the state anyone who has just
    /// re-imported the sample is in.
    /// </para>
    /// </remarks>
    public static class ArenaWorkspace
    {
        /// <summary>Where <see cref="SetupFromMenu"/> puts a workspace.</summary>
        public const string DefaultRoot = "Assets/ArenaWorkspace";

        /// <summary>Folder the catalog asset is written into, under the workspace root.</summary>
        public const string CatalogFolder = "Catalog";

        /// <summary>Folder the raw art — meshes and materials — is kept in, under the root.</summary>
        /// <remarks>
        /// Beside the prefabs rather than among them, and it is the same folder for art this tool
        /// generated and art it filed. A mesh has to be an asset before a prefab can reference it,
        /// and a folder of its own keeps the prop folders holding nothing but props — which is
        /// what <see cref="CatalogSync"/> reads, so a stray material in <c>Props/Walls</c> would
        /// be one more thing it has to be told to ignore.
        /// </remarks>
        public const string MeshFolder = "Meshes";

        /// <summary>File name of the catalog a new workspace gets.</summary>
        public const string CatalogName = "ArenaCatalog";

        /// <summary>
        /// The folders a workspace holds, each one relative to its root.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every name below <c>Props</c> is one <see cref="CatalogSync"/> recognises; the rest are
        /// filing. <c>Buildings</c> is where <c>BuildingExport</c> is meant to be pointed, so an
        /// exported building is picked up by the next sync as a <c>structure/building</c> row, and
        /// <c>Meshes</c> holds the raw art the prop folders must not.
        /// </para>
        /// <para>
        /// <strong>The tree is deep because placement is specific.</strong> A folder here does not
        /// say what a prop <em>is</em>, it says where the generator is allowed to put it — which is
        /// the only question the placement stages ask. <c>PropBuilding/Decor/Decoration</c> is what
        /// goes in a room's corners and <c>PropBuilding/Decor/Centerpieces</c> is what goes in its
        /// middle; both are furniture, and a single <c>Decor</c> folder would have made them one
        /// query returning both. Outside the building the same split is
        /// <c>OutDecor/ContinueAround</c>, which tiles round the shell without a gap, against
        /// <c>OutDecor/UniqueGroup</c>, which lands in clusters.
        /// </para>
        /// <para>
        /// <c>Covers</c> stays at the top, beside <c>PropBuilding</c> rather than under it, and it
        /// is the one folder whose separation is about the map rather than the building. Tactical
        /// cover is what a fight is fought around and <c>CoverPlacer</c> queries it by
        /// <c>cover/low</c> and <c>cover/high</c>; a sideboard filed in with it would be shot
        /// over.
        /// </para>
        /// <para>
        /// <strong>Which is why a room's clutter has a folder of its own.</strong>
        /// <c>PropBuilding/Decor/InteriorCovers</c> is the third thing that goes inside a building,
        /// beside the corner decor and the centrepiece, and it is what
        /// <see cref="ArenaForge.Core.BuildingGenerator.InteriorCoverTag"/> queries. The floor
        /// scatter read <c>Props/Covers</c> once — the same rows the outdoor placer draws from —
        /// so a workspace that filed a dumpster where a dumpster belongs got one in the living
        /// room. The two folders are one level and one question apart, and nothing but the tag
        /// keeps them so.
        /// </para>
        /// <para>
        /// <strong>Which is why the folder in the middle of a room is <c>Centerpieces</c> and not
        /// <c>Covers</c>.</strong> It held that name once, and the two folders were kept apart by
        /// <see cref="CatalogSync.FolderRole.Branch"/> alone — correctly, but invisibly. Two folders
        /// a level apart with the same name on them is a trap whatever the table does with it: a
        /// person filing a dumpster reads the folder name, not the branch rule, and a person reading
        /// <c>propbuilding/decor/covers</c> in a catalog cannot tell at a glance which kind of cover
        /// it means. Distinct names make the distinction legible where it is actually made, which is
        /// in the workspace.
        /// </para>
        /// <para>
        /// <c>Road</c> splits the same way and for the same reason. <c>Road/Kerb</c> is art laid end
        /// to end along the edge of a carriageway, tiled with no gap in it; <c>Road/Furniture</c> is
        /// what stands on the verge behind that edging, a piece at a time and a spacing apart. They
        /// are two folders because <see cref="ArenaForge.Core.RoadKerbs"/> and
        /// <see cref="ArenaForge.Core.RoadFurniture"/> are two questions, and a lamp post drawn into
        /// a kerb run would be tiled into a line of lamp posts touching end to end.
        /// </para>
        /// </remarks>
        public static readonly string[] Folders =
        {
            "Buildings",
            CatalogFolder,
            MeshFolder,
            "Props",
            "Props/PropBuilding",
            "Props/PropBuilding/Decor",
            "Props/PropBuilding/Decor/Decoration",
            "Props/PropBuilding/Decor/InteriorCovers",
            "Props/PropBuilding/Decor/OutDecor",
            "Props/PropBuilding/Decor/OutDecor/ContinueAround",
            "Props/PropBuilding/Decor/OutDecor/UniqueGroup",
            "Props/PropBuilding/Decor/Centerpieces",
            "Props/PropBuilding/Decor/Corners",
            "Props/PropBuilding/Doorways",
            "Props/PropBuilding/Floors",
            "Props/PropBuilding/Parapets",
            "Props/PropBuilding/Stairs",
            "Props/PropBuilding/Walls",
            "Props/PropBuilding/Windows",
            "Props/Covers",
            "Props/Covers/High",
            "Props/Covers/Low",
            "Props/fence",
            "Props/fence/WoodFence",
            "Props/fence/StoneFence",
            "Props/Houses",
            "Props/Road",
            "Props/Road/Furniture",
            "Props/Road/Kerb",
            "Props/Spawns",
        };

        /// <summary>
        /// Folders an earlier layout put art in, and where that art belongs now.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A workspace is a folder of somebody's own art, so changing the map underneath it is not
        /// a thing that can be done by publishing a new one and hoping. <see cref="Relocate"/>
        /// walks this table and moves what it finds, which is what makes the new layout arrive on
        /// a project that already had the old one instead of arriving beside it.
        /// </para>
        /// <para>
        /// <c>Props/Decor</c> becomes <c>Decoration</c> and not one of the other three, because
        /// corners are where the decor pass already puts what it finds — so the art lands in the
        /// folder that describes what was being done with it rather than in the one that reads
        /// most like its old name.
        /// </para>
        /// <para>
        /// <c>Decor/Covers</c> becomes <c>Decor/Centerpieces</c>, which is a rename rather than a
        /// reorganisation: the same art, placed by the same pass, under a name that cannot be
        /// mistaken for the tactical cover in <c>Props/Covers</c>. It matters that this is on the
        /// table and not left to the person, because the tag the sync reads out of the folder is
        /// what <see cref="ArenaForge.Core.BuildingGenerator.CentrepieceTag"/> queries — a
        /// workspace left on the old name syncs to rows nothing asks for, and the failure looks
        /// like rooms that stopped being furnished.
        /// </para>
        /// <para>
        /// The list only shrinks from here. A folder is on it because a released version of this
        /// tool created it; once no project can still be on that version, its row is dead code.
        /// </para>
        /// </remarks>
        static readonly Relocation[] Relocations =
        {
            new Relocation("Props/Decor", "Props/PropBuilding/Decor/Decoration"),
            new Relocation(
                "Props/PropBuilding/Decor/Covers", "Props/PropBuilding/Decor/Centerpieces"),
            new Relocation("Props/Doorways", "Props/PropBuilding/Doorways"),
            new Relocation("Props/Floors", "Props/PropBuilding/Floors"),
            new Relocation("Props/Parapets", "Props/PropBuilding/Parapets"),
            new Relocation("Props/Stairs", "Props/PropBuilding/Stairs"),
            new Relocation("Props/Walls", "Props/PropBuilding/Walls"),
            new Relocation("Props/Windows", "Props/PropBuilding/Windows"),
        };

        /// <summary>
        /// Builds the workspace at <see cref="DefaultRoot"/> out of everything the project has:
        /// the folders, the demo art filed into them, and the starter art on the end.
        /// </summary>
        [MenuItem("Tools/ArenaForge/Setup Complete Workspace")]
        public static void SetupFromMenu()
        {
            WorkspaceSetupResult result = Setup(DefaultRoot, DemoMigration.ProjectRoot);

            Selection.activeObject = result.Catalog;
            EditorGUIUtility.PingObject(result.Catalog);
            Debug.Log(
                $"ArenaForge: workspace ready at {DefaultRoot} — {result}. Press Sync from Folders " +
                "on the catalog to measure it all in.", result.Catalog);
        }

        /// <summary>
        /// Creates the workspace under <paramref name="root"/>, files the demo art found under
        /// <paramref name="migrateFrom"/> into it, and writes the starter art.
        /// </summary>
        /// <remarks>
        /// The four steps run in that order because each needs the one before it: art cannot be
        /// filed into folders that do not exist, art an earlier layout filed has to reach its new
        /// folder before anything else is put in that folder's way, and the starter art is written
        /// last so that a demo floor slab and a generated floor tile end up in the same folder
        /// rather than racing for it.
        /// </remarks>
        /// <param name="root">Project-relative folder to build the workspace in.</param>
        /// <param name="migrateFrom">
        /// Project-relative folder to search for demo art — <see cref="DemoMigration.ProjectRoot"/>
        /// from the menu, so the whole project is searched.
        /// </param>
        /// <returns>What the run produced.</returns>
        /// <exception cref="ArgumentException"><paramref name="root"/> is blank or outside <c>Assets</c>.</exception>
        public static WorkspaceSetupResult Setup(string root, string migrateFrom)
        {
            CatalogAsset catalog = Create(root);
            int relocated = Relocate(root);
            DemoMigrationResult migration = DemoMigration.Migrate(root, migrateFrom);
            IReadOnlyList<GameObject> starterArt = ArenaAssetBuilder.Generate(root);

            return new WorkspaceSetupResult(catalog, relocated, migration, starterArt);
        }

        /// <summary>
        /// Creates the workspace folders under <paramref name="root"/> and the empty catalog
        /// inside it, leaving anything that is already there alone.
        /// </summary>
        /// <remarks>
        /// Idempotent on purpose: running it twice is what a person does when they cannot remember
        /// whether they ran it, and the second run must not be the one that empties the catalog.
        /// Every folder is checked with <see cref="AssetDatabase.IsValidFolder"/> before it is
        /// created, because <see cref="AssetDatabase.CreateFolder"/> does not refuse a name that
        /// is taken — it makes <c>Props 1</c> beside <c>Props</c>, which is a second workspace
        /// nobody asked for and a sync that reads half the art.
        /// </remarks>
        /// <param name="root">Project-relative folder to create the workspace in.</param>
        /// <returns>The workspace's catalog, new or already there.</returns>
        /// <exception cref="ArgumentException"><paramref name="root"/> is blank or outside <c>Assets</c>.</exception>
        public static CatalogAsset Create(string root)
        {
            root = RequireWorkspaceRoot(root);

            EnsureFolder(root);
            for (int i = 0; i < Folders.Length; i++)
            {
                EnsureFolder(root + "/" + Folders[i]);
            }

            string path = $"{root}/{CatalogFolder}/{CatalogName}.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<CatalogAsset>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CatalogAsset>();
                catalog.SourceFolder = root;
                AssetDatabase.CreateAsset(catalog, path);
            }
            else if (string.IsNullOrEmpty(catalog.SourceFolder))
            {
                catalog.SourceFolder = root;
                EditorUtility.SetDirty(catalog);
            }

            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>
        /// Moves art an earlier layout filed into the folder it belongs in now, and returns how
        /// many assets were moved.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="AssetDatabase.MoveAsset"/> rather than a copy, for the reason
        /// <see cref="DemoMigration"/> gives: a move keeps an asset's GUID, so every scene, prefab
        /// and catalog row pointing at it follows without being rewritten. A relocation that
        /// copied would leave a project with two of every wall and the catalog bound to the one
        /// nobody is editing.
        /// </para>
        /// <para>
        /// Whole trees rather than the prefabs in them. A project that had filed its walls under
        /// <c>Props/Walls/Brick</c> keeps that folder — it lands at
        /// <c>Props/PropBuilding/Walls/Brick</c>, and the tag it spells is unchanged, because
        /// <see cref="CatalogSync"/> restarts the path at <c>Walls</c> either way. Moving only the
        /// files would flatten somebody's filing to make this method simpler.
        /// </para>
        /// <para>
        /// Idempotent, like everything else here: the second run finds no old folder, moves
        /// nothing and reports zero. An asset whose name is already taken at the destination is
        /// left where it is with a warning rather than overwritten, so the source folder survives
        /// with the leftovers in it and nothing is lost to a collision.
        /// </para>
        /// </remarks>
        /// <param name="root">Project-relative workspace root.</param>
        /// <returns>How many assets were moved.</returns>
        /// <exception cref="ArgumentException"><paramref name="root"/> is blank or outside <c>Assets</c>.</exception>
        public static int Relocate(string root)
        {
            root = RequireWorkspaceRoot(root);

            int moved = 0;
            for (int i = 0; i < Relocations.Length; i++)
            {
                moved += MoveTree(
                    root + "/" + Relocations[i].From, root + "/" + Relocations[i].To);
            }

            if (moved > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return moved;
        }

        /// <summary>
        /// The project-relative form of an absolute folder path, or null when the folder is outside
        /// this project's <c>Assets</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// What turns the answer <see cref="UnityEditor.EditorUtility.OpenFolderPanel"/> gives —
        /// an absolute path on disk, with the platform's own separators — into the
        /// <c>Assets/…</c> path every asset API in this tool takes. Every tool here that asks a
        /// person where to put something offers that panel, because a dropdown of folders somebody
        /// else chose is not a folder browser.
        /// </para>
        /// <para>
        /// Null rather than a guess when the folder is somewhere else on the machine. A path
        /// outside the project cannot hold an asset at all, and the tool that asked can say so;
        /// silently writing into the workspace instead would put the prefab somewhere the person
        /// did not choose and did not look.
        /// </para>
        /// </remarks>
        internal static string ProjectFolder(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return null;
            }

            string chosen = absolutePath.Replace('\\', '/').TrimEnd('/');
            string assets = Application.dataPath.Replace('\\', '/').TrimEnd('/');

            if (string.Equals(chosen, assets, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets";
            }

            return chosen.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase)
                ? "Assets" + chosen.Substring(assets.Length)
                : null;
        }

        /// <summary>Creates one folder and every folder above it that is missing.</summary>
        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            string name = path.Substring(split + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>Moves one asset into a folder, or says why it could not.</summary>
        /// <remarks>
        /// A taken destination is a warning and not a failure. It means the workspace already has
        /// something of that name — a previous run's work, or a project's own art — and
        /// overwriting it to make the counts tidy would be the tool destroying the thing it was
        /// asked to organise.
        /// </remarks>
        internal static bool MoveInto(string path, string destinationFolder)
        {
            EnsureFolder(destinationFolder);
            string destination = destinationFolder + "/" + Path.GetFileName(path);

            if (AssetDatabase.LoadMainAssetAtPath(destination) != null)
            {
                Debug.LogWarning(
                    $"ArenaForge: left '{path}' where it is — '{destination}' is already taken.");
                return false;
            }

            string error = AssetDatabase.MoveAsset(path, destination);
            if (string.IsNullOrEmpty(error))
            {
                return true;
            }

            Debug.LogWarning($"ArenaForge: could not move '{path}' to '{destination}' — {error}");
            return false;
        }

        /// <summary>
        /// Moves everything under one folder into another, subfolders and all, and takes the
        /// source folder away once it is empty.
        /// </summary>
        /// <remarks>
        /// Depth first, so a child is emptied and removed before its parent is looked at and the
        /// parent is genuinely empty by the time it is tested. The source folder is only deleted
        /// when nothing is left in it: a collision, or a file Unity would not move, leaves the
        /// folder standing with its contents rather than taking them with it.
        /// </remarks>
        static int MoveTree(string from, string to)
        {
            if (!AssetDatabase.IsValidFolder(from))
            {
                return 0;
            }

            int moved = 0;

            string[] children = AssetDatabase.GetSubFolders(from);
            Array.Sort(children, StringComparer.Ordinal);
            for (int i = 0; i < children.Length; i++)
            {
                moved += MoveTree(children[i], to + "/" + Path.GetFileName(children[i]));
            }

            List<string> assets = AssetsIn(from);
            for (int i = 0; i < assets.Count; i++)
            {
                if (MoveInto(assets[i], to))
                {
                    moved++;
                }
            }

            if (AssetDatabase.GetSubFolders(from).Length == 0 && AssetsIn(from).Count == 0)
            {
                AssetDatabase.DeleteAsset(from);
            }

            return moved;
        }

        /// <summary>
        /// The assets lying directly in one folder, in path order.
        /// </summary>
        /// <remarks>
        /// Off the file system rather than out of <see cref="AssetDatabase.FindAssets(string, string[])"/>,
        /// which searches a whole tree and would move a subfolder's contents twice — once as its
        /// own tree and once as its parent's. The <c>.meta</c> files are skipped because
        /// <see cref="AssetDatabase.MoveAsset"/> takes each one along with the asset it describes;
        /// moving one on its own would separate an asset from its GUID, which is the one thing
        /// this whole method exists to avoid.
        /// </remarks>
        static List<string> AssetsIn(string folder)
        {
            var assets = new List<string>();
            if (!Directory.Exists(folder))
            {
                return assets;
            }

            string[] files = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i].Replace('\\', '/');
                if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    assets.Add(path);
                }
            }

            assets.Sort(StringComparer.Ordinal);
            return assets;
        }

        /// <summary>The root, trimmed, or an exception saying why it is not one.</summary>
        static string RequireWorkspaceRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("A workspace needs somewhere to go.", nameof(root));
            }

            root = root.TrimEnd('/');
            if (!root.Equals("Assets", StringComparison.Ordinal) &&
                !root.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"A workspace lives under Assets; '{root}' does not.", nameof(root));
            }

            return root;
        }

        /// <summary>One folder an earlier layout used, and where its contents go now.</summary>
        readonly struct Relocation
        {
            public Relocation(string from, string to)
            {
                From = from;
                To = to;
            }

            /// <summary>The old folder, relative to the workspace root.</summary>
            public string From { get; }

            /// <summary>The folder its contents belong in, relative to the workspace root.</summary>
            public string To { get; }
        }
    }
}
