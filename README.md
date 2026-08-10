# RS WebGL Landscape

Portrait/landscape orientation adapter for Unity **WebGL** (and the Editor).

Keeps a game that was designed for **landscape** playable when the browser/device is in
**portrait**, by rotating cameras and every Screen-Space Overlay / Screen-Space Camera canvas
instead of forcing the player to rotate their device. Works automatically via screen-size
polling, plus a small JavaScript bridge that forwards browser `resize` / `orientationchange`
events.

Screen-Space Overlay, Screen-Space Camera and World Space canvases are all handled, with
different mechanics under the hood:
- **Overlay** and **Camera** canvases both get a manual `__PortraitRotationRoot__` pivot
  inserted and rotated to compensate, plus a `CanvasScaler` reference-resolution swap.
  Screen-Space Camera's own RectTransform gets re-oriented by Unity to match its Render
  Camera (shown as "driven by Canvas" in the Inspector), but the rendered result still comes
  out screen-locked/unrotated on the final frame regardless — same as Overlay — so it needs
  the identical manual compensation, not less.
- **World Space** canvases are rendered by the camera like any other world content, so the
  camera rotation already tilts them — no extra handling needed.

A canvas can switch `renderMode` at runtime (e.g. Overlay → Camera to attach a popup canvas to
whichever camera the current scene loaded) — the pivot is applied per canvas reference
regardless of its current render mode, so this is safe.

## Installation

The package lives under `Assets/RSWebGLLandscape/`, so Unity picks it up automatically.
To reuse it in another project, copy the whole `RSWebGLLandscape` folder into that project's
`Assets/` directory (or import the exported `.unitypackage`).

## Usage

1. Create an empty GameObject in your first scene (e.g. `Home`).
2. Add the **`WebGLOrientationAdapter`** component to it.
3. (Optional) Assign a **World Root** transform if your gameplay objects should also rotate.
4. (Optional) Tune **Portrait Rotation Deg** (default `-90`).

The component is a `DontDestroyOnLoad` singleton — add it once. Access it anywhere via
`WebGLOrientationAdapter.Instance`.

### Handling input in portrait mode

When portrait rotation is active the canvas is rotated, so raw screen deltas have swapped axes.
Convert them before doing direction checks:

```csharp
Vector2 logical = WebGLOrientationAdapter.Instance.ScreenDeltaToLogical(rawScreenDelta);
Vector2 point   = WebGLOrientationAdapter.Instance.ScreenPointToLogical(Input.mousePosition);
```

Check `WebGLOrientationAdapter.Instance.IsPortrait` for the current state and `.IsReady`
to know the first orientation pass has completed.

### Editor testing

Call `WebGLOrientationAdapter.Instance.SimulatePortrait(true/false)` to preview portrait
handling in the Editor without a browser.

## WebGL template

The package ships an optional **RSLandscape** WebGL template (importable via
Package Manager → Samples). It's Unity's built-in template with one change: the
canvas is fitted to the browser viewport by a configurable fit mode and aspect
ratio. The default `aspect` mode keeps the design ratio and letterboxes, auto-
flipping the target aspect by orientation — a portrait window stays portrait so
this adapter still rotates the content, and off-ratio desktop/landscape screens
are letterboxed instead of cropped (fixed purely in CSS, no adapter change).
Configure it under **Project Settings → RS WebGL Landscape** (enum dropdown). See
[Samples~/WebGLTemplate/README.md](Samples~/WebGLTemplate/README.md) for setup
and the config options.

## Requirements

- Unity 2021.3 or newer
- `com.unity.ugui` (uGUI) — declared as a dependency

## Assembly

Runtime code compiles into the auto-referenced assembly **`RSWebGLLandscape`**, so your
game scripts in `Assembly-CSharp` can use the API without any extra asmdef reference.
