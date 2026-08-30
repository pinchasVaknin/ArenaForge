using ArenaForge.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The editor markup — the tool window's and the scene-view overlay's — is loaded by path at
    /// runtime, so nothing about it is checked by the compiler.
    /// </summary>
    /// <remarks>
    /// This exists because it broke exactly once, in the way this kind of thing always breaks: the
    /// package was restructured, the files moved, and the two path constants did not. Everything
    /// still compiled and every other test still passed — the only symptom was a tool window with
    /// nothing in it, which no automated check would have noticed.
    /// </remarks>
    public sealed class EditorWindowAssetTests
    {
        [Test]
        public void TheWindowsMarkupResolvesAtThePathTheWindowLooksFor()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ArenaForgeWindow.UxmlPath);

            Assert.That(tree, Is.Not.Null, $"no VisualTreeAsset at {ArenaForgeWindow.UxmlPath}");
        }

        [Test]
        public void TheWindowsStylesheetResolvesAtThePathTheWindowLooksFor()
        {
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(ArenaForgeWindow.UssPath);

            Assert.That(style, Is.Not.Null, $"no StyleSheet at {ArenaForgeWindow.UssPath}");
        }

        [Test]
        public void TheMarkupCarriesEveryElementTheWindowLooksUpByName()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ArenaForgeWindow.UxmlPath);
            var root = new VisualElement();
            tree.CloneTree(root);

            // The containers the window fills in code. A rename in the markup that missed the code
            // would otherwise surface as a null reference the first time the window is opened.
            foreach (string name in new[]
            {
                "target-slot", "empty-state", "body", "seed-slot", "playfield-slot", "metric-rows",
                "override-rows", "orphan-rows", "catalog-rows", "ramp", "status", "report-verdict", "report-caveat",
                "placement-summary", "exposure-summary", "override-empty", "heatmap",
                "overrides-foldout", "orphans-foldout", "catalog-foldout", "show-in-scene",
                "lane-count", "grid-size", "structure-density", "cover-density", "low-high-ratio",
                "fine-rotation", "terrain-amplitude", "terrain-feature", "eye-height", "create-map",
                "generate", "regenerate", "clear", "save", "load",
            })
            {
                Assert.That(root.Q(name), Is.Not.Null, $"the markup has no element named '{name}'");
            }
        }

        [Test]
        public void TheOverlaysMarkupResolvesAtThePathTheOverlayLooksFor()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ArenaForgeOverlay.UxmlPath);

            Assert.That(tree, Is.Not.Null, $"no VisualTreeAsset at {ArenaForgeOverlay.UxmlPath}");
        }

        [Test]
        public void TheOverlaysStylesheetResolvesAtThePathTheOverlayLooksFor()
        {
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(ArenaForgeOverlay.UssPath);

            Assert.That(style, Is.Not.Null, $"no StyleSheet at {ArenaForgeOverlay.UssPath}");
        }

        [Test]
        public void TheOverlaysPanelBuildsAndCarriesEveryElementItLooksUpByName()
        {
            var overlay = new ArenaForgeOverlay();

            // Building it is most of the assertion: every lookup the overlay makes happens inside
            // here, so a renamed element is a null reference before a single Assert runs. The list
            // below then says which names the contract is, for whoever edits the markup next.
            VisualElement panel = overlay.CreatePanelContent();

            foreach (string name in new[]
            {
                "mode", "mode-map", "mode-item",
                "map-mode", "map-slot", "controls", "seed-slot", "map-params", "generate",
                "regenerate", "clear",
                "save", "load", "map-export", "override-count", "selection", "guides", "roads",
                "placement", "swap-slot",
                "item-mode", "building-slot", "building-controls", "building-seed-slot",
                "building-params", "building-generate", "building-regenerate", "building-clear",
                "floors", "export", "building-summary",
                "status",
            })
            {
                Assert.That(panel.Q(name), Is.Not.Null, $"the markup has no element named '{name}'");
            }
        }

        /// <remarks>
        /// The split the overlay is built around: one mode is on screen at a time, and neither
        /// carries a panel that belongs to the tool window. A regression here would be the overlay
        /// starting to grow into a second copy of the window.
        /// </remarks>
        [Test]
        public void TheOverlayShowsOneModeAtATime()
        {
            var overlay = new ArenaForgeOverlay();
            VisualElement panel = overlay.CreatePanelContent();

            // The inline style rather than the resolved one: a panel that has never been laid out
            // has no resolved style yet, and what is being asserted is what the overlay set.
            bool mapHidden = panel.Q("map-mode").style.display.value == DisplayStyle.None;
            bool itemHidden = panel.Q("item-mode").style.display.value == DisplayStyle.None;

            Assert.That(mapHidden, Is.Not.EqualTo(itemHidden),
                "exactly one of the two mode sections is visible");
        }
    }
}
