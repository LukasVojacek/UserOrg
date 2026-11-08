using System.Text.Json.Serialization;

namespace UserOrg.Shared;

[JsonConverter(typeof(CaseInsensitiveEnumConverter<EServiceType>))]
public enum EServiceType
{
    Unknown,
    Service,
    Provider,
    DDI
}