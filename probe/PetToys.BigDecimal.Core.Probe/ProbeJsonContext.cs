using System.Text.Json.Serialization;
using PetToys.BigDecimal.Numerics;

namespace BigDecimalProbes;

/// <summary>
/// The source-generated metadata for <see cref="Holder"/>. This is the form that has to work when
/// reflection-based serialization is disabled, and whether <c>[JsonConverter]</c> on the type is
/// honoured through the generator's path is the one question the trim analyzer cannot answer.
/// </summary>
[JsonSerializable(typeof(Holder))]
[JsonSerializable(typeof(BigDecimal))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;
