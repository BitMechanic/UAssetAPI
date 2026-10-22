# RCTools Native AOT bridge

Build from this fork with the .NET 10 SDK and the Windows C++ toolchain:

```powershell
dotnet publish UAssetAPI.Native/UAssetAPI.Native.csproj -c Release -r win-x64 -o UAssetAPI.Native/publish -p:GeneratePackageOnBuild=false
```

The native DLL has no CoreCLR/hostfxr dependency. Keep it loaded for the process
lifetime; Native AOT unloading is unsupported. The bridge serializes calls.
All UTF-8 strings and the error buffer belong to the caller and remain valid
until the synchronous call returns. No managed pointers cross the ABI.

```c
int rct_uasset_abi_version(void); /* 1 */
int rct_uasset_process(int operation, const char* input, const char* output,
                      const char* options_json, char* error, int error_capacity);
```

Return 0 means success, 1 means failure; diagnostics are UTF-8, truncated at a
code-point boundary and terminated when error_capacity > 0. Outputs must be new
files. Failed conversion never overwrites an existing output. Publication of
complete pattern metadata/payloads belongs to RCTools.

- 0: UE5.8 self-contained editor asset to UAssetAPI JSON; verifies reconstruction.
- 1: JSON to asset; options contains `bindings` and `destinations` string maps
  passed to the existing PackageBinding.Apply implementation. The native caller
  obtains destination classes from Unreal and checks semantic compatibility.
- 2: JSON to a compact PackageBinding manifest; the authoritative JSON is unchanged.

The bridge refuses split bulk-data files, unversioned properties and filtered
editor data. It uses the fork's existing JSON ingress binder and pin codec.
It does not resolve dependencies, reconstruct graphs, compile Blueprints or
implement another serializer. RCTools performs native asset capture/read/edit.

The library's reflection metadata is preserved for polymorphic JSON. Json.NET's
attribute-created StringEnumConverter parameterless constructor is explicitly
preserved; its entire assembly is not rooted. Concrete
value-type collections required by the prior Blueprint/Animation AOT evidence
are explicitly instantiated. Analyzers remain enabled and native compiler warnings
fail publication. See [AOT_DIAGNOSTICS.md](AOT_DIAGNOSTICS.md) for the exact
metadata contracts, native-only annotations and unsupported dynamic API removals.
The warning-free bridge is not a claim that arbitrary managed extensions run under AOT.
