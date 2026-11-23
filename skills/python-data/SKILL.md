---
name: python-data
description: "Python data code for reporting and Microsoft Fabric work: pandas and polars idioms, explicit dtypes, joins that cannot silently duplicate rows, money and timestamps, notebook code versus importable modules, and testing transforms with small fixture frames. Use when writing or reviewing dataframe code, a Fabric notebook, a billing or reporting job, or tests for a transform. For project setup use python-architect; for backend services use python-developer; for Fabric APIs, Spark and Lakehouse operations use Microsoft's skills-for-fabric."
allowed-tools: Read Grep Glob Edit Write Bash
metadata:
  category: domain-skills
---

# Python Data Skill

How to write dataframe code: reporting jobs, billing exports, and the Python behind Fabric
notebooks.

| For | Use |
|---|---|
| Project file, tooling, layout, pipeline | `python-architect`, applied by `python-project` |
| FastAPI, Django, SQLAlchemy services | `python-developer` |
| Fabric REST APIs, Spark, Lakehouse, notebooks as Fabric items, semantic models | Microsoft's public `skills-for-fabric` |
| Test discipline | `test-filter-development` |

## Auto-Detection

This skill activates when:
- Code imports `pandas`, `polars` or `pyarrow`
- Working in a `*.ipynb` file or a Fabric notebook
- Writing a transform, export, reconciliation or billing calculation

## Choosing pandas or polars

| Situation | Use |
|---|---|
| Default, and anything running in a Fabric notebook | pandas. The Fabric runtime preinstalls it, and most examples and libraries assume it |
| A job that is slow or runs out of memory in pandas | polars, with the lazy API. Check that the target runtime has it, or add it to the project's dependencies |
| Data too large for one machine | Spark, through `skills-for-fabric` |

Do not mix the two in one transform. Convert at the boundary (`pl.from_pandas`, `df.to_pandas()`)
if a library needs the other.

## Read with explicit types

Inferred types change with the data. A serial number column of digits reads as an integer, drops
its leading zeros, and stops joining to the table that kept them as text.

```python
# ❌ BAD - types inferred from whatever this file contains
df = pd.read_csv(path)

# ✅ GOOD - identifiers as text, dates parsed, the rest declared
df = pd.read_csv(
    path,
    dtype={"serial_number": "string", "part_number": "string", "qty": "Int64"},
    parse_dates=["received_at"],
)
```

```python
# polars: the same, with a schema override
df = pl.read_csv(path, schema_overrides={"serial_number": pl.String, "qty": pl.Int64})
```

Identifiers (serials, part numbers, order numbers, ZIP codes) are always text, even when every
value is digits.

## Vectorise instead of looping

```python
# ❌ BAD - a Python call per row
df["total"] = df.apply(lambda r: r["qty"] * r["unit_price"], axis=1)
for i, row in df.iterrows():
    ...

# ✅ GOOD - one operation over the column
df["total"] = df["qty"] * df["unit_price"]
df["tier"] = np.select([df["qty"] >= 100, df["qty"] >= 10], ["bulk", "volume"], default="unit")
```

```python
# polars
df = df.with_columns(
    total=pl.col("qty") * pl.col("unit_price"),
    tier=pl.when(pl.col("qty") >= 100).then(pl.lit("bulk"))
    .when(pl.col("qty") >= 10).then(pl.lit("volume"))
    .otherwise(pl.lit("unit")),
)
```

## Assign with `.loc`, never through a chain

```python
# ❌ BAD - assigns into a temporary copy; df is unchanged
df[df["status"] == "open"]["priority"] = 1

# ✅ GOOD
df.loc[df["status"] == "open", "priority"] = 1
```

With copy-on-write (the default from pandas 3.0, and opt-in on 2.x with
`pd.options.mode.copy_on_write = True`), the bad form never modifies `df`.

## Joins that cannot duplicate rows silently

A many-to-many join multiplies rows and the totals with them, and nothing raises. Declare the
cardinality you expect, and check the row count.

