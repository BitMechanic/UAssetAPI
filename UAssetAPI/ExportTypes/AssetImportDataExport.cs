using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes
{
    public class AssetImportDataExport : NormalExport
    {
        public FString Json;

        public AssetImportDataExport(Export super) : base(super)
        {

        }

        public AssetImportDataExport(UAsset asset, byte[] extras) : base(asset, extras)
        {

        }

        public AssetImportDataExport()
        {

        }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            Json = reader.ReadFString();

            base.Read(reader, nextStarting);
        }

        public override void Write(AssetBinaryWriter writer)
        {
            long exportStart = writer.BaseStream.Position;
            writer.Write(Json);
            long prefix = writer.BaseStream.Position - exportStart;

            base.Write(writer);
            // Script offsets are relative to the export start, which precedes the native Json prefix.
            ScriptSerializationStartOffset += prefix;
            ScriptSerializationEndOffset += prefix;
        }
    }
}
