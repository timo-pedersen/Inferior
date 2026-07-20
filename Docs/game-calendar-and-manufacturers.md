# Game Calendar and Manufacturers

This document defines the galaxy-wide civil calendar and the deterministic manufacturer 
registry used for containers, modules and other manufactured objects.

The calendar descends from Earth standards established during the human-led Universal Federation 
in the First Aeon. Earth is no longer an active galactic force, but the standard survived because 
standards often outlive the reason for their existence.

---

# Purpose

The game requires stable date and time utilities for:

- manufacturing dates;
- manufacturer active periods;
- contracts and deadlines;
- age and wear calculations;
- historical records;
- save data;
- lore timelines;
- future economic simulation.

The game also requires a deterministic manufacturer registry so physical objects can have:

- manufacturer identity;
- origin system;
- active date range;
- visual branding;
- persistent historical meaning.

---

# Calendar authority

The galaxy uses the inherited Earth civil calendar:

- twelve standard months;
- the same month lengths as the modern Gregorian calendar;
- leap years;
- 24-hour days;
- ordinary hours, minutes and seconds;
- dates directly translatable by month and day.

Leap years remain part of the standard even though almost nobody remembers their astronomical origin.

The civil date is displayed by Aeon and year-within-Aeon.

Example current date (2026.07.19, translated to game date, 4838 years in the future, just a convenience):

```text
E3.326-07-19
```

Meaning:

```text
Third Aeon
Year 326 of the Third Aeon
Month 07
Day 19
```

The exact start and end dates of each Aeon are defined in the lore timeline, see below.

---

# Internal representation

Dates must not be stored as formatted strings.

Use an immutable numeric representation based on elapsed civil days from:

```text
0001-01-01
```

This is the first day of calendar year 1 under the inherited Earth standard.

Suggested conceptual type:

```text
GameDate
    AbsoluteDay
```

Where:

```text
AbsoluteDay = number of days since 0001-01-01
```

Example, 0001-01-01 is AbsoluteDay 0. 
Example, 0001-01-02 is AbsoluteDay 1.

Time of day may use a separate value:

```text
GameTimeOfDay
    TicksSinceMidnight
```

Or a combined value:

```text
GameDateTime
    AbsoluteDay
    TicksSinceMidnight
```

The internal representation should support:

- ordering;
- subtraction;
- adding days, months and years through calendar rules;
- formatting into aeon notation;
- parsing controlled data formats;
- persistence without locale dependence;
- getting weekday from a internal representation.

---

# Gregorian leap-year rules

The inherited calendar uses the Gregorian leap-year rule:

```text
A year is a leap year when:
- old earth year is divisible by 4,
- except years divisible by 100,
- unless also divisible by 400.
```

Examples (fictive earth dates, aeon years will not necessarily be on even 4 years):

- 320: leap year;
- 324: leap year;
- 300: not a leap year;
- 400: leap year.

Leap day remains February 29.

These rules apply to the continuous absolute calendar. Aeon display boundaries do not 
change month lengths or leap-year calculation.

---

# Aeons

An Aeon is a historical display era laid over the continuous civil calendar.

The lore document defines:

- Aeon 1 start date;
- Hyperspace Eclipse 1 start date;
- Aeon 2 start date;
- Hyperspace Eclipse 2 start date;
- Aeon 3 start date;
- current date, roughly.

The calendar utility owns conversion between:

```text
Absolute civil date
<->
Aeon.Year-Month-Day
```

## Timeline from lore

Time is divided into Eras (or Ages) and Eclipses. These are referred to as "Aeons".
There has been three Ages / Eras, and two eclipses. 
Eras are denoted by "E" plus a number.
Eclipses are denoted by "O" plus a number. It is believed that "O" stands for "Oclusion".

No intergalactic time standard existed before E1. 
Humans reached hyper space and became a member of what would become the U.F. in 
the earth year 2814.

U.F became established in earth year 2877, under strong human initiative.

In the earth year 3046 humans reached absolute galactic dominance. 
The year 3047 became year one of the First Aeon.

