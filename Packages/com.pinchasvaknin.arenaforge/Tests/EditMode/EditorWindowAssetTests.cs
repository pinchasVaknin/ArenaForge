using ArenaForge.Editor;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The tool window's markup is loaded by path at runtime, so nothing about it is checked by the
    /// compiler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because it broke exactly once, in the way this kind of thing always breaks: the
    /// package was restructured, the files moved, and the two path constants did not. Everything
    /// still compiled and every other test still passed — the only symptom was a tool window with
    /// nothing in it, which no automated check would have noticed.
    /// </para>
    /// <para>
    /// It loads through <see cref="ArenaForgeWindow.LoadAsset{T}"/> rather than calling the asset
    /// database directly, so what is asserted is the path the window actually takes, import
    /// fallback included.
    /// </para>
    /// </remarks>
    public sealed class EditorWindowAssetTests
    {
        [Test]
        public void TheWindowsMarkupResolvesAtThePathTheWindowLooksFor()
        {
            var tree = ArenaForgeWindow.LoadAsset<VisualTreeAsset>(ArenaForgeWindow.UxmlPath);

            Assert.That(tree, Is.Not.Null, $"no VisualTreeAsset at {ArenaForgeWindow.UxmlPath}");
        }

        [Test]
        public void TheWindowsStylesheetResolvesAtThePathTheWindowLooksFor()
        {
            var style = ArenaForgeWindow.LoadAsset<StyleSheet>(ArenaForgeWindow.UssPath);

            Assert.That(style, Is.Not.Null, $"no StyleSheet at {ArenaForgeWindow.UssPath}");
        }

        [Test]
        public void TheMarkupCarriesEveryElementTheWindowLooksUpByName()
        {
            var tree = ArenaForgeWindow.LoadAsset<VisualTreeAsset>(ArenaForgeWindow.UxmlPath);
            var root = new VisualElement();
            tree.CloneTree(root);

            // The containers the window fills in code. A rename in the markup that missed the code
            // would otherwise surface as a null reference the first time the window is opened.
            foreach (string name in new[]
            {
                "target-slot", "empty-state", "body", "seed-slot", "playfield-slot", "metric-rows",
                "override-rows", "orphan-rows", "catalog-rows", "ramp", "status", "report-verdict",
                "placement-summary", "exposure-summary", "override-empty", "heatmap",
                "overrides-foldout", "orphans-foldout", "catalog-foldout", "show-in-scene",
                "lane-count", "grid-size", "structure-density", "cover-density", "low-high-ratio",
                "fine-rotation", "eye-height", "create-map", "generate", "regenerate", "clear",
                "save", "load",
            })
            {
                Assert.That(root.Q(name), Is.Not.Null, $"the markup has no element named '{name}'");
            }
        }
    }
}
