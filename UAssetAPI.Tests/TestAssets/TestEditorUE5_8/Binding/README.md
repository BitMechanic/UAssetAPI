Author-created UE5.8.3 fixtures for PackageBindingTests (FORK-S1 S1-4), generated natively by
the owned binding probe (mode prepare-source); the curve and struct dependencies are not needed
by the managed readers.

BP_SharedNames.uasset is an Actor Blueprint whose HardA and SoftA defaults reference
/Game/S1Shared/A/Curve.Curve and whose HardB default references /Game/S1Shared/B/Curve.Curve:
two assets sharing the short name Curve in different packages. Its double variable is also named
Curve (default 3.5). Rebinding A must leave B, the shared name entry and the variable untouched.

BP_StructUse.uasset is an Actor Blueprint with a variable Data of the user-defined struct
/Game/S1Struct/S_Original (one double member Value), default Value 4.5.

Native controls, the receiver classes read with Epic AssetTools.get_asset_class, and the
observations behind the binding rules are retained in the shared project evidence repository:
projects/rctools-mcp-server/resources/artifacts/2026-10-18-blueprint-animation-native-pattern-context/FORK-S1/S1-4/.
