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
// aspectW / aspectH: the aspect ratio YOUR GAME is designed at (e.g. a game
//   built at 1920x1080 → aspectW: 1920, aspectH: 1080), NOT the player's
//   screen size. Only used by fitMode "aspect"/"contain"/"cover" — ignored
//   by "expand"/"stretch". 0 = template default 1920x1080.
window.RSConfigOverride = {
  fitMode: "expand",
  aspectW: 0,
  aspectH: 0,
  background: "#231F20",
  maxDevicePixelRatio: 0
};
