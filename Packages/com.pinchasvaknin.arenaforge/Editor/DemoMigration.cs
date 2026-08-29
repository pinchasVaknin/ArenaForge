using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// What one run of the demo migration did.
    /// </summary>
    public readonly struct DemoMigrationResult
    {
        /// <summary>Creates a result.</summary>
        public DemoMigrationResult(int prefabs, int art, int skipped)
        {
            Prefabs = prefabs;
            Art = art;
            Skipped = skipped;
        }

        /// <summary>Demo prefabs filed into a prop folder.</summary>
        public int Prefabs { get; }

        /// <summary>Materials and meshes moved into the workspace's raw-art folder.</summary>
        public int Art { get; }

        /// <summary>Assets left where they were, each one reported as a warning.</summary>
        public int Skipped { get; }

        /// <summary>A one-line summary for a status line or the console.</summary>
        public override string ToString()
        {
            if (Prefabs == 0 && Art == 0 && Skipped == 0)
            {
                return "no demo art left to file";
            }

            var text = new StringBuilder();
            text.Append(Prefabs).Append(" demo prefabs filed, ").Append(Art).Append(" materials and meshes moved");

            if (Skipped > 0)
            {
                text.Append(", ").Append(Skipped).Append(" left where they were");
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Files the demo art the package ships into a workspace: each prefab into the prop folder
    /// that names its tag, and the materials and meshes it is built from into the raw-art folder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The demo is a sample, and a sample is scattered by design — one flat folder holding nine
    /// prefabs, seven materials and a catalog, because that is what Package Manager copies. It is
    /// also the only art most projects have on the first day. So rather than leave a new workspace
    /// empty beside a folder of art that cannot be synced into it, the setup walks the art over:
    /// <c>structure_wall_panel_2m</c> lands in <c>Props/PropBuilding/Walls</c>, and the next
    /// <see cref="CatalogSync"/> reads <c>structure/wall</c> off the folder without anyone typing
    /// a row.
    /// </para>
    /// <para>
    /// <strong>Prefabs and the art they are made of are filed apart.</strong> A prop folder is
    /// read as a statement about tags, so a material sitting in <c>PropBuilding/Walls</c> is a
    /// wall that is not a prefab — nothing the sync can use and one more thing it would have to be told to
    /// ignore. Materials and meshes therefore go to
    /// <see cref="ArenaWorkspace.MeshFolder"/>, which is where the starter art already writes its
    /// own, and the prop folders hold nothing but props.
    /// </para>
    /// <para>
    /// <strong>Moving is safe and copying would not be.</strong>
    /// <see cref="AssetDatabase.MoveAsset"/> keeps an asset's GUID, so the demo scene, the demo
    /// catalog and every prefab's reference to its material all follow the move without being
    /// rewritten. Copying would leave two of everything with one of them bound to nothing, and
    /// the demo would be the one that broke.
    /// </para>
    /// <para>
    /// <strong>Nothing here is required to be there.</strong> The sample can be absent, already
    /// filed, renamed or deleted, and each case is the same case: the name is not found, so there
    /// is nothing to move. That is what makes the setup safe to run a second time — which is
    /// exactly what a person does after re-importing the sample, and the run after that one finds
    /// the fresh copy and files it too.
    /// </para>
    /// </remarks>
    public static class DemoMigration
    {
        /// <summary>The whole project — what <c>Setup Complete Workspace</c> searches.</summary>
        public const string ProjectRoot = "Assets";

        /// <summary>
        /// The demo's prefabs, and the workspace folder each one belongs in.
        /// </summary>
        /// <remarks>
        /// A closed list of exact names rather than a rule over prefixes, for the same reason
        /// <see cref="CatalogSync"/> keeps a closed table of folder names: these are the assets the
        /// package ships and nothing else. A rule that filed everything called
        /// <c>structure_wall_*</c> would reach into a project's own art and move it, and a tool
        /// that moves art you did not ask it to move is one you stop running.
        /// </remarks>
        static readonly DemoPiece[] Pieces =
        {
            new DemoPiece("cover_low_crate_wood_01", "Props/Covers/Low"),
            new DemoPiece("cover_low_sandbags_01", "Props/Covers/Low"),
            new DemoPiece("cover_high_barrier_concrete_01", "Props/Covers/High"),
            new DemoPiece("marker_spawn", "Props/Spawns"),
            new DemoPiece("structure_building_two_storey_01", "Buildings"),
            new DemoPiece("structure_doorway_frame_2m", "Props/PropBuilding/Doorways"),
            new DemoPiece("structure_floor_slab_2m", "Props/PropBuilding/Floors"),
            new DemoPiece("structure_house_small_01", "Props/Houses"),
            new DemoPiece("structure_wall_panel_2m", "Props/PropBuilding/Walls"),
        };

        /// <summary>
        /// Moves whatever demo art is found under <paramref name="searchIn"/> into the workspace
        /// at <paramref name="root"/>.
        /// </summary>
        /// <param name="root">Project-relative workspace root, for example <c>Assets/ArenaWorkspace</c>.</param>
        /// <param name="searchIn">
        /// Project-relative folder to search — <see cref="ProjectRoot"/> to search the project.
        /// </param>
        /// <returns>What was moved, and what was left alone.</returns>
        /// <exception cref="ArgumentException"><paramref name="root"/> is blank.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="searchIn"/> is not a folder.</exception>
        public static DemoMigrationResult Migrate(string root, string searchIn)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("A migration needs somewhere to file art into.", nameof(root));
            }

            if (string.IsNullOrWhiteSpace(searchIn) || !AssetDatabase.IsValidFolder(searchIn.TrimEnd('/')))
            {
                throw new InvalidOperationException(
                    $"'{searchIn}' is not a folder in this project, so there is nothing to search.");
            }

            root = root.TrimEnd('/');
            List<Filing> found = Find(searchIn.TrimEnd('/'), root);

            // Read before anything moves: a dependency is looked up by path, and the folder an
            // asset came out of is the folder its loose materials are still sitting in.
            List<string> art = RawArtFor(found, root);

            int prefabs = 0;
            int skipped = 0;

            for (int i = 0; i < found.Count; i++)
            {
                if (ArenaWorkspace.MoveInto(found[i].Path, root + "/" + found[i].Folder))
                {
                    prefabs++;
                }
                else
                {
                    skipped++;
                }
            }

            int moved = 0;
            for (int i = 0; i < art.Count; i++)
            {
                if (ArenaWorkspace.MoveInto(art[i], root + "/" + ArenaWorkspace.MeshFolder))
                {
                    moved++;
                }
                else
                {
                    skipped++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new DemoMigrationResult(prefabs, moved, skipped);
        }

        /// <summary>
        /// The demo prefabs under <paramref name="searchIn"/> that are not already filed, in path
        /// order.
        /// </summary>
        /// <remarks>
        /// Sorted rather than left in whatever order the asset database enumerated, so two runs
        /// over the same project move the same assets in the same order and report the same
        /// counts — the same reason <see cref="CatalogSync.Scan"/> sorts its paths.
        /// </remarks>
        static List<Filing> Find(string searchIn, string root)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { searchIn });
            var found = new List<Filing>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (IsUnder(path, root) || !TryFolderFor(path, out string folder))
                {
                    continue;
                }

                found.Add(new Filing(path, folder));
            }

            found.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return found;
        }

        /// <summary>
        /// The materials and meshes the demo is built from: everything raw sitting beside a demo
        /// prefab, and everything raw any of them references.
        /// </summary>
        /// <remarks>
        /// Both, because neither on its own is the demo's art. A dependency walk misses the
        /// material the demo <em>scene</em> paints its ground with, which no prefab references; a
        /// folder sweep misses art a prefab reached for from somewhere else. Sweeping only the
        /// folders that actually held a demo prefab is what keeps the second half from wandering
        /// into a project's own art — a folder earns the sweep by having the demo in it.
        /// </remarks>
        static List<string> RawArtFor(List<Filing> found, string root)
        {
            var art = new List<string>();
            var swept = new List<string>();

            for (int i = 0; i < found.Count; i++)
            {
                string folder = Path.GetDirectoryName(found[i].Path)?.Replace('\\', '/') ?? string.Empty;
                if (folder.Length > 0 && !swept.Contains(folder))
                {
                    swept.Add(folder);
                    SweepFolder(folder, root, art);
                }

                string[] dependencies = AssetDatabase.GetDependencies(found[i].Path, true);
                for (int d = 0; d < dependencies.Length; d++)
                {
                    Consider(dependencies[d], root, art);
                }
            }

            art.Sort(StringComparer.Ordinal);
            return art;
        }

        /// <summary>Adds the raw art lying directly in one folder.</summary>
        /// <remarks>
        /// The folder itself and not what is under it. A demo prefab that someone has already
        /// dragged into a folder of their own would otherwise pull that whole tree's materials
        /// along with it, and the blast radius of a mistake in this file should be one folder.
        /// </remarks>
        static void SweepFolder(string folder, string root, List<string> art)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            string[] files = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                Consider(files[i].Replace('\\', '/'), root, art);
            }
        }

        static void Consider(string path, string root, List<string> art)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
                IsUnder(path, root) ||
                !IsRawArt(path) ||
                art.Contains(path))
            {
                return;
            }

            art.Add(path);
        }

        /// <summary>
        /// True for a material, a model, or a <c>.asset</c> that holds a mesh.
        /// </summary>
        /// <remarks>
        /// The extension decides everything except <c>.asset</c>, which is the extension Unity
        /// gives a saved mesh <em>and</em> every ScriptableObject in the project — the demo's own
        /// <c>DemoCatalog</c> among them. A catalog is not raw art and does not belong in a folder
        /// of meshes, so that one case is settled by asking what the asset actually is.
        /// </remarks>
        static bool IsRawArt(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mat":
                case ".fbx":
                case ".obj":
                case ".blend":
                case ".dae":
                    return true;

                case ".asset":
                    Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                    return type != null && typeof(Mesh).IsAssignableFrom(type);

                default:
                    return false;
            }
        }

        /// <summary>The folder a demo prefab belongs in, if this is one of the demo's prefabs.</summary>
        static bool TryFolderFor(string assetPath, out string folder)
        {
            string name = Path.GetFileNameWithoutExtension(assetPath);
            for (int i = 0; i < Pieces.Length; i++)
            {
                if (string.Equals(Pieces[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    folder = Pieces[i].Folder;
                    return true;
                }
            }

            folder = null;
            return false;
        }

        static bool IsUnder(string path, string root) =>
            path.StartsWith(root + "/", StringComparison.Ordinal);

        /// <summary>One of the demo's prefabs, and where it goes.</summary>
        readonly struct DemoPiece
        {
            public DemoPiece(string name, string folder)
            {
                Name = name;
                Folder = folder;
            }

            /// <summary>File name, without the extension.</summary>
            public string Name { get; }

            /// <summary>Destination folder, relative to the workspace root.</summary>
            public string Folder { get; }
        }

        /// <summary>One demo prefab that was found, and where it is going.</summary>
        readonly struct Filing
        {
            public Filing(string path, string folder)
            {
                Path = path;
                Folder = folder;
            }

            /// <summary>Where the prefab is now.</summary>
            public string Path { get; }

            /// <summary>Destination folder, relative to the workspace root.</summary>
            public string Folder { get; }
        }
    }
}
