using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Samaz.Observatory.Astronomy.Tests
{
    public sealed class AstronomyEngineLocalSkyTransformTests
    {
        private static readonly DateTime TestUtc = new DateTime(2026, 3, 20, 3, 0, 0, DateTimeKind.Utc);

        [Test]
        public void HorizontalAxisMappingMatchesDocumentedUnityConvention()
        {
            Assert.That(
                AstronomyEngineLocalSkyTransform.HorizontalToUnity(new Vector3(1f, 0f, 0f)),
                Is.EqualTo(Vector3.forward)
            );
            Assert.That(
                AstronomyEngineLocalSkyTransform.HorizontalToUnity(new Vector3(0f, 1f, 0f)),
                Is.EqualTo(Vector3.left)
            );
            Assert.That(
                AstronomyEngineLocalSkyTransform.HorizontalToUnity(new Vector3(0f, 0f, 1f)),
                Is.EqualTo(Vector3.up)
            );
        }

        [Test]
        public void GeoCoordinateValidatesAndNormalizesLongitude()
        {
            GeoCoordinate location = new GeoCoordinate(23.2494d, 253.5889d, 10d);

            Assert.That(location.LatitudeDegrees, Is.EqualTo(23.2494d));
            Assert.That(location.LongitudeDegrees, Is.EqualTo(-106.4111d).Within(0.0000001d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeoCoordinate(90.1d, 0d));
        }

        [Test]
        public void CuratedTestLocationsCoverBothHemispheresAndResolveTheirNames()
        {
            Assert.That(BuiltInSkyLocations.All.Count, Is.EqualTo(9));

            BuiltInSkyLocationDefinition guanajuato = BuiltInSkyLocations.GetDefinition(BuiltInSkyLocation.GuanajuatoMexico);
            BuiltInSkyLocationDefinition ushuaia = BuiltInSkyLocations.GetDefinition(BuiltInSkyLocation.UshuaiaArgentina);

            Assert.That(guanajuato.Coordinate.LatitudeDegrees, Is.GreaterThan(0d));
            Assert.That(ushuaia.Coordinate.LatitudeDegrees, Is.LessThan(0d));
            Assert.That(BuiltInSkyLocations.TryFindDefinition(guanajuato.Coordinate, out BuiltInSkyLocationDefinition resolved), Is.True);
            Assert.That(resolved.Id, Is.EqualTo(BuiltInSkyLocation.GuanajuatoMexico));
        }

        [Test]
        public void SameObservationContextProducesSameSkyDirection()
        {
            AstronomyEngineLocalSkyTransform transform = new AstronomyEngineLocalSkyTransform();
            ObservationContext context = new ObservationContext(new GeoCoordinate(23.2494d, -106.4111d, 10d), TestUtc);
            Vector3 equatorialDirection = new Vector3(0.25f, 0.5f, 0.75f).normalized;

            Vector3 first = transform.CreateFrame(context).TransformEquatorialJ2000Direction(equatorialDirection);
            Vector3 second = transform.CreateFrame(context).TransformEquatorialJ2000Direction(equatorialDirection);

            Assert.That(Vector3.Angle(first, second), Is.LessThan(0.0001f));
        }

        [Test]
        public void NorthCelestialPoleIsHigherInLondonThanMazatlan()
        {
            AstronomyEngineLocalSkyTransform transform = new AstronomyEngineLocalSkyTransform();
            SkyFrame mazatlan = transform.CreateFrame(
                new ObservationContext(new GeoCoordinate(23.2494d, -106.4111d, 10d), TestUtc)
            );
            SkyFrame london = transform.CreateFrame(
                new ObservationContext(new GeoCoordinate(51.5072d, -0.1276d, 35d), TestUtc)
            );

            // In StellarVaultRenderer's EQJ mapping, Unity +Y is the J2000 north celestial pole.
            Vector3 northPoleFromMazatlan = mazatlan.TransformEquatorialJ2000Direction(Vector3.up);
            Vector3 northPoleFromLondon = london.TransformEquatorialJ2000Direction(Vector3.up);

            Assert.That(
                northPoleFromMazatlan.y,
                Is.EqualTo(Mathf.Sin(23.2494f * Mathf.Deg2Rad)).Within(0.012f)
            );
            Assert.That(
                northPoleFromLondon.y,
                Is.EqualTo(Mathf.Sin(51.5072f * Mathf.Deg2Rad)).Within(0.012f)
            );
            Assert.That(northPoleFromLondon.y, Is.GreaterThan(northPoleFromMazatlan.y + 0.2f));
            Assert.That(london.IsAboveHorizon(Vector3.up), Is.True);
        }

        [Test]
        public void DifferentLongitudesProduceDifferentLocalSkyAtSameUtc()
        {
            AstronomyEngineLocalSkyTransform transform = new AstronomyEngineLocalSkyTransform();
            SkyFrame mazatlan = transform.CreateFrame(
                new ObservationContext(new GeoCoordinate(23.2494d, -106.4111d, 10d), TestUtc)
            );
            SkyFrame london = transform.CreateFrame(
                new ObservationContext(new GeoCoordinate(51.5072d, -0.1276d, 35d), TestUtc)
            );

            // Unity +Z corresponds to RA=0, Dec=0 in the renderer's fixed EQJ catalogue convention.
            Vector3 raZeroFromMazatlan = mazatlan.TransformEquatorialJ2000Direction(Vector3.forward);
            Vector3 raZeroFromLondon = london.TransformEquatorialJ2000Direction(Vector3.forward);

            Assert.That(Vector3.Dot(raZeroFromMazatlan, raZeroFromLondon), Is.LessThan(0.99f));
        }

        [Test]
        public void RendererUpdatesAboveHorizonStateForTheSameCatalogueUsedBySelection()
        {
            Type rendererType = Type.GetType("StellarVaultRenderer, Assembly-CSharp");
            Assert.That(rendererType, Is.Not.Null, "El renderizador principal debe estar disponible para la integración del cielo local.");

            GameObject gameObject = new GameObject("Local Sky Renderer Test");
            Component renderer = null;

            try
            {
                renderer = gameObject.AddComponent(rendererType);
                MethodInfo setCelestialFrame = rendererType.GetMethod("SetCelestialFrame");
                setCelestialFrame.Invoke(renderer, new object[] { Matrix4x4.identity, true, 0f });

                FieldInfo visibleStarsField = rendererType.GetField("visibleStars", BindingFlags.Instance | BindingFlags.NonPublic);
                IList visibleStars = visibleStarsField.GetValue(renderer) as IList;
                Assert.That(visibleStars, Is.Not.Null);
                Assert.That(visibleStars.Count, Is.GreaterThan(0));

                FieldInfo isAboveHorizonField = visibleStars[0].GetType().GetField("IsAboveHorizon");
                int aboveHorizonCount = 0;
                int belowHorizonCount = 0;

                for (int index = 0; index < visibleStars.Count; index++)
                {
                    bool isAboveHorizon = (bool)isAboveHorizonField.GetValue(visibleStars[index]);
                    if (isAboveHorizon)
                    {
                        aboveHorizonCount++;
                    }
                    else
                    {
                        belowHorizonCount++;
                    }
                }

                Assert.That(aboveHorizonCount, Is.GreaterThan(0));
                Assert.That(belowHorizonCount, Is.GreaterThan(0));
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

        [Test]
        public void RendererCanResolveTheSeedMissionTargetsFromItsOwnCatalogue()
        {
            Type rendererType = Type.GetType("StellarVaultRenderer, Assembly-CSharp");
            Assert.That(rendererType, Is.Not.Null);

            GameObject gameObject = new GameObject("Mission Target Catalogue Test");
            Component renderer = null;

            try
            {
                renderer = gameObject.AddComponent(rendererType);

                MethodInfo getStarByHipId = rendererType.GetMethod("TryGetStarByHipId");
                object[] polarisArguments = { 11767, null };
                bool foundPolaris = (bool)getStarByHipId.Invoke(renderer, polarisArguments);
                Assert.That(foundPolaris, Is.True);

                MethodInfo getConstellation = rendererType.GetMethod("TryGetBrightestStarInConstellation");
                object[] orionArguments = { "Ori", false, null };
                bool foundOrion = (bool)getConstellation.Invoke(renderer, orionArguments);
                Assert.That(foundOrion, Is.True);
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

        [Test]
        public void TestSceneWiresTheLocalSkyHudAndMissionPrototype()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/test.unity");

            GameObject vault = GameObject.Find("Celestial Vault Prototype");
            GameObject camera = GameObject.Find("Main Camera");
            Assert.That(vault, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);

            Type localSkyControllerType = Type.GetType("Samaz.Observatory.Astronomy.LocalSkyController, Assembly-CSharp");
            Type missionControllerType = Type.GetType("CelestialMissionController, Assembly-CSharp");
            Type hudType = Type.GetType("LocalSkyTestHud, Assembly-CSharp");
            Type detailControllerType = Type.GetType("StarDetailController, Assembly-CSharp");

            Assert.That(localSkyControllerType, Is.Not.Null);
            Assert.That(missionControllerType, Is.Not.Null);
            Assert.That(hudType, Is.Not.Null);
            Assert.That(detailControllerType, Is.Not.Null);
            Assert.That(vault.GetComponent(localSkyControllerType), Is.Not.Null);
            Assert.That(camera.GetComponent(missionControllerType), Is.Not.Null);
            Assert.That(camera.GetComponent(hudType), Is.Not.Null);
            Assert.That(camera.GetComponent(detailControllerType), Is.Not.Null);
            Assert.That(Resources.Load<Shader>("Shaders/StarDetailURP"), Is.Not.Null);
        }

        [Test]
        public void TestHudCanConstructItsInspectableControls()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/test.unity");

            Type hudType = Type.GetType("LocalSkyTestHud, Assembly-CSharp");
            Type buttonType = Type.GetType("UnityEngine.UI.Button, UnityEngine.UI");
            Type textType = Type.GetType("UnityEngine.UI.Text, UnityEngine.UI");
            Assert.That(hudType, Is.Not.Null);
            Assert.That(buttonType, Is.Not.Null);
            Assert.That(textType, Is.Not.Null);

            Component hud = GameObject.Find("Main Camera").GetComponent(hudType);
            MethodInfo buildHud = hudType.GetMethod("BuildHud", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(buildHud, Is.Not.Null);

            GameObject generatedCanvas = null;
            try
            {
                buildHud.Invoke(hud, null);
                generatedCanvas = GameObject.Find("Local Sky Test HUD");

                Assert.That(generatedCanvas, Is.Not.Null);
                Assert.That(generatedCanvas.GetComponent<Canvas>(), Is.Not.Null);
                Assert.That(
                    generatedCanvas.GetComponentsInChildren(buttonType, true).Length,
                    Is.EqualTo(BuiltInSkyLocations.All.Count + 3)
                );

                PropertyInfo textProperty = textType.GetProperty("text");
                Assert.That(textProperty, Is.Not.Null);
                HashSet<string> labels = new HashSet<string>();
                Component[] texts = generatedCanvas.GetComponentsInChildren(textType, true);
                for (int index = 0; index < texts.Length; index++)
                {
                    labels.Add((string)textProperty.GetValue(texts[index]));
                }

                foreach (BuiltInSkyLocationDefinition location in BuiltInSkyLocations.All)
                {
                    Assert.That(labels, Does.Contain(location.DisplayName));
                }

                Assert.That(labels, Does.Contain("Anterior"));
                Assert.That(labels, Does.Contain("Siguiente misión"));
                Assert.That(labels, Does.Contain("Trazado de constelaciones"));
            }
            finally
            {
                if (generatedCanvas != null)
                {
                    UnityEngine.Object.DestroyImmediate(generatedCanvas);
                }
            }
        }

        [Test]
        public void DetailAppearanceUsesTheExistingPhotometricFieldsWithoutClaimingComposition()
        {
            Type rendererType = Type.GetType("StellarVaultRenderer, Assembly-CSharp");
            Type starInfoType = rendererType?.GetNestedType("StarInfo", BindingFlags.Public);
            Type appearanceFactoryType = Type.GetType("StarDetailAppearanceFactory, Assembly-CSharp");
            Assert.That(rendererType, Is.Not.Null);
            Assert.That(starInfoType, Is.Not.Null);
            Assert.That(appearanceFactoryType, Is.Not.Null);

            object starInfo = Activator.CreateInstance(starInfoType);
            starInfoType.GetField("HipId").SetValue(starInfo, 91262);
            starInfoType.GetField("DisplayName").SetValue(starInfo, "Vega");
            starInfoType.GetField("Magnitude").SetValue(starInfo, 0.03f);
            starInfoType.GetField("ColorIndex").SetValue(starInfo, 0f);
            starInfoType.GetField("Luminosity").SetValue(starInfo, 40.12f);
            starInfoType.GetField("SpectralType").SetValue(starInfo, "A0Va");
            starInfoType.GetField("TwinkleSeed").SetValue(starInfo, 0.42f);

            MethodInfo createAppearance = appearanceFactoryType.GetMethod("Create");
            object appearance = createAppearance.Invoke(null, new[] { starInfo });
            Type appearanceType = appearance.GetType();

            float temperature = (float)appearanceType.GetField("EstimatedTemperatureKelvin").GetValue(appearance);
            float radius = (float)appearanceType.GetField("EstimatedRadiusSolarRadii").GetValue(appearance);
            float visualDiameter = (float)appearanceType.GetField("VisualDiameter").GetValue(appearance);

            Assert.That(temperature, Is.InRange(8000f, 13000f));
            Assert.That(radius, Is.GreaterThan(0f));
            Assert.That(visualDiameter, Is.GreaterThan(0f));
        }
    }
}
