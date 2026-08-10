// RS WebGL Landscape — canvas fit config.
//
// This file is overwritten on every WebGL build from
// Project Settings > RS WebGL Landscape (enum dropdown / color picker) when the
// package's Editor code is present. If you don't have the package installed,
// edit the values here by hand.
//
// fitMode: "aspect" | "expand" | "contain" | "cover" | "stretch"
//   "aspect" (recommended): keep the design ratio, letterbox, auto-flip by
//   orientation so the in-Unity adapter still sees portrait as portrait.
// aspectW / aspectH: design aspect (0 = use Default Canvas size)
window.RSConfigOverride = {
  fitMode: "aspect",
  aspectW: 0,
  aspectH: 0,
  background: "#231F20",
  maxDevicePixelRatio: 0
};
