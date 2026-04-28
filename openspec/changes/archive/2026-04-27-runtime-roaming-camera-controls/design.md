## Context

`GSViewerUI` currently owns the runtime IMGUI controls for camera reset, fixed view preset buttons, point picking, pick result display, and training controls. Camera presets directly set the main camera transform to fixed positions around the splat renderer. This works for quick orthographic-style inspection but does not support natural navigation through a reconstructed 3DGS environment.

The same UI also owns point-picking input. Left mouse clicks in pick mode are routed to `GsPixelPicker.TryPickDetailed`, while UI-window hit testing prevents scene picks when the click starts inside the IMGUI windows. The roaming camera controls should fit into this existing input handling without changing the accurate picker or measurement semantics.

## Goals / Non-Goals

**Goals:**

- Replace fixed preset view controls with runtime roaming controls suitable for exploring a 3DGS scene.
- Use `W/A/S/D` for camera-relative horizontal movement, `Q/E` for world-space down/up movement, and `Left Shift` for temporary speed boost.
- Use right-mouse drag for yaw/pitch camera rotation.
- Keep the reset view button and initial camera transform capture unchanged in behavior.
- Keep left-click point picking, pick markers, and `GsPixelPicker` integration working as before.
- Avoid rotating the camera when the right mouse interaction begins over an IMGUI window.
- Update runtime operation instructions so users can discover the new controls.

**Non-Goals:**

- Do not change Gaussian splat rendering, sorting, picking compute logic, training, or import behavior.
- Do not add a new Unity UI package or redesign the full IMGUI layout.
- Do not implement collision, gravity, physics, or navmesh-style walking.
- Do not add persistent camera bookmarks or saved navigation state.

## Decisions

### Decision 1: Keep roaming control inside `GSViewerUI`

Implement the new controls in `GSViewerUI` rather than creating a separate camera controller component.

Rationale:
- The existing preset camera behavior, reset behavior, UI hit testing, and pick input handling already live in `GSViewerUI`.
- The change is scoped to replacing one runtime viewer interaction model, not introducing a general-purpose camera system.
- Keeping the logic local avoids extra scene setup and preserves existing references.

Alternative considered: Add a standalone `RoamingCameraController` component. This would be cleaner if multiple scenes reused the behavior, but it would require new component setup and coordination with `GSViewerUI` for UI-window hit testing and pick-mode behavior.

### Decision 2: Use first-person/free-fly movement semantics

Movement uses the camera transform for forward/back and left/right movement, plus world-space up/down for `E/Q`. Movement speed scales by `Time.deltaTime`; holding `Left Shift` applies a configurable multiplier.

Rationale:
- Camera-relative movement matches user expectations for WASD roaming.
- World-space up/down is predictable for inspecting 3D reconstructions and avoids roll-dependent vertical drift.
- Delta-time scaling makes movement frame-rate independent.

Alternative considered: Ground-plane-only movement. This is less useful for floating 3DGS captures and scenes without a meaningful floor.

### Decision 3: Use right mouse button as the look modifier

Camera rotation only occurs while the right mouse button is held and dragged. The camera stores yaw/pitch state derived from its current transform, clamps pitch to avoid flipping, and applies rotation without roll.

Rationale:
- Right-drag avoids conflict with left-click picking and common IMGUI button interactions.
- Explicit yaw/pitch state gives stable mouse-look behavior and prevents accidental camera roll.
- Pitch clamping avoids disorienting upside-down views.

Alternative considered: Always-look mode with cursor lock. This is immersive but inconvenient in an editor/runtime debug UI with clickable IMGUI controls.

### Decision 4: Reuse existing IMGUI window exclusion for scene input

Before starting or continuing right-drag camera look, convert `Input.mousePosition` into GUI coordinates and check `controlWindowRect` and `infoWindowRect`. If the cursor is over either window, camera look does not run.

Rationale:
- This mirrors the existing pick-mode protection.
- It prevents UI dragging or button interaction from rotating the camera.

Alternative considered: Disable camera controls whenever any GUI is visible. That would be too restrictive because users should still roam while panels are open.

### Decision 5: Remove preset buttons but keep reset

The front/back/left/right/top/bottom buttons are removed from the runtime control panel, while the reset button remains.

Rationale:
- The user explicitly no longer wants preset view functionality.
- Reset is still valuable as a safe way back to the initial view after roaming.

Alternative considered: Keep presets behind a foldout. This preserves legacy behavior but conflicts with the request to remove the feature and adds unnecessary UI.

## Risks / Trade-offs

- Right-drag may still feel too fast or too slow on different mice → expose existing movement/rotation speed fields in the inspector and use simple multipliers.
- Camera-relative movement can move vertically when the camera is pitched up or down → acceptable for free-fly roaming; `Q/E` still provide explicit vertical adjustment.
- No collision or scene bounds means users can fly through or away from content → reset view remains available.
- IMGUI window hit testing only protects the known control and info windows → if new windows are added later, their rects should be included in the same helper.