| Era | Galactic time | Earth date | Major event |
|---|---|---|---|
| BE | BE.1-06-06    | 3046-06-06 | The human domination began | 
| E1 | E1.1-01-01    | 3047-01-01 | Start of human dominated U.F, also called the Human Empire, Human Intergalactic Era etc. | 
|    | E1.2475-08-16 | 5521-08-16 | First HyperSpace Eclipse | 
| O1 | O1.1-01-01    | 5522-01-01 | Official start of First Hyperspace Eclipse era | 
|    | O1.250-05-26  | 5771-05-26 | Hyperspace reinvented, signals end of era. | 
| E2 | E2.1-01-01    | 5772-01-01 | Official first day of new aeon E2, the second age. | 
|    | E2.641-03-03  | 6412-03-03 | Second hyperspace eclipse begins | 
| O2 | O2.1-01-01    | 6413-01-01 | Official start of second Hyperspace Eclipse era. | 
|    | O2.126-11-03  | 6538-11-03 | Hyperspace reinvented | 
| E3 | E3.1-01-01    | 6539-01-01 | Official start of Current Era, E3 | 
|    | E3.326-07-19  | 6864-07-19 | Current date translated to in-game. A simple convention to track time in-game. | 

To refer back to times before the first era, a "BE" is used together with a number counting backwards from E1.
So the year 3046 becomes "BE.1", excluding year zero.
The year 3045 becomes "BE.2".
The year 3044 becomes "BE.3".

The year following BE.1 is E1.1.

For more details about eg leap years, see file Timeline.csv.

## Aeon year numbering

Each Aeon restarts displayed year numbering.

Example:

```text
Absolute date X = E3.326-07-19
```

The year component is calculated relative to the beginning of Aeon 3.

The first calendar year in an Aeon is year 1 (not 0). Year 0 is never used anywhere.

## Civil calendar baseline

AbsoluteDay 0 = 0001-01-01
Weekday of AbsoluteDay 0 = Monday (following ISO 8601)

AddMonths and AddYears need explicit end-of-month behaviour.

Conventional policy:

E3.x-01-31 + 1 month → February's final valid day
Leap-day date + 1 year → February 28 in a non-leap year

## Boundary rule

Aeon boundaries should occur at midnight on an explicit civil date, always considered to be 
first January 1 following the event that caused an aeon to shift.

The underlying civil calendar remains continuous. At an Era boundary, only the displayed Era code and 
year-within-Era change; month, day, weekday and leap-year rules continue from the absolute civil calendar.

---

# Date formatting

Canonical galaxy display:

```text
<Aeon>.<Year>-<Month>-<Day>
```

Like in civil time, "-<Day>" or "-<Month>-<Day>" may be excluded.

Example2:

```text
E3.326-07-19 - A translation of current date (2026-07-19) into game date. Game takes place somewhere in E3.326
O2.54-02-05 - Second hyperspace eclipse, 54'th year, 5'th of february
E2.558-10-30 - Second aeon, year 558, 30'th of october
```

Use zero-padded two-digit month and day.

The year may remain unpadded unless visual style requires a fixed width. Prefer unpadded.

Optional extended time format:

```text
E3.326-07-19 14:32:08
```

Persistence should use numeric fields or a strict machine format, not the presentation string.


---

# Calendar utility responsibilities

Suggested responsibilities:

## GameCalendar

- month lengths;
- leap-year calculation;
- civil-date validation;
- conversion to and from absolute day;
- adding days/months/years;
- day-of-year calculation;
- days-between calculation.

## AeonTimeline

- ordered aeon boundaries;
- conversion from absolute date to aeon date;
- conversion from aeon date to absolute date;
- validation of dates near boundaries;
- lore-facing display names.

## GameDate

- immutable absolute date value;
- comparison;
- arithmetic through `GameCalendar`;
- persistence representation.

## Phase 1 implementation

The implemented date API lives in `Inferior.Core.Time` so gameplay, simulation, and
persistence can share it without depending on rendering or the main game project:

