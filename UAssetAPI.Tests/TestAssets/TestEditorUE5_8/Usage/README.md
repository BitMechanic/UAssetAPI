BP_Usage.uasset is an original UE5.8.3 Actor Blueprint fixture. Its runtime hard/soft
fields use /Game/Usage/Old; editor-only fields use Target and Other. All three
dependencies are CurveFloat assets (Old.uasset, Target.uasset, Other.uasset). Exact
is int64 9007199254740993. The binary readers do not need to load the curves.

The fixtures serve CarrierFidelityTests (native bytes through JSON), the writer's
dependency-table preservation and refusal checks (DependencyTableWriteTests), and the
FORK-S1 S1-2 native convergence check: stale and corrected usage bits written by the
managed writer converge after native load, compile and save to the registry
dependencies of the native expected asset. Usage bits are registry-only metadata that
native save regenerates; the managed writer does not maintain them.

Native creation source, complete dependencies, reconstructions and execution evidence
are retained in the shared project evidence repository:
projects/rctools-mcp-server/resources/artifacts/2026-10-18-blueprint-animation-native-pattern-context/FORK-1/usage-promotion/
and .../FORK-S1/S1-2/. No custom native class is required by this fixture.

Transient/BP_Usage.uasset is a native variant whose RuntimeHard and RuntimeSoft
declarations carry CPF_Transient. Weak/BP_Usage.uasset is a native variant with a weak
RuntimeHard Target reference; its Target import usage remains editor-only.
Both are unchanged native outputs from the independent usage-promotion review:
FORK-1/usage-promotion-review/native-evidence.zip (TransientEvidence/Fixtures/Usage
and WeakEvidence/Final/native-runtime.uasset). They retain the same package and
class names. Curve dependencies are the original Usage assets above.
