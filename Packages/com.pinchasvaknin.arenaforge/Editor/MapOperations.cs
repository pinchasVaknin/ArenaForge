using System;
using System.IO;
using ArenaForge.Core;
using ArenaForge.Unity;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// What a whole-map operation did: the world it resolved to, or the reason it did not run.
    /// </summary>
    readonly struct MapOperationResult
    {
        MapOperationResult(ResolvedWorld resolved, string error)
        {
            Resolved = resolved;
            Error = error;
        }

        /// <summary>The resolution the operation produced, or null if it failed.</summary>
        public ResolvedWorld Resolved { get; }

        /// <summary>Why the operation did not run, or null if it did.</summary>
        public string Error { get; }

        /// <summary>True if the operation ran and the document now holds its result.</summary>
        public bool Succeeded => Error == null;

        /// <summary>A successful outcome.</summary>
        public static MapOperationResult Ok(ResolvedWorld resolved) =>
            new MapOperationResult(resolved, null);

        /// <summary>A refusal, carrying the message to show the user.</summary>
        public static MapOperationResult Failed(string error) =>
            new MapOperationResult(null, error);
    }

    /// <summary>
    /// The editor-side mutations of a map that more than one surface performs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generating, regenerating, clearing and loading all have the same shape — take an undo
    /// snapshot, run something that can throw, and either collapse the whole thing into one named
    /// step or put the document back the way it was. That shape lived in the tool window while the
    /// window was the only thing performing it. The scene-view overlay is the second caller, so it
    /// moved here.
    /// </para>
    /// <para>
    /// The same is true of the two file panels. Saving and loading a map were the window's while
    /// only the window offered them; the overlay's Map tab offers them too now, and a second copy
    /// of "which folder, which extension, what the file is called by default" is a second copy to
    /// keep in step.
    /// </para>
    /// <para>
    /// It is a handful of static methods over the component being worked on, deliberately.
    /// Neither caller needs to be told when the other acts — both re-read the component they
    /// already hold — so there is nothing here to be an object, and no state for a controller to
    /// own. Nothing here shows a message either: each surface reports in its own way, and the
    /// window's status line and the overlay's are not the same line.
    /// </para>
    /// <para>
    /// Generic over that component rather than typed to <see cref="ArenaMap"/>, because
    /// <see cref="ArenaBuilding"/> is rebuilt the same way and by the same overlay. The type
    /// parameter is the whole of the generalisation: there is no interface the two components
    /// implement and no base class between them, because nothing in here asks either of them for
    /// anything beyond what <c>Undo</c> asks of any Unity object.
    /// </para>
    /// </remarks>
    static class MapOperations
    {
        /// <summary>
        /// Runs one whole-document operation inside a single named undo group.
        /// </summary>
        /// <remarks>
        /// An operation that throws leaves nothing behind: the undo stack is reverted to the group
        /// it started from, which puts the document back as it was, and the message comes out in the
        /// result for the caller to show. A partial regeneration — a document replaced but a scene
        /// half realised, or the reverse — would be a map that neither the tool nor the user could
        /// describe.
        /// </remarks>
        public static MapOperationResult Run<T>(T target, string action, Func<T, ResolvedWorld> operation)
            where T : UnityEngine.Object
        {
            // Assigned to a UnityEngine.Object local before the null test on purpose. C# does not
            // apply a constraint type's user-defined operators to a type parameter, so `target ==
            // null` inside a generic method is plain reference equality — which is true of a
            // destroyed Unity object that this check exists to catch.
            UnityEngine.Object bound = target;
            if (bound == null)
            {
                return MapOperationResult.Failed("There is nothing bound to work on.");
            }

            string name = "ArenaForge: " + action;
            int group = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(bound, name);

            ResolvedWorld resolved;
            try
            {
                resolved = operation(target);
            }
            catch (Exception error) when (error is InvalidOperationException ||
                                          error is ArgumentException ||
                                          error is UnsupportedSchemaVersionException)
            {
                Undo.RevertAllDownToGroup(group);
                return MapOperationResult.Failed(error.Message);
            }

            EditorUtility.SetDirty(bound);
            Undo.SetCurrentGroupName(name);
            Undo.CollapseUndoOperations(group);

            return MapOperationResult.Ok(resolved);
        }

        /// <summary>
        /// Changes one parameter on the component, as its own undo step.
        /// </summary>
        /// <remarks>
        /// Not collapsed with anything: a parameter change is not a regeneration, and until the user
        /// presses Generate it has not moved a single object.
        /// </remarks>
        public static void Edit<T>(T target, string action, Action<T> apply)
            where T : UnityEngine.Object
        {
            UnityEngine.Object bound = target;
            if (bound == null)
            {
                return;
            }

            Undo.RegisterCompleteObjectUndo(bound, "ArenaForge: " + action);
            apply(target);
            EditorUtility.SetDirty(bound);
        }

        /// <summary>
        /// Asks for a path and writes the map's document to it.
        /// </summary>
        /// <remarks>
        /// The file the panel writes is the same bytes the scene holds — <see cref="ArenaMap"/>
        /// keeps its document as serialised JSON precisely so there is one persistence path and
        /// not two. Saving is therefore a copy, not a conversion, and nothing about it can drift
        /// from what a regeneration would produce.
        /// </remarks>
        /// <returns>The path written, or null if the user cancelled.</returns>
        /// <exception cref="InvalidOperationException">The map has nothing to save yet.</exception>
        /// <exception cref="IOException">The file could not be written.</exception>
        public static string SaveWorld(ArenaMap map)
        {
            WorldDoc doc = map != null ? map.Document : null;
            if (doc == null)
            {
                throw new InvalidOperationException("There is no map to save yet.");
            }

            string path = EditorUtility.SaveFilePanel(
                "Save ArenaForge map", Application.dataPath, "arena_" + map.Seed, "json");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            File.WriteAllText(path, ArenaJson.SerializeWorld(doc));

            // Only worth doing when the file landed inside the project; outside it there is
            // nothing for the asset database to notice.
            AssetDatabase.Refresh();
            return path;
        }

        /// <summary>
        /// Asks for a file and reads a world document out of it.
        /// </summary>
        /// <returns>The document, or null if the user cancelled.</returns>
        /// <exception cref="UnsupportedSchemaVersionException">The file is of a schema this build does not read.</exception>
        /// <exception cref="InvalidOperationException">The file is a document of another kind.</exception>
        /// <exception cref="ArgumentException">The file is not a world document.</exception>
        /// <exception cref="IOException">The file could not be read.</exception>
        public static WorldDoc OpenWorld()
        {
            string path = EditorUtility.OpenFilePanel("Load ArenaForge map", Application.dataPath, "json");
            return string.IsNullOrEmpty(path)
                ? null
                : ArenaJson.DeserializeWorld(File.ReadAllText(path));
        }

        /// <summary>A seed to re-roll to.</summary>
        /// <remarks>
        /// The project's own RNG rather than <c>System.Random</c>, per CLAUDE.md — seeded from the
        /// clock because picking a fresh seed is the one thing in the tool that is meant not to be
        /// repeatable.
        /// </remarks>
        public static ulong RandomSeed()
        {
            var rng = new Rng((ulong)DateTime.UtcNow.Ticks);
            return ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
        }
    }
}
