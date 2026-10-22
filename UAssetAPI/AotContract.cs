#if RCT_NATIVE_BRIDGE
namespace UAssetAPI;

// Compiled only for the native bridge, whose project roots the complete model.
// Keep each annotation on its exact reflection/JSON method, not the assembly.
internal static class AotContract
{
    internal const string Model = "The native bridge roots UAssetAPI. Lookups select built-in property, field or custom-version types; AOT extension-assembly discovery is disabled. See UAssetAPI.Native/AOT_DIAGNOSTICS.md.";
    internal const string Json = "The native bridge roots the UAssetAPI model and required concrete collections/converters. Json.NET uses its AOT reflection fallback. Capture checks reconstruction before publishing; unsupported input fails inside the C ABI. See UAssetAPI.Native/AOT_DIAGNOSTICS.md.";
}
#endif
