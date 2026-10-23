# Native bridge AOT diagnostic dispositions

The supported artifact is this Win64, .NET 10 native bridge with its three file
operations. This is not a claim that every public UAssetAPI or Json.NET API is
compatible with arbitrary trimming/AOT hosts.

The baseline publish emitted 123 diagnostics. The repaired publish emits zero
IL diagnostics with analysis enabled. `IlcTreatWarningsAsErrors` makes future
native compiler warnings fail publication. There is no IL `NoWarn`, disabled
analysis, assembly-wide suppression, or replacement serializer.

## Dispositions of all 123 baseline diagnostics

| Baseline sites | Count | Resolution |
|---|---:|---|
| Jmap object/property converters and both `GetObjectBase` overloads | 46 | Pass `JsonTypeInfo` obtained from the existing options/source-generated context. The same converters and options remain in use; no reflection fallback or new schema. |
| `ReadUProperty(Type/T)`, `StructPropertyData.ReadOnce`, ordered-member helpers, custom-version enum-field readers | 7 | Propagate constructor/field/property metadata requirements with `DynamicallyAccessedMembers`; inspect only the fields/properties actually used. |
| Property registry initialization | 6 | Read the three virtual `PropertyData` properties directly. Keep discovery/constructor reflection against the rooted library; four exact native-only annotations also account for the newly explicit registry-field metadata requirement. Invoke retained constructors directly under AOT instead of retaining expression compilation. |
| Referenced-assembly discovery | 1 | RuntimeFeature guard inside the discovery helper eliminates the unsupported call under AOT. Managed extension discovery remains available. |
| Built-in F/U-property and custom-version name resolution | 4 | Exact method annotations, only under `RCT_NATIVE_BRIDGE`; the full UAssetAPI assembly is rooted, and names select built-in field/custom-version types. |
| General object traversal/JSON helpers and diagnostic mapping exports | 14 | Declare their actual `RequiresUnreferencedCode`/`RequiresDynamicCode` contracts. These public APIs accept arbitrary caller types and are not called by the bridge. Their warnings propagate to a future caller instead of implying general AOT support. |
| Asset JSON/settings, binder/resolver, FString/TMap/FMetaData converters and enum-converter constructor dependency | 30 | Exact method annotations, scoped to the bridge's retained model and concrete generic/converter dependencies. The ordinary managed library does not receive these suppressions. |
| Json.NET dynamic-token/conversion entry points and their DLR/COM closure | 15 | Native-only ILC substitutions remove unsupported entry-point bodies and force the dynamic-code probe to its AOT value (`false`). No dynamic API is exported. Typed converters/indexers are unchanged. |
| **Total** | **123** | |

## Conditions behind the narrow annotations

`UAssetAPI.Native.csproj` roots the entire UAssetAPI assembly, including model
members, built-in properties, fields and custom-version enum attributes.
`Exports.PreserveEnumConverter` preserves the enum converter constructor;
`Exports.Process` preserves the concrete
value-type collections established by prior Blueprint/Animation AOT evidence.
The existing Control Rig, Material and Niagara captures additionally require
the public properties/constructor of `KeyValuePair<FName, uint>` (package build
dependencies); Niagara requires the concrete
`List<FNiagaraVariableCommonReference>` instantiation and retained members.
These are explicit dependencies, not additional diagnostic suppressions.
The `RCTNativeBridge=true` project reference enables `RCT_NATIVE_BRIDGE` only
for this build. Each `UnconditionalSuppressMessage` is on its exact method and
has a justification; it is not attached to the assembly or all methods of a type.
The C ABI entry point itself is unsuppressed, so newly introduced dynamic-code or
trimming requirements there fail the native build. Only the enum-dependency helper
has its two dependency-specific annotations.

The JSON ingress binder admits asset data and standard data containers. The C ABI
does not accept caller-defined managed types, converters, dynamic proxies, mapping
objects or assembly extensions. It does not expose general-purpose JSON methods.
Json.NET's retained reflection fallback constructs the rooted model. Capture
reconstructs the JSON and compares serialized bytes before publication; failures
stay inside the C ABI and do not replace existing destinations. This bounds failure
handling; it does not prove arbitrary JSON edits or all asset formats.

`Aot.Substitutions.xml` applies only during native publication, not to the NuGet
package or ordinary managed tests. The three `GetMetaObject` implementations,
`CreateDynamicContract`, and `JValue.IConvertible.ToType` are unsupported native
entry points and throw if reached. The bridge/UAssetAPI paths use typed converters,
JToken indexers and primitive values, not these dynamic/general-conversion APIs.
The probe substitution removes Json.NET's otherwise retained dynamic-code factory;
its AOT value is already false. Do not expand these substitutions to a required
conversion method just to make a warning disappear.

## Focused evidence

Reuse the existing connected `BP_PinMatrix` and native-baseline `ABP_Unarmed`
assets. Both native-DLL round trips remain byte-equal; manifest/repeated-call,
malformed-input, invalid-operation and existing-output preservation checks pass,
without CoreCLR/hostfxr. Existing managed tests cover the changed Jmap reader,
pin/JSON/FString paths and package binding; the independent custom-property test
checks that managed extension registration still works. RCTools' existing public
Blueprint JSON lifecycle test checks the packaged DLL. Exact build hashes, logs,
test counts and publication revisions live in the shared BP-3 evidence report.

The 123-message pre-repair log is retained, along with the warning-free build.
Zero emitted diagnostics includes justified native-only annotations and removal
of unsupported internal APIs; it does not mean every warning was fixed by removing
reflection, nor does it complete AN-2, Supabase or default/legacy cutover.

The 2026-10-23 capture repair reuses `CR_Mannequin_FootIK`, `NS_JumpPad`,
`M_FlatCol`, `BP_PinMatrix` and the native-baseline `ABP_Unarmed`. All five pass
native capture, describe and restore with exact input/output bytes. Existing-output,
malformed-JSON and invalid-operation refusals preserve outputs and leave no staging
files. The publish still emits zero IL diagnostics. Eighteen focused managed tests
pass, including four new Niagara variable cases and existing JSON-ingress/pin tests.
Niagara also needed `[JsonProperty]` on four existing native-payload fields:
`VariableName`, `TypeDef`, `VarData` and `VariableOffset`. This managed serializer
omission was independent of AOT; `NS_JumpPad` now retains all 795,908 input bytes
instead of losing 102,200 bytes. No new binary codec or serializer was introduced.
These checks establish preservation of the existing assets, not new editor/import
or relocation coverage. Exact build identities and retained red/green evidence live
in the shared `PATTERN-CONSOLIDATION/CAPTURE_REPAIR_REPORT.md`.

The project reference shares the library's `bin`/`obj` directories with ordinary
managed builds. A native publish leaves a bridge-profile library assembly there.
Build the managed project/tests before consuming their output; do not treat the
last DLL in that shared directory as profile-independent. The retained managed-test
assembly was built without `RCTNativeBridge` and has no `AotContract`; that observation
applies to that build, not every later file at the same path. The profile changes
only diagnostic attributes and internal constants. A normal rebuild restores the
ordinary managed diagnostic contract.

References: [Microsoft's warning guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings),
[ILC/ILLink data formats](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/data-formats.md),
[Json.NET 13.0.4 AOT reflection selection](https://github.com/JamesNK/Newtonsoft.Json/blob/13.0.4/Src/Newtonsoft.Json/Serialization/JsonTypeReflector.cs).
