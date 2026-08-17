# Prototipo de ubicación y misiones — escena TEST

Esta iteración sólo se activa en `Assets/Scenes/test.unity`. Es una interfaz de prueba en pantalla para comprobar el cálculo de cielo local antes de diseñar la interfaz diegética de VR.

## Uso

1. Ejecutar la escena `test`.
2. Usar el panel izquierdo para elegir una ciudad. El cielo se recalcula con la coordenada, altitud y hora UTC actuales.
3. El cuadro superior derecho muestra el rumbo geográfico de la cámara, su altura de mirada y la ubicación activa. El norte virtual respeta la calibración `northYaw` de `LocalSkyController`.
4. La ficha de la estrella bajo la mirada aparece debajo de ese cuadro. Se movió allí para no invadir la referencia de orientación.
5. Un clic breve sobre una estrella intenta seleccionarla; mantener el clic izquierdo y arrastrar sobre el cielo rota la cámara. Los gestos que empiezan sobre un botón del HUD se reservan para la interfaz.
6. Mantener una estrella centrada durante `2.25 s` también abre su vista de detalle. La representación se aproxima desde la bóveda y se mantiene delante del observador; `Esc` la cierra.
7. En la parte inferior del panel izquierdo, elegir una misión. Para completarla hay que mantener una estrella objetivo dentro de `1.25°` del centro durante `1.25 s`.

La configuración de TEST usa arrastre con el botón izquierdo y deja el cursor libre, con una tolerancia de `6 px` para distinguir un clic de un arrastre.

## Ubicaciones curadas

Las coordenadas siguen el convenio interno: latitud norte positiva, longitud este positiva y elevación en metros sobre el nivel medio del mar. No se persiste ninguna de estas selecciones.

| Ubicación | Latitud | Longitud | Elevación | Propósito de prueba | Fuente |
| --- | ---: | ---: | ---: | --- | --- |
| Guanajuato, México | 21.0160975 | -101.2536214 | 2020 m | Altiplano mexicano | [INEGI: Guanajuato](https://www.inegi.org.mx/contenidos/productos/prod_serv/contenidos/espanol/bvinegi/productos/nueva_estruc/889463909286.pdf) |
| Mazatlán, México | 23.2003158 | -106.4222214 | 10 m | Costa del Pacífico y sede SAMAZ | [INEGI: Sinaloa](https://www.inegi.org.mx/contenidos/productos/prod_serv/contenidos/espanol/bvinegi/productos/nueva_estruc/889463911173.pdf) |
| Ciudad de México, México | 19.4333333 | -99.1333333 | 2240 m | Comparación local de altitud | [INEGI: Ciudad de México](https://www.inegi.org.mx/contenidos/productos/prod_serv/contenidos/espanol/bvinegi/productos/historicos/2104/702825200794/702825200794_1.pdf) |
| Greenwich, Reino Unido | 51.4772222 | 0.0000000 | 46 m | Meridiano cero | [Royal Geographical Society](https://www.rgs.org/media/5oilgjyd/backgroundinformationandcontext.pdf) |
| Quito, Ecuador | -0.2150000 | -78.5025000 | 2823 m | Cercanía al ecuador | [Observatorio Astronómico de Quito](https://oaq.epn.edu.ec/documentos/boletin2020.pdf) |
| Tromsø, Noruega | 69.6493000 | 18.9557100 | 5 m | Círculo polar ártico | [Kartverket](https://stadnamn.kartverket.no/fakta/86632) |
| Tokio, Japón | 35.6916667 | 139.7500000 | 6 m | Longitud oriental | [Japan Meteorological Agency](https://www.data.jma.go.jp/stats/etrn/view/monthly_s3_en.php?block_no=47662&view=2) |
| Sídney, Australia | -33.8607000 | 151.2050000 | 39 m | Hemisferio sur oriental | [Bureau of Meteorology](https://www.bom.gov.au/clim_data/cdio/metadata/pdf/siteinfo/IDCJMD0040.066062.SiteInfo.pdf) |
| Ushuaia, Argentina | -54.8108889 | -68.2956944 | 25 m | Latitud austral extrema | [Administración Portuaria de Ushuaia](https://www.argentina.gob.ar/economia/agencia-nacional-de-puertos-y-navegacion/puertos/ushuaia) |

Los puntos mundiales se escogieron para forzar diferencias visibles en el horizonte, el polo celeste y las estrellas accesibles; no representan la posición exacta de cada jugador.

## Misiones incluidas

- Localiza Polaris (`HIP 11767`).
- Localiza Orión (cualquier estrella HYG con código IAU `Ori`).
- Localiza Sirius (`HIP 32349`).
- Localiza la Osa Mayor (código IAU `UMa`).
- Localiza Vega (`HIP 91262`).

`CelestialMissionController` pregunta al `StellarVaultRenderer` tanto por visibilidad como por la estrella centrada. Así no hay una segunda matemática de misión que pueda separarse del renderizado, de la selección o del horizonte local.

## Selección y vista de detalle

`StellarFlyCameraController` sólo clasifica el gesto de ratón y emite una posición de pantalla para un clic breve. `StarDetailController` convierte esa posición en un rayo y pregunta al mismo `StellarVaultRenderer` que dibuja la bóveda; por ello el clic, la mirada, las misiones y el horizonte local comparten un único catálogo y marco de coordenadas.

La esfera de inspección es un único objeto procedural con `StarDetailURP.shader`. `StarDetailAppearanceFactory` deriva su color, intensidad, tamaño didáctico y patrón de granulación a partir de los campos ya conservados en el binario HYG: índice de color B-V, tipo espectral, luminosidad, magnitud y semilla visual. El efecto no desplaza ni modifica la estrella original de la bóveda.

## Beta de constelaciones

`ConstellationOverlayRenderer` añade a TEST un trazado de figuras occidentales modernas. El botón **TRAZADO** del panel izquierdo —o la tecla `C`— alterna entre:

1. `misión`: sólo la figura de la constelación vinculada a la misión actual; también funciona para una misión de estrella, resolviendo primero su código IAU en HYG.
2. `todas`: las 88 figuras para inspección visual.
3. `oculto`: sin trazado.

La fuente de topología es la cultura celeste `modern` de Stellarium, fijada al commit `daace2add6a1bf886e8ee1934f51e9c69f818d18` y convertida de forma reproducible con `Tools/import_stellarium_modern_constellations.py`. El asset resultante conserva sólo polilíneas de identificadores HIP: 88 figuras, 219 polilíneas, 695 segmentos y 710 HIP únicos. La validación de editor comprueba que los 710 HIP se resuelven en el HYG incluido antes de que el trazado llegue a ejecución.

```text
Stellarium Modern (HIP, offline) ──importador──> catálogo compacto
                                                  │
HYG + Astronomy Engine ──> StellarVaultRenderer ─┴──> una malla de arcos geodésicos
                                                        (mismo marco local y mismo horizonte)
```

No se usa Skyhook ni un segundo motor de posiciones para esta beta: HYG ya contiene los extremos HIP y `StellarVaultRenderer` ya realiza el marco local de la ubicación/hora. Cada arco consulta la dirección que la propia bóveda está renderizando, por lo que se actualiza junto con ella al cambiar de ciudad, tiempo u horizonte. Las líneas se agrupan en una sola malla con un solo material, en vez de crear cientos de `LineRenderer`, para preservar el presupuesto de Quest.

Las líneas de figuras **no** son una geometría oficial de la IAU; la IAU estandariza las 88 constelaciones y sus fronteras. Por ello la interfaz y el código las describen como el *trazado occidental moderno de Stellarium*, no como líneas IAU oficiales. La procedencia y condiciones de redistribución están documentadas en `Docs/ThirdPartyNotices.md`.

## Límite científico y siguiente iteración

Esta primera vista no afirma composición química ni una superficie observada de la estrella. El HYG binario local no conserva abundancias, temperatura efectiva medida, radio/masa medidos, gravedad superficial, edad, rotación, campos magnéticos ni parámetros de variabilidad. Por eso el panel llama explícitamente a temperatura y radio **estimados** y etiqueta la esfera como modelo visual didáctico.

Para la siguiente iteración, la ruta propuesta es una importación offline opcional de Gaia DR3 enlazada por HIP/Gaia, con un `StarDetailCatalog` compacto indexado por HIP y con procedencia/indicadores de calidad. Debe conservar los valores y sus incertidumbres cuando estén disponibles: temperatura efectiva, `log g`, metalicidad, radio, luminosidad, masa y edad. Gaia DR3 publica estos productos astrofísicos y evolutivos; se deberán filtrar por las banderas de calidad del propio catálogo antes de alimentar el shader. No se harán consultas de red durante la experiencia VR.

## Límite deliberado

El HUD es `ScreenSpaceOverlay`, adecuado para depurar TEST con teclado, ratón o pantalla táctil. La siguiente integración deberá llevar estas mismas acciones a un panel espacial XR con ray interactor, no reutilizar esta superficie de depuración como interfaz final de Quest.
