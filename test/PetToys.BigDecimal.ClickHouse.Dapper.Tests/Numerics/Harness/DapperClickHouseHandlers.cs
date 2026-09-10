using Dapper;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

[assembly: AssemblyFixture(typeof(DapperClickHouseHandlers))]

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Registers the package's Dapper handlers once for the process, through the package's own public
/// call.
/// </summary>
/// <remarks>
/// <para>
/// Dapper's registry is static and takes no scope argument, so this is the only honest shape: one
/// registration for the whole run, made before the first test in the assembly. It also means the
/// suite cannot test the unregistered state - that state is a fact about Dapper, measured once and
/// recorded in the change, not something a test here can return to.
/// </para>
/// <para>
/// The registration is made rather than simulated. A suite that added the handlers itself would
/// pass over a <c>UseBigDecimal</c> that had stopped doing anything.
/// </para>
/// <para>
/// An assembly fixture, which is what the container fixture deliberately is not: that one is
/// class-scoped because building it resolves a Docker endpoint, and this one touches nothing but a
/// static dictionary.
/// </para>
/// </remarks>
public sealed class DapperClickHouseHandlers
{
    /// <summary>Registers both handlers.</summary>
    public DapperClickHouseHandlers() => ClickHouseSqlMapperBigDecimalExtensions.UseBigDecimal();
}
