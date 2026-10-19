using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace UAssetAPI.JSON;

/// <summary>JSON type metadata may select asset data and standard data containers only.</summary>
internal sealed class UAssetJsonBinder : ISerializationBinder
{
    private readonly Type explicitRoot;
    private readonly DefaultSerializationBinder resolver = new();
    internal UAssetJsonBinder(Type explicitRoot) { this.explicitRoot = explicitRoot; }

    public Type BindToType(string assemblyName, string typeName)
    {
        string assembly = assemblyName?.Split(',')[0].Trim();
        if (assembly != typeof(UAsset).Assembly.GetName().Name && assembly != explicitRoot.Assembly.GetName().Name &&
            assembly is not ("mscorlib" or "System.Private.CoreLib" or "System.Collections" or "System.Runtime"))
            throw new JsonSerializationException($"Unsupported asset JSON assembly {assemblyName}");
        var type = resolver.BindToType(assemblyName, typeName);
        if (!Allowed(type)) throw new JsonSerializationException($"Unsupported asset JSON type {typeName}");
        return type;
    }

    private bool Allowed(Type type)
    {
        if (type == explicitRoot && type != typeof(object)) return true;
        if (type.IsArray) return Allowed(type.GetElementType());
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            bool container = definition == typeof(List<>) || definition == typeof(Dictionary<,>) ||
                definition == typeof(SortedDictionary<,>) || definition == typeof(HashSet<>) ||
                definition == typeof(KeyValuePair<,>) || definition == typeof(Nullable<>) ||
                definition == typeof(Tuple<,>) || definition == typeof(Tuple<,,>);
            return (container || type.Assembly == typeof(UAsset).Assembly) && type.GetGenericArguments().All(Allowed);
        }
        return type.Assembly == typeof(UAsset).Assembly || type.IsPrimitive || type == typeof(string) ||
            type == typeof(decimal) || type == typeof(Guid) || type == typeof(BigInteger) || type == typeof(object);
    }

    public void BindToName(Type serializedType, out string assemblyName, out string typeName) =>
        resolver.BindToName(serializedType, out assemblyName, out typeName);
}
