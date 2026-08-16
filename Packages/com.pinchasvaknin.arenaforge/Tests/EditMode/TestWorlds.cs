using System.Collections.Generic;
using ArenaForge.Core;
using NUnit.Framework;

// UnityEngine is referenced by its full name rather than imported: it also declares a Pose type,
// and an ambiguous reference here would be a confusing way to learn that.

namespace ArenaForge.Tests
{
    /// <summary>
    /// The sample catalog and world used across the suites, built in code so it can be compared
    /// against the committed fixtures under Tests/Fixtures.
    /// </summary>
    static class TestWorlds
    {
        const string FixtureFolder = "Packages/com.pinchasvaknin.arenaforge/Tests/Fixtures";

        /// <remarks>
        /// Through the asset database rather than the file system, because a package installed from
        /// a git URL lives in <c>Library/PackageCache</c> under a hashed folder name and there is no
        /// <c>Packages/…</c> path on disk to open. Unity resolves the logical path either way.
        /// </remarks>
        public static string ReadFixture(string fileName)
        {
            string path = FixtureFolder + "/" + fileName;
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(path);
            Assert.That(asset, Is.Not.Null, $"Missing fixture: {path}");
            return asset.text;
        }

        /// <summary>
        /// The sample catalog, deliberately constructed out of logical-id order so the tests can
        /// show that the catalog, not the caller, decides enumeration order.
        /// </summary>
        public static Catalog SampleCatalog() => new Catalog(new[]
        {
            new CatalogEntry(
                "structure/house/small_01",
                new[] { "structure", "structure/house" },
                new Rect2(-4f, -3f, 4f, 3f),
                3.5f,
                1f,
                null),
            new CatalogEntry(
                "cover/low/crate_wood_01",
                new[] { "cover", "cover/low", "wood" },
                new Rect2(-0.5f, -0.5f, 0.5f, 0.5f),
                1f,
                3f,
                new[]
                {
                    new CatalogSocket("top", new[] { "prop_surface" }, Pose.At(new Vec3(0f, 1f, 0f))),
                }),
            new CatalogEntry(
                "marker/spawn",
                new[] { "marker", "spawn" },
                new Rect2(-1f, -1f, 1f, 1f),
                0.5f,
                1f,
                null),
            new CatalogEntry(
                "cover/high/barrier_concrete_01",
                new[] { "cover", "cover/high", "concrete" },
                new Rect2(-1f, -0.25f, 1f, 0.25f),
                1.8f,
                1f,
                null),
            new CatalogEntry(
                "structure/building/two_storey_01",
                new[] { "structure", "structure/building" },
                new Rect2(-6f, -5f, 6f, 5f),
                6f,
                1f,
                null),
            new CatalogEntry(
                "cover/low/sandbags_01",
                new[] { "cover", "cover/low", "fabric" },
                new Rect2(-1f, -0.5f, 1f, 0.5f),
                0.9f,
                2f,
                null),
        });

        /// <summary>Logical ids of <see cref="SampleCatalog"/> in the order the catalog holds them.</summary>
        public static readonly string[] SampleCatalogOrder =
        {
            "cover/high/barrier_concrete_01",
            "cover/low/crate_wood_01",
            "cover/low/sandbags_01",
            "marker/spawn",
            "structure/building/two_storey_01",
            "structure/house/small_01",
        };

        /// <summary>A five-object map carrying one override of each kind.</summary>
        public static WorldDoc SampleWorld()
        {
            var doc = new WorldDoc { Parameters = new ArenaParams { Seed = 20260816UL } };

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/spawn_a/marker",
                "marker/spawn",
                Pose.At(new Vec3(0f, 0f, -26f)),
                new[] { "marker", "spawn" },
                new Dictionary<string, string> { { "team", "a" } }));

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/spawn_b/marker",
                "marker/spawn",
                new Pose(new Vec3(0f, 0f, 26f), HalfTurn, 1f),
                new[] { "marker", "spawn" },
                new Dictionary<string, string> { { "team", "b" } }));

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_mid/structure_00",
                "structure/building/two_storey_01",
                Pose.At(Vec3.Zero),
                new[] { "structure", "structure/building" },
                new Dictionary<string, string>
                {
                    { "lane", "lane_mid" },
                    { "doorway_00", "-2,4,2,4.5" },
                }));

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_north/cover_00",
                "cover/low/crate_wood_01",
                Pose.At(new Vec3(-14.5f, 0f, 8f)),
                new[] { "cover", "cover/low" },
                new Dictionary<string, string> { { "lane", "lane_north" } }));

            doc.GeneratedObjects.Add(new PlacedObject(
                "map/lane_south/cover_00",
                "cover/low/sandbags_01",
                new Pose(new Vec3(14.5f, 0f, -8f), HalfTurn, 1f),
                new[] { "cover", "cover/low" },
                new Dictionary<string, string> { { "lane", "lane_south" } }));

            doc.Overrides.Add(EditOverride.Move(
                "map/lane_north/cover_00", Pose.At(new Vec3(-12f, 0f, 9.5f))));
            doc.Overrides.Add(EditOverride.Delete("map/lane_south/cover_00"));
            doc.Overrides.Add(EditOverride.Add(
                "user/crate_00",
                "cover/low/crate_wood_01",
                Pose.At(new Vec3(3.5f, 0f, -4f)),
                new[] { "cover", "cover/low", "user" },
                new Dictionary<string, string> { { "note", "placed by hand" } }));
            doc.Overrides.Add(EditOverride.SwapAsset(
                "map/lane_mid/structure_00", "structure/house/small_01"));

            return doc;
        }

        /// <summary>
        /// An exact 180 degree yaw. Written out rather than produced by
        /// <see cref="Quat.FromYawDegrees"/> so the fixture's float text does not depend on the
        /// runtime's trigonometry.
        /// </summary>
        public static Quat HalfTurn => new Quat(0f, 1f, 0f, 0f);
    }
}
