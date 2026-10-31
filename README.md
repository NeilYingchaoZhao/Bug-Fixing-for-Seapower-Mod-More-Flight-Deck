# Bug-Fixing-for-Seapower-Mod-More-Flight-Deck
Two fixes were applied to `FlightDeckOpsMod.dll`. No vessel INIs, editor files, configuration files, or `AirOpsMod.dll` were changed.

### 1. E-2C automatically launches after being brought up manually

**Cause**

`DeckOpsAirOpsBridge.Tick` treated an aircraft’s AEW role as sufficient authorization for automatic launch. This meant an E-2C brought up manually could enter the automatic launch flow without an explicit sortie order.

`DeckOpsZones.CancelLaunchWait` removed the aircraft from the launch queue, but did not prevent the automatic sortie bridge from adding it again on a later check.

**Fix**

- Removed AEW role alone as an automatic-launch condition.
- Preserved automatic launch eligibility for assigned patrol aircraft and explicitly scheduled AEW sorties. Explicit AEW orders are recorded when `DeckOpsLaunchScheduler.Spawn` creates the aircraft.
- Added a cancellation override associated with the aircraft object. A successful `CancelLaunchWait` sets this override.
- The automatic sortie bridge and the three launch entry points—`AssignLaunch`, `AssignLaunchTo`, and `LaunchFromLaunchPoint`—respect the override.
- A new manual launch command through the mod’s launch controls clears the override.
- The bridge now also respects `AutoLaunchOnArrival` when an aircraft reaches launch-ready status.

The cancellation override is held in memory for the current aircraft object; it is not serialized into saved missions.

### 2. “Reversed” taxi routes still move nose-first

**Cause**

The editor correctly writes `IsBackwards=True`. However, the custom route builder flattened taxi paths into a `List<Vector3>`, losing each leg’s direction. The native-path fallback also discarded direction when converting paths into waypoints.

Consequently, movement received positions without the corresponding reverse instructions.

**Fix**

- Preserved a forward/reverse flag for each waypoint:
  - Custom graph routes read `IsBackwards` from the corresponding INI path.
  - Native fallback routes retain `TaxiPath._isBackwards`.
- Preserved those flags through route copying, waypoint insertion/removal, replacement, and clearing.
- Before `DeckOpsMove.Advance` issues a movement target, the patch applies the active leg’s direction to `Taxi._isBackwards` and the ground controller’s `backTaxi` field, using the existing reverse-speed helper.
- Reduced waypoint arrival tolerance at forward/reverse transitions from approximately 5 metres to 0.5 metres, so direction changes occur nearer the intended transition point.
- Adjusted pushout-origin selection so it does not skip an explicitly marked reverse departure.
- Kept existing manual reverse and reverse-docking controls separate from ordinary route-direction handling.

### Implementation and validation

This was a local compiled-DLL patch using Mono.Cecil, with helper code merged into the existing assembly. It requires no additional helper DLL. For an upstream fix, the same logic could be implemented directly in the mod source, preferably using route steps that store both position and direction.

Validation completed:

- 26 regression assertions passed, covering launch eligibility, cancellation persistence, manual resumption, route-direction metadata, and transition tolerance.
- IL control-flow and stack checks passed for 73 modified or added methods.
- 2,617 method and field references resolved.
- The installed DLL’s hash was verified; all 18 checked layout, configuration, editor, and related-mod files remained unchanged.

**Limitation:** These were code-level checks. Live aircraft movement and complete launch cycles have not yet been verified in Sea Power.
