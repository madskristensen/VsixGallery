using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VsixGallery;

[JsonSerializable(typeof(Package))]
[JsonSerializable(typeof(List<Package>))]
[JsonSerializable(typeof(ManageInfo))]
[JsonSerializable(typeof(ValidationFinding))]
[JsonSerializable(typeof(List<ValidationFinding>))]
internal partial class PackageJsonContext : JsonSerializerContext { }
