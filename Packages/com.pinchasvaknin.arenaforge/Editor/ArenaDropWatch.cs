using System.Collections.Generic;
using ArenaForge.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Notices prefabs dropped into the scene and hands them to <see cref="ArenaEditCapture.Adopt"/>,
    /// so art dragged in from the Project window joins the map as a user object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It looks at the scene rather than listening for a notification, and the reason is
    /// that the notification cannot be tested.</strong> The first version of this subscribed to
    /// <c>ObjectChangeEvents.changesPublished</c>, which is the obvious way to hear about an object
    /// appearing. It never fired for a single drop. What it also never did was fail a test: the
    /// editor publishes no change at all for a prefab instantiation in a batch-mode run — see
    /// <c>EditorWorkflowTests.TheEditorPublishesNoChangeForAPrefabInstantiation</c> — so the delivery
    /// half of the feature shipped with no coverage possible and stayed broken while the suite
    /// stayed green. Comparing the scene against what was there last time uses nothing the headless
    /// runner cannot do, so the whole path is exercised by the suite that guards it.
    /// </para>
    /// <para>
    /// <strong>Adopted at rest, which is the same rule a drag of an existing object follows.</strong>
    /// A candidate has to sit still for one pass before it is taken. A prefab dragged in from the
    /// Project window is carried under the cursor by an instance that already exists, so a watcher
    /// that took the first thing it saw would take an object out of somebody's hand halfway through
    /// placing it. <see cref="ArenaEditCapture"/> settles a drag the same way and for the same
    /// reason.
    /// </para>
    /// <para>
    /// <strong>Only what has appeared since it started looking.</strong> The first pass after a
    /// domain reload files every root it finds as known and adopts none of them, so scenery that was
    /// already standing in the scene stays scenery. Without that, opening a scene would sweep every
    /// piece of catalog art in it into the document.
    /// </para>
    /// <para>
    /// <strong>It runs whether or not the tool window is open.</strong> That is a change from where
    /// this started: capture listens only while the window is on screen, on the bargain that a tool
    /// which mutates a document should do it while somebody is looking at it. Dropping art in is a
    /// deliberate act aimed at the map, and a person who drags a crate into an arena has said what
    /// they meant whether or not a particular window is open — so the bargain that still holds is
    /// the narrower one below: exactly one map, with a document, or nothing happens.
    /// </para>
    /// <para>
    /// <strong>One map or none.</strong> A dropped object belongs to a map, and with two generated
    /// maps loaded there is no way to tell which — so with anything other than exactly one, the pass
    /// files what it sees and adopts nothing. Guessing would put somebody's art in the wrong
    /// document, which is worse than leaving it where they dropped it.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    static class ArenaDropWatch
    {
        /// <summary>How often the scene is compared against the last pass, in seconds.</summary>
        /// <remarks>
        /// The interval <see cref="ArenaForgeWindow"/> ticks capture at, for the same reason: it is
        /// short enough that a drop is picked up while the hand is still on the mouse and long
        /// enough that the work does not show up in the editor's frame time.
        /// </remarks>
        const double Interval = 0.1;

        /// <summary>Roots already accounted for, by instance id.</summary>
        static readonly HashSet<int> Known = new HashSet<int>();

        /// <summary>Candidates seen once and where they were, waiting to stop moving.</summary>
        static readonly Dictionary<int, Vector3> Settling = new Dictionary<int, Vector3>();

        static readonly List<GameObject> Roots = new List<GameObject>();

        static double _next;
        static bool _baselined;

        static ArenaDropWatch()
        {
            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _next)
            {
                return;
            }

            _next = now + Interval;
            Pass();
        }

        /// <summary>
        /// Forgets everything, so the next pass is a first one again.
        /// </summary>
        /// <remarks>
        /// What a domain reload does, which is the only time it happens in the editor — and the
        /// only way a test can ask what the <em>first</em> pass does, since by the time one runs
        /// the statics are whatever the test before it left behind.
        /// </remarks>
        internal static void Restart()
        {
            Known.Clear();
            Settling.Clear();
            _baselined = false;
        }

        /// <summary>
        /// One comparison of the scene against the last one. Internal so the suite can drive it
        /// without the editor's update loop, which is the whole point of it working this way.
        /// </summary>
        internal static void Pass()
        {
            CurrentRoots(Roots);

            ArenaMap map = SoleMap(Roots);
            var live = new HashSet<int>();

            for (int i = 0; i < Roots.Count; i++)
            {
                GameObject root = Roots[i];
                int id = root.GetInstanceID();
                live.Add(id);

                if (Known.Contains(id))
                {
                    continue;
                }

                // Everything standing here before this started looking is scenery, and so is
                // everything at all when there is no one map to adopt it into.
                if (!_baselined || map == null || !ArenaEditCapture.CanAdopt(map, root))
                {
                    Known.Add(id);
                    Settling.Remove(id);
                    continue;
                }

                Vector3 at = root.transform.position;

                if (!Settling.TryGetValue(id, out Vector3 was))
                {
                    Settling[id] = at;
                    continue;
                }

                if (was != at)
                {
                    Settling[id] = at;
                    continue;
                }

                Settling.Remove(id);
                Known.Add(id);
                new ArenaEditCapture(map).Adopt(root);
            }

            Forget(Known, live);
            Forget(Settling, live);

            _baselined = true;
        }

        /// <summary>Drops what is no longer in the scene, so the sets do not grow without end.</summary>
        static void Forget(HashSet<int> known, HashSet<int> live)
        {
            known.RemoveWhere(id => !live.Contains(id));
        }

        static void Forget(Dictionary<int, Vector3> settling, HashSet<int> live)
        {
            var gone = new List<int>();
            foreach (KeyValuePair<int, Vector3> pair in settling)
            {
                if (!live.Contains(pair.Key))
                {
                    gone.Add(pair.Key);
                }
            }

            for (int i = 0; i < gone.Count; i++)
            {
                settling.Remove(gone[i]);
            }
        }

        /// <summary>Every root object of every loaded scene, including the prefab stage's own.</summary>
        static void CurrentRoots(List<GameObject> into)
        {
            into.Clear();

            // A prefab being edited in isolation is its own stage with its own scene, and a drop in
            // there belongs to the prefab rather than to any map. Filing its roots as known is what
            // stops them being adopted when the stage closes.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    into.AddRange(scene.GetRootGameObjects());
                }
            }

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene.isLoaded)
            {
                into.AddRange(stage.scene.GetRootGameObjects());
            }
        }

        /// <summary>
        /// The one generated map in the loaded scenes, or null where there is not exactly one.
        /// </summary>
        /// <remarks>
        /// The same question this pass asks, asked by <see cref="ArenaDragGuides"/> before a drop
        /// rather than after it. Shared rather than copied so the two can never disagree about
        /// which map a dropped object belongs to — a preview drawn against one map and an adoption
        /// into another would be worse than no preview at all.
        /// </remarks>
        internal static ArenaMap SoleMap()
        {
            var roots = new List<GameObject>();
            CurrentRoots(roots);
            return SoleMap(roots);
        }

        /// <summary>The one generated map among the roots, or null where there is not exactly one.</summary>
        static ArenaMap SoleMap(List<GameObject> roots)
        {
            ArenaMap found = null;

            for (int i = 0; i < roots.Count; i++)
            {
                ArenaMap[] maps = roots[i].GetComponentsInChildren<ArenaMap>(true);
                for (int m = 0; m < maps.Length; m++)
                {
                    if (!maps[m].HasDocument)
                    {
                        continue;
                    }

                    if (found != null)
                    {
                        return null;
                    }

                    found = maps[m];
                }
            }

            return found;
        }
    }
}
