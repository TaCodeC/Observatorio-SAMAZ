# Cielo local dependiente de ubicación y tiempo

## Objetivo

Mostrar el catálogo HYG como se observa desde una ubicación terrestre y un instante
determinados. Las entradas de ubicación pueden venir de coordenadas manuales, un menú de
sitios predefinidos o una consulta de ubicación del dispositivo; todas terminan en el mismo
`GeoCoordinate` validado.

## Decisión de biblioteca

La base matemática es [Astronomy Engine](https://github.com/cosinekitty/astronomy), versión
fijada `v2.1.19`. Es C# puro, no usa plugins nativos ni efemérides descargadas en ejecución,
y está distribuida bajo MIT. El proyecto upstream declara precisión aproximada de ±1 minuto de
arco y valida sus cálculos contra NOVAS/JPL Horizons; es adecuada para un planetario VR
educativo, pero no se presenta como astrometría profesional subarcosegundo.

La fuente se conserva sin modificaciones en
`Packages/com.samaz.astronomy-engine/Runtime/ThirdParty/astronomy.cs`. La URL, versión,
checksum y licencia están en `UPSTREAM.md` y `LICENSE.md` del mismo paquete.

## Marcos y convención de ejes

HYG entrega RA/Dec J2000. Astronomy Engine transforma ese sistema (`EQJ`) directamente al
horizonte topocéntrico de un observador:

```text
HYG RA/Dec (EQJ, J2000)
          │  Astronomy.Rotation_EQJ_HOR(UTC, latitud, longitud, elevación)
          ▼
Horizontal: x=norte, y=oeste, z=cenit
          │
          ▼
Unity local: +X=este, +Y=cenit, +Z=norte
```

La conversión de horizonte a Unity está encapsulada en
`AstronomyEngineLocalSkyTransform.HorizontalToUnity`; no debe duplicarse en UI ni shaders.
La longitud es positiva hacia el este, la latitud positiva hacia el norte, la elevación usa
metros y todo cálculo usa UTC. La zona horaria pertenece sólo a la interfaz de entrada/salida.

## Diseño por responsabilidades

```text
texto manual ─┐
menú/preset ─┼─> LocalSkyController ─> AstronomyEngineLocalSkyTransform ─> StellarVaultRenderer
GPS opcional ┘          │                         │                              │
reloj UTC ──────────────┘                         └─ SkyFrame EQJ→mundo          └─ dibujo + selección
```

- `GeoCoordinate` y `ObservationContext` validan unidades y evitan fechas ambiguas.
- `AstronomyEngineLocalSkyTransform` es el único adaptador que conoce Astronomy Engine.
- `SkyFrame` encapsula una transformación inmutable para un instante y observador.
- `LocalSkyController` reúne ubicación, reloj y calibración norte; no construye UI.
- `StellarVaultRenderer` conserva cada dirección EQJ y actualiza, desde la misma fuente,
  las matrices de render y los datos usados por selección. Así un puntero nunca selecciona
  una estrella en una posición distinta de la dibujada.

## Activación segura

No se modificó ninguna escena existente, porque la escena de build actual está sin trackear.
Para activar la fase local en una escena controlada:

1. Añadir `LocalSkyController` al mismo GameObject que `StellarVaultRenderer`.
2. Dejar `Sky Center Anchor` vacío para usar `Camera.main`, o asignar explícitamente el
   ancla de seguimiento/XR apropiada.
3. Configurar las coordenadas manuales, asignar un `LocationPreset`, o llamar a
   `SetBuiltInLocation` desde el menú. El valor inicial manual es Mazatlán.
4. Elegir `SystemUtc`, `FixedUtc` o `AcceleratedUtc`; para demos conviene fijar primero
   una hora UTC reproducible.

Al desactivar el controlador, el renderer vuelve al modo estático J2000 anterior. En modo
local se excluye el registro solar fijo de HYG, porque el Sol deberá añadirse después como un
cuerpo calculado por Astronomy Engine y no como una estrella inmóvil del catálogo.

## Puntos de entrada de UI

Una futura UI VR debe usar únicamente estas APIs de `LocalSkyController`:

- `TrySetManualLocation(latitud, longitud, elevación, out error)` para campos de texto.
- `SetBuiltInLocation(...)` para el menú compacto Mazatlán/Londres.
- `SetLocationPreset(...)` para un catálogo ampliable de ScriptableObjects.
- `SetSystemUtcTime`, `SetFixedUtcTime` y `SetAcceleratedUtcTime` para el reloj.
- `RequestDeviceLocation()` sólo detrás de una acción explícita de la persona usuaria.

La geolocalización se consulta una vez, se detiene inmediatamente y sólo permanece en memoria.
Unity añade el permiso Android de ubicación al usar `LocationService`; el runtime solicita el
permiso antes de iniciar el servicio. Quest puede no ofrecer una fijación GPS fiable, por lo
que las coordenadas manuales y presets siempre permanecen disponibles.

## Rendimiento y horizonte

`Rotation_EQJ_HOR` se ejecuta una vez por actualización (por defecto cada 0.5 segundos), no
por estrella ni por frame. Después se actualizan las matrices de las aproximadamente 15,599
estrellas; esto mantiene el coste acotado y permite acelerar el tiempo para demostraciones.

El shader corta las estrellas bajo el horizonte y `TryFindStarInView` aplica el mismo estado
`IsAboveHorizon`. La bóveda se centra visualmente en el ancla para evitar paralaje artificial
al moverse la cabeza.

## Límites y siguiente fase científica

Esta fase corrige orientación terrestre, tiempo sidéreo, precesión y nutación al convertir
J2000 al horizonte local. El binario HYG actual no guarda movimiento propio, velocidad radial
ni paralaje de precisión: las estrellas de alto movimiento propio requerirán una versión 2
del formato HYG antes de afirmar precisión de largo plazo. Sol, Luna, planetas, refracción
meteorológica, extinción y brillo de crepúsculo se añadirán como capas separadas, sin mezclar
efemérides con el cargador del catálogo.

