# Test harness

Shared machinery for the tests in `test/PetToys.BigDecimal.Core.Tests`. Three
things live here: reference oracles, a value generator, and the measurement and
culture helpers the deterministic suites use.

## The rule the oracles answer to

`BigIntegerOracle` computes what each operation is **required** to produce, from
`System.Numerics.BigInteger` arithmetic and the package's documented scale and
rounding rules. It must never call `BigDecimal`, `Words`, or any other internal
of the package under test to produce an expected value - an
oracle written by reading the code under test agrees with it by construction,
including where it is wrong.

When the oracle and the implementation disagree, the documented rule decides
which one moves. If nothing is written down on the point, the rule gets written
**before** either side changes. Adjusting the oracle until the suite goes green
is how a harness becomes decoration.

Every reduction rounds once, from the full exact input. A reference that rounds
to an intermediate scale and then again can land a unit in the last place away
from the correct answer, and the harness would then report its own error as the
implementation's.

`DecimalParityOracle` is the second opinion, used only inside `decimal`'s own
domain and only where parity with `decimal` is the actual requirement: the five
rounding modes, how lenient parsing is about group separators, and
culture-sensitive output. Where both oracles apply they have to agree, and that
agreement is itself a test.

## Running a soak

Two environment variables control the randomised suites. Both have fixed
defaults, so an unconfigured run executes the same cases on every machine.

| Variable                | Default      | Effect                                     |
| ----------------------- | ------------ | ------------------------------------------ |
| `BIGDECIMAL_FUZZ_CASES` | `2000`       | Cases per randomised test                  |
| `BIGDECIMAL_FUZZ_SEED`  | `0x5F3D1A27` | The constant every derived seed is mixed with |

A soak is the same tests with a larger count and a different base seed:

```bash
BIGDECIMAL_FUZZ_CASES=200000 BIGDECIMAL_FUZZ_SEED=20260826 dotnet test big-decimal.tests.slnf -c Release
```

Anything a soak finds becomes a fixed `[InlineData]` case in the suite it came
from, so that the default run keeps it forever.

## Reproducing a failure

Every randomised case reports the seed it was drawn from and its position within
that seed's batch:

```
[seed 440125329 case 54] left 6277101735386680763835789423207666416102355444464034512895e-5 [BelowWordBoundary], right 1000…000e-116 [PowerOfTen]: %
```

Seeds are derived from a fixed constant and the test's own name, so that line
reproduces on any machine, operating system and target framework. To replay one
seed, run the test and read down to that case - the batch is deterministic - or
paste the operands into a `[Fact]`.

## Categories

`FuzzDataAttribute` puts the `Category=Fuzz` trait on every row it produces, so
a randomised test cannot be written without it. The trait is drawn around what is
*randomised*, not around what is new: the allocation inventory and the culture
matrix are deterministic and carry no category, so a future decision to drop the
randomised tests from continuous integration - one clause in the workflow's
filter - cannot take them along.

## The server layer

The two wire codecs have a fourth layer that does not live here. The vectors, the
`BigInteger` oracle and the round trip over the corpus all share our reading of
the documented layout with the code they check, so a misreading of it would be
invisible to all three: the oracle would compose the same wrong bytes the codec
writes. Only a real server disproves that, and the tests that ask one live in
`test/PetToys.BigDecimal.Npgsql.Tests` and `test/PetToys.BigDecimal.ClickHouse.Tests`
under `Numerics/`, carrying `Category=Integration`.

The rule there is the same one as here, one step further out. The server is the
oracle, and our own decoder never judges our own encoder: the server composes the
payload for a value it was given as text, we decode that, and separately we
encode and let the server render what it stored. The payload crosses by a route
that converts nothing - a raw binary `COPY` stream for PostgreSQL, `RowBinary`
over the HTTP interface for ClickHouse - so no driver stands between the codec
and the bytes. `WireFormatOracle` and `ValueGenerator` are reused from here
rather than copied, which is why those projects reference this one.

It reaches two things the layers here cannot. A payload composed by the server
for a value larger than this type holds, or with a longer fraction than the scale
carries, exercises the overflow and rounding contract from the side that
originates it. And the servers disagree about what text is: PostgreSQL renders
the display scale, trailing zeros included, while ClickHouse trims them and
prints a bare `0` for zero, because there the scale lives in the column type and
nowhere in the value.

## Adding public surface

`AllocationInventory` lists every operation held to the zero-allocation
guarantee, and `AllocationTests` walks the public members of `BigDecimal` by
reflection to check that each one is either in that list or in the exclusion list
with a written reason. A new public member fails the suite until it is
classified. Exclusions are for signatures that make an allocation unavoidable -
returning a `string`, or a contract expressed in terms of `object` - never for an
implementation that happens to allocate.
