## ADDED Requirements

### Requirement: Render-consistent 3DGS picking
The system SHALL determine 3DGS pick hits using the same active camera, renderer transform, splat scale, opacity scale, sorted order, view data, Gaussian footprint, and alpha cutoff semantics used by visible splat rendering.

#### Scenario: User clicks visible 3DGS content
- **WHEN** the user clicks a pixel covered by rendered Gaussian splats
- **THEN** the picker returns a successful hit
- **AND** the returned hit corresponds to the visible splat contribution at that pixel

#### Scenario: User picks after camera movement
- **WHEN** the camera has moved or rotated immediately before a click
- **THEN** the picker uses current camera-dependent sort and view data
- **AND** the returned hit is not based on stale projection or sorting data

### Requirement: Measurement-ready pick result
The system SHALL return a structured pick result containing a world position suitable for measurement and debug metadata for diagnosis. The measurement world position SHALL be aligned to the clicked screen pixel and use a configurable surface-alpha weighted visible depth rather than blindly returning the selected Gaussian center.

#### Scenario: A pick succeeds
- **WHEN** the picker finds visible 3DGS content at the clicked pixel
- **THEN** the result includes `hit`, `worldPosition`, `splatIndex`, `splatCenterWorld`, `depth`, `alpha`, `contribution`, `accumulatedAlpha`, and `confidence`
- **AND** existing measurement and viewer flows can consume the returned world position

#### Scenario: A pick misses
- **WHEN** the user clicks outside visible 3DGS content
- **THEN** the result reports a miss
- **AND** the result does not return a misleading fallback point from whole-asset or chunk bounds as the primary hit

### Requirement: Coordinate-space correctness
The system SHALL convert Unity mouse input into the coordinate space used by the pick/render pass without vertical inversion, camera-pixel-rect offset, or projection/backbuffer mismatch.

#### Scenario: User clicks top and bottom regions
- **WHEN** the user clicks near the top or bottom of visible 3DGS content
- **THEN** the picker evaluates the corresponding rendered pixel
- **AND** the selected result is not vertically flipped

#### Scenario: User clicks inside a non-fullscreen camera pixel rect
- **WHEN** the active camera renders to a pixel rect that does not cover the full screen
- **THEN** the picker accounts for the camera pixel rect before evaluating the pick pixel

### Requirement: Debuggable picking behavior
The system SHALL expose enough debug information to compare expected visual hits with computed pick hits.

#### Scenario: User enables debug output
- **WHEN** a pick is attempted
- **THEN** the UI or console shows the pick pixel, hit state, splat index, world position, depth, alpha, contribution, accumulated alpha, and confidence

#### Scenario: Pick accuracy is investigated
- **WHEN** a pick seems visually incorrect
- **THEN** the debug metadata allows distinguishing coordinate mismatch, stale sort/view data, low confidence, and dense-overlap ambiguity

### Requirement: Existing viewer workflow remains intact
The system SHALL preserve the current viewer and measurement interaction model while improving pick accuracy.

#### Scenario: User performs two-point measurement
- **WHEN** the user enables point picking and selects two visible 3DGS points
- **THEN** the measurement tool uses the new pick result world positions
- **AND** the UI still displays the selected coordinates and measurement status

#### Scenario: Existing UI is used without debug analysis
- **WHEN** the user uses the normal point-picking workflow
- **THEN** no additional setup is required beyond the existing `GSViewerUI`, `GsPixelPicker`, and `GaussianSplatRenderer` references
