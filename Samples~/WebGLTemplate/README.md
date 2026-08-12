# RS WebGL Landscape — WebGL Template

A Unity WebGL template based on Unity's built-in **Default** template, changed in
one way only: **how the canvas is sized**. Instead of a fixed `WIDTH x HEIGHT`
box (Player Settings' Default Canvas size is not used at all) it fits the canvas
to the browser viewport according to a few fields you set in **Project Settings
→ RS WebGL Landscape**.

This pairs with the `WebGLOrientationAdapter` in this package: with the default
`expand` fit mode the canvas fills the whole browser window, so Unity always sees
the real window size and the adapter can rotate the game between portrait and
landscape on its own.

## Install

Unity only discovers WebGL templates under `Assets/WebGLTemplates/`, so importing
this sample is a two-step process:

1. In **Package Manager**, select this package → **Samples** → *Import* the
   **WebGL Template (Landscape Fit)** sample. It lands under
   `Assets/Samples/RS WebGL Landscape/<version>/WebGLTemplate/`.
2. Move (or copy) the **`RSLandscape`** folder from there into
   `Assets/WebGLTemplates/` (create that folder if it doesn't exist). Final path:

   ```
   Assets/WebGLTemplates/RSLandscape/index.html
   Assets/WebGLTemplates/RSLandscape/TemplateData/style.css
   ```

3. **Project Settings → Player → WebGL → Resolution and Presentation → WebGL
   Template**, choose **RSLandscape**.

## Config (Project Settings → RS WebGL Landscape)

Unity's built-in WebGL template custom fields are text-only (no dropdowns), so
the config lives in a dedicated settings page instead: **Project Settings → RS
WebGL Landscape**. It has a real **enum dropdown** for the fit mode and a color
picker for the background.

| Setting                 | Default        | Meaning |
|-------------------------|----------------|---------|
| **Fit Mode** (dropdown) | `Expand`       | `Expand` = fill the whole viewport, no aspect kept (default); `Aspect` = keep the design ratio + letterbox, **auto-flipped by orientation** (a portrait window stays portrait-shaped so the in-Unity adapter rotates the content, and off-ratio desktop/landscape screens are letterboxed instead of cropped); `Contain` = fixed aspect + letterbox (no flip); `Cover` = fixed aspect + crop; `Stretch` = fill, ignore aspect. |
| **Aspect Width**        | `0` (auto)     | Design aspect width for Aspect/Contain/Cover. `0` = template default (1920), independent of Player Settings. |
| **Aspect Height**       | `0` (auto)     | Design aspect height for Aspect/Contain/Cover. `0` = template default (1080), independent of Player Settings. |
| **Background**          | `#231F20`      | Letterbox / page background color. |
| **Max Device Pixel Ratio** | `0` (unlimited) | Cap on `devicePixelRatio` to reduce GPU load on high-DPI mobile screens (e.g. `2`). |

On each WebGL build these values are written to `TemplateData/rsconfig.js` in the
output (via a build post-processor in the package's Editor code), and
`index.html` reads them from `window.RSConfigOverride`.

> **Using the template without the package's Editor code?** The dropdown page and
> the auto-generated `rsconfig.js` both come from the package. If you only copied
> the template files, just edit `TemplateData/rsconfig.js` by hand — it holds the
> same values with the same defaults.

## Notes

- The template is self-contained: the loading spinner and progress bar are pure
  CSS/SVG, so there are no bundled `.png`/`.ico` assets to manage.
- `config.matchWebGLToCanvasSize` is left on, so the CSS size set by the fit
  logic drives the real WebGL render-target resolution.
