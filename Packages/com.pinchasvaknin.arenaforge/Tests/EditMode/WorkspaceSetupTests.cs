using System.Text.RegularExpressions;
using ArenaForge.Core;
using ArenaForge.Editor;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ArenaForge.Tests
{
    /// <summary>
    /// One menu item that scaffolds the folders, files the demo art into them and writes the
    /// starter art — and the migration underneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The suite builds its own demo rather than reaching for the sample in the project. The
    /// migration searches for prefabs by name, and a test that searched the whole project would
    /// walk the real demo art into a folder it then deletes — so every run is scoped to a sandbox,
    /// which is what the search-folder argument on <see cref="ArenaWorkspace.Setup"/> is for.
    /// </para>
    /// <para>
    /// What is worth asserting is where things ended up and what still points at what. A migration
    /// that moved everything correctly and broke one material reference has done more damage than
    /// one that moved nothing.
    /// </para>
    /// </remarks>
    public sealed class WorkspaceSetupTests
    {
        const string Sandbox = "Assets/ArenaForgeSetupTests";
        const string Legacy = Sandbox + "/Legacy";
        const string Root = Sandbox + "/Workspace";

        const string Crate = "cover_low_crate_wood_01";
        const string Wall = "structure_wall_panel_2m";

        [SetUp]
        public void WriteALegacyDemo()
        {
            Delete();
            AssetDatabase.CreateFolder("Assets", "ArenaForgeSetupTests");
            AssetDatabase.CreateFolder(Sandbox, "Legacy");

            Material shared = WriteMaterial("Crate");

            // Nothing references this one, exactly as nothing but the demo scene references the
            // real demo's ground material. It is here to prove the folder sweep finds it.
            WriteMaterial("Ground");

            WritePrefab(Crate, shared);
            WritePrefab(Wall, shared);
            WritePrefab("NotDemo", shared);

            AssetDatabase.CreateAsset(new Mesh { name = "crate_mesh" }, $"{Legacy}/crate_mesh.asset");
            AssetDatabase.CreateAsset(
                ScriptableObject.CreateInstance<CatalogAsset>(), $"{Legacy}/DemoCatalog.asset");

            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void Clean() => Delete();

        static void Delete()
        {
            if (AssetDatabase.IsValidFolder(Sandbox))
            {
                AssetDatabase.DeleteAsset(Sandbox);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>How many prefabs <see cref="ArenaAssetBuilder"/> writes.</summary>
        /// <remarks>
        /// Named rather than repeated, because what these tests are about is the three steps
        /// running in order and none of them being skipped — a number that changes whenever the
        /// starter set gains a piece is not the property, it is the arithmetic in front of it.
        /// </remarks>
        const int StarterPrefabs = 4;

        // --- the three steps, in one gesture -----------------------------------------------------

        [Test]
        public void TheSetupFilesTheDemoArtAndThenWritesTheStarterArt()
        {
            WorkspaceSetupResult result = ArenaWorkspace.Setup(Root, Sandbox);

            Assert.That(result.Catalog, Is.Not.Null);
            Assert.That(result.Migration.Prefabs, Is.EqualTo(2), "two of the prefabs are the demo's");
            Assert.That(result.Migration.Skipped, Is.Zero);
            Assert.That(result.StarterArt.Count, Is.EqualTo(StarterPrefabs));

            Assert.That(Exists($"{Root}/Props/Covers/Low/{Crate}.prefab"), Is.True);
            Assert.That(Exists($"{Root}/Props/PropBuilding/Walls/{Wall}.prefab"), Is.True);

            Assert.That(Exists($"{Root}/Props/PropBuilding/Stairs/{ArenaAssetBuilder.StairsName}.prefab"), Is.True);
            Assert.That(Exists($"{Root}/Props/PropBuilding/Parapets/{ArenaAssetBuilder.FenceName}.prefab"), Is.True);
            Assert.That(Exists($"{Root}/Props/PropBuilding/Floors/{ArenaAssetBuilder.FloorName}.prefab"), Is.True);
            Assert.That(Exists($"{Root}/Props/PropBuilding/Windows/{ArenaAssetBuilder.WindowName}.prefab"), Is.True);
        }

        /// <remarks>
        /// The whole point of filing art by folder: the sync reads the folder names, so a demo
        /// that has been walked into the workspace binds to the tags the generators query with
        /// nothing typed in.
        /// </remarks>
        [Test]
        public void TheFiledDemoArtSyncsIntoTheTagsTheGeneratorQueries()
        {
            WorkspaceSetupResult result = ArenaWorkspace.Setup(Root, Sandbox);
            CatalogSync.Sync(result.Catalog, Root);

            Catalog catalog = result.Catalog.ToCatalog();

            Assert.That(
                catalog.Query(TagQuery.All(CoverPlacer.CoverTag, CoverPlacer.LowCoverTag)),
                Is.Not.Empty,
                "the demo crate did not come back as low cover");
            Assert.That(
                catalog.Query(TagQuery.All(BuildingGenerator.WallTag)), Is.Not.Empty,
                "the demo wall panel did not come back as a wall");
        }

        // --- prefabs and the art they are made of ------------------------------------------------

        [Test]
        public void TheRawArtGoesToTheMeshFolderAndWhatUsedItStillDoes()
        {
            ArenaWorkspace.Setup(Root, Sandbox);

            string material = $"{Root}/{ArenaWorkspace.MeshFolder}/Crate.mat";

            Assert.That(Exists(material), Is.True);
            Assert.That(Exists($"{Root}/{ArenaWorkspace.MeshFolder}/Ground.mat"), Is.True,
                "a material nothing references was left behind");
            Assert.That(Exists($"{Root}/{ArenaWorkspace.MeshFolder}/crate_mesh.asset"), Is.True);

            var wall = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Props/PropBuilding/Walls/{Wall}.prefab");
            Material bound = wall.GetComponent<MeshRenderer>().sharedMaterial;

            Assert.That(bound, Is.Not.Null, "the move broke the prefab's material reference");
            Assert.That(AssetDatabase.GetAssetPath(bound), Is.EqualTo(material));
        }

        /// <remarks>
        /// A prop folder is read as a statement about tags, so a material in <c>PropBuilding/Walls</c> is
        /// a wall that is not a prefab — one more thing the sync has to be told to ignore.
        /// </remarks>
        [Test]
        public void NoPropFolderEndsUpHoldingRawArt()
        {
            ArenaWorkspace.Setup(Root, Sandbox);

            Assert.That(
                AssetDatabase.FindAssets("t:Material", new[] { $"{Root}/Props" }), Is.Empty);
            Assert.That(
                AssetDatabase.FindAssets("t:Mesh", new[] { $"{Root}/Props" }), Is.Empty);
        }

        /// <remarks>
        /// <c>.asset</c> is the extension of a saved mesh and of every ScriptableObject in the
        /// project, the demo's own catalog included. Filing a catalog under <c>Meshes</c> would be
        /// the migration deciding by extension what it can only decide by type.
        /// </remarks>
        [Test]
        public void ACatalogIsNotRawArtAndStaysWhereItIs()
        {
            ArenaWorkspace.Setup(Root, Sandbox);

            Assert.That(Exists($"{Legacy}/DemoCatalog.asset"), Is.True);
            Assert.That(Exists($"{Root}/{ArenaWorkspace.MeshFolder}/DemoCatalog.asset"), Is.False);
        }

        /// <remarks>
        /// The migration knows a closed list of names — the prefabs the package ships. A rule over
        /// prefixes would reach into a project's own art, and a tool that moves art you did not
        /// ask it to move is one you stop running.
        /// </remarks>
        [Test]
        public void APrefabTheDemoNeverShippedIsLeftWhereItIs()
        {
            ArenaWorkspace.Setup(Root, Sandbox);

            Assert.That(Exists($"{Legacy}/NotDemo.prefab"), Is.True);
        }

        // --- running it again ---------------------------------------------------------------------

        /// <remarks>
        /// Running it twice is what a person does when they cannot remember whether they ran it,
        /// and after re-importing the sample it is what they are meant to do. The second run must
        /// find nothing left to move, leave one copy of everything, and not empty the catalog.
        /// </remarks>
        [Test]
        public void RunningTheSetupTwiceMovesNothingTheSecondTime()
        {
            WorkspaceSetupResult first = ArenaWorkspace.Setup(Root, Sandbox);
            first.Catalog.SetRows(new[]
            {
                new CatalogAsset.Row { LogicalId = "cover/low/typed_in", Weight = 1f },
            });
            EditorUtility.SetDirty(first.Catalog);
            AssetDatabase.SaveAssets();

            WorkspaceSetupResult second = ArenaWorkspace.Setup(Root, Sandbox);

            Assert.That(second.Migration.Prefabs, Is.Zero);
            Assert.That(second.Migration.Art, Is.Zero);
            Assert.That(second.Migration.Skipped, Is.Zero);
            Assert.That(second.Catalog.Rows.Count, Is.EqualTo(1), "the second run emptied the catalog");
            Assert.That(
                AssetDatabase.FindAssets("t:Prefab", new[] { Root }).Length,
                Is.EqualTo(2 + StarterPrefabs),
                "two demo prefabs and the starter prefabs, once each");
        }

        /// <remarks>
        /// The three ways demo art can be absent — never imported, renamed, deleted — are one
        /// case: the name is not found, so there is nothing to move. None of them may stop the
        /// rest of the setup.
        /// </remarks>
        [Test]
        public void DemoArtThatWasRenamedOrDeletedIsSimplyNotFound()
        {
            AssetDatabase.DeleteAsset($"{Legacy}/{Crate}.prefab");
            AssetDatabase.RenameAsset($"{Legacy}/{Wall}.prefab", "wall_panel_of_my_own");
            AssetDatabase.SaveAssets();

            WorkspaceSetupResult result = ArenaWorkspace.Setup(Root, Sandbox);

            Assert.That(result.Migration.Prefabs, Is.Zero);
            Assert.That(result.Migration.Skipped, Is.Zero, "a name that is not there is nothing to skip");
            Assert.That(Exists($"{Legacy}/wall_panel_of_my_own.prefab"), Is.True,
                "a renamed prefab was moved anyway");
            Assert.That(result.StarterArt.Count, Is.EqualTo(StarterPrefabs),
                "the rest of the setup did not run");
        }

        /// <remarks>
        /// A taken destination means the workspace already has something of that name — a previous
        /// run's work, or the project's own art. Overwriting it to make the counts tidy would be
        /// the tool destroying the thing it was asked to organise.
        /// </remarks>
        [Test]
        public void ADestinationThatIsAlreadyTakenIsLeftAlone()
        {
            ArenaWorkspace.Create(Root);
            var placeholder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(placeholder, $"{Root}/Props/PropBuilding/Walls/{Wall}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(placeholder);
            }

            LogAssert.Expect(LogType.Warning, new Regex("is already taken"));
            WorkspaceSetupResult result = ArenaWorkspace.Setup(Root, Sandbox);

            Assert.That(result.Migration.Skipped, Is.EqualTo(1));
            Assert.That(result.Migration.Prefabs, Is.EqualTo(1), "the other demo prefab was filed anyway");
            Assert.That(Exists($"{Legacy}/{Wall}.prefab"), Is.True,
                "the demo prefab was moved over one that was already there");
        }

        [Test]
        public void AMigrationWithNowhereToSearchIsRefused()
        {
            Assert.Throws<System.InvalidOperationException>(
                () => DemoMigration.Migrate(Root, "Assets/NoSuchFolder"));
            Assert.Throws<System.ArgumentException>(() => DemoMigration.Migrate(" ", Sandbox));
        }

        // --- the folder browser --------------------------------------------------------------------

        /// <remarks>
        /// <para>
        /// What every tool here that asks a person where to put something needs from the editor's
        /// own folder panel: the panel answers with an absolute path and Unity's asset API takes an
        /// <c>Assets/…</c> one. The tools browse the project rather than offering a list of folders
        /// somebody else chose, so this conversion is on the path of all of them.
        /// </para>
        /// <para>
        /// Backslashes because that is what the panel returns on Windows, and it is a path that
        /// looks right and matches nothing.
        /// </para>
        /// </remarks>
        [Test]
        public void AnAbsoluteFolderInsideTheProjectComesBackAsAProjectPath()
        {
            string assets = Application.dataPath;

            Assert.That(ArenaWorkspace.ProjectFolder(assets), Is.EqualTo("Assets"));
            Assert.That(
                ArenaWorkspace.ProjectFolder(assets + "/ArenaWorkspace/Props"),
                Is.EqualTo("Assets/ArenaWorkspace/Props"));
            Assert.That(
                ArenaWorkspace.ProjectFolder(assets.Replace('/', '\\') + "\\ArenaWorkspace"),
                Is.EqualTo("Assets/ArenaWorkspace"));
            Assert.That(
                ArenaWorkspace.ProjectFolder(assets + "/ArenaWorkspace/"),
                Is.EqualTo("Assets/ArenaWorkspace"),
                "a trailing slash is a path the panel can return and Unity cannot use");
        }

        /// <remarks>
        /// Null rather than a guess. Unity cannot write an asset outside the project at all, so the
        /// tool that asked can say which folder was refused — writing into the workspace instead
        /// would put the prefab somewhere the person did not choose and did not look.
        /// </remarks>
        [Test]
        public void AFolderOutsideTheProjectIsRefusedRatherThanGuessedAt()
        {
            string outside = System.IO.Path
                .GetDirectoryName(Application.dataPath.Replace('\\', '/'))
                ?.Replace('\\', '/') + "/Library";

            Assert.That(ArenaWorkspace.ProjectFolder(outside), Is.Null);
            Assert.That(ArenaWorkspace.ProjectFolder(null), Is.Null);
            Assert.That(ArenaWorkspace.ProjectFolder("   "), Is.Null);

            // A sibling folder whose name starts with the project's own is not inside it.
            Assert.That(ArenaWorkspace.ProjectFolder(Application.dataPath + "Extra"), Is.Null);
        }

        // --- helpers ------------------------------------------------------------------------------

        static bool Exists(string path) => AssetDatabase.LoadMainAssetAtPath(path) != null;

        static Material WriteMaterial(string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, $"{Legacy}/{name}.mat");
            return material;
        }

        static void WritePrefab(string name, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                cube.GetComponent<MeshRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(cube, $"{Legacy}/{name}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(cube);
            }
        }
    }
}
