# Bug-Fixing-for-Seapower-Mod-More-Flight-Deck

发现两个疑似由模组代码导致的问题：

**1. E-2C 会在没有起飞指令的情况下自动起飞**
在美国航母上，将 E-2C 从机库调到甲板后，即使没有给它安排巡逻任务或下达起飞指令，它也会自动进入起飞流程。手动取消起飞后，过一会儿又会自动进入起飞队列，无法让它留在甲板待命。

从代码来看，自动出动逻辑将飞机具有 AEW（空中预警）角色作为自动起飞的条件之一，没有区分“手动调到甲板待命”和“明确安排了自动出动任务”。取消起飞时只移除了当前队列项，没有阻止后续自动检查重新安排起飞。

**2. 编辑器中的“倒行”路径设置不生效**
在甲板编辑器中将滑行路径勾选为“倒行”后，飞机沿该路径移动时仍然机头朝前，而不是机尾朝前倒退。这会影响需要先从停机位倒推出去、再进入滑行通道的路线。

编辑器能够正确保存 `IsBackwards=True`，但模组在把滑行路径转换为航点列表时，没有保留每一段路径的前进／倒行标记。因此，飞机移动逻辑只收到了航点位置，没有收到该路段需要倒行的信息。

预期行为是：飞机在标记为“倒行”的路段机尾朝前移动，进入普通路段后恢复前进。


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
