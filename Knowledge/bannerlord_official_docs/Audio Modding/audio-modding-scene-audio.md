+++
title = "Scene Audio"
weight = 30
+++

_Last updated: April 3, 2026_

---

## Adding Sound Entities

In our proprietary game engine, placing sounds into a scene is straightforward. Start by creating an **empty entity** in the scene.

Once the entity is added, select it in the scene hierarchy to open the **Inspector Window**. If the Inspector doesn't appear, enable it through **Window > Inspector Window**.

In the Inspector, expand the **Scripts** dropdown and choose **Add New Script**. From the dialog, select **AmbientSoundEmitter** to attach a sound emitter script to the entity.

The script exposes several options for controlling playback:

| Property | Description |
|---|---|
| **Event Path** | The FMOD event path for the sound to be played in the scene |
| **Play On Startup** | If enabled, the sound plays automatically when the scene loads. Useful for ambiences or persistent sounds |
| **Is Static** | Marks the emitter as fixed in place. Static emitters are cheaper to process since the engine doesn't need to track their position dynamically |
| **Override Sound** | Enables overrides for certain FMOD parameters specifically for 3D sound events |
| **Override Volume Multiplier** | Adjusts playback volume by a multiplier. Use commas (`,`) instead of periods (`.`) for decimal values |
| **Override Max Distance** | Sets a custom maximum audible distance for the event |
| **Override Min Distance** | Sets a custom minimum audible distance for the event |
| **Is Master Sound** | Tags the emitter as a master sound, used for global mix behaviors |
| **Is Master Suppressor** | When active inside a trigger volume, ducks (reduces) any sounds tagged as master sound |
| **Is Triggered** | Configures the emitter as a trigger volume, only playing when the listener enters the defined space |
| **Trigger Shape** | Defines the shape of the trigger volume (box or sphere) |

---

## Reverb Zones

The same process applies when adding reverb zones to a scene. In FMOD, reverbs are snapshot events that can be triggered through trigger volumes, just like standard sound events.

To set one up:

1. Create a new entity in the scene
2. Attach an **AmbientSoundEmitter** script
3. Enter the path to the reverb snapshot event
4. Enable **Is Triggered**
5. Select the appropriate trigger shape
6. Resize the volume using the transform tools to cover the desired area

The following snapshot event paths are available out of the box. You're also free to create and add your own snapshots and snapshot events.

- `event:/mission/ambient/reverb/alley`
- `event:/mission/ambient/reverb/caves`
- `event:/mission/ambient/reverb/courtyard`
- `event:/mission/ambient/reverb/dungeon`
- `event:/mission/ambient/reverb/forest`
- `event:/mission/ambient/reverb/gatehouse`
- `event:/mission/ambient/reverb/keep`
- `event:/mission/ambient/reverb/stone_corridor`
- `event:/mission/ambient/reverb/stone_interior`
- `event:/mission/ambient/reverb/valley`
- `event:/mission/ambient/reverb/wood_interior`

---

## Paths

Paths are routes you can place in a scene for entities to follow. This is useful when you want a sound emitter to move along a defined trajectory relative to the listener — for example, to simulate a river flowing past.

### Creating a Path

1. Click **Add New Path** on the taskbar
2. Assign a name to the path
3. Click **Add Component**
4. Place path points in the scene — they will automatically be connected in order

To attach a sound emitter to a path, select the sound emitter entity, go to the **Scripts** tab, click **Add New Script**, and add the **path_converger** script. This enables the entity to follow the defined path.

The **path_converger** script exposes a few options:

| Property | Description |
|---|---|
| **Path Name** | The name of the path the emitter should follow. Must exactly match the path you created |
| **Rotate To Camera** | If enabled, the emitter rotates to face the listener's camera as it moves along the path |
| **Apply Scale** | Determines whether the emitter inherits scaling transformations from the path |

---

## Modifying Emitter Transforms

To see a sound emitter's trigger volume shape and its min/max distance spheres, you need to enable **Sound Entities** visibility. If the **Visibility** tab isn't visible, open it via **Window > Visibility Window**.

In the **Visibility** tab, go to **Visibility Masks** and check the box next to **Sound Entities**. This enables display of sound-related components in the scene view. Once enabled, the trigger volume (a box or sphere wireframe) and two concentric distance spheres representing the min and max audible range become visible directly in the scene, making it much easier to position and resize emitters accurately.

---

## Adding Sound Events to Animation Clips

To attach sounds to animation clips, open the **Resource Browser** via **Window > Show Resource Browser**. The Resource Browser lets you browse, filter, and search through project assets.

