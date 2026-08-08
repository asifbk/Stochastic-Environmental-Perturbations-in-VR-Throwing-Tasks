# Basketball VR Training Project

Unity project for a VR/mixed-reality basketball free-throw training experience with an AI coach, hand-tracking/haptic glove support, shot-trajectory visualization, and session data logging.

## 1. Requirements

| Item | Version / Notes |
|---|---|
| Unity Editor | **2022.3.62f1** (install via Unity Hub, exact patch version recommended) |
| Render Pipeline | Built-in Render Pipeline |
| Platform | PC VR (SteamVR / OpenVR) and Android-based standalone headsets (Meta Quest / VIVE, via OpenXR). Android Build Support module + Gradle files are included in `Assets/Plugins/Android`. |
| OS for full feature set | Windows (the AI coach's text-to-speech uses Windows SAPI via PowerShell — see `CoachTTSClient.cs`) |

### Unity Packages
All required packages are embedded directly in the project's `Packages/` folder, so Unity will resolve them automatically on first open (no manual installation needed):

- `com.unity.xr.interaction.toolkit` — XR Interaction Toolkit
- `com.unity.xr.openxr` — OpenXR plugin
- `com.unity.xr.hands` — Hand tracking
- `com.valvesoftware.unity.openvr` — SteamVR/OpenVR support
- `com.unity.animation.rigging` — Procedural rigging (coach avatar)
- `com.unity.cloud.gltfast` — glTF import support
- `com.unity.textmeshpro`, `com.unity.ugui`, `com.unity.timeline`, `com.unity.visualscripting`, `com.unity.collab-proxy`, `com.unity.feature.development`

The **new Input System** (`com.unity.inputsystem`) is used project-wide — input bindings are defined in `Assets/Resources/InputActions.asset`. **Do not hand-edit the Input System–generated files.**

Additional integrated SDKs live directly under `Assets/`:
- `Assets/SteamVR`, `Assets/SteamVR_Input`, `Assets/SteamVR_Resources` — SteamVR Unity Plugin
- `Assets/SenseGlove` — SenseGlove haptic glove SDK (used by `HandThrow.cs`, `IMUResetButton.cs`)
- `Assets/VIVEOpenXRInstaller.cs` — Editor helper to install the VIVE OpenXR package

### External runtime dependencies (install separately on the new PC)
- **Steam + SteamVR** runtime app, if testing/building for PC VR headsets.
- **VIVE OpenXR runtime**, if targeting VIVE headsets (run the installer script mentioned above from the Unity Editor menu after opening the project).
- **SenseGlove drivers / SenseCom software**, if using the physical haptic gloves.
- **Ollama** — the AI coach's vision/language feedback (`OllamaVLMClient.cs`) talks to an Ollama server over HTTP (`localhost:11434` by default). In this project, the LLM is **not** run locally — it runs remotely on the **Bizon cluster** and is reached through an SSH tunnel (see "Connecting to the Bizon LLM" below).
- **Qwen VLM endpoint** — `QwenVLMClient.cs` calls a configured Qwen vision-language model API; check the Inspector fields on the relevant GameObject for the endpoint/API key before deploying.

### Connecting to the Bizon LLM (required for the AI Coach)

The `AICoach` / `OllamaVLMClient` feature depends on an Ollama instance running on the university's **Bizon cluster**, not on your local machine. Before entering Play mode (or before making a build that relies on AI coach feedback), open a terminal and start an SSH tunnel that forwards a local port to the remote Ollama service:

```bash
ssh -L 11435:localhost:11434 mkarim1@bizon.host.ualr.edu -p 22415 -N
```

- `-L 11435:localhost:11434` forwards local port **11435** to port **11434** (Ollama's default port) on the Bizon cluster.
- `-p 22415` is the cluster's SSH port.
- `-N` keeps the tunnel open without starting a remote shell — leave this terminal window running for the whole session; closing it disconnects the LLM.
- You'll need a valid Bizon cluster account/credentials (`mkarim1@...` above is a personal login — each user should connect with their own account).

**Activating the LLM in Unity:**
1. Run the `ssh` command above in a terminal and keep it open in the background.
2. In the Unity Editor, select the GameObject holding the `OllamaVLMClient` component and set its base URL/port to point at the **local** end of the tunnel: `http://localhost:11435` (not `11434`, since that's the forwarded local port).
3. Enter Play mode — `AICoach` will route its feedback requests through the tunnel to the Ollama model running on Bizon.
4. If requests fail, first verify the tunnel terminal is still connected (no dropped connection) and that `http://localhost:11435` responds before checking in-editor settings.

## 2. How to Hand Off / Open on a New PC

1. Copy the **entire `Basketball` project folder** to the new machine (or push it to a Git repository — the project already includes `com.unity.collab-proxy`, so Unity Version Control / Git can be used).
   - You may safely delete the `Library/`, `Temp/`, `obj/`, and `.vs/` folders before zipping/transferring — Unity regenerates them automatically and they only bloat the transfer size.
   - Keep `Assets/`, `Packages/`, `ProjectSettings/`, and `.bezi/` (if present) intact.
2. Install **Unity 2022.3.62f1** via Unity Hub on the new PC.
3. Install any external runtimes listed above that your colleague needs (SteamVR, VIVE OpenXR, SenseGlove software, Ollama) depending on which hardware/features they'll use.
4. Open the project folder from Unity Hub — Unity will import assets and resolve packages automatically on first load (this can take several minutes).
5. Open the main scene: `Assets/Scenes/SampleScene.unity`.
6. For builds: `File > Build Settings`. Android build files (`gradleTemplate.properties`, `mainTemplate.gradle`) are already configured under `Assets/Plugins/Android` for standalone headset deployment; for PC VR builds, ensure SteamVR is installed and running before entering Play mode.

## 3. Script Reference (`Assets/Scripts`)

### Gameplay & Ball Physics
| Script | Responsibility |
|---|---|
| `BallThrower.cs` | Manages all basketballs in the scene, resets their positions, tracks total shot count, fires a shot-taken event. |
| `HandThrow.cs` | Bridges SenseGlove grab/release events to the throw pipeline: samples hand velocity, applies release physics, raises `OnBallReleased`. |
| `AutoShot.cs` | Debug/test shot: press T to auto-calculate a parabolic launch velocity toward the hoop and fire the ball. |
| `RackBall.cs` | Keeps a ball anchored to its rack slot until grabbed; snaps it back if released too close to the rack. |
| `BallBounceSound.cs` | Plays a 3D bounce sound on ball collisions, with volume/pitch scaled by impact speed. |
| `BallRimCollisionReporter.cs` | Detects ball-rim collisions and forwards impact data to `ScoringTrigger`. |
| `ScoringTrigger.cs` | Detects a ball entering the hoop, prevents duplicate scoring, raises score/rim-hit events. |
| `HoopMagnet.cs` | Applies an "assist" magnetic force pulling balls toward the hoop once inside the trigger zone. |
| `GhostBallAnimator.cs` | Animates a translucent "ghost ball" along a calculated arc, for shot preview/replay. |

### Trajectory & Visual Feedback
| Script | Responsibility |
|---|---|
| `TrajectoryPreview.cs` | Calculates and renders the projected ball trajectory using launch/gravity parameters. |
| `AngleWedgeRenderer.cs` | Draws a colored wedge and rays comparing actual vs. ideal release angle, with a text label. |
| `CoachVisuals.cs` | Displays floor markers, release zones, angle indicators, and live trajectory tracking during a shot. |
| `FeedbackMetricsPanel.cs` | Displays immediate shot metrics (angle/speed/outcome) with green/yellow/red status coloring. |

### AI Coach
| Script | Responsibility |
|---|---|
| `AICoach.cs` | Central coach pipeline: records shot history from `HandThrow`/`AutoShot`/`ScoringTrigger` and requests feedback from the VLM client. |
| `OllamaVLMClient.cs` | Sends text/vision requests to a local Ollama server, handles health checks and streaming responses. |
| `QwenVLMClient.cs` | Sends prompts/images to a Qwen vision-language model API and processes the response. |
| `CoachTTSClient.cs` | Converts coach text responses to speech using Windows SAPI (via PowerShell). |
| `CoachLipSync.cs` | Animates the coach avatar's jaw in sync with `CoachTTSClient` speech playback. |
| `CoachAnimationSwitch.cs` | Switches the coach avatar between "speaking" and "idle" animator states. |
| `CoachIdleController.cs` | Drives the coach's idle pose/gestures by posing avatar bones directly. |
| `AvatarAnimationController.cs` | Procedural avatar animations: wave, celebrate, dribble, point, taunt, return-to-rest. |

### XR / Input / Camera
| Script | Responsibility |
|---|---|
| `XRTrackingOriginSetup.cs` | Configures the XR rig's tracking-origin mode on startup. |
| `DisableXRCameraTracking.cs` | Disables XR positional/rotational tracking on the main camera when needed. |
| `IMUResetButton.cs` | Exposes a button action to reset the left/right SenseGlove IMU tracking. |
| `EyeTrackingVisualizer.cs` | Visualizes eye-tracking gaze points as scene markers. |
| `FlyCameraController.cs` | Free-fly keyboard/mouse camera for desktop debugging. |
| `BigScreenCameraSetup.cs` | Sets up a runtime `RenderTexture` spectator camera feed shown on an in-scene screen. |
| `ScreenCanvasAligner.cs` | Aligns a UI canvas to a target screen transform. |
| `VIVEOpenXRInstaller.cs` | Editor utility to install/configure the VIVE OpenXR package. |

### Environment (Wind / Flag)
| Script | Responsibility |
|---|---|
| `WindSystem.cs` | Simulates court wind: exposes speed, direction, cardinal direction, and angle to other systems. |
| `FlagWindController.cs` | Applies the active wind direction from `WindSystem` to the flag. |
| `FlagRotationController.cs` | Constrains the flag to Y-axis rotation and auto-aligns it to wind when not grabbed. |
| `Editor/FixFlagClothConstraints.cs` | Editor tool to fix/configure the flag's cloth simulation constraints. |

### UI & Data Logging
| Script | Responsibility |
|---|---|
| `ScoreboardUI.cs` | Listens for score events and updates the on-screen scoreboard. |
| `SessionMetadataLogger.cs` | Writes session metadata (participant/condition info) to the session log. |
| `TrialDataLogger.cs` | Records per-trial/shot data (release, trajectory, outcome, timing) to a data file. |
| `WavUtility.cs` | Utility for converting between WAV audio data and Unity `AudioClip`s. |

## 4. Notes for the New Developer
- Do not modify any Input System–generated files (per project convention); edit input bindings only through `Assets/Resources/InputActions.asset` in the Input Actions editor.
- `CoachTTSClient.cs` is Windows-only (PowerShell/SAPI) — text-to-speech will not function on macOS/Linux.
- `OllamaVLMClient.cs` and `QwenVLMClient.cs` both require reachable external services; confirm the endpoint/model settings in the Inspector match the new environment before relying on AI coach feedback.
- Third-party SDK folders (`SteamVR*`, `SenseGlove`, `TextMesh Pro`) are vendor code — treat them as read-only and update only via their official packages/installers.
