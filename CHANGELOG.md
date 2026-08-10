# Changelog

All notable changes to this package are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **RSLandscape WebGL template** (importable UPM sample under `Samples~/WebGLTemplate`): based on Unity's built-in template, changed only in how the canvas is sized — it fits the browser viewport by a configurable fit mode (`expand` / `contain` / `cover` / `stretch`) and design aspect ratio. The default `expand` mode fills the viewport so `WebGLOrientationAdapter` can drive portrait/landscape rotation from the real window size.
- **Project Settings → RS WebGL Landscape** page (package Editor code) with a real **enum dropdown** for the fit mode plus a color picker — Unity's built-in template custom fields are text-only. A WebGL build post-processor writes the chosen values into `TemplateData/rsconfig.js`, which `index.html` reads via `window.RSConfigOverride`. The template still works standalone (edit `rsconfig.js` by hand) when the package Editor code isn't present.

### Fixed
- Assembly definition no longer excludes the `WebGL` platform (it was excluding the very target the package is for).
- Camera `orthographicSize` correction in portrait now uses `baseOrthographicSize / cam.aspect`, matching the CanvasScaler's own scaling exactly so world-space content (e.g. tutorial hitboxes) stays pixel-aligned with Screen-Space UI. The old fill/fit min/max formula could drift out of sync with the canvas.

### Added
- Screen-Space Camera canvases are now handled the same way as Screen-Space Overlay: a `__PortraitRotationRoot__` pivot plus the `CanvasScaler` reference-resolution swap. Screen-Space Camera's own RectTransform does get re-oriented by Unity to match its Render Camera (shown as "driven by Canvas" in the Inspector), but the final rendered result still comes out screen-locked/unrotated regardless, same as Overlay, so it needs the identical manual compensation.
- Canvases no longer need a `worldCamera` assigned at the moment this adapter captures its baseline (useful for canvases that switch `renderMode`/`worldCamera` at runtime, e.g. a popup canvas re-attached per scene).

### Fixed
- The adapter is scene-scoped (not `DontDestroyOnLoad`), but a canvas it manages can be (e.g. a persistent popup canvas). On scene unload, `OnDestroy` now resets any still-portrait-swapped canvases back to baseline before the adapter is destroyed. Previously, the next scene's fresh adapter would read the already-swapped `CanvasScaler.referenceResolution` as if it were the original landscape baseline and swap it a second time, leaving the reference resolution landscape-shaped while the screen was still physically portrait — shrinking/misscaling all of that canvas's content.

## [1.0.0]

### Added
- `WebGLOrientationAdapter` singleton MonoBehaviour: portrait/landscape handling for WebGL and Editor via screen-size polling.
- Camera rotation + orthographic-size correction and a per-canvas `__PortraitRotationRoot__` pivot for ScreenSpace-Overlay canvases in portrait mode.
- `WebGLOrientationBridge.jslib` bridge forwarding browser `resize` / `orientationchange` events to Unity.
- `ScreenDeltaToLogical` / `ScreenPointToLogical` helpers for input mapping in portrait mode.
- `SimulatePortrait` for previewing portrait handling in the Editor.
- Packaged as `com.rs.webgllandscape` (folder `Assets/RSWebGLLandscape`) with the `RSWebGLLandscape` assembly.
