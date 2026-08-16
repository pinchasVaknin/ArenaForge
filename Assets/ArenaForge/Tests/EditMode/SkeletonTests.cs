using ArenaForge.Core;
using ArenaForge.Unity;
using NUnit.Framework;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Proves the assembly graph is wired: Core compiles against Newtonsoft with
    /// no engine reference, and the Unity adapter can see Core.
    /// </summary>
    public sealed class SkeletonTests
    {
        [Test]
        public void CoreSerialisesSchemaStampWithNewtonsoft()
        {
            Assert.AreEqual("{\"schemaVersion\":1}", CoreInfo.SchemaStampJson());
        }

        [Test]
        public void UnityAdapterReferencesCore()
        {
            StringAssert.Contains($"Core schema {CoreInfo.SchemaVersion}", ArenaForgeRuntimeInfo.Describe());
        }
    }
}
