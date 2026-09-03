<p align="center">
  <a href="./README.md">한국어</a> · <strong>English</strong> · <a href="./README.ja.md">日本語</a>
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">
    <img src="https://api.vrchat.cloud/api/1/file/file_c6b519ec-141a-4edb-83f3-2fc3dc39c2e1/5/file" alt="Shinjuku Live Street key visual" width="900">
  </a>
</p>

<h1 align="center">Shinjuku Live Street</h1>

<p align="center">
  <strong>A VRChat social world where anyone can start a street performance and passersby naturally become the audience.</strong>
</p>

<p align="center">
  Music, conversation, and group photos unfold throughout the street.
</p>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info"><strong>Visit in VRChat</strong></a>
  ·
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live"><strong>Explore #VRSJK</strong></a>
  ·
  <a href="#code-map"><strong>Browse the code</strong></a>
</p>

---

## About the world

Shinjuku Live Street is a VRChat social world built around street performances, where performers and audiences meet in the streets of Shinjuku.

There is no single fixed stage. Performances begin throughout the streets, passersby stop to watch, and the night continues through conversations and group photos after each set.

<table>
  <tr>
    <td width="33%" align="center"><strong>1,693,697</strong><br><sub>Total visits</sub></td>
    <td width="33%" align="center"><strong>64,453</strong><br><sub>Favorites</sub></td>
    <td width="33%" align="center"><strong>Up to 80</strong><br><sub>Capacity</sub></td>
  </tr>
</table>

<p align="center"><sub>VRChat social world · Unity / UdonSharp · Two-person team · Public since April 4, 2025 · Latest update August 31, 2026</sub></p>
<p align="center"><sub><a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">Official VRChat world information</a> · Checked September 3, 2026</sub></p>

## Street performances and community

<p align="center">
  <img src="./Docs/images/community-gallery-placeholder.svg" alt="Reserved image area for community performance scenes" width="900">
</p>

<table>
  <tr>
    <td width="50%" valign="top"><strong>Every corner can become a stage</strong><br><sub>Solo vocals, instrumentals, and full-band sets beginning wherever performers choose</sub></td>
    <td width="50%" valign="top"><strong>Passersby become the audience</strong><br><sub>People stopping for an unfamiliar performance, then listening, dancing, and cheering together</sub></td>
  </tr>
</table>

