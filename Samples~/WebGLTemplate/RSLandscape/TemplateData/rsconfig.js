// RS WebGL Landscape — canvas fit config.
//
// This file is overwritten on every WebGL build from
// Project Settings > RS WebGL Landscape (enum dropdown / color picker) when the
// package's Editor code is present. If you don't have the package installed,
// edit the values here by hand.
//
// fitMode: "expand" | "contain" | "cover" | "stretch"
// aspectW / aspectH: design aspect for contain/cover (0 = use Default Canvas size)
window.RSConfigOverride = {
  fitMode: "expand",
  aspectW: 0,
  aspectH: 0,
  background: "#231F20",
  maxDevicePixelRatio: 0
};
