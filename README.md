# Observatorio Estelar en Realidad Virtual

Un observatorio astronómico inmersivo para Meta Quest 3, desarrollado por la **Sociedad Astronómica Mazatleca (SAMAZ)** en colaboración con la **Universidad de Guanajuato**.

## Estado actual

**Fase 0 completada** — la base de trabajo está lista.

- [x] Proyecto Unity 6 configurado con Universal Render Pipeline
- [x] Repositorio Git con Git LFS activo para binarios pesados
- [x] Catálogo HYG importado y convertido a formato binario (15,599 estrellas, mag ≤ 7.0)
- [x] Script de conversión Python reproducible y documentado
- [x] Escena base lista para desarrollo

Lo que viene: **Fase 1 — Bóveda Celeste Funcional**. GPU Instancing de estrellas, shader espectral por tipo estelar, selección con controlador Touch y panel informativo. El núcleo del proyecto.

---

## Prototipo temprano de bóveda estelar

La escena `Assets/Scenes/SampleScene.unity` ya incluye un objeto **Celestial Vault Prototype** con `StellarVaultRenderer`.

Al entrar a Play Mode:

- Carga `Assets/StreamingAssets/Data/hyg_stars.bytes` con `UnityWebRequest`, compatible con Android/Quest.
- Convierte RA/Dec del catálogo HYG a posiciones sobre una esfera de radio 90.
- Filtra el Sol del catálogo para evitar que domine el render inicial.
- Dibuja las estrellas como quads instanciados en lotes de 1023 usando el shader `Observatorio/StarBillboardURP`.
- Aplica color aproximado desde B-V y tamaño/brillo desde magnitud aparente.

Este renderer es deliberadamente de prototipo: permite validar visualmente densidad, color, escala y rendimiento inicial antes de migrar la parte astronómica fina a Astronomy Engine.

Controles en Play Mode:

- Mantener clic derecho + mover mouse: mirar alrededor.
- Flechas o `I`/`J`/`K`/`L`: girar la vista sin mouse.
- `W`/`A`/`S`/`D`: desplazarse; `Q`/`E`: bajar/subir.
- `Shift`: movimiento rápido; `Ctrl`: movimiento fino.
- Rueda del mouse o `Z`/`X`: cambiar campo de visión.
- `+`/`-`: subir/bajar brillo global.
- `[`/`]`: reducir/aumentar tamaño aparente de estrellas.
- `Page Up`/`Page Down`: relajar o endurecer el filtro de magnitud.
- `R`: regresar la cámara a la pose inicial; `Home`: restaurar visibilidad de estrellas.

El Game View debe estar enfocado para recibir teclado. En una bóveda celeste el desplazamiento puede sentirse sutil porque las estrellas se renderizan sobre una esfera grande; para explorar el cielo, las flechas o `I/J/K/L` son la forma más directa.

En Scene View la bóveda también tiene preview. Si no aparece inmediatamente después de recompilar scripts, seleccionar **Celestial Vault Prototype** o mover la cámara del Scene View fuerza el repintado.

Nota: el binario actual contiene estrellas hasta magnitud 7.0. `Page Up` solo revelará más si el filtro se bajó antes o si se regenera `hyg_stars.bytes` con un límite mayor.

---

## Cómo empezar

### Prerrequisitos

- **Unity 6000.3.10f1** (instalar desde Unity Hub)
- **Git** con **Git LFS** instalado (`git lfs install`)
- **Python 3.8+** (para regenerar datos del catálogo, si es necesario)
- **Meta Quest 3** en modo desarrollador (para pruebas en dispositivo)

### Clonar el repositorio

```bash
git clone https://github.com/tu-usuario/Observatorio-SAMAZ.git
cd Observatorio-SAMAZ
git lfs pull
```

### Abrir en Unity

1. Abrir **Unity Hub** → Add → seleccionar la carpeta del proyecto
2. Asegurarse de que la versión sea **6000.3.10f1**
3. En **File → Build Settings**, cambiar la plataforma a **Android**
4. La escena principal está en `Assets/Scenes/SampleScene.unity`

---

## El script de conversión

El catálogo HYG viene como un CSV de ~120,000 estrellas. Para no parsear CSV en el Quest 3 (que bastante tiene con renderizar a 72 fps), lo convertimos a un formato binario compacto.

```bash
# Descarga el catálogo automáticamente y genera los binarios
python3 Tools/hyg_to_bytes.py

# O con un CSV local
python3 Tools/hyg_to_bytes.py --input ruta/al/hygdata_v41.csv

# Cambiar el límite de magnitud (default: 7.0)
python3 Tools/hyg_to_bytes.py --mag-limit 6.0
```

Genera dos archivos en `Assets/StreamingAssets/Data/`:

- **`hyg_stars.bytes`** — datos binarios (header + records de 48 bytes por estrella)
- **`star_names.json`** — nombres propios indexados por HIP ID (Sirio, Betelgeuse, Vega...)

El script solo usa la biblioteca estándar de Python. Sin `pip install` de nada.

Los datos provienen del [catálogo HYG](https://github.com/astronexus/HYG-Database) (CC BY-SA), que combina los catálogos Hipparcos, Yale Bright Star y Gliese.

---

## Estructura del proyecto

```
Observatorio-SAMAZ/
├── Assets/
│   ├── Scenes/                  # Escenas de Unity
│   ├── Settings/                # Configuración URP (renderers, volumes)
│   └── StreamingAssets/
│       └── Data/
│           ├── hyg_stars.bytes  # Catálogo binario (generado)
│           └── star_names.json  # Nombres propios (generado)
├── Packages/                    # Dependencias de Unity
├── ProjectSettings/             # Configuración del proyecto
├── Tools/
│   └── hyg_to_bytes.py          # Script de conversión HYG → binario
└── README.md
```

---

## Equipo y colaboración

Proyecto conjunto de la **Sociedad Astronómica Mazatleca** y la **Universidad de Guanajuato**.

### Workflow Git

- **`main`** — versión estable
- **`develop`** — rama de integración para desarrollo activo
- Los PRs van a `develop`. Al cerrar cada fase se integra a `main`.

---

## Licencia y datos

- **Datos astronómicos**: [HYG Database](https://github.com/astronexus/HYG-Database) — CC BY-SA 2.5
- **Código del proyecto**: Uso académico — SAMAZ + Universidad de Guanajuato
- Todos los datos astronómicos provienen de fuentes abiertas (HYG, NASA Open Data)
