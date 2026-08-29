using System.Collections.Generic;
using ArenaForge.Editor;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The Doorway Setup tool: markers on an imported house, and the row that ends up believing
    /// them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves are asserted, because either alone is a trap. A marker that the sync cannot
    /// match is a house that declares nothing however carefully it was placed, and a row that was
    /// not written is a house that declares it to nobody — the generator reads the catalog and
    /// never the prefab.
    /// </para>
    /// <para>
    /// The marker is read back through <see cref="CatalogSync.DoorwaysIn"/> rather than compared
    /// against numbers written here, so what the tool produces is checked against the reader that
    /// has to accept it rather than against a second opinion about it.
    /// </para>
    /// </remarks>
    public sealed class DoorwaySetupTests
    {
        const string Folder = "Assets/ArenaForgeDoorwayTests";
        const string HousePath = Folder + "/house.prefab";

        CatalogAsset _catalog;
        readonly List<GameObject> _made = new List<GameObject>();

        [SetUp]
        public void CreateWorkspace()
        {
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "ArenaForgeDoorwayTests");

            _catalog = ScriptableObject.CreateInstance<CatalogAsset>();
            _catalog.SourceFolder = Folder;
        }

        [TearDown]
        public void RemoveWorkspace()
        {
            for (int i = 0; i < _made.Count; i++)
            {
                if (_made[i] != null)
                {
                    Object.DestroyImmediate(_made[i]);
                }
            }

            _made.Clear();

            if (_catalog != null)
            {
                Object.DestroyImmediate(_catalog);
            }

            AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>A four-metre cube saved as a prefab and dropped back into the scene.</summary>
        /// <remarks>
        /// An instance rather than a bare scene object, because applying to the prefab is half of
        /// what the tool does and a scene object with no prefab behind it cannot show that.
        /// </remarks>
        GameObject House(bool withRow)
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Cube);
            source.name = "House";
            source.transform.localScale = new Vector3(4f, 3f, 4f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, HousePath);
            Object.DestroyImmediate(source);

            if (withRow)
            {
                _catalog.SetRows(new List<CatalogAsset.Row>
                {
                    new CatalogAsset.Row
                    {
                        LogicalId = "structure/house/house",
                        Tags = new[] { "structure", "structure/house" },
                        Weight = 1f,
                        Prefab = prefab,
                    },
                });
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _made.Add(instance);

            return instance;
        }

        // --- the marker -----------------------------------------------------------------------

        /// <remarks>
        /// The name is the contract. <see cref="CatalogSync"/> matches a marker on the prefix and
        /// nothing else, so a tool that made a child called anything else would be making an
        /// ordinary empty with a collider on it.
        /// </remarks>
        [Test]
        public void AMarkerIsNamedAndShapedTheWayTheSyncReadsOne()
        {
            GameObject house = House(false);

            GameObject marker = DoorwaySetup.AddMarker(house);

            Assert.That(marker.name, Does.StartWith(CatalogSync.DoorwayMarkerName));
            Assert.That(marker.transform.parent, Is.EqualTo(house.transform));

            var box = marker.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null, "a marker with no box declares a metre square");
            Assert.That(box.isTrigger, Is.True, "a doorway is not something to collide with");
        }

        [Test]
        public void MarkersAreNumberedSoAHouseCanDeclareTwo()
        {
            GameObject house = House(false);

            GameObject first = DoorwaySetup.AddMarker(house);
            GameObject second = DoorwaySetup.AddMarker(house);

            Assert.That(first.name, Is.Not.EqualTo(second.name));
            Assert.That(DoorwaySetup.MarkersIn(house).Count, Is.EqualTo(2));
        }

        /// <remarks>
        /// The middle of the model across the floor and standing on it, so the box is somewhere a
        /// person can see and grab. Nothing about it says which wall the door is in — that is what
        /// the dragging is for — and only X and Z ever reach the catalog.
        /// </remarks>
        [Test]
        public void AMarkerStartsInTheMiddleOfTheModelAndOnItsFloor()
        {
            GameObject house = House(false);

            GameObject marker = DoorwaySetup.AddMarker(house);

            Assert.That(marker.transform.localPosition.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(marker.transform.localPosition.z, Is.EqualTo(0f).Within(1e-3f));
            // In the house's own space, which is where the sync measures too: a unit cube's floor
            // is half a unit under its pivot however the transform above it is scaled.
            Assert.That(
                marker.transform.localPosition.y,
                Is.EqualTo(-0.5f + DoorwaySetup.MarkerHeight * 0.5f).Within(1e-3f));
        }

        // --- the row --------------------------------------------------------------------------

        /// <remarks>
        /// The end of the workflow, and the half that a person cannot check by looking: the map
        /// keeps the ground in front of a doorway clear because the <em>row</em> says where it is.
        /// </remarks>
        [Test]
        public void SavingWritesTheMarkersIntoTheRowBoundToThePrefab()
        {
            GameObject house = House(true);
            GameObject marker = DoorwaySetup.AddMarker(house);
            marker.transform.localPosition = new Vector3(0f, 0.5f, -0.5f);

            int declared = DoorwaySetup.Sync(house, _catalog);

            Assert.That(declared, Is.EqualTo(1));

            CatalogAsset.Row row = _catalog.Rows[0];
            Assert.That(row.Doorways.Count, Is.EqualTo(1));

            // Against the reader rather than against numbers written out here, so what the row
            // holds is what the sync would have put in it.
            List<CatalogAsset.DoorwayRow> read = CatalogSync.DoorwaysIn(
                AssetDatabase.LoadAssetAtPath<GameObject>(HousePath));

            Assert.That(read.Count, Is.EqualTo(1));
            Assert.That(row.Doorways[0].Center, Is.EqualTo(read[0].Center));
            Assert.That(row.Doorways[0].Size, Is.EqualTo(read[0].Size));
        }

        /// <remarks>
        /// The marker has to reach the asset, not just the scene. A row describing markers that
        /// vanish when the scene is closed is worse than no row at all, because the map goes on
        /// keeping ground clear in front of doors that are not in the prefab.
        /// </remarks>
        [Test]
        public void SavingAppliesTheMarkerToThePrefabItself()
        {
            GameObject house = House(true);
            DoorwaySetup.AddMarker(house);

            DoorwaySetup.Sync(house, _catalog);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HousePath);
            Assert.That(DoorwaySetup.MarkersIn(prefab).Count, Is.EqualTo(1));
        }

        /// <remarks>
        /// A measurement that grew because somebody said where the door was would be a measurement
        /// reporting on itself. The sync excludes markers already; this is the tool's half of that
        /// promise — the box it makes is a marker and is therefore never art.
        /// </remarks>
        [Test]
        public void AMarkerDoesNotChangeWhatTheHouseMeasures()
        {
            GameObject house = House(true);
            CatalogSync.TryMeasure(house, out Bounds before);

            DoorwaySetup.AddMarker(house);

            Assert.That(CatalogSync.TryMeasure(house, out Bounds after), Is.True);
            Assert.That(after.size, Is.EqualTo(before.size));
        }

        // --- what it refuses -------------------------------------------------------------------

        [Test]
        public void AddingAMarkerToNothingIsRefusedWithAMessage()
        {
            Assert.That(
                Assert.Throws<System.InvalidOperationException>(
                    () => DoorwaySetup.AddMarker(null)).Message,
                Does.Contain("Select"));
        }

        [Test]
        public void AddingAMarkerToAProjectAssetIsRefused()
        {
            House(false);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HousePath);

            Assert.Throws<System.InvalidOperationException>(() => DoorwaySetup.AddMarker(prefab));
        }

        [Test]
        public void SavingNothingIsRefusedWithAMessage()
        {
            Assert.Throws<System.InvalidOperationException>(
                () => DoorwaySetup.Sync(null, _catalog));
        }

        [Test]
        public void SavingWithoutACatalogIsRefused()
        {
            GameObject house = House(true);

            Assert.Throws<System.ArgumentNullException>(() => DoorwaySetup.Sync(house, null));
        }

        /// <remarks>
        /// A house nothing in the catalog points at has nowhere for its doorways to go, and the
        /// message says which button to press first. Writing a fresh row instead would be this tool
        /// guessing at tags, which is the folder table's job and not this one's.
        /// </remarks>
        [Test]
        public void SavingAHouseWithNoRowNamesTheProblem()
        {
            GameObject house = House(false);
            DoorwaySetup.AddMarker(house);

            Assert.That(
                Assert.Throws<System.InvalidOperationException>(
                    () => DoorwaySetup.Sync(house, _catalog)).Message,
                Does.Contain("Sync from Folders"));
        }

        [Test]
        public void SavingSomethingThatIsNotAPrefabInstanceNamesTheProblem()
        {
            var loose = new GameObject("Not a prefab");
            _made.Add(loose);

            Assert.That(
                Assert.Throws<System.InvalidOperationException>(
                    () => DoorwaySetup.Sync(loose, _catalog)).Message,
                Does.Contain("prefab"));
        }
    }
}
