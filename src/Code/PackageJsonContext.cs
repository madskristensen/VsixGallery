using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VsixGallery;

[JsonSerializable(typeof(Package))]
[JsonSerializable(typeof(List<Package>))]
internal partial class PackageJsonContext : JsonSerializerContext { }
