using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Samaz.Observatory.Astronomy.Tests
{
    /// <summary>
    /// Contract tests for the offline Stellarium Modern stick-figure catalogue. They intentionally
    /// use reflection because the main game scripts live in Assembly-CSharp while these editor
    /// tests are in the astronomy assembly definition.
    /// </summary>
    public sealed class ConstellationOverlayCatalogTests
    {
        private const int ExpectedFigureCount = 88;
        private const int ExpectedPolylineCount = 219;
        private const int ExpectedSegmentCount = 695;
        private const int ExpectedUniqueHipCount = 710;

        [Test]
        public void RuntimeCatalogMatchesPinnedModernDataAndEveryHipResolvesInHyg()
        {
            object catalog = LoadRuntimeCatalog();

            Assert.That(GetRequiredIntMember(catalog, "FigureCount"), Is.EqualTo(ExpectedFigureCount));
            Assert.That(GetRequiredIntMember(catalog, "PolylineCount"), Is.EqualTo(ExpectedPolylineCount));
            Assert.That(GetRequiredIntMember(catalog, "SegmentCount"), Is.EqualTo(ExpectedSegmentCount));

            List<object> figures = ToObjectList(GetRequiredMember(catalog, "Figures"), "Figures");
            Assert.That(figures.Count, Is.EqualTo(ExpectedFigureCount));

            int polylineCount = 0;
            int segmentCount = 0;
            HashSet<int> uniqueHipIds = new HashSet<int>();

            for (int figureIndex = 0; figureIndex < figures.Count; figureIndex++)
            {
                object figure = figures[figureIndex];
                string code = GetRequiredMember(figure, "Code") as string;
                Assert.That(code, Is.Not.Null.And.Not.Empty, $"La figura {figureIndex} debe tener un código IAU.");

                string displayName = GetRequiredMember(figure, "DisplayName") as string;
                Assert.That(displayName, Is.Not.Null.And.Not.Empty, $"La figura {code} debe tener nombre visible.");

                List<object> polylines = ToObjectList(
                    GetRequiredMember(figure, "Polylines"),
                    $"Polylines de {code}"
                );
                Assert.That(polylines.Count, Is.GreaterThan(0), $"La figura {code} debe tener al menos una polilínea.");

                for (int polylineIndex = 0; polylineIndex < polylines.Count; polylineIndex++)
                {
                    List<int> hipIds = ToIntList(
                        GetRequiredMember(polylines[polylineIndex], "HipIds"),
                        $"HipIds de {code}, polilínea {polylineIndex}"
                    );

                    Assert.That(
                        hipIds.Count,
                        Is.GreaterThanOrEqualTo(2),
                        $"La polilínea {polylineIndex} de {code} requiere al menos dos HIP."
                    );

                    polylineCount++;
                    segmentCount += hipIds.Count - 1;

                    for (int hipIndex = 0; hipIndex < hipIds.Count; hipIndex++)
                    {
                        int hipId = hipIds[hipIndex];
                        Assert.That(hipId, Is.GreaterThan(0), $"HIP inválido en {code}, polilínea {polylineIndex}.");
                        uniqueHipIds.Add(hipId);
                    }
                }
            }

            Assert.That(polylineCount, Is.EqualTo(ExpectedPolylineCount));
            Assert.That(segmentCount, Is.EqualTo(ExpectedSegmentCount));
            Assert.That(uniqueHipIds.Count, Is.EqualTo(ExpectedUniqueHipCount));

            AssertAllHipIdsResolveInHyg(uniqueHipIds);
        }

        [Test]
        public void TestSceneWiresConstellationOverlayToCelestialVault()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/test.unity");

            GameObject vault = GameObject.Find("Celestial Vault Prototype");
            Assert.That(vault, Is.Not.Null, "La escena TEST debe contener la bóveda celeste.");

            Type overlayType = Type.GetType("ConstellationOverlayRenderer, Assembly-CSharp");
            Assert.That(overlayType, Is.Not.Null, "Debe existir ConstellationOverlayRenderer en Assembly-CSharp.");
            Assert.That(
                vault.GetComponent(overlayType),
                Is.Not.Null,
                "TEST debe tener ConstellationOverlayRenderer en Celestial Vault Prototype."
            );
        }

        [Test]
        public void OverlayBuildsOneCombinedMeshForAllResolvableSegments()
        {
            Type rendererType = Type.GetType("StellarVaultRenderer, Assembly-CSharp");
            Type overlayType = Type.GetType("ConstellationOverlayRenderer, Assembly-CSharp");
            Type overlayModeType = Type.GetType("ConstellationOverlayMode, Assembly-CSharp");
            Assert.That(rendererType, Is.Not.Null);
            Assert.That(overlayType, Is.Not.Null);
            Assert.That(overlayModeType, Is.Not.Null);
            Assert.That(Resources.Load<Shader>("Shaders/ConstellationLineURP"), Is.Not.Null);

            GameObject gameObject = new GameObject("Constellation Combined Mesh Test");
            try
            {
                Component renderer = gameObject.AddComponent(rendererType);
                PropertyInfo catalogReady = rendererType.GetProperty("IsCatalogReady");
                Assert.That(catalogReady, Is.Not.Null);
                Assert.That(
                    (bool)catalogReady.GetValue(renderer),
                    Is.True,
                    "La bóveda debe cargar HYG antes de construir el overlay."
                );
                Component overlay = gameObject.AddComponent(overlayType);

                MethodInfo ensureReferences = overlayType.GetMethod(
                    "EnsureReferences",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.That(ensureReferences, Is.Not.Null);
                ensureReferences.Invoke(overlay, null);

                MethodInfo setMode = overlayType.GetMethod("SetVisibilityMode");
                Assert.That(setMode, Is.Not.Null);
                object allMode = Enum.Parse(overlayModeType, "All");
                setMode.Invoke(overlay, new[] { allMode });
                Assert.That(GetRequiredMember(overlay, "Mode"), Is.EqualTo(allMode));

                MethodInfo ensureCatalog = overlayType.GetMethod(
                    "EnsureCatalogLoaded",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                MethodInfo ensureRuntimeObjects = overlayType.GetMethod(
                    "EnsureRuntimeObjects",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                MethodInfo rebuildMesh = overlayType.GetMethod(
                    "RebuildMesh",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.That(ensureCatalog, Is.Not.Null);
                Assert.That(ensureRuntimeObjects, Is.Not.Null);
                Assert.That(rebuildMesh, Is.Not.Null);
                Assert.That((bool)ensureCatalog.Invoke(overlay, null), Is.True);
                Assert.That((bool)ensureRuntimeObjects.Invoke(overlay, null), Is.True);
                Assert.That(GetRequiredIntMember(overlay, "LoadedFigureCount"), Is.EqualTo(ExpectedFigureCount));

                FieldInfo vaultField = overlayType.GetField("vaultRenderer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(vaultField, Is.Not.Null);
                Assert.That(vaultField.GetValue(overlay), Is.EqualTo(renderer));

                rebuildMesh.Invoke(overlay, new object[] { string.Empty });
                Assert.That(GetRequiredIntMember(overlay, "DrawnSegmentCount"), Is.EqualTo(ExpectedSegmentCount));
                Assert.That(GetRequiredIntMember(overlay, "MissingEndpointCount"), Is.EqualTo(0));

                FieldInfo meshField = overlayType.GetField("runtimeMesh", BindingFlags.Instance | BindingFlags.NonPublic);
                Mesh generatedMesh = meshField.GetValue(overlay) as Mesh;
                Assert.That(generatedMesh, Is.Not.Null);
                Assert.That(generatedMesh.vertexCount, Is.GreaterThan(0));
                Assert.That(generatedMesh.subMeshCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static object LoadRuntimeCatalog()
        {
            Type loaderType = Type.GetType("ConstellationLineCatalogLoader, Assembly-CSharp");
            Assert.That(loaderType, Is.Not.Null, "Debe existir ConstellationLineCatalogLoader en Assembly-CSharp.");

            MethodInfo loadMethod = loaderType.GetMethod(
                "Load",
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null
            );
            Assert.That(loadMethod, Is.Not.Null, "ConstellationLineCatalogLoader debe exponer Load() público y estático.");

            object catalog = loadMethod.Invoke(null, null);
            Assert.That(catalog, Is.Not.Null, "ConstellationLineCatalogLoader.Load() no debe devolver null.");
            return catalog;
        }

        private static void AssertAllHipIdsResolveInHyg(IEnumerable<int> hipIds)
        {
            Type rendererType = Type.GetType("StellarVaultRenderer, Assembly-CSharp");
            Assert.That(rendererType, Is.Not.Null, "Debe existir StellarVaultRenderer en Assembly-CSharp.");

            GameObject gameObject = new GameObject("Constellation HYG Resolution Test");
            Component renderer = null;

            try
            {
                renderer = gameObject.AddComponent(rendererType);
                MethodInfo resolver = FindHipResolver(rendererType);
                Assert.That(resolver, Is.Not.Null, "StellarVaultRenderer debe exponer TryGetStarByHipId(int, out StarInfo).");

                foreach (int hipId in hipIds)
                {
                    object[] arguments = { hipId, null };
                    bool resolved = (bool)resolver.Invoke(renderer, arguments);
                    Assert.That(resolved, Is.True, $"El HIP {hipId} usado por el overlay debe existir en HYG local.");
                }
            }
            finally
            {
                if (renderer != null)
                {
                    UnityEngine.Object.DestroyImmediate(renderer.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }
        }

        private static MethodInfo FindHipResolver(Type rendererType)
        {
            MethodInfo[] methods = rendererType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "TryGetStarByHipId"
                    && method.ReturnType == typeof(bool)
                    && parameters.Length == 2
                    && parameters[0].ParameterType == typeof(int)
                    && parameters[1].IsOut)
                {
                    return method;
                }
            }

            return null;
        }

        private static int GetRequiredIntMember(object instance, string memberName)
        {
            object value = GetRequiredMember(instance, memberName);
            Assert.That(value, Is.TypeOf<int>(), $"{instance.GetType().Name}.{memberName} debe ser int.");
            return (int)value;
        }

        private static object GetRequiredMember(object instance, string memberName)
        {
            Assert.That(instance, Is.Not.Null, $"No se puede leer {memberName} de una instancia nula.");

            Type type = instance.GetType();
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

            PropertyInfo property = type.GetProperty(memberName, Flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(instance);
            }

            FieldInfo field = type.GetField(memberName, Flags);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            Assert.Fail($"{type.FullName} debe exponer el miembro público {memberName}.");
            return null;
        }

        private static List<object> ToObjectList(object value, string description)
        {
            Assert.That(value, Is.Not.Null, $"{description} no debe ser null.");
            Assert.That(value, Is.InstanceOf<IEnumerable>(), $"{description} debe ser enumerable.");

            List<object> values = new List<object>();
            foreach (object item in (IEnumerable)value)
            {
                Assert.That(item, Is.Not.Null, $"{description} no debe contener elementos null.");
                values.Add(item);
            }

            return values;
        }

        private static List<int> ToIntList(object value, string description)
        {
            Assert.That(value, Is.Not.Null, $"{description} no debe ser null.");
            Assert.That(value, Is.InstanceOf<IEnumerable>(), $"{description} debe ser enumerable.");

            List<int> values = new List<int>();
            foreach (object item in (IEnumerable)value)
            {
                Assert.That(item, Is.TypeOf<int>(), $"{description} debe contener HIP enteros.");
                values.Add((int)item);
            }

            return values;
        }
    }
}
