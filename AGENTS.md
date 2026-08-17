# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

**Observatorio Estelar en Realidad Virtual** — a VR stellar observatory built for Meta Quest 3 by the Sociedad Astronómica Mazatleca (SAMAZ) and Universidad de Guanajuato. The app renders real stars from the HYG catalog on a 360° celestial vault, lets users select stars with Touch controllers, shows info panels, and includes constellation overlays with multicultural mythology content (Greek, Arab, Chinese, Mesoamerican).

## Engine & Platform

- **Unity 6000.3.10f1** (Unity 6) with **Universal Render Pipeline (URP)**
- Target: **Meta Quest 3 standalone** (Android build target, API Level 32+). Secondary: PC VR via Quest Link/AirLink
- XR stack: Meta XR SDK (primary) + OpenXR Plugin (portability layer)
- Input: Unity Input System package (`com.unity.inputsystem`)
- Performance target: **72 fps** on standalone Quest 3

## Build & Run

```bash
# CLI build (requires Unity installed and in PATH)
unity -batchmode -projectPath . -buildTarget Android -executeMethod BuildScript.Build -quit

# Run tests
unity -batchmode -projectPath . -runTests -testResults results.xml -quit
```

Most workflow happens inside the Unity Editor: open the project, switch platform to Android, and use **File > Build & Run** with a connected Quest 3.

## Architecture (from project documentation)

The project is organized into five functional modules:

1. **Bóveda Celeste (Celestial Vault)** — GPU Instanced billboard quads for ~9,096 stars (mag < 7.0 from HYG catalog). Milky Way as inverted-sphere SkyBox panoramic texture.
2. **Sistema de Estrellas (Star System)** — Ray-based star selection via Touch controller, zoom transition, detail view with plasma/convection HLSL shader, info panel (name, spectral type, magnitude, distance, radius, mass).
3. **Constelaciones** — Line renderers connecting constellation stars, mythological figure overlays (engraving style), tabbed info panel (astronomy, global mythology, Mexican context with `has_data` flag).
4. **Gamificación** — Observation challenges mode, geolocation-based sky simulation.
5. **UI VR** — Holographic floating panels operable with Touch controllers, legible at 60–80 cm, max 30% of visual field.

### Rendering Strategy

- Stars rendered as GPU Instanced billboard quads with a shared material
- Spectral color via ShaderGraph + per-instance HDR property
- Twinkle effect via ShaderGraph noise + time
- Detail star shader: custom HLSL (plasma + convection) — only runs for the selected star
- Solar flares: Particle System + Additive shader (detail view only)
- Simplified shader variants for standalone; high-quality versions activate in PC VR mode

### Data Pipeline

- **HYG Catalog** (~119,000 stars, CC BY-SA): imported as CSV at edit-time, converted to `.bytes` binary (ScriptableObject or binary file) for efficient runtime loading — no CSV parsing on device
- Star detail data: JSON indexed by HIP ID, loaded on-demand when a star is selected
- Constellation data: ScriptableObjects per constellation
- Mythology text: localized JSON files (`es`/`en`)
- Mesoamerican context: separate JSON with `has_data` flag to hide empty sections
- Mythological illustrations: PNGs loaded from `Resources/` on demand
- External APIs (optional): NASA Exoplanet Archive (REST), Heavens-Above or Stellarium Web API for ephemerides

## Key Constraints

- All astronomical data must come from open sources (HYG, NASA Open Data)
- No commercial-license dependencies
- UI language: Spanish (Mexico); English localization deferred to v1.1
- Star positions must be astronomically precise (J2000 epoch)
- No user data collection in initial version

## Project Phases

The project follows a semester roadmap (18 weeks) with minimum viable scope per phase:

- **Fase 0** (weeks 1–2): Unity config for Quest 3, Git + LFS, HYG catalog import, base scene
- **Fase 1** (weeks 3–7): Functional celestial vault — GPU instancing, spectral shader, star selection, info panel, 72 fps optimization
- **Fase 2** (weeks 8–13): Constellations + cultural content — line groupings, mythology panels, Mesoamerican module, illustrations
- **Fase 3** (weeks 14–18): Gamification, QA on device, VR tutorial, demo video + APK
