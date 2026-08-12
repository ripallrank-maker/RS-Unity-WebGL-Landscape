// RS WebGL Landscape — canvas fit config.
//
// This file is overwritten on every WebGL build from
// Project Settings > RS WebGL Landscape (enum dropdown / color picker) when the
// package's Editor code is present. If you don't have the package installed,
// edit the values here by hand.
//
// fitMode: "expand" | "aspect" | "contain" | "cover" | "stretch"
//   "expand" (default): fill the whole viewport, no aspect kept.
//   "aspect": keep the design ratio, letterbox, auto-flip by orientation so
//   the in-Unity adapter still sees portrait as portrait.
// aspectW / aspectH: design aspect, only used by aspect/contain/cover
//   (0 = template default 1920x1080, independent of Player Settings)
window.RSConfigOverride = {
  fitMode: "expand",
  aspectW: 0,
  aspectH: 0,
  background: "#231F20",
  maxDevicePixelRatio: 0
};
