<p align="center">
  <a href="./README.md">한국어</a> · <strong>English</strong> · <a href="./README.ja.md">日本語</a>
</p>

<div align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info">
    <img src="./Docs/images/shinjuku-live-street-hero.png" alt="Shinjuku Live Street key visual" width="900">
  </a>
  <h1>Shinjuku Live Street</h1>
</div>

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="./Docs/images/world-intro.en.svg">
    <img src="./Docs/images/world-intro.en.light.svg" alt="A VRChat world where anyone can perform on the streets, and passersby naturally become part of the audience." width="900">
  </picture>
</p>

<table align="center">
  <tr>
    <td width="180" align="center"><img src="./Docs/images/metric-visits.svg" alt="" width="28"><br><strong>1,693,697</strong><br><sub>Total visits</sub></td>
    <td width="180" align="center"><img src="./Docs/images/metric-favorites.svg" alt="" width="28"><br><strong>64,453</strong><br><sub>Favorites</sub></td>
    <td width="180" align="center"><img src="./Docs/images/metric-capacity.svg" alt="" width="28"><br><strong>Up to 80</strong><br><sub>Capacity</sub></td>
  </tr>
</table>

<p align="center">
  <a href="https://vrchat.com/home/world/wrld_c82a5c14-97a5-4782-a034-d897d2d943a2/info"><strong>Open the world in VRChat ↗</strong></a>
</p>

<p align="center"><sub>VRChat social world · Unity / UdonSharp · Two-person team<br>Visits and favorites as of September 3, 2026</sub></p>

## Street performances and community

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="./Docs/images/community-highlights.en.svg">
    <img src="./Docs/images/community-highlights.en.light.svg" alt="Street performances and the passersby who become part of the audience" width="900">
  </picture>
</p>

<p align="center">
  <a href="https://x.com/search?q=%23VRSJK&src=typed_query&f=live"><strong>See performances and moments shared by visitors on #VRSJK ↗</strong></a>
</p>

<p align="center">
  <img src="./Docs/images/community-gallery.png" alt="A street performance with space reserved for more community photos" width="900">
</p>

---

<p align="center">
  <a href="https://github.com/hjcud/Shinjuku-Live-Street/issues"><img src="./Docs/images/issue-planned-work.en.svg" alt="View planned work — work in progress and planned improvements" width="49%"></a>
  <a href="https://github.com/hjcud/Shinjuku-Live-Street/issues/new/choose"><img src="./Docs/images/issue-feedback.en.svg" alt="Share feedback — report a bug or suggest an improvement" width="49%"></a>
</p>

---

## Recent improvements

<sub>As of September 3, 2026</sub>

Live-equipment synchronization and the traffic simulation were redesigned to keep shared state consistent in crowded instances while reducing CPU frame time, physics-simulation time, and per-frame GC allocation.

### Shared live equipment — reliable state from setup to return

Portable speakers sometimes kept their screen, pen, or media settings after being returned or after their user left. Players joining later could also miss speakers that had already been placed in the world.

<p align="center">
  <img src="./Docs/images/live-performance-sync.svg" alt="Previous synchronization problems and the current behavior of shared live equipment" width="900">
</p>

All players now see the same equipment state, and returned equipment no longer keeps the previous user's settings.

### Traffic simulation — centralizing repeated per-vehicle work

Each vehicle previously calculated its destination, ran a `BoxCast`, updated its Transform, and triggered serialization every frame. CPU frame time, physics-simulation time, and GC allocation rose together as the number of vehicles and players increased.

<p align="center">
  <img src="./Docs/images/traffic-system-architecture.svg" alt="Traffic-state flow from editor data through the traffic owner and network to remote clients" width="900">
</p>

The traffic owner calculates all ten vehicles through a single manager and sends each vehicle's state as a 64-bit record. Remote clients reconstruct the vehicles from the same lane data and interpolate their movement every frame to keep it smooth.

<details>
<summary><strong>View the runtime debug screen</strong></summary>

<p align="center">
  <img src="./Docs/images/shinjuku-traffic-system-debug.png" alt="Runtime lane and vehicle debugging for the traffic system" width="900">
  <br>
  <sub>Baked lanes, occupied areas, predicted positions, and obstacle-sensor ranges</sub>
</p>

</details>

### Performance results

![Unity Profiler comparison between the initial and latest traffic-system snapshots](./Docs/images/traffic-performance-comparison.svg)

Ten vehicles and 80 remote players simulated with ClientSim were concentrated in the same area. We captured 300 frames from both the initial and latest snapshots. Average CPU frame time fell from `17.65 ms to 11.92 ms`, while P95 frame time fell from `24.60 ms to 17.44 ms`. Physics time fell by 65.3%, and GC allocation per frame by 88.1%.

Vehicle position and rotation are interpolated every frame so motion remains smooth between simulation updates.

<p align="center">
  <a href="./Docs/optimization.en.md"><strong>View test conditions and implementation details →</strong></a>
</p>

---

## Model and rendering optimization

<p align="center">
  <img src="./Docs/images/shinjuku-model-rendering-comparison.png" alt="Comparison of the normal render and wireframe view" width="900">
  <br>
  <sub>Left: normal render · Right: wireframe captured from the same camera</sub>
</p>

The environment is divided into sections so occlusion culling can exclude areas outside the camera view. Static objects use static batching, while lighting for the streets and buildings is baked.

<table align="center">
  <tr>
    <td width="260" align="center"><strong>Model geometry</strong><br><sub>246,921 triangles · 240 environment meshes</sub></td>
    <td width="260" align="center"><strong>Rendering</strong><br><sub>392 static-batched objects · 330 occluders</sub></td>
    <td width="260" align="center"><strong>Collision</strong><br><sub>2 mesh colliders</sub></td>
  </tr>
</table>

Baked lighting is applied to approximately 220 meshes. The scene uses three 4096×4096 lightmaps and one 512×512 lightmap.

## Code map

| Area | Key files | Responsibility |
| --- | --- | --- |
| Live equipment | [`SpeakerManager.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerManager.cs), [`SpeakerController.cs`](./Assets/Shinjuku%20Udon/Speaker/v2.7/SpeakerController.cs) | Speaker placement, validation, ownership, late-join sync, and reset |
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
| [hjcud](https://github.com/hjcud) | Unity/UdonSharp systems and optimization |
