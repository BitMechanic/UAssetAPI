using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

// Synchronous, serialized C ABI. All pointers are caller-owned UTF-8 strings;
// no managed allocation crosses the ABI. Retain the DLL until process exit.
public static unsafe class Exports
{
    private static readonly object Gate = new();
    private static readonly object[] AotCollections = {
        new List<UAssetAPI.ExportTypes.SerializedInterfaceReference>(),
        new List<FFrameNumber>(),
        new List<UAssetAPI.Kismet.Bytecode.Expressions.FKismetSwitchCase>()
    };

    [UnmanagedCallersOnly(EntryPoint = "rct_uasset_abi_version", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int Version() => 1;

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The exact enum converter constructor below is preserved for attributes; all asset model types are rooted. No open-ended reflection API is exposed by this C ABI.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The enum converter uses retained enum metadata through Json.NET's AOT reflection fallback; the existing Blueprint and Animation DLL round trips cover it.")]
    // Json.NET constructs this built-in converter from UAssetAPI's attributes.
    // Preserve that constructor without rooting Json.NET's unrelated schema/DLR APIs.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<UAssetAPI.ExportTypes.SerializedInterfaceReference>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<FFrameNumber>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<UAssetAPI.Kismet.Bytecode.Expressions.FKismetSwitchCase>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SortedDictionary<FPackageIndex, List<FName>>))]
    [UnmanagedCallersOnly(EntryPoint = "rct_uasset_process", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int Process(int operation, byte* input, byte* output, byte* options, byte* error, int capacity)
    {
        if (error != null && capacity > 0) error[0] = 0;
        try
        {
            lock (Gate)
            {
                GC.KeepAlive(AotCollections);
                string source = Path.GetFullPath(Utf8(input));
                string destination = Path.GetFullPath(Utf8(output));
                if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Input and output must be different files");
                // All bridge outputs are new staging files. Publication/replacement belongs
                // to the caller, after the complete result and metadata have been checked.
                if (File.Exists(destination)) throw new IOException("Output already exists: " + destination);
                if (operation < 0 || operation > 2) throw new ArgumentOutOfRangeException(nameof(operation));
                var asset = operation == 0
                    ? new UAsset(source, EngineVersion.VER_UE5_8, customSerializationFlags: CustomSerializationFlags.SkipPreloadDependencyLoading)
                    : UAsset.DeserializeJson(File.ReadAllText(source));
                if (asset.UseSeparateBulkDataFiles || asset.HasUnversionedProperties || asset.IsFilterEditorOnly)
                    throw new NotSupportedException("The editor bridge requires a self-contained, versioned, uncooked asset");
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    if (operation == 0)
                    {
                        string json = asset.SerializeJson();
                        using var original = asset.WriteData();
                        using var restored = UAsset.DeserializeJson(json).WriteData();
                        if (!original.ToArray().AsSpan().SequenceEqual(restored.ToArray()))
                            throw new InvalidDataException("JSON reconstruction changes the serialized asset");
                        File.WriteAllText(temporary, json, new UTF8Encoding(false));
                    }
                    else if (operation == 1)
                    {
                        var request = JObject.Parse(Utf8(options));
                        PackageBinding.Apply(asset, Strings(request, "bindings"), Strings(request, "destinations"));
                        asset.Write(temporary);
                    }
                    else
                    {
                        var manifest = PackageBinding.Describe(asset);
                        var imports = new JArray();
                        foreach (var entry in manifest.Imports)
                            imports.Add(new JObject {
                                ["index"] = entry.Index, ["package"] = entry.Package,
                                ["object_name"] = entry.ObjectName, ["class_package"] = entry.ClassPackage,
                                ["class_name"] = entry.ClassName, ["outer_chain"] = new JArray(entry.OuterChain),
                                ["load_critical"] = entry.LoadCritical });
                        var soft = new JArray();
                        foreach (var entry in manifest.SoftPackages)
                            soft.Add(new JObject { ["package"] = entry.Package, ["paths"] = new JArray(entry.Paths) });
                        File.WriteAllText(temporary, new JObject {
                            ["package"] = manifest.Package, ["imports"] = imports, ["soft_packages"] = soft,
                            ["converter"] = UAPUtils.DisplayVersion
                        }.ToString(Formatting.None), new UTF8Encoding(false));
                    }
                    File.Move(temporary, destination, false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return 0;
            }
        }
        catch (Exception ex)
        {
            if (error != null && capacity > 0)
            {
                var bytes = Encoding.UTF8.GetBytes(ex.GetType().Name + ": " + ex.Message);
                int count = Math.Min(bytes.Length, capacity - 1);
                // Do not return a partial UTF-8 code point when diagnostics are truncated.
                if (count < bytes.Length) while (count > 0 && (bytes[count] & 0xc0) == 0x80) count--;
                Marshal.Copy(bytes, 0, (nint)error, count);
                error[count] = 0;
            }
            return 1;
        }
    }

    private static string Utf8(byte* value) => value == null
        ? throw new ArgumentNullException(nameof(value))
        : Marshal.PtrToStringUTF8((nint)value) ?? throw new ArgumentException("Invalid UTF-8 pointer");

    private static Dictionary<string, string> Strings(JObject request, string key)
    {
        if (request[key] is not JObject values) throw new FormatException(key + " must be an object");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in values.Properties())
        {
            if (entry.Value.Type != JTokenType.String) throw new FormatException(key + " values must be strings");
            result.Add(entry.Name, (string)entry.Value!);
        }
        return result;
    }
}
