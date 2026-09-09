+++
title = "Custom Water Materials for Non-Naval Scenes"
description = ""
weight = 1
+++

This guide explains how to give a scene its own water look when the Naval DLC macro-displacement renderer is **not** in use — for example, a river ford, a lake, or any land battle with a water body in it.

---

## Overview

By default every non-naval scene shares a single engine water material called `base_water_scene_default`. That material controls the colour of the water, how light scatters through it, and what the surface looks like.

You can replace it per-scene with any material that uses the `water_surface` shader. When you do, the engine uses your material instead — and the wave simulation parameters (strength, choppiness, wind direction influence) are no longer overridden by gameplay code, so you can tune them directly in the Scene Inspector.

> **Naval DLC note:** This override only applies when the Naval DLC macro-displacement renderer is **inactive**. In naval combat missions the override is ignored and the naval pipeline takes over as usual.

---

## Step 1 — Create a Water Material

1. Open the **Resource Browser** and find `base_water_scene_default`.  
2. Duplicate it and give it a clear name, for example `my_module_lake_water`.  
3. Open the duplicate in the **Material Inspector**.  
4. Verify that the **Shader** field shows `water_surface`. Only materials with this exact shader will appear in the scene's water material picker.

---

## Step 2 — Set the Material on the Scene

1. Open your scene in the editor.  
2. In the **Scene Inspector**, expand the **Water** group.  
3. Find the **Non-Naval Water Material Override** field.  
4. Click the picker and choose your material.  
   - The list is filtered to show only `water_surface` materials.  
   - Leave the field empty to revert to the engine default (`base_water_scene_default`).  
5. Save the scene. The override is stored in the scene file and loaded automatically every time the scene is opened in-game or in the editor.

>   
> **Editor preview:** If the Naval DLC is active in your editor session, the **Visibility Manager** panel will show a **"Preview Naval Water Renderer"** checkbox. Make sure this is **unchecked** while working on non-naval water — when it is checked the naval macro-displacement renderer is active and your material override will not be visible. Uncheck it to switch the viewport to the non-naval path and see your material changes live.

---

## Step 3 — Tune the Material

The two fields that have the most visual impact are in the Material Inspector. Everything else (wave animation, displacement, caustics) is driven by the simulation and cannot be changed per-material.

### Factor Color — Water Body Color

This controls how light is absorbed as it travels down through the water. Think of it as the colour the water takes on when it is deep.

| Channel | What it controls |
| :---- | :---- |
| **R (red)** | How much red light survives at depth. Low \= reds disappear quickly, giving a blue-green tint. |
| **G (green)** | How much green light survives. |
| **B (blue)** | How much blue light survives. High \= clear blue water. |
| **A (alpha)** | How quickly the scattered haze takes over with depth. Low values make the haze appear almost immediately; higher values (0.5–2.0) give a natural depth gradient. |

**Starting points to try:**

| Water type | Factor Color (R, G, B, A) |
| :---- | :---- |
| Clear tropical blue | 0.05, 0.35, 0.80, 1.0 |
| Deep dark ocean | 0.02, 0.10, 0.25, 1.5 |
| Murky river | 0.10, 0.50, 0.30, 0.8 |
| Shallow lagoon | 0.15, 0.60, 0.70, 0.5 |
| Swamp / bog | 0.20, 0.35, 0.10, 0.4 |

> The colour values are in linear space. The Material Inspector displays them converted to sRGB for readability — what you see in the colour picker is approximately what you will see in-game in bright sunlight.

### Factor2 Color — Underwater Haze / Scatter Color

This is the tint of the murky glow you see when looking through shallow water at a lit surface below. Think of it as the colour of the water itself rather than what is behind it.

| Channel | What it controls |
| :---- | :---- |
| **R, G, B** | The haze colour. A good starting point is a lighter, slightly greenish version of your Factor Color. |
| **A (alpha)** | How strongly the haze colour appears near the surface. 0.1–0.3 is a natural range. |

**Starting points to try:**

| Water type | Factor2 Color (R, G, B, A) |
| :---- | :---- |
| Clear tropical blue | 0.10, 0.60, 0.50, 0.2 |
| Deep dark ocean | 0.05, 0.20, 0.40, 0.1 |
| Murky river | 0.15, 0.40, 0.20, 0.3 |
| Swamp / bog | 0.20, 0.30, 0.08, 0.35 |

> Set Factor2 Color to a colour in the same family as Factor Color but lighter and slightly more green or cyan. Very saturated values can look unnatural.

---

## Step 4 — Tune the Scene Wave Settings

When a custom water material override is active, the gameplay code no longer forces fixed wave parameters onto your scene. The sliders in the **Scene Inspector Water group** now have full effect.

| Slider | What it does | Suggested range |
| :---- | :---- | :---- |
| **Strength** | Controls how energetic and tall the waves are. Higher \= choppier surface, more foam at the edges. | 0.5–3.0 for rivers and lakes; up to 6–8 for stormy coasts |
| **Choppiness** | Controls the shape of wave peaks. 0 \= smooth rolling waves, 1 \= sharp peaked waves. | 0.3–0.7 for most water bodies |
| **Wind Dependency** | How strongly the waves align with the wind direction. 0 \= waves travel in all directions equally, 1 \= waves strongly follow the wind. | 0.0–0.5 for enclosed water; 0.5–1.0 for open water |

> Without a material override, **Strength** is forced to 10 and **Wind Dependency** to 0 by the engine regardless of what you set here. The sliders only take effect when a custom material override is assigned.

---

## What Cannot Be Changed per Material

The following are driven by the engine simulation or the global render pipeline. They are the same for every non-naval water surface in every scene and cannot be overridden by changing the water material.

- Wave normal maps and displacement (these come from the FFT water simulation)  
- Caustics  
- Refraction of the scene behind the water

---

## Hot-Reloading in the Editor

When you save changes to your water material while the scene is open in the editor, the engine detects the change and refreshes the water automatically within a few seconds. You do not need to reload the scene.

If the water does not update after saving the material, try toggling the **Non-Naval Water Material Override** picker off and back on to force a refresh.

---

## Quick Checklist

- [ ] Material shader is exactly `water_surface`  
- [ ] Override field is set in the Scene Inspector Water group  
- [ ] Scene is saved after setting the override  
- [ ] Factor Color alpha is above 0.1 (very low values make all water look uniformly hazy)  
- [ ] Factor2 Color is in the same colour family as Factor Color but lighter  
- [ ] Strength and Choppiness sliders are set to the values you want (they now matter)