You can filter by asset class to narrow down results. If you're not sure where an asset is, use the search bar — just make sure you're searching within the correct directory. Once you find the animation clip, select it to open the **Animation Clip Inspector**.

In the **Animation Clip Inspector**, you can view and adjust all parameters linked to the selected clip. For audio, focus on the sound-related parameters:

| Property | Description |
|---|---|
| **Step Points** | The X-axis value defines the timing offset within the clip. Set to `−1.00` by default if no sound is attached. Set to `0.00` or adjust to sync sound with animation |
| **Sound Code** | The FMOD event path to attach. Entering a valid path ensures the sound plays at the correct point in the animation timeline |
| **Voice Code** | Combat voice type to attach to the animation. See built-in types below, or add custom types as described in the [Voice System]({{< ref "audio-modding-systems.md#voice-system" >}}) section |

**Built-in Voice Codes:**

| Voice Code |
|---|
| Grunt |
| Jump |
| Yell |
| Pain |
| Death |
| Stun |
| Fear |
| Climb |
| Focus |
| Debacle |
| Victory |
| HorseStop |
| HorseRally |
| Drown |

**Animation sound flags:**

| Flag | Description |
|---|---|
| `make_bodyfall_sound` | Triggers a body-fall sound at this animation point |
| `make_walk_sound` | Triggers a footstep sound at this animation point |
| `do_not_keep_track_of_sound` | Stops the engine from tracking the sound's playback state |
| `attach_sound_to_agent` | Attaches the sound directly to the agent rather than the world position |

---

## Adding Sound Events to Particles

Just like animation clips, particle assets can be accessed through the **Resource Browser**. Filter the asset list to show only particles, or use the search bar to locate a specific particle within a directory. Once found, double-click it to open the **Inspector Window**.

In the Inspector, you can view the particle's layers, LODs, and parameters. Before attaching audio, make sure you've selected the correct **layer** and **LOD** of the particle.

> Keep in mind: if the effect isn't visually strong enough to justify rendering at a higher LOD, it isn't strong enough to justify a sound either. In those cases, keep audio only at **LOD 0**.

Once the correct layer and LOD are selected, open the **Sound** tab:

| Property | Description |
|---|---|
| **Continuous sound** | Use for looping sounds. Enter the FMOD event path of the looped event |
| **One shot sound** | Use for single-playback sounds. Enter the FMOD event path of the one-shot event |
| **Size Parameter** | Applies a multiplier to override the size of a 3D sound event |

---

## Quadraphonic Ambients {#quadraphonic-ambients}

**Mount & Blade II: Bannerlord** is a performance-critical title. Adding hundreds of 3D sound emitters into scenes can quickly overwhelm CPU, memory, and disk I/O — especially during large-scale battles with thousands of active agents. To maintain immersion without sacrificing performance, we use **quadraphonic ambients**.

Quadraphonic is a four-channel surround format that creates the illusion of being surrounded by many emitters while actually using only four channels. Rather than relying on FMOD's built-in quad system, we implemented our own approach. It's a bit more involved to set up, but once you understand it, it's more flexible than FMOD's internal solution.

The system spawns **four 3D emitters** around the listener at fixed positions: **0°, 90°, 180°, and 270°** relative to the listener's facing direction. These emitters move with the listener's position, but their distance to the listener stays constant. As the listener turns, they continuously receive different signals from the surrounding emitters — creating the illusion of a dynamic, shifting environment without needing dozens of independent sources.

### Setting Up Quad Ambients

First, create a **stereo ambient event**. This is **not optional** — it acts as a fallback in case the quad setup fails or isn't found. Without it, ambience may be missing during gameplay.

For example, create a 2D event called `plains`.

Next, create a **folder with the same name** as the stereo event (`plains` in this example). This naming is critical — the system matches stereo events to their quad alternatives by name. Events inside this folder will be treated as the quad channels.

Inside the folder, create four 3D events named exactly:

- `north`
- `east`
- `south`
- `west`

> The names are **case-sensitive** and must be **lowercase**. Event order doesn't matter.

For each of the four quad events, open the **Spatializer** and **disable distance attenuation**. These emitters should radiate a consistent signal regardless of their distance from the listener. If attenuation is left on, volumes may be incorrectly reduced depending on distance settings.

Finally, go back to the stereo event (`plains`) and set a **User Property**:

| Property | Value |
|---|---|
| `isQuad` | `True` |

This flag tells the system to look for a matching quad setup. If found, the quad version plays; if not, the stereo event plays as a fallback.
