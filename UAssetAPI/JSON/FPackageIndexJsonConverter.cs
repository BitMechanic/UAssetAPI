using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Globalization;
using UAssetAPI.UnrealTypes;
using UAssetAPI.ExportTypes;

namespace UAssetAPI.JSON
{
    // Json.NET uses TypeConverter rather than JsonConverter for dictionary keys.
    // Keep SearchableNames' existing numeric-key JSON representation readable.
    public sealed class FPackageIndexTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value) => value is string text
            ? new FPackageIndex(int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))
            : base.ConvertFrom(context, culture, value);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) => destinationType == typeof(string) && value is FPackageIndex index
            ? index.Index.ToString(CultureInfo.InvariantCulture)
            : base.ConvertTo(context, culture, value, destinationType);
    }

    internal class FPackageIndexJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(FPackageIndex);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue((value as FPackageIndex).Index);
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return new FPackageIndex(Convert.ToInt32(reader.Value));
        }
    }
}
