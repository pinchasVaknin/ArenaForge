using System.Linq;
using System.Reflection;
using ArenaForge.Core;
using ArenaForge.Unity;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// The layer boundary from ARCHITECTURE.md section 1. The asmdef's
    /// <c>noEngineReferences</c> flag is what actually enforces it; this suite exists so the
    /// enforcement shows up as a named test failure rather than only as a compile error, and so
    /// removing the flag cannot pass unnoticed.
    /// </summary>
    public sealed class AssemblyBoundaryTests
    {
        [Test]
        public void CoreReferencesNoUnityAssembly()
        {
            Assembly core = typeof(WorldDoc).Assembly;

            Assert.That(core.GetName().Name, Is.EqualTo("ArenaForge.Core"));
            foreach (AssemblyName reference in core.GetReferencedAssemblies())
            {
                Assert.That(reference.Name, Does.Not.StartWith("Unity"),
                    $"ArenaForge.Core must not reference {reference.Name}.");
            }
        }

        [Test]
        public void CoreReferencesNewtonsoftAndNothingElseOutsideTheFramework()
        {
            Assembly core = typeof(WorldDoc).Assembly;

            foreach (AssemblyName reference in core.GetReferencedAssemblies())
            {
                bool framework =
                    reference.Name == "mscorlib" ||
                    reference.Name == "netstandard" ||
                    reference.Name.StartsWith("System");

                Assert.That(framework || reference.Name == "Newtonsoft.Json", Is.True,
                    $"ArenaForge.Core picked up an unexpected dependency: {reference.Name}.");
            }
        }

        [Test]
        public void TheUnityAdapterSitsInItsOwnAssemblyAndReferencesCore()
        {
            Assembly adapter = typeof(WorldRealizer).Assembly;

            Assert.That(adapter.GetName().Name, Is.EqualTo("ArenaForge.Unity"));
            Assert.That(
                adapter.GetReferencedAssemblies().Select(a => a.Name),
                Does.Contain("ArenaForge.Core"));
        }
    }
}