```python
# ✅ GOOD - raises MergeError if a repair has two price rows
priced = repairs.merge(
    prices, on="part_number", how="left", validate="many_to_one", indicator=True
)
unpriced = priced[priced["_merge"] == "left_only"]
if not unpriced.empty:
    raise ValueError(f"{len(unpriced)} repairs have no price: {unpriced['part_number'].unique()[:10]}")
assert len(priced) == len(repairs)
```

```python
# polars: the same contract
priced = repairs.join(prices, on="part_number", how="left", validate="m:1")
```

## Money

Floats cannot hold most decimal amounts exactly, so totals drift by cents.

| Approach | When |
|---|---|
| Integer minor units (`amount_cents`, `Int64`) | Default for calculation and storage |
| `decimal.Decimal` | Small row counts where exact rounding rules matter more than speed |
| `float` | Never for amounts that are billed or reconciled |

Round once, at the output, with the rounding rule the contract states (for example
`ROUND_HALF_UP`), and write that rule in a comment beside the call.

## Timestamps

- Store and compute in UTC: `pd.to_datetime(col, utc=True)`.
- Convert to a local zone only for display or for a day boundary the business defines, and name
  the zone: `dt.tz_convert("America/Chicago")`.
- A "day" in a report is a business decision. Write down which zone's midnight it uses.

## Notebook code versus module code

A notebook is for running, exploring and showing. Logic that a test must cover lives in a module.

| In the notebook | In a module (`tools/` or `src/`) |
|---|---|
| Parameters, reading inputs, writing outputs | Every transform: dataframe in, dataframe out |
| Calling the transforms in order | Validation and business rules |
| Displays and exploratory cells | Anything another notebook or job reuses |

```python
# transforms.py - pure, typed, testable
def price_repairs(repairs: pd.DataFrame, prices: pd.DataFrame) -> pd.DataFrame:
    ...

# notebook cell
priced = price_repairs(read_repairs(month), read_prices())
write_billing(priced, month)
```

How a Fabric notebook imports the module (a custom library in an environment, or the notebook's
resources folder) is Fabric item configuration, covered by `skills-for-fabric`.

## Testing transforms

Build each input as a small frame in the test, with only the columns the transform reads and with
the rows that decide the behaviour: the boundary, the missing key, the duplicate.

```python
import pandas as pd
import pytest
from pandas.testing import assert_frame_equal

from transforms import price_repairs


def test_price_repairs_multiplies_qty_by_unit_price_in_cents():
    repairs = pd.DataFrame({"part_number": ["0042"], "qty": pd.array([3], dtype="Int64")})
    prices = pd.DataFrame({"part_number": ["0042"], "unit_cents": pd.array([1250], dtype="Int64")})

    result = price_repairs(repairs, prices)

    expected = pd.DataFrame(
        {"part_number": ["0042"], "qty": pd.array([3], dtype="Int64"),
         "unit_cents": pd.array([1250], dtype="Int64"), "total_cents": pd.array([3750], dtype="Int64")}
    )
    assert_frame_equal(result, expected)


def test_price_repairs_refuses_a_part_with_two_prices():
    repairs = pd.DataFrame({"part_number": ["0042"], "qty": pd.array([1], dtype="Int64")})
    prices = pd.DataFrame({"part_number": ["0042", "0042"], "unit_cents": pd.array([1, 2], dtype="Int64")})

    with pytest.raises(pd.errors.MergeError):
        price_repairs(repairs, prices)
```

- `assert_frame_equal` checks values, dtypes, column order and index. Compare whole frames rather
  than single cells, so an extra row or a changed dtype fails the test.
- A test whose expected value comes from running the transform proves only that the transform
  still does what it did. Work the expected value out by hand.
- A job that writes or overwrites a table changes persistent state. `test-filter-development`
  requires its tests to state what must be unchanged afterwards (other months' rows, other
  customers), and to assert it.
- Seed mutants on the business rules (`>=` to `>`, `many_to_one` dropped, a rounding mode
  changed) with `mutation-harness --test-cmd "python -m pytest -q"`.
