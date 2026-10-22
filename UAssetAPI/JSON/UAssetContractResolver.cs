using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.JSON
{
    internal class UAssetContractResolver : DefaultContractResolver
    {
        public Dictionary<FName, string> ToBeFilled;

        protected override JsonConverter ResolveContractConverter(Type objectType)
        {
            if (typeof(FName).IsAssignableFrom(objectType))
            {
                return new FNameJsonConverter(ToBeFilled);
            }
            return base.ResolveContractConverter(objectType);
        }

#if RCT_NATIVE_BRIDGE
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = AotContract.Json)]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = AotContract.Json)]
#endif
        public UAssetContractResolver(Dictionary<FName, string> toBeFilled) : base()
        {
            ToBeFilled = toBeFilled;
        }
    }
}
