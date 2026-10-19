using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>The native custom serializer, also used by a node's pin payload.</summary>
public class EdGraphPinTypePropertyData : PropertyData<FEdGraphPinType>
{
    public EdGraphPinTypePropertyData() { }
    public EdGraphPinTypePropertyData(FName name) : base(name) { }
    private static readonly FString CurrentPropertyType = new("EdGraphPinType");
    public override FString PropertyType => CurrentPropertyType;
    public override bool HasCustomStructSerialization => true;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) ReadEndPropertyTag(reader);
        Value = FEdGraphPinType.Read(reader);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) WriteEndPropertyTag(writer);
        long start = writer.BaseStream.Position;
        Value.Write(writer);
        return checked((int)(writer.BaseStream.Position - start));
    }
}
