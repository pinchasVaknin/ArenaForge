using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ArenaForge.Core;
using ArenaForge.Unity;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ArenaForge.Tests
{
    /// <summary>
    /// Every parameter survives every copy the parameter set is put through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A parameter is declared once and copied in three more places: its own
    /// <c>[JsonProperty]</c>, <see cref="ArenaParams.Clone"/>, and the two halves of the component
    /// mirror, <see cref="ArenaMap.ApplyParams"/> and <see cref="ArenaMap.BuildParams"/>. Nothing
    /// about adding the fifteenth parameter reminds anyone of the other three.
    /// </para>
    /// <para>
    /// <strong>All three fail silently, and the one that matters fails worst.</strong>
    /// <see cref="ArenaLayoutGenerator.Generate"/> builds the map from the caller's parameters and
    /// writes <c>Clone()</c> into the document. A parameter missing from that copy is recorded at
    /// its default while the map was built from the real value — so the map is reproducible for as
    /// long as it stays in memory and stops being reproducible the moment it is saved and loaded
    /// again. That is the project's first invariant failing with nothing on screen to say so.
    /// </para>
    /// <para>
    /// The thousand-seed sweep cannot catch it. That suite is a claim about generation and never
    /// reloads a document; this is a claim about the round trip.
    /// </para>
    /// <para>
    /// Written over reflection rather than over a list of names on purpose. A test that enumerated
    /// the fourteen parameters by hand would be a fourth place to forget the fifteenth, which is
    /// the whole failure it exists to prevent.
    /// </para>
    /// </remarks>
    public sealed class ParameterMirrorTests
    {
        /// <summary>
        /// The settable parameters, in a fixed order.
        /// </summary>
        /// <remarks>
        /// Sorted, because <see cref="Type.GetProperties()"/> promises nothing about member order
        /// and this list decides both which probe value each parameter gets and the order failures
        /// are reported in. Unsorted, the same missing parameter would be reported differently on
        /// two machines — which is the failure this suite is about, committed by the suite itself.
        /// </remarks>
        static readonly IReadOnlyList<PropertyInfo> Parameters = Settable(typeof(ArenaParams));

        ArenaMap _map;

        [SetUp]
        public void SetUp()
        {
            // RequireComponent brings the realiser with it. Neither component has an Awake or an
            // OnEnable, so an unparented one in an edit-mode test does nothing but hold fields.
            _map = new GameObject("ArenaForge Parameter Mirror Test").AddComponent<ArenaMap>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_map != null)
            {
                Object.DestroyImmediate(_map.gameObject);
            }
        }

        [Test]
        public void TheParameterSetHasSomethingToCheck()
        {
            // Guards the other three. If a refactor made the parameters read-only, or moved them
            // behind fields, every test below would pass over an empty list and report nothing.
            Assert.That(Parameters.Count, Is.GreaterThan(0),
                "No settable parameters were found on ArenaParams, so nothing below is checking anything.");
        }

        [Test]
        public void CloneCopiesEveryParameter()
        {
            ArenaParams source = Distinct();
            AssertEveryParameterSurvived(source, source.Clone(), "ArenaParams.Clone()");
        }

        [Test]
        public void TheComponentRoundTripsEveryParameter()
        {
            ArenaParams source = Distinct();
            _map.ApplyParams(source);

            AssertEveryParameterSurvived(
                source, _map.BuildParams(), "ArenaMap.ApplyParams followed by ArenaMap.BuildParams");
        }

        [Test]
        public void EveryParameterSurvivesADocumentRoundTrip()
        {
            // The fourth copy: a parameter with no [JsonProperty] on it is written nowhere and read
            // back as its default, which looks exactly like a parameter Clone dropped.
            var doc = new WorldDoc { Parameters = Distinct() };
            WorldDoc reloaded = ArenaJson.DeserializeWorld(ArenaJson.SerializeWorld(doc));

            AssertEveryParameterSurvived(
                doc.Parameters, reloaded.Parameters, "a serialise and deserialise round trip");
        }

        /// <summary>
        /// A parameter set with every parameter at a distinctive value.
        /// </summary>
        /// <remarks>
        /// Distinctive twice over: different from the parameter's own default, so a copy that
        /// dropped it comes back as the default and cannot match by luck; and different from every
        /// other parameter's value, so a copy that reads the wrong source — <c>ArteryWidth =
        /// PathWidth</c>, the mistake the shape of these methods invites — fails as loudly as one
        /// that reads nothing at all.
        /// </remarks>
        static ArenaParams Distinct()
        {
            var defaults = new ArenaParams();
            var parameters = new ArenaParams();

            for (int i = 0; i < Parameters.Count; i++)
            {
                PropertyInfo parameter = Parameters[i];
                object probe = Probe(parameter, i);

                Assert.That(probe, Is.Not.EqualTo(parameter.GetValue(defaults)),
                    $"The probe value for {parameter.Name} is its default, so a copy that dropped " +
                    "the parameter would still pass. Move the probe, not the default.");

                parameter.SetValue(parameters, probe);
            }

            return parameters;
        }

        static void AssertEveryParameterSurvived(ArenaParams expected, ArenaParams actual, string copy)
        {
            var lost = new List<string>();

            for (int i = 0; i < Parameters.Count; i++)
            {
                PropertyInfo parameter = Parameters[i];
                object wanted = parameter.GetValue(expected);
                object got = parameter.GetValue(actual);

                if (!Equals(wanted, got))
                {
                    lost.Add($"  {parameter.Name}: expected {Text(wanted)}, got {Text(got)}");
                }
            }

            if (lost.Count == 0)
            {
                return;
            }

            // Every one at once rather than the first, for the reason MapValidationTests reports
            // every broken property at once: one run should say everything that is wrong.
            Assert.Fail(
                $"{copy} did not carry {lost.Count} of {Parameters.Count} parameter(s):\n" +
                string.Join("\n", lost) +
                "\n\nA parameter that is not carried here is recorded in the saved document at its " +
                "default, so the map reloads as a different map. Add it to all four places a " +
                "parameter lives: its own [JsonProperty], ArenaParams.Clone, ArenaMap.ApplyParams " +
                "and ArenaMap.BuildParams.");
        }

        /// <summary>A value for one parameter that is unlike every other parameter's.</summary>
        /// <remarks>
        /// Refuses a type it does not know rather than skipping it. A parameter this returned some
        /// harmless default for would sit in the suite being checked against itself, reporting a
        /// pass for a copy nobody had verified — which is worse than the gap it was meant to close,
        /// because it reads as covered.
        /// </remarks>
        static object Probe(PropertyInfo parameter, int index)
        {
            Type type = parameter.PropertyType;

            if (type == typeof(bool))
            {
                // Two values, so the only distinctive one is the opposite of whatever this
                // parameter defaults to.
                return !(bool)parameter.GetValue(new ArenaParams());
            }

            if (type == typeof(int))
            {
                return 101 + index;
            }

            if (type == typeof(float))
            {
                // Whole numbers, so the value that comes back out of JSON is the value that went
                // in and a failure here is a lost parameter rather than a rounding argument.
                return 1000f + index;
            }

            if (type == typeof(ulong))
            {
                return 0x0123456789ABCDEFUL + (ulong)index;
            }

            if (type == typeof(Vec2))
            {
                return new Vec2(1000f + index, 2000f + index);
            }

            throw new NotSupportedException(
                $"No probe value for parameter '{parameter.Name}' of type {type.Name}. Add one to " +
                $"{nameof(ParameterMirrorTests)}.{nameof(Probe)}: this suite cannot protect a " +
                "parameter it does not know how to give a distinctive value to, and silently " +
                "skipping it would report a pass for a copy nothing checked.");
        }

        static IReadOnlyList<PropertyInfo> Settable(Type type)
        {
            var found = new List<PropertyInfo>();

            foreach (PropertyInfo property in
                     type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite &&
                    property.GetIndexParameters().Length == 0)
                {
                    found.Add(property);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return found;
        }

        // Invariant culture so a failure message reads the same on a machine whose decimal
        // separator is a comma.
        static string Text(object value) =>
            value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString() ?? "null";
    }
}
