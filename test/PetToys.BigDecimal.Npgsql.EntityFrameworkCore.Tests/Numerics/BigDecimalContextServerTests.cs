using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

// EF1002 and EF1003 are about SQL built from strings, and they are right in general. This
// class builds every statement from its own constants - a table name it chose and a column
// name it wrote - and none of it is reachable from a caller. EF1003 exists only from EF Core
// 10, so it appears on one of the two provider runs and not the other.
#pragma warning disable EF1002, EF1003

/// <summary>
/// The mapping through Entity Framework, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Every case goes through a context configured by this package's own registration over a data
/// source built with the adapter's, so either registration ceasing to take effect fails here
/// rather than being replaced by the test. The oracle is the server: a value read back is compared
/// against the decomposition of the server's own text rendering, never against what the package
/// wrote.
/// </para>
/// <para>
/// Two of these cases exist because this layer's worst failures are silent. A change tracker that
/// misses an edit produces no error, no wrong value and no failing round trip, so the assertions
/// are what the save reported and what the server holds afterwards. A query that stops translating
/// does not fail either, because Entity Framework evaluates on the client what it cannot
/// translate, so the assertion is the generated SQL rather than the result.
/// </para>
/// </remarks>
/// <param name="server">The server, one per this class, started on first use.</param>
[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class BigDecimalContextServerTests(PostgresServer server) : IClassFixture<PostgresServer>
{
    private const string Table = "EfItems";

    private const string Wide = "123456789012345678901234567890.123456789";

    [Fact]
    public async Task AWideValue_CrossesBothWaysExactly()
    {
        await using var context = await this.ReadyAsync();

        var value = BigDecimal.Parse(Wide, CultureInfo.InvariantCulture);
        context.Items.Add(new NumericItem { Id = 1, Amount = value });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var read = await context.Items.SingleAsync(x => x.Id == 1, TestContext.Current.CancellationToken);

        // Compared against the server's own rendering rather than against the value written: a
        // symmetric mistake in a read and a write passes a round trip exactly as it passes one
        // through the codec.
        OracleValue.Observe(read.Amount).Should().Be(FromServerText(await RenderAsync(context, "Amount")));
    }

    [Fact]
    public async Task AScaleOnlyEdit_IsWrittenBack()
    {
        await using var context = await this.ReadyAsync();

        context.Items.Add(new NumericItem { Id = 1, Amount = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture) });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracked = await context.Items.SingleAsync(x => x.Id == 1, TestContext.Current.CancellationToken);
        tracked.Amount = BigDecimal.Parse("1.50", CultureInfo.InvariantCulture);

        var rows = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Asserting the property afterwards would prove nothing: it holds the new value whether or
        // not anything was written. These two assertions are the whole test.
        rows.Should().Be(1, "a change of scale is a change, and the tracker has to see it");
        (await RenderAsync(context, "Amount")).Should().Be("1.50");
    }

    [Fact]
    public async Task AFacetedColumn_ReturnsItsOwnScaleAndABareColumnKeepsTheValues()
    {
        await using var context = await this.ReadyAsync();

        var half = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture);
        context.Items.Add(new NumericItem { Id = 1, Amount = half, Faceted = half });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var read = await context.Items.SingleAsync(x => x.Id == 1, TestContext.Current.CancellationToken);

        // The package does not rescale; the server applies the column's display scale. A caller who
        // wants the scale they wrote has to declare a column that carries it, which is what the
        // README states and this pins.
        read.Faceted.Scale.Should().Be(4);
        (await RenderAsync(context, "Faceted")).Should().Be("1.5000");
        read.Amount.Scale.Should().Be(1);
        (await RenderAsync(context, "Amount")).Should().Be("1.5");
    }

    [Fact]
    public async Task AValueBeyondTheFacets_IsRefusedBeforeTheServerSeesIt()
    {
        await using var context = await this.ReadyAsync();

        context.Items.Add(new NumericItem
        {
            Id = 1,
            Faceted = BigDecimal.Parse("12345678901234.5", CultureInfo.InvariantCulture),
        });

        var save = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var thrown = (await save.Should().ThrowAsync<DbUpdateException>()).Which;

        thrown.InnerException.Should().BeOfType<OverflowException>()
            .Which.Message.Should().Match("*12345678901234.5*numeric(12,4)*");

        // The property name is one level up, and this is the half a caller has no reason to expect
        // and the README's sample depends on.
        var entry = thrown.Entries.Should().ContainSingle().Subject;
        entry.Entity.Should().BeOfType<NumericItem>();
        entry.State.Should().Be(EntityState.Added);
        entry.Properties.Select(p => p.Metadata.Name).Should().Contain(nameof(NumericItem.Faceted));

        context.ChangeTracker.Clear();
        (await context.Items.CountAsync(TestContext.Current.CancellationToken))
            .Should().Be(0, "the refusal runs before the statement is sent");
    }

    [Fact]
    public async Task ALongerFractionThanTheColumnHolds_IsRefusedRatherThanRounded()
    {
        await using var context = await this.ReadyAsync();

        context.Items.Add(new NumericItem
        {
            Id = 1,
            Faceted = BigDecimal.Parse("1.55555", CultureInfo.InvariantCulture),
        });

        var save = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // PostgreSQL would round this to 1.5556. Rounding a value the caller handed us whole is a
        // conversion this package declines to take over, so it refuses instead.
        (await save.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<OverflowException>()
            .Which.Message.Should().Match("*1.55555*numeric(12,4)*");
    }

    [Fact]
    public async Task ATrailingZeroTheColumnCannotKeep_IsNotARefusal()
    {
        await using var context = await this.ReadyAsync();

        // 1.50000 into numeric(12,4) is 1.5000, which loses nothing. Refusing it would be refusing
        // a value PostgreSQL stores exactly, and the round trip already returns the column's scale.
        context.Items.Add(new NumericItem
        {
            Id = 1,
            Faceted = BigDecimal.Parse("1.50000", CultureInfo.InvariantCulture),
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await RenderAsync(context, "Faceted")).Should().Be("1.5000");
    }

    [Fact]
    public async Task AColumnWhosePrecisionEqualsItsScale_TakesZeroAndAFraction()
    {
        await using var context = await this.ReadyAsync();

        // numeric(4,4) holds values below 1 with four decimals. Zero is the case worth pinning:
        // it reports one significant digit at every scale, so counting its integer part the way
        // every other value's is counted would refuse a value the server accepts.
        context.Items.Add(new NumericItem { Id = 1, Tight = BigDecimal.Zero });
        context.Items.Add(new NumericItem
        {
            Id = 2,
            Tight = BigDecimal.Parse("0.5", CultureInfo.InvariantCulture),
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await RenderAsync(context, "Tight")).Should().Be("0.0000");
        (await RenderAsync(context, "Tight", 2)).Should().Be("0.5000");
    }

    [Fact]
    public async Task AValueThatDoesNotFitAPrecisionEqualsScaleColumn_IsStillRefused()
    {
        await using var context = await this.ReadyAsync();

        context.Items.Add(new NumericItem
        {
            Id = 1,
            Tight = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture),
        });

        var save = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await save.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<OverflowException>()
            .Which.Message.Should().Match("*1.5*numeric(4,4)*");
    }

    [Fact]
    public async Task ANonFiniteLiteral_ReachesTheServerAsAValueRatherThanAColumnName()
    {
        await using var context = await this.ReadyAsync();

        context.Items.Add(new NumericItem { Id = 1, Amount = BigDecimal.NaN });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Unquoted, NaN::numeric is a reference to a column called nan and the query fails with
        // 42703. Executing it is the assertion; the SQL is checked so the reason stays visible.
        context.ChangeTracker.Clear();
        var query = context.Items.Where(x => x.Amount == EF.Constant(BigDecimal.NaN));

        query.ToQueryString().Should().Contain("'NaN'::numeric");
        (await query.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task AColumnDeclaredWithoutFacets_IsNotChecked()
    {
        await using var context = await this.ReadyAsync();

        var beyond = BigDecimal.Parse("12345678901234.5", CultureInfo.InvariantCulture);
        context.Items.Add(new NumericItem { Id = 1, Amount = beyond });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await RenderAsync(context, "Amount")).Should().Be("12345678901234.5");
    }

    [Fact]
    public async Task TheNonFiniteValues_CrossAsThemselves()
    {
        await using var context = await this.ReadyAsync();

        context.Items.Add(new NumericItem { Id = 1, Amount = BigDecimal.NaN, Faceted = BigDecimal.NaN });
        context.Items.Add(new NumericItem { Id = 2, Amount = BigDecimal.PositiveInfinity });
        context.Items.Add(new NumericItem { Id = 3, Amount = BigDecimal.NegativeInfinity });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var read = await context.Items.OrderBy(x => x.Id).ToListAsync(TestContext.Current.CancellationToken);

        BigDecimal.IsNaN(read[0].Amount).Should().BeTrue();
        BigDecimal.IsNaN(read[0].Faceted).Should().BeTrue("a constrained column carries them too");
        BigDecimal.IsPositiveInfinity(read[1].Amount).Should().BeTrue();
        BigDecimal.IsNegativeInfinity(read[2].Amount).Should().BeTrue();

        (await RenderAsync(context, "Amount", 1)).Should().Be("NaN");
        (await RenderAsync(context, "Amount", 2)).Should().Be("Infinity");
        (await RenderAsync(context, "Amount", 3)).Should().Be("-Infinity");
    }

    [Fact]
    public async Task TheCollectionForms_CrossBothWays()
    {
        await using var context = await this.ReadyAsync();

        var wide = BigDecimal.Parse(Wide, CultureInfo.InvariantCulture);
        var trailing = BigDecimal.Parse("1.50", CultureInfo.InvariantCulture);

        context.Items.Add(new NumericItem
        {
            Id = 1,
            Amounts = [wide, trailing],
            More = [trailing],
            Sparse = [wide, null],
            SparseList = [null, trailing],
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var read = await context.Items.SingleAsync(x => x.Id == 1, TestContext.Current.CancellationToken);

        read.Amounts.Select(OracleValue.Observe).Should()
            .Equal(OracleValue.Observe(wide), OracleValue.Observe(trailing));
        read.More.Select(OracleValue.Observe).Should().Equal(OracleValue.Observe(trailing));
        read.Sparse[1].Should().BeNull("a null element crosses as a null");
        OracleValue.Observe(read.Sparse[0]!.Value).Should().Be(OracleValue.Observe(wide));
        read.SparseList[0].Should().BeNull();
        OracleValue.Observe(read.SparseList[1]!.Value).Should().Be(OracleValue.Observe(trailing));

        (await RenderAsync(context, "Amounts")).Should().Be("{" + Wide + ",1.50}");
    }

    [Fact]
    public async Task AScaleOnlyEditInsideACollection_IsWrittenBack()
    {
        await using var context = await this.ReadyAsync();

        context.Items.Add(new NumericItem
        {
            Id = 1,
            Amounts = [BigDecimal.Parse("1.5", CultureInfo.InvariantCulture)],
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracked = await context.Items.SingleAsync(x => x.Id == 1, TestContext.Current.CancellationToken);
        tracked.Amounts = [BigDecimal.Parse("1.50", CultureInfo.InvariantCulture)];

        (await context.SaveChangesAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        (await RenderAsync(context, "Amounts")).Should().Be("{1.50}");
    }

    [Fact]
    public async Task TheQuery_ReachesTheServerRatherThanTheClient()
    {
        await using var context = await this.ReadyAsync();

        var value = BigDecimal.Parse("1.50", CultureInfo.InvariantCulture);

        // Asserted on the SQL, not on the result. A mapping that stopped translating would keep
        // returning correct answers over a table this size while a consumer's production query
        // pulled theirs across the wire.
        context.Items.Where(x => x.Amount > value).ToQueryString()
            .Should().Contain("WHERE").And.Contain("\"Amount\" > @");

        context.Items.Where(x => x.Amount * value > value).ToQueryString()
            .Should().Contain("\"Amount\" * @");

        context.Items.GroupBy(x => x.Id).Select(g => g.Max(x => x.Amount)).ToQueryString()
            .Should().Contain("max(");

        // A captured value is a parameter, which is the shape a real query takes and the reason
        // the mapping configures one at all. EF.Constant is what forces the other path, where the
        // literal is rendered by the mapping and the cast is what keeps the comparison typed.
        context.Items.Where(x => x.Amount == EF.Constant(value)).ToQueryString()
            .Should().Contain("'1.50'::numeric");
    }

    [Fact]
    public async Task TheServersArithmetic_IsTheServersAndTheTypesIsTheTypes()
    {
        await using var context = await this.ReadyAsync();

        var half = BigDecimal.Parse("0.5", CultureInfo.InvariantCulture);
        var oneAndAHalf = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture);

        // Each side is asserted on its own. No test here says the two agree, because nothing in
        // PostgreSQL promises that across versions, and a package that quietly reconciled them
        // would be making a promise it cannot keep.
        (await ScalarAsync(context, "round(0.5::numeric)")).Should().Be("1");
        (await ScalarAsync(context, "round(1.5::numeric)")).Should().Be("2");
        BigDecimal.Round(half, 0).ToString(CultureInfo.InvariantCulture).Should().Be("0");
        BigDecimal.Round(oneAndAHalf, 0).ToString(CultureInfo.InvariantCulture).Should().Be("2");

        var division = await ScalarAsync(context, "(1::numeric / 3::numeric)");
        division.Should().Be("0.33333333333333333333", "the server picks its own scale");
        BigDecimal.Divide(BigDecimal.One, BigDecimal.Parse("3", CultureInfo.InvariantCulture), 5, MidpointRounding.ToEven)
            .ToString(CultureInfo.InvariantCulture)
            .Should().Be("0.33333", "the type is told its scale rather than choosing one");
    }

    [Fact]
    public async Task AColumnWiderThanTheModelDeclares_ReturnsTheColumnsScale()
    {
        await using var context = await this.ReadyAsync();

        // The design's open question, pinned rather than answered: a column altered behind the
        // model's back returns its own scale, not the model's. Whether that is worth detecting is
        // not settled here; this is the starting point a later answer needs.
        await context.Database.ExecuteSqlRawAsync(
            "alter table \"" + Table + "\" alter column \"Faceted\" type numeric(20,10)",
            TestContext.Current.CancellationToken);

        context.Items.Add(new NumericItem
        {
            Id = 1,
            Faceted = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture),
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.ChangeTracker.Clear();
        var read = await context.Items.SingleAsync(x => x.Id == 1, TestContext.Current.CancellationToken);

        read.Faceted.Scale.Should().Be(10, "the server applied the column's scale, not the model's");
    }

    [Fact]
    public async Task ADataSourceWithoutTheAdaptersRegistration_FailsRatherThanNarrowing()
    {
        await server.RequireAsync();

        var (context, source) = NumericModel.WithoutTheAdapter(server.ConnectionString, Table);

        await using (context)
        await using (source)
        {
            await context.CreateTableAsync();

            context.Items.Add(new NumericItem
            {
                Id = 1,
                Amount = BigDecimal.Parse(Wide, CultureInfo.InvariantCulture),
            });

            var save = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            // The half-configured combination a reader who skimmed will build. What it does is
            // pinned here and stated in the README; what it must not do is narrow the value.
            var thrown = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
            thrown.InnerException.Should().BeOfType<InvalidCastException>(
                "the driver has no mapping for the type, which is the adapter's half of the setup");

            context.ChangeTracker.Clear();
            (await context.Items.CountAsync(TestContext.Current.CancellationToken))
                .Should().Be(0, "nothing was narrowed on the way to the server");
        }
    }

    private async Task<NumericContext> ReadyAsync()
    {
        await server.RequireAsync();

        var context = NumericModel.Over(server.DataSource, Table);
        await context.CreateTableAsync();

        return context;
    }

    private static async Task<string> RenderAsync(NumericContext context, string column, int id = 1)
    {
        var sql = "select \"" + column + "\"::text as \"Value\" from \"" + Table
            + "\" where \"Id\" = " + id.ToString(CultureInfo.InvariantCulture);

        return await context.Database.SqlQueryRaw<string>(sql)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string> ScalarAsync(NumericContext context, string expression) =>
        await context.Database.SqlQueryRaw<string>("select (" + expression + ")::text as \"Value\"")
            .SingleAsync(TestContext.Current.CancellationToken);

    /// <summary>Decomposes the server's own rendering, independently of the package.</summary>
    /// <param name="text">The text the server produced.</param>
    /// <returns>The unscaled value and the scale it denotes.</returns>
    private static OracleValue FromServerText(string text)
    {
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? text : text.Remove(point, 1);
        var scale = point < 0 ? 0 : text.Length - point - 1;

        return new OracleValue(BigInteger.Parse(digits, CultureInfo.InvariantCulture), scale);
    }
}
