using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ArenaForge.Editor
{
    /// <summary>
    /// Builds a starter set of art — a staircase, a roof fence, a floor tile and a window — as
    /// real meshes with exactly fitted colliders, and writes them into a workspace as prefabs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generator that composes prefabs is only as good as the prefabs, and the first thing anyone
    /// has to hand is Unity's primitives. A cube is a fine placeholder for a crate and a poor one
    /// for everything the shell is made of: a stair built from a stretched cube has no steps, and a
    /// parapet built from one is a metre-thick block on the edge of a roof. So this makes four
    /// pieces that are the right shape <em>and</em> the right size, which is the part that matters
    /// to the generator — see the class remarks on <see cref="CatalogSync"/> for why a row is a
    /// measurement rather than a description.
    /// </para>
    /// <para>
    /// <strong>Every piece is modelled on a metre grid, on its base, centred on its footprint.</strong>
    /// That is not decoration either. The floor tile is a metre square, so a stairwell opening is
    /// cut to the metre instead of to the two metres a larger tile would round it up to; the
    /// staircase is a whole number of those tiles across and along, so the opening is exactly the
    /// ground the flight covers; the fence is a metre long, so a run of it divides the roof it
    /// stands on and closes without a gap at the corners. Art whose sizes do not agree still works
    /// and simply leaves the slack the catalog declares — but the slack is what a person sees, so
    /// the starter set has none.
    /// </para>
    /// <para>
    /// The window is the exception that makes the rule plainer. It is not measured against the
    /// floor tile at all, because nothing lays it out: it is swapped into a module a wall run had
    /// already been tiled with, so what it has to match is the <em>wall</em> — the demo pack's two
    /// metres by 2.9, which is the module the whole plan is laid out in. A window of any other
    /// size is a gap in the middle of a run.
    /// </para>
    /// <para>
    /// This is the one place in the package that authors geometry, and it is a tool that writes an
    /// asset rather than a generator that builds a scene: nothing in <c>Runtime/</c> knows it
    /// exists, and a map is composed from the prefabs it wrote exactly as it would be from bought
    /// art. See ARCHITECTURE.md section 3.
    /// </para>
    /// </remarks>
    public static class ArenaAssetBuilder
    {
        /// <summary>File name of the generated staircase.</summary>
        public const string StairsName = "MyStairs_Gen";

        /// <summary>File name of the generated roof fence.</summary>
        public const string FenceName = "MyFence_Gen";

        /// <summary>File name of the generated floor tile.</summary>
        public const string FloorName = "MyFloor_1x1";

        /// <summary>File name of the generated window wall.</summary>
        public const string WindowName = "MyWindow_Gen";

        /// <summary>Folder the staircase is written into, under the workspace root.</summary>
        /// <remarks>
        /// One of the workspace's own prop folders and not a folder of its own beside them. The
        /// tag would come out the same either way — <see cref="CatalogSync"/> restarts the tag
        /// path at a name it recognises, so <c>Stairs</c> and <c>Props/PropBuilding/Stairs</c> both spell
        /// <c>structure/stairs</c> — but a workspace whose art is in folders that are not on its
        /// own map is a workspace with two places to put a staircase, and the next person to add
        /// one has to guess which.
        /// </remarks>
        public const string StairsFolder = "Props/PropBuilding/Stairs";

        /// <summary>Folder the roof fence is written into, under the workspace root.</summary>
        public const string FenceFolder = "Props/PropBuilding/Parapets";

        /// <summary>Folder the floor tile is written into, under the workspace root.</summary>
        public const string FloorFolder = "Props/PropBuilding/Floors";

        /// <summary>Folder the window wall is written into, under the workspace root.</summary>
        public const string WindowFolder = "Props/PropBuilding/Windows";

        /// <summary>How many steps the generated flight is cut into.</summary>
        /// <remarks>
        /// Twelve over three metres of rise and three of run: a quarter-metre step, which is steep
        /// for a staircase and is what a flight climbing a whole storey inside its own footprint
        /// has to be. Fewer would read as a ramp with lines on it.
        /// </remarks>
        const int StairSteps = 12;

        /// <summary>Footprint and height of the generated staircase, in metres.</summary>
        /// <remarks>
        /// Two metres wide because the building generator lays its walls out in two-metre modules
        /// and the corridors that leaves come out that wide, so a flight narrower than one is a
        /// flight with a strip of open stairwell beside it. Three long and three high is one whole
        /// storey climbed inside a footprint of two floor tiles by three — a whole number of tiles
        /// on both axes, which is what lets the opening be cut to exactly the ground it covers.
        /// </remarks>
        static readonly Vector3 StairsSize = new Vector3(2f, 3f, 3f);

        /// <summary>Length, thickness and height of the generated fence, in metres.</summary>
        static readonly Vector3 FenceSize = new Vector3(1f, 1f, 0.1f);

        /// <summary>Footprint and thickness of the generated floor tile, in metres.</summary>
        static readonly Vector3 FloorSize = new Vector3(1f, 0.2f, 1f);

        /// <summary>How thick the fence's rails and posts are, in metres.</summary>
        const float FenceBar = 0.1f;

        /// <summary>Length, thickness and height of the generated window wall, in metres.</summary>
        /// <remarks>
        /// A wall's measurements and not its own. A window is not a piece the plan makes room for:
        /// it is swapped into a module a wall run had already been tiled with, so anything other
        /// than the wall's own length leaves a gap in the middle of the run and anything other than
        /// its height is a step in the top of it. Two metres by 2.9 is what the demo art pack's
        /// <c>structure/wall</c> measures, which is the module the whole plan is laid out in.
        /// </remarks>
        static readonly Vector3 WindowSize = new Vector3(2f, 2.9f, 0.2f);

        /// <summary>Height of the window opening's sill and head above the wall's base, in metres.</summary>
        /// <remarks>
        /// In the upper half of the wall, which is where a window goes and also where it is worth
        /// having: an opening at eye height is one you can see and shoot through from standing,
        /// and the wall below it is the cover that makes the building worth being inside. The head
        /// stops short of the top so the piece has a lintel rather than an open-topped slot.
        /// </remarks>
        const float WindowSill = 1.5f;

        /// <inheritdoc cref="WindowSill"/>
        const float WindowHead = 2.4f;

        /// <summary>How much wall is left either side of the opening, in metres.</summary>
        const float WindowJamb = 0.4f;

        /// <summary>How thick the bar down the middle of the opening is, in metres.</summary>
        /// <remarks>
        /// Thinner than the wall and centred in it, so it sits inside the reveal rather than flush
        /// with either face. It is what makes the piece read as a window rather than as a hole
        /// somebody knocked in a wall, and it is the only part of this that is decoration.
        /// </remarks>
        const float WindowBar = 0.06f;

        /// <summary>How thick a tread and the riser under it are, in metres.</summary>
        /// <remarks>
        /// What makes the flight a staircase rather than a wedge. A step that reaches the floor
        /// fills everything under the run, and a building stacks its flights one above another in
        /// the same shaft — so the solid underside of the flight above is the ceiling of the
        /// flight below, a hand's breadth over the top tread and a whole storey over the bottom
        /// one. Cut to a slab of its own thickness the flight is open underneath, and every tread
        /// of the flight below has the same clearance over it.
        /// </remarks>
        const float StairThickness = 0.15f;

        /// <summary>
        /// Writes the four starter prefabs under <paramref name="root"/>, replacing any it wrote
        /// before.
        /// </summary>
        /// <remarks>
        /// Idempotent, like <see cref="ArenaWorkspace.Create"/> and for the same reason: running it
        /// twice is what a person does when they cannot remember whether they ran it. Each prefab
        /// is written over its own path, so a hand edit to one of them is lost on the second run —
        /// which is why they are named as generated art and a project that wants to keep an edit
        /// should rename the file first. It is the last step of
        /// <see cref="ArenaWorkspace.Setup"/> and has no menu item of its own.
        /// </remarks>
        /// <param name="root">Project-relative workspace root, for example <c>Assets/ArenaWorkspace</c>.</param>
        /// <returns>The prefabs written, in the order above.</returns>
        public static IReadOnlyList<GameObject> Generate(string root)
        {
            root = root.TrimEnd('/');
            Material material = Material(root);

            return new[]
            {
                Write(root, StairsFolder, StairsName, Stairs(), material),
                Write(root, FenceFolder, FenceName, Fence(), material),
                Write(root, FloorFolder, FloorName, Floor(), material),
                Write(root, WindowFolder, WindowName, Window(), material),
            };
        }

        // --- the four pieces ------------------------------------------------------------------

        /// <summary>
        /// A hollow flight: every step a tread of its own thickness with a riser closing the step
        /// under it, so the run is a diagonal with open air beneath it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Modelled from the foot of the run to the top with its pivot at the centre of the
        /// footprint, so the catalog measures it at exactly its own size with no offset — the shape
        /// that used to be declared nearly twice as long as it is, and the reason nobody has to type
        /// a footprint offset in behind a sync. See <see cref="CatalogSync.TryMeasure"/>.
        /// </para>
        /// <para>
        /// <strong>Two boxes a step rather than one, and the riser is what makes it affordable.</strong>
        /// A tread on its own is a slab <see cref="StairThickness"/> thick with its top face at the
        /// nosing — and since a tread is thinner than the step is high, a flight of nothing but
        /// treads is a flight of slabs floating clear of each other with daylight between them. The
        /// riser is the plate that closes the step: it runs from the underside of the tread below to
        /// the underside of this one, and it is the first riser, cut off at the floor rather than
        /// hanging under it, that stands the flight on the ground it is placed on.
        /// </para>
        /// <para>
        /// The two never occupy the same ground — the tread starts where the riser stops — so the
        /// mesh has no faces buried inside itself, and each meets its neighbour over a face rather
        /// than along an edge, so the run is one closed solid seen from the side.
        /// </para>
        /// </remarks>
        static List<Bounds> Stairs()
        {
            float rise = StairsSize.y / StairSteps;
            float run = StairsSize.z / StairSteps;
            float half = StairsSize.x * 0.5f;

            var boxes = new List<Bounds>(StairSteps * 2);
            for (int i = 0; i < StairSteps; i++)
            {
                float front = -StairsSize.z * 0.5f + i * run;
                float top = (i + 1) * rise;
                float soffit = top - StairThickness;

                // The riser first, from the underside of the step below — or from the floor, for
                // the one step that has no step below it.
                boxes.Add(Box(
                    -half, Mathf.Max(0f, soffit - rise), front,
                    half, soffit, front + StairThickness));

                boxes.Add(Box(-half, soffit, front, half, top, front + run));
            }

            return boxes;
        }

        /// <summary>A railing: a foot rail, a hand rail and a post at each end.</summary>
        /// <remarks>
        /// Thin on purpose. A parapet is tiled round the whole edge of a roof and stood round every
        /// opening a person could fall through, so a piece that is a solid metre thick is a wall
        /// where a railing was wanted — which is exactly what a stretched cube gives.
        /// </remarks>
        static List<Bounds> Fence()
        {
            float halfLength = FenceSize.x * 0.5f;
            float halfDepth = FenceSize.z * 0.5f;
            float top = FenceSize.y;

            return new List<Bounds>
            {
                // The two rails, run the whole length so a row of pieces reads as one railing.
                Box(-halfLength, top - FenceBar, -halfDepth, halfLength, top, halfDepth),
                Box(-halfLength, top * 0.45f, -halfDepth, halfLength, top * 0.45f + FenceBar, halfDepth),

                // A post at each end, so two pieces side by side share one apparent post.
                Box(-halfLength, 0f, -halfDepth, -halfLength + FenceBar, top, halfDepth),
                Box(halfLength - FenceBar, 0f, -halfDepth, halfLength, top, halfDepth),
            };
        }

        /// <summary>
        /// A wall module with a window in the upper half of it: a panel under the sill, a lintel
        /// over the head, a jamb either side, and a bar down the middle of the opening.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Five boxes rather than one with a hole, because a hole is not something a box has. The
        /// four solid pieces are the wall the opening leaves behind, so the piece measures exactly
        /// what a wall module measures — a sill panel and a lintel both running the full length,
        /// and the jambs filling the ends between them.
        /// </para>
        /// <para>
        /// <strong>The opening is a hole all the way through, colliders included.</strong> That is
        /// the point of the piece rather than a detail of it: a window you cannot see or shoot
        /// through is a wall with a picture of a window on it, and the generator swaps these into
        /// exterior runs precisely so a building has sightlines out of it. The bar down the middle
        /// is the one thing standing in the opening, and it is thin enough to see past.
        /// </para>
        /// </remarks>
        static List<Bounds> Window()
        {
            float half = WindowSize.x * 0.5f;
            float back = -WindowSize.z * 0.5f;
            float front = WindowSize.z * 0.5f;
            float bar = WindowBar * 0.5f;

            return new List<Bounds>
            {
                // The wall under the sill and the lintel over the head, both the full length.
                Box(-half, 0f, back, half, WindowSill, front),
                Box(-half, WindowHead, back, half, WindowSize.y, front),

                // The jambs, filling the ends of the opening between the two.
                Box(-half, WindowSill, back, -half + WindowJamb, WindowHead, front),
                Box(half - WindowJamb, WindowSill, back, half, WindowHead, front),

                // And the bar, inside the reveal rather than flush with either face.
                Box(-bar, WindowSill, -bar, bar, WindowHead, bar),
            };
        }

        /// <summary>A floor tile, a metre square and standing on its own base.</summary>
        static List<Bounds> Floor() => new List<Bounds>
        {
            Box(
                -FloorSize.x * 0.5f, 0f, -FloorSize.z * 0.5f,
                FloorSize.x * 0.5f, FloorSize.y, FloorSize.z * 0.5f),
        };

        // --- writing them out ------------------------------------------------------------------

        /// <summary>
        /// Builds one prefab from a set of boxes: one combined mesh, and one collider per box.
        /// </summary>
        /// <remarks>
        /// A collider per box rather than one round the lot, because the lot is not the shape: a
        /// single collider over a staircase is a ramp you cannot walk up. What the catalog measures
        /// is the box holding all of them, which for every piece here is exactly the size it was
        /// asked for.
        /// </remarks>
        static GameObject Write(
            string root, string folder, string name, List<Bounds> boxes, Material material)
        {
            string path = $"{root}/{folder}/{name}.prefab";
            ArenaWorkspace.EnsureFolder($"{root}/{folder}");

            Mesh mesh = SaveMesh(root, name, boxes);

            var piece = new GameObject(name);
            try
            {
                piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                piece.AddComponent<MeshRenderer>().sharedMaterial = material;

                for (int i = 0; i < boxes.Count; i++)
                {
                    BoxCollider box = piece.AddComponent<BoxCollider>();
                    box.center = boxes[i].center;
                    box.size = boxes[i].size;
                }

                return PrefabUtility.SaveAsPrefabAsset(piece, path);
            }
            finally
            {
                Object.DestroyImmediate(piece);
            }
        }

        /// <summary>Writes the combined mesh as its own asset and returns it.</summary>
        /// <remarks>
        /// Created before the prefab, never after: a prefab saved while its mesh is still a loose
        /// object in memory serialises a reference to nothing, and comes back as an invisible
        /// prefab with a working collider — which looks like a broken generator rather than a
        /// broken asset.
        /// </remarks>
        static Mesh SaveMesh(string root, string name, List<Bounds> boxes)
        {
            string folder = $"{root}/{ArenaWorkspace.MeshFolder}";
            ArenaWorkspace.EnsureFolder(folder);

            string path = $"{folder}/{name}.asset";
            Mesh mesh = Build(boxes);
            mesh.name = name;

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // Rewritten in place, so every prefab and scene already pointing at it follows the
            // change rather than being left on an orphan.
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        /// <summary>One mesh holding every box, with flat normals and no shared vertices.</summary>
        /// <remarks>
        /// Twenty-four vertices to a box rather than eight, because a cube's corner has three
        /// normals and a vertex can only carry one. Sharing them gives the soft, smeared shading
        /// that reads as a modelling mistake on anything with a hard edge, which is all of this.
        /// </remarks>
        static Mesh Build(List<Bounds> boxes)
        {
            var vertices = new List<Vector3>(boxes.Count * 24);
            var normals = new List<Vector3>(boxes.Count * 24);
            var uvs = new List<Vector2>(boxes.Count * 24);
            var triangles = new List<int>(boxes.Count * 36);

            for (int i = 0; i < boxes.Count; i++)
            {
                AddBox(boxes[i], vertices, normals, uvs, triangles);
            }

            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddBox(
            Bounds box,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles)
        {
            Vector3 min = box.min;
            Vector3 max = box.max;

            // Six faces, each its own four vertices: right, left, top, bottom, front, back.
            AddFace(vertices, normals, uvs, triangles, Vector3.right,
                new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z));

            AddFace(vertices, normals, uvs, triangles, Vector3.left,
                new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z));

            AddFace(vertices, normals, uvs, triangles, Vector3.up,
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z));

            AddFace(vertices, normals, uvs, triangles, Vector3.down,
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z));

            AddFace(vertices, normals, uvs, triangles, Vector3.back,
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z));

            AddFace(vertices, normals, uvs, triangles, Vector3.forward,
                new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z));
        }

        static void AddFace(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 normal,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            int first = vertices.Count;

            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
            }

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));

            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 1);
            triangles.Add(first);
            triangles.Add(first + 3);
            triangles.Add(first + 2);
        }

        /// <summary>
        /// The material the starter art is shown with, made once and shared by all of it.
        /// </summary>
        /// <remarks>
        /// Written rather than left null, because a renderer with no material draws magenta and a
        /// magenta staircase reads as a broken tool. The shader is looked up rather than named, so
        /// a project on the universal pipeline gets its lit shader and one on the built-in pipeline
        /// gets the standard shader; a project with neither gets whatever Unity falls back to,
        /// which is the same bargain any generated asset makes.
        /// </remarks>
        static Material Material(string root)
        {
            string path = $"{root}/{ArenaWorkspace.MeshFolder}/ArenaStarter.mat";
            ArenaWorkspace.EnsureFolder($"{root}/{ArenaWorkspace.MeshFolder}");

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "ArenaStarter" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>A box from two corners.</summary>
        static Bounds Box(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            var box = new Bounds();
            box.SetMinMax(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
            return box;
        }
    }
}
