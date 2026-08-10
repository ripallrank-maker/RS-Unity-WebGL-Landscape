# RS WebGL Landscape — WebGL Template

A Unity WebGL template based on Unity's built-in **Default** template, changed in
one way only: **how the canvas is sized**. Instead of a fixed `WIDTH x HEIGHT`
box it fits the canvas to the browser viewport according to a few fields you set
in **Player Settings**.

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

## Config fields (Player Settings)

Once **RSLandscape** is selected, the fields below appear under *Resolution and
Presentation*. All are optional — leaving one blank uses the default shown.

| Field           | Default                     | Meaning |
|-----------------|-----------------------------|---------|
| `RS_FIT_MODE`   | `expand`                    | `expand` = fill viewport (recommended, let the adapter handle rotation); `contain` = keep aspect, letterbox; `cover` = keep aspect, crop to fill; `stretch` = fill, ignore aspect. |
| `RS_ASPECT_W`   | Default Canvas **Width**    | Design aspect width (used by contain/cover). |
| `RS_ASPECT_H`   | Default Canvas **Height**   | Design aspect height (used by contain/cover). |
| `RS_BACKGROUND` | `#231F20`                   | Letterbox / page background color. |
| `RS_MAX_DPR`    | `0` (unlimited)             | Cap on `devicePixelRatio` to reduce GPU load on high-DPI mobile screens (e.g. `2`). |

> If the custom fields don't show in your Unity version, edit the `RSConfig`
> block at the top of `index.html` directly — the same values live there with the
> same defaults.

## Notes

- The template is self-contained: the loading spinner and progress bar are pure
  CSS/SVG, so there are no bundled `.png`/`.ico` assets to manage.
- `config.matchWebGLToCanvasSize` is left on, so the CSS size set by the fit
  logic drives the real WebGL render-target resolution.
