using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Payloads;

/// <summary>
/// A named allocation value carried alongside a resource (the old
/// <c>AssignedLocationValue</c> name/value pairs): leftover allocation components plus the derived
/// <c>HEAppELocalBasePath</c> entry (offering <c>plugin_options.heappe_local_base_path</c> joined
/// with the resource name).
/// </summary>
/// <param name="Name">Value key (allocation component name, or <c>HEAppELocalBasePath</c>).</param>
/// <param name="Value">Value serialized with invariant culture in mapping.</param>
[GenerateIdentityHash]
public sealed partial record ResourceAllocationValuePayload(string Name, string Value);