using System.Collections.Generic;
using ArenaForge.Core;
using ArenaForge.Editor;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// How the catalog inspector files rows under headings.
    /// </summary>
    /// <remarks>
    /// Only the grouping is tested, because only the grouping is a decision. The drawing around it
    /// is <c>EditorGUILayout</c> calls with no branch worth asserting, where this is a pure function
    /// over the ids with three cases a catalog really produces: the convention, an id that does not
    /// follow it, and a row nobody has named yet.
    /// </remarks>
    public sealed class CatalogInspectorTests
    {
        [Test]
        public void RowsAreFiledUnderTheFirstSegmentOfTheirLogicalId()
        {
            List<CatalogAssetEditor.Category> categories = CatalogAssetEditor.Group(new[]
            {
                "structure/wall/panel_2m",
                "cover/low/crate_wood_01",
                "structure/floor/slab_1m",
                "cover/high/barrier_concrete_01",
                "prop/decor/plant_01",
                "structure/window/pane_2m",
            });

            Assert.That(Names(categories), Is.EqualTo(new[] { "cover", "prop", "structure" }),
                "headings are sorted, so a hand-authored catalog does not shuffle as rows are added");

            Assert.That(categories[0].Rows, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(categories[1].Rows, Is.EqualTo(new[] { 4 }));
            Assert.That(categories[2].Rows, Is.EqualTo(new[] { 0, 2, 5 }),
                "rows keep the order the flat list holds them in");
        }

        /// <remarks>
        /// A tag the generator has only just learned needs nothing from the inspector, and that is
        /// the property rather than the absence of one: the heading is the first segment of the
        /// logical id, so a kind of art that did not exist when this was written files itself under
        /// the heading its siblings are already under. A table of known kinds here would have to be
        /// edited every time the generator learned a new tag, and would draw a heading of its own
        /// for one it had not heard of.
        /// </remarks>
        [Test]
        public void ANewKindOfStructureNeedsNoHeadingOfItsOwn()
        {
            List<CatalogAssetEditor.Category> categories = CatalogAssetEditor.Group(new[]
            {
                "structure/wall/panel_2m",
                BuildingGenerator.WindowTag + "/pane_2m",
            });

            Assert.That(Names(categories), Is.EqualTo(new[] { "structure" }));
            Assert.That(categories[0].Rows, Is.EqualTo(new[] { 0, 1 }));
        }

        /// <remarks>
        /// The flat list is what the asset stores and what the inspector edits through, so a
        /// grouping that lost or duplicated an offset would edit the wrong row.
        /// </remarks>
        [Test]
        public void EveryRowIsFiledExactlyOnce()
        {
            var ids = new[] { "cover/a", "structure/b", "cover/c", "loose", null, "  " };

            var seen = new List<int>();
            List<CatalogAssetEditor.Category> categories = CatalogAssetEditor.Group(ids);
            for (int i = 0; i < categories.Count; i++)
            {
                seen.AddRange(categories[i].Rows);
            }

            seen.Sort();
            Assert.That(seen, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
        }

        /// <remarks>
        /// A catalog that has not adopted the convention still groups — it simply gets a heading per
        /// row, which is the flat list back and is the honest answer for ids with no structure in
        /// them.
        /// </remarks>
        [Test]
        public void AnIdWithNoSeparatorIsItsOwnHeading()
        {
            Assert.That(CatalogAssetEditor.CategoryOf("crate"), Is.EqualTo("crate"));
        }

        [Test]
        public void RowsWithNoIdAreCollectedRatherThanGivenABlankHeading()
        {
            List<CatalogAssetEditor.Category> categories =
                CatalogAssetEditor.Group(new[] { null, string.Empty, "   ", "cover/low/crate" });

            Assert.That(Names(categories),
                Is.EqualTo(new[] { CatalogAssetEditor.UnnamedCategory, "cover" }));
            Assert.That(categories[0].Rows, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void AnEmptyCatalogHasNoHeadings()
        {
            Assert.That(CatalogAssetEditor.Group(new string[0]), Is.Empty);
        }

        static string[] Names(IReadOnlyList<CatalogAssetEditor.Category> categories)
        {
            var names = new string[categories.Count];
            for (int i = 0; i < categories.Count; i++)
            {
                names[i] = categories[i].Name;
            }

            return names;
        }
    }
}
