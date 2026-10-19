BP_Usage.uasset is an original UE5.8.3 Actor Blueprint fixture generated for
DependencyUsageTests. Its runtime hard/soft fields use /Game/Usage/Old; editor-only
fields use Target and Other. All three dependencies are CurveFloat assets. Exact
is int64 9007199254740993. The binary readers do not need to load the curves.

Native creation source, complete dependencies, pre-fix/post-fix reconstructions
and execution evidence are retained in the shared project evidence repository:
projects/rctools-mcp-server/resources/artifacts/2026-10-18-blueprint-animation-native-pattern-context/FORK-1/usage-promotion/.
No custom native class is required by this fixture.

Transient/BP_Usage.uasset is a native variant whose RuntimeHard and RuntimeSoft
declarations carry CPF_Transient. These CDO defaults still serialize and collect
runtime dependencies. Weak/BP_Usage.uasset is a native variant with a weak
RuntimeHard Target reference; its Target import usage remains editor-only.
Both are unchanged native outputs from the independent usage-promotion review:
FORK-1/usage-promotion-review/native-evidence.zip (TransientEvidence/Fixtures/Usage
and WeakEvidence/Final/native-runtime.uasset). They retain the same package and
class names. Curve dependencies are the original Usage assets above.
