using Dapper;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

[assembly: AssemblyFixture(typeof(DapperHandler))]

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Registers the package's Dapper handler once for the process, through the package's own public
/// call.
/// </summary>
/// <remarks>
/// <para>
/// Dapper's registry is static and takes no scope argument, so this is the only honest shape: one
/// registration for the whole run, made before the first test in the assembly. It also means the
/// suite cannot test the unregistered state - that state is a fact about Dapper,
/// measured once and recorded in the change, not something a test here can return to.
/// </para>
/// <para>
/// The registration is made rather than simulated. A suite that added the handler itself would
/// pass over a <c>UseBigDecimal</c> that had stopped doing anything.
/// </para>
/// <para>
/// An assembly fixture, which is what the container fixtures deliberately are not: the reason
/// those are class-scoped is that building one resolves a Docker endpoint, and this one touches
/// nothing but a static dictionary.
/// </para>
/// </remarks>
public sealed class DapperHandler
{
    /// <summary>Registers the handler.</summary>
    public DapperHandler() => SqlMapperBigDecimalExtensions.UseBigDecimal();
}