- `GameDate` stores one validated `AbsoluteDay` integer and provides comparison and day arithmetic;
- `CivilDate` carries explicit year, month, and day components;
- `GameCalendar` owns Gregorian conversion, validation, arithmetic, day-of-year, and weekday operations;
- `GalacticEraDate` and `GalacticEraTimeline` own Era conversion, validation, canonical formatting, and strict parsing;
- `GalacticEraTimeline.InitialGameDate` is the fixed `6864-07-19` / `E3.326-07-19` setting anchor;
- JSON persists `GameDate` directly as its numeric absolute-day value.

Manufacturer generation, persistence, and object integration remain unimplemented.

## GameDateTime

- date plus time of day;
- simulation clock integration later;
- contract deadlines;
- manufacturer timestamps where needed.

---

## Aeon Nomenclature

Whenever an aeon / Era / age is referred in UI formally, it will use the term "era".
It is common to refer to older eras as 'aeons', esp eras E1, O1 and E2.
It is common to refer to older eras E1, E2 and E3 as 'ages', ie 'First Age', 'Second Age' and 'Third Age'.

It is commonly believed (although disputed) that "E" stands for "Era" and that "O" stands for "Occlusion". 

# Lore rationale

The inherited standard exists because the First Aeon was dominated by the human-led Universal Federation, UF.

As a result, galaxy-wide standards still use familiar Earth-derived conventions:

- English as a major standard language;
- inherited units and abbreviations;
- 24-hour time;
- twelve-month calendar;
- Gregorian leap years;
- legacy terms whose origin is no longer widely understood.

This is comparable to obsolete historical conventions surviving in present-day units and abbreviations.

The system should present this as an old standard, not as evidence that Earth remains politically central.

---

# Manufacturer registry

The game contains approximately 500 deterministic manufacturer definitions.

Manufacturers may produce:

- cargo containers;
- ship modules;
- engines;
- cockpits;
- panels;
- station equipment;
- consumer and industrial goods later.

The registry is generated from a fixed world seed and remains stable across sessions.

---

# Manufacturer definition

Suggested data:

```text
ManufacturerId
DisplayName
HomeSystemId
FoundedDate
InactiveDate or null
BrandPrimaryColour
BrandSecondaryColour
FontStyleKey
LogoSeed or MarkId
NameGenerationSource
IndustryTags later
QualityClass
Reputation later
```

An inactive manufacturer remains in the registry because historical objects continue to reference it.

Example active ranges:

```text
E3.250 -
E2.420 - E3.125
```

Internally these are full numeric dates, not partial strings.

---

# Manufacturer generation

## Determinism

Generation must be:

- seeded;
- stable;
- independent of traversal order;
- independent of which systems have been visited;
- repeatable across save/load;
- stable under unrelated content additions where possible.

Use stable IDs and per-entity seed derivation rather than consuming one global random sequence in arbitrary order.

Conceptually:

```text
Manufacturer seed
    = hash(world seed, manufacturer index or stable source key)
```

## Name sources

Approximately 50% of manufacturer names should match or directly derive from existing star-system names.

Possible forms:

- exact system name;
- `<System> Industrial`;
- `<System> Fabrication`;
- `<System> Transit Systems`;
- `<System> Container Works`;
- `<System> Engineering`;
- `<System> Cooperative`;
- `<System> Heavy Industries`.

The remaining names may use independent seeded corporate, family, cooperative or institutional patterns.

Examples:

- Ennaor Logistical Corp;
- Ennaor Industrial;
- Nova Anchorage Fabrication;
- Kestrel Transit Systems;
- Varo Cooperative Works.

The final name generator should avoid obvious duplicates and unintentional offensive combinations.

## Home system

Every manufacturer should normally reference a valid star system, even if not named after one.

The home system may influence:

- name;
- brand colours;
- industrial style;
- market availability later;
- shipping patterns later.

## Active period generation

Manufacturers receive:

- founded date;
- inactive date or null.

Generation must respect:

- the known aeon timeline;
- the current game date;
- reasonable operating duration;
- manufacturer age distribution;
- almost no companies were founded during the hyper space eclipse ages ( < 1% );
- historical persistence.

Some should be:

- newly founded, this aeon E3 (50%);
- long-lived (10%);
- recently defunct (10%);
- ancient and historically important (5%);
- active across an aeon boundary (20%).

Exact distribution to be tuned. 
Categories can overlap. 
Values in paranthesis are just ballpark numbers. 