Community performances and visit records can be found through the [#VRSJK search on X](https://x.com/search?q=%23VRSJK&src=typed_query&f=live).

---

<p align="center">
  <strong>Planned improvements and suggestions</strong><br><br>
  <a href="https://github.com/hjcud/Shinjuku-Live-Street/issues"><strong>View planned work</strong></a>
  &nbsp;·&nbsp;
  <a href="https://github.com/hjcud/Shinjuku-Live-Street/issues/new"><strong>Share feedback</strong></a>
</p>

---

## Recent development and improvements

The live-equipment synchronization and traffic simulation were redesigned to keep shared state consistent with many users connected while reducing repeated CPU, physics, and GC work.

### Shared live equipment — consistent state from placement to return

Portable speakers could retain part of their linked state after being returned or left behind, while late joiners could miss speakers already placed in the world.

<p align="center">
  <img src="./Docs/images/live-performance-sync.svg" alt="Previous synchronization problems and the current behavior of shared live equipment" width="900">
</p>

Everyone sees the same equipment state, and returned equipment no longer retains the previous user's settings.

### Traffic simulation — centralizing repeated per-vehicle work

Each vehicle previously calculated its destination, ran a `BoxCast`, moved its Transform, and requested serialization every frame. CPU, physics, and GC costs rose together as the number of vehicles and players increased.

<p align="center">
  <img src="./Docs/images/traffic-system-architecture.svg" alt="Traffic-state flow from editor data through the traffic owner and network to remote clients" width="900">
</p>

The traffic owner calculates all ten vehicles in one place and sends each vehicle's state as a 64-bit record. Remote clients rebuild the vehicles from the same lane data and interpolate every frame to reduce visible stepping.

<details>
<summary><strong>View the runtime debug screen</strong></summary>

<p align="center">
  <img src="./Docs/images/shinjuku-traffic-system-debug.png" alt="Runtime lane and vehicle debugging for the traffic system" width="900">
  <br>
  <sub>Baked lanes, vehicle occupancy, predicted positions, and obstacle sensor ranges</sub>
</p>

</details>

### Performance results

![Unity Profiler comparison between the initial and latest traffic-system snapshots](./Docs/images/traffic-performance-comparison.svg)

The comparison uses the same 300-frame segment from the initial and latest snapshots, with ten cars and 80 ClientSim remote players gathered at one point. Average CPU frame time fell from `17.65 ms to 11.92 ms`, while P95—which represents recurring slow frames—fell from `24.60 ms to 17.44 ms`. Physics time fell by 65.3%, and GC allocation per frame by 88.1%.

The calculation rate is reduced to `10 Hz`, while position and rotation are interpolated every frame to keep vehicles moving smoothly on screen.

[Read the test conditions and implementation details](./Docs/optimization.en.md)

## Model and rendering optimization

<p align="center">
  <img src="./Docs/images/shinjuku-model-rendering-comparison.png" alt="Comparison of the normal render and wireframe view" width="900">
  <br>
  <sub>Left: normal render · Right: wireframe captured from the same camera</sub>
</p>

The environment is divided into sections so occlusion culling can skip areas outside the camera view. Static batching is applied to fixed objects, while street and building lights use baked lighting.

<table>
  <tr>
    <td align="center"><strong>246,921</strong><br><sub>Triangles</sub></td>
    <td align="center"><strong>240</strong><br><sub>Environment meshes</sub></td>
    <td align="center"><strong>392</strong><br><sub>Static-batched objects</sub></td>
    <td align="center"><strong>330</strong><br><sub>Occluders</sub></td>
    <td align="center"><strong>2</strong><br><sub>Mesh colliders</sub></td>
  </tr>
</table>

Baked lighting covers approximately 220 meshes using three 4096 lightmaps and one 512 lightmap.

## Code map

| Area | Key files | Responsibility |
| --- | --- | --- |
| Performance equipment | [`SpeakerManager.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerManager.cs), [`SpeakerController.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerController.cs) | Speaker placement, validation, ownership, late-join sync, and reset |
| Stage voice | [`VoiceRange.cs`](./Assets/Shinjuku%20Udon/Speaker/VoiceRange.cs) | Shared performer voice range and gain |
| Shared interactions | [`ObjectGlobalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectGlobalToggle.cs), [`ObjectLocalToggle.cs`](./Assets/Shinjuku%20Udon/ObjectToggle/ObjectLocalToggle.cs) | Separation of global and local state |
| Traffic runtime | [`TrafficSimulationManager.cs`](./Assets/Shinjuku%20Udon/Traffic/TrafficSimulationManager.cs) | Simulation, state packing, transfer, and remote reconstruction |
| Lane data | [`TrafficLaneDatabase.cs`](./Assets/Shinjuku%20Udon/Traffic/TrafficLaneDatabase.cs) | Baked lane lookup and vehicle pose reconstruction |
| Editor tooling | [`TrafficLaneBakerEditor.cs`](./Assets/Shinjuku%20Udon/Traffic/Editor/TrafficLaneBakerEditor.cs), [`TrafficSimulationManagerEditor.cs`](./Assets/Shinjuku%20Udon/Traffic/Editor/TrafficSimulationManagerEditor.cs) | Lane baking, validation, and visualization |
| World utilities | [`PosterSlide.cs`](./Assets/Shinjuku%20Udon/Posters/PosterSlide.cs), [`PortalToggle.cs`](./Assets/Shinjuku%20Udon/Portal/PortalToggle.cs), [`CollisionTeleport.cs`](./Assets/Shinjuku%20Udon/Teleport/CollisionTeleport.cs) | Poster transitions, portals, and teleportation |

## Repository scope

This repository is not a complete Unity project. It contains only original C# and UdonSharp code plus documentation. Scenes, prefabs, models, images, audio, video, materials, animations, shaders, and Unity `.meta` files are not included.

<details>
<summary><strong>SDKs, packages, and third-party components</strong></summary>

### Development environment

- Unity `2022.3.22f1`
- VRChat SDK - Worlds `3.8.1`
- UdonSharp
- TextMesh Pro

### Third-party components used in the world

- Topaz Chat `0.1.6`
- lilToon `1.10.3`
- VRWorldToolkit `3.2.1`
- AVPro Video
- Bakery GPU Lightmapper
- QvPen
- IwaSync3 / HoshinoLabs
- Media Manager
- Mochie Shaders
- EasyTextures
- VRC Players Only Mirror
- VRC Music Event Calendar
- Year Progress Bar
- imagePad
- Lura's Switch (Udon)
- Prototype Collection
- AllSkyFree
- Noriben Lunch shader assets
- Models and environment resources from Atelier Rayrell, RIONESTA, Zelkova Tree, and other creators

Third-party components are not included in this repository. Their respective authors and distributors retain all rights and define their own license terms.

</details>

## Copyright

No open-source license is granted for the original code in this repository. The code may not be used, modified, or redistributed without separate permission. Rights to third-party components and world assets remain with their respective authors.

## Team

| Member | Role |
| --- | --- |
| [Artistoid](https://github.com/Artistoid) · [X @Artistoid_VRC](https://x.com/Artistoid_VRC) | Planning · Graphics · 3D modeling |
| [hjcud](https://github.com/hjcud) | Unity and UdonSharp system development and optimization |

---

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">VRChat World</a>
  ·
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live">#VRSJK</a>
  ·
  <a href="https://github.com/hjcud/Shinjuku-Live-Street">GitHub</a>
</p>
