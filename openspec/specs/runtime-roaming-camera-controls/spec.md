## Purpose

Runtime roaming camera controls allow users to move through and inspect a 3DGS scene naturally during play mode while preserving the existing point-picking measurement workflow.

## Requirements

### Requirement: Runtime roaming movement
The system SHALL allow users to move the runtime camera through the 3DGS scene using keyboard controls during play mode.

#### Scenario: User moves relative to the camera view
- **WHEN** the user presses `W`, `A`, `S`, or `D` while the viewer is running
- **THEN** the camera moves forward, left, backward, or right relative to its current orientation

#### Scenario: User moves vertically
- **WHEN** the user presses `Q` or `E` while the viewer is running
- **THEN** the camera moves down or up in world space

#### Scenario: User accelerates movement
- **WHEN** the user holds `Left Shift` while pressing a movement key
- **THEN** the camera moves faster than the normal roaming speed

### Requirement: Right-drag camera rotation
The system SHALL rotate the runtime camera when the user holds the right mouse button and drags outside viewer UI windows.

#### Scenario: User right-drags in the scene
- **WHEN** the user holds the right mouse button and moves the mouse over the scene area
- **THEN** the camera yaw and pitch update according to mouse movement
- **AND** the camera does not roll

#### Scenario: User right-drags over an IMGUI window
- **WHEN** the user holds or starts holding the right mouse button while the cursor is over the control or information window
- **THEN** the camera does not rotate from that mouse input

### Requirement: Preset view controls removed
The system SHALL remove runtime fixed view preset controls while keeping camera reset available.

#### Scenario: User opens the control panel
- **WHEN** the runtime control panel is displayed
- **THEN** front, back, left, right, top, and bottom view buttons are not shown
- **AND** the reset view button remains available

#### Scenario: User resets after roaming
- **WHEN** the user clicks the reset view button after moving or rotating the camera
- **THEN** the camera returns to the initially recorded position and rotation

### Requirement: Measurement picking remains available
The system SHALL preserve the existing point-picking measurement workflow while adding roaming controls.

#### Scenario: User picks a point
- **WHEN** point-picking mode is enabled and the user left-clicks visible 3DGS content outside the UI windows
- **THEN** the existing accurate picker flow is used to produce the measurement point

#### Scenario: User rotates without picking
- **WHEN** the user right-drags to rotate the camera
- **THEN** no left-click pick is triggered by that rotation action

### Requirement: Runtime instructions describe roaming controls
The system SHALL present operation instructions that describe the roaming camera controls instead of fixed preset view controls.

#### Scenario: User reads operation instructions
- **WHEN** the information panel is displayed
- **THEN** it describes right mouse drag rotation, `W/A/S/D` movement, `Q/E` vertical movement, `Left Shift` acceleration, and left-click point picking