---

# Object-manufacturer validity

A manufactured object references a manufacturer and manufacture date.

The date must satisfy:

```text
ManufactureDate >= Manufacturer.FoundedDate

and

Manufacturer.InactiveDate is null
or
ManufactureDate <= Manufacturer.InactiveDate
```

The date must also not be later than the object's first appearance/current game date.

Old containers may therefore bear marks from manufacturers that have been inactive for centuries.

---

# Branding

A manufacturer definition provides stable visual branding data.

Possible uses:

- exterior text;
- container colours;
- module data plates;
- logos or generated marks;
- fonts/style keys;
- UI manufacturer labels;
- serial-number formatting later.

Branding must be deterministic. The same manufacturer always uses the same visual 
identity unless lore explicitly models historical rebranding later.

Fonts should be referenced by style key, not embedded per manufacturer.

---

# Persistence

Persist dates numerically.

Persist manufacturer references by stable `ManufacturerId`.

Do not persist a full duplicate manufacturer definition on every object.

A save should remain resolvable even when a manufacturer is inactive.

If registry generation changes in a future version, migration must preserve existing manufacturer 
IDs or store enough historical generated data to avoid changing object identity.

---

# Initial implementation phases

## Phase 1: calendar core

- `GameDate` absolute-day representation;
- Gregorian month lengths;
- leap-year rules;
- validation and arithmetic;
- strict persistence format;
- tests against known dates.

## Phase 2: Aeon timeline

- lore-provided boundaries;
- conversion to `Aeon.Year.Month.Day`;
- current date configuration;
- formatting;
- boundary tests.

## Phase 3: manufacturer registry

- deterministic generation of approximately 500 manufacturers;
- stable IDs;
- star-system-derived names for approximately half;
- active periods;
- branding seeds;
- registry tests.

## Phase 4: object integration

- container manufacturer and manufacture date;
- exterior display formatting;
- active-range validation;
- historical/defunct manufacturers;
- UI inspection.

## Phase 5: time-dependent gameplay

- contract deadlines;
- wear by age;
- market history;
- maintenance schedules;
- production and shipping records later.

---

# Tests

Calendar tests should cover:

- month lengths;
- normal and leap February;
- divisible-by-100 and divisible-by-400 rules;
- conversion to/from absolute day;
- adding/subtracting across months and years;
- aeon boundary conversion;
- round-trip formatting/parsing where parsing is supported;
- invalid dates;
- current date mapping once lore is supplied.

Manufacturer tests should cover:

- same world seed produces identical registry;
- generation is independent of access order;
- stable IDs are unique;
- target manufacturer count is reached;
- approximately half of names derive from star systems within a defined tolerance;
- active ranges are valid;
- object manufacture dates fall inside manufacturer activity;
- inactive manufacturers remain resolvable;
- branding data is stable.

---

# Future extensions

- historical rebranding;
- mergers and successor companies;
- manufacturer reputation;
- regional product distribution;
- counterfeit branding;
- serial-number schemes;
- production batches;
- warranty and maintenance records;
- local calendars retained for cultural use;
- time zones on planets and stations;
- relativistic or simulation-time concerns if ever needed.

---

# Design invariants

1. The galaxy-wide civil calendar follows inherited Earth month lengths and leap-year rules.
2. Leap years remain for historical-standard reasons.
3. Dates are stored numerically, not as display strings.
4. Absolute time is continuous across aeon boundaries.
5. Aeons are historical display eras over the continuous calendar.
6. Exact aeon boundaries come from the lore timeline.
7. Manufacturer generation is deterministic and order-independent.
8. Manufacturer IDs remain stable.
9. Defunct manufacturers remain valid references.
10. Manufactured-object dates must fall inside the manufacturer's active period.
11. Branding is derived from manufacturer identity, not generated anew per object.

---

===== End of main document =========================================

# Appendix A - Lore data required

Before aeon conversion is final, the lore document must provide:

- start date of Aeon 1;
- end/start boundary between Aeons 1 and 2;
- end/start boundary between Aeons 2 and 3;
- absolute civil date corresponding to current date `E3.326-07-19`;
- official aeon names, if any;
