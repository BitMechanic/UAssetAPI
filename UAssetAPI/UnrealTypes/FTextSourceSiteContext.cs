using Newtonsoft.Json;

namespace UAssetAPI.UnrealTypes
{
    public struct FTextSourceSiteContext
    {
        // FFortniteMainBranchObjectVersion::AddDevNotesToFText (UE 5.8).
        internal const int AddDevNotesVersion = 260;
        [JsonProperty] public FString KeyName { get; set; }
        [JsonProperty] public FString SiteDescription { get; set; }
        [JsonProperty] public bool IsEditorOnly { get; set; }
        [JsonProperty] public bool IsOptional { get; set; }
        [JsonProperty] public FString DevNotes { get; set; }
        [JsonProperty] public FLocMetadataObject InfoMetaData { get; set; }
        [JsonProperty] public FLocMetadataObject KeyMetaData { get; set; }
    }
}
