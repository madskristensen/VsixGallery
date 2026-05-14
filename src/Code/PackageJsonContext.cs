using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VsixGallery;

[JsonSerializable(typeof(Package))]
[JsonSerializable(typeof(List<Package>))]
[JsonSerializable(typeof(ManageInfo))]
internal partial class PackageJsonContext : JsonSerializerContext { }
