# HeyClicky research and Windows scope

Research date: 11 October 2026. Sources: [official homepage](https://www.heyclicky.com/), [changelog](https://www.heyclicky.com/changelog), and the [public MIT source](https://github.com/farzaa/clicky). The changelog showed version 1.0.53 dated 2 October 2026 at inspection time. OpenClicky is independent of that product.

## Actual video inspection

The five video files embedded by the official homepage were downloaded to the ignored local research folder. Frames were extracted every three seconds across each full timeline, timestamped, and inspected in contact sheets. This covers the actual videos, rather than relying only on descriptions. It is sampled visual inspection; it does not claim that every frame or every spoken word was reviewed. Marketing demonstrations also do not establish reliability on this machine.

| Official video | Approximate duration | Observed behavior |
| --- | --- | --- |
| [hello.mp4](https://www.heyclicky.com/assets/hello.mp4) | 53 seconds | Introduction and operator customization, skills, and memory presentation |
| [usecase.mp4](https://www.heyclicky.com/assets/usecase.mp4) | 109 seconds | Voice shortcut interaction, Maps dark/light changes, route arrows, sheet music highlighting, a Pythagorean diagram, and Blender pointing |
| [heyclicky-draw.mp4](https://www.heyclicky.com/assets/heyclicky-draw.mp4) | 140 seconds | Shapes and labels over math/video content, squares on triangle sides, and FL Studio control highlighting |
| [nohandstricklol.mp4](https://www.heyclicky.com/assets/nohandstricklol.mp4) | 104 seconds | Voice-driven Spotify interaction, dashboard assistance, and Reminders |
| [daddyshome.mp4](https://www.heyclicky.com/assets/daddyshome.mp4) | 55 seconds | Multi-monitor navigation between dashboards, maps/routes, and websites |

The official-linked [powerpuff clickies](https://www.youtube.com/watch?v=Bo5v6QxsLnw) short was played through and visually inspected at six timestamps. It presents multiple named companions; it is a promotion, not an end-to-end agent acceptance test.

## Product decisions

The recurring useful interaction is: invoke from the current app, ask a short question, receive a spoken answer, and see the relevant screen location. OpenClicky preserves that core Windows workflow and also provides a home window for typing, readable replies, clear connection state, and discoverable demos. A consistent green pointer identity replaces placeholder branding.

The public videos make desktop action results visible. OpenClicky's action demonstration therefore checks the actual button count and exact text field contents; a successful input call or log line alone is insufficient. Every input step keeps an explicit human approval gate.

| Reference behavior | OpenClicky 0.2 |
| --- | --- |
| Voice invocation and screen questions | Implemented; local recognition tested with generated speech; physical mic and live-provider acceptance pending |
| Screen pointing during speech | Implemented; position/hold and mixed-monitor mapping tested |
| Desktop clicks and typing | Implemented with per-action approval; actual sandbox result verified |
| Multiple monitors | Captured and mapped; real two-monitor check passed |
| Named companions and separate persistent workspaces | Not implemented; one companion with optional short Talk history |
| Drawing, arrows, polygons, freehand markup | Not implemented; pointer and label only |
| Dictation into arbitrary focused apps | Not implemented as a dedicated mode |
| Routines, scheduling, background tasks | Not implemented |
| Account connectors and multiple connected accounts | Not implemented |
| Ongoing walkthroughs, proactive suggestions, file skills | Not implemented |

The release is a scoped Windows preview. These gaps are recorded as product work, not represented as built capability.
