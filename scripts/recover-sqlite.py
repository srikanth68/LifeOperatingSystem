#!/usr/bin/env python3
"""Rebuild a SQLite database whose first page (the header) has been destroyed.

    python3 scripts/recover-sqlite.py DAMAGED.db REBUILT.db

Why this exists
---------------
Page 1 of a SQLite file holds two things: the 100-byte header, and the root of the table that
lists every other table (sqlite_master). When page 1 is zeroed SQLite answers "file is not a
database" and will not open the file at all, even though every OTHER page, which is where the
rows are, is untouched.

A database with more than a handful of tables does not keep its schema rows ON page 1: page 1
only points at the leaf pages that hold them, and those leaves are ordinary pages elsewhere in
the file. So the schema survives, with each table's root page number in it. This script

  1. finds those leaf pages by shape and reads the CREATE statements out of them,
  2. walks each table's page tree from its recorded root and reads the rows,
  3. writes a brand new, healthy database with the same tables, indexes and rows.

It reads the damaged file and never writes to it. It prints COUNTS ONLY (table names, row
counts, page numbers): no row value is ever printed, so its output is safe to paste somewhere.

What it cannot do
-----------------
  - Bring back rows that lived on pages that were also destroyed. It lists every page that is
    entirely zero so that loss is visible rather than silent.
  - Know which rows were deleted. SQLite leaves a deleted row's bytes on a free page until the
    page is reused; those pages are NOT reachable from any table's root, so they are not copied,
    and the report counts them so a surprise is not a mystery.
  - Recover a schema that sat on page 1 itself (a tiny database). It says so and stops.

Standard library only.
"""

import argparse
import os
import struct
import sqlite3
import sys

# The app's own table and index definitions, as the Vitara code creates them (28 tables). Used ONLY to
# finish a rebuild when some of the schema's own pages were also destroyed: a table the damaged file no
# longer describes is created from here, and its rows are then matched to it by shape. It contains no
# data. If the app's tables change, regenerate this from a healthy database.
REFERENCE_SCHEMA = r'''
CREATE TABLE "Activity" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Activity" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "Score" INTEGER NULL,
    "Steps" INTEGER NOT NULL,
    "ActiveCalories" INTEGER NOT NULL,
    "TotalCalories" INTEGER NOT NULL,
    "EquivalentWalkingDistance" INTEGER NOT NULL,
    "HighActivityMinutes" INTEGER NOT NULL,
    "MediumActivityMinutes" INTEGER NOT NULL,
    "LowActivityMinutes" INTEGER NOT NULL,
    "SedentaryMinutes" INTEGER NOT NULL,
    "RestMinutes" INTEGER NOT NULL,
    "AvgMet" REAL NULL
);
CREATE TABLE "Baselines" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Baselines" PRIMARY KEY AUTOINCREMENT,
    "Metric" TEXT NOT NULL,
    "BaselineSignature" TEXT NOT NULL,
    "ComputedOnLocal" TEXT NOT NULL,
    "WindowDays" INTEGER NOT NULL,
    "Mean" REAL NOT NULL,
    "StdDev" REAL NOT NULL,
    "Median" REAL NOT NULL,
    "P25" REAL NOT NULL,
    "P75" REAL NOT NULL,
    "N" INTEGER NOT NULL,
    "IsValid" INTEGER NOT NULL,
    "ExclusionsJson" TEXT NULL,
    "RegimeStartLocal" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "CardiovascularAge" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CardiovascularAge" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "VascularAge" REAL NULL
);
CREATE TABLE "Correlations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Correlations" PRIMARY KEY AUTOINCREMENT,
    "Driver" TEXT NOT NULL,
    "Outcome" TEXT NOT NULL,
    "LagDays" INTEGER NOT NULL,
    "Rho" REAL NOT NULL,
    "N" INTEGER NOT NULL,
    "PValue" REAL NOT NULL,
    "WindowDays" INTEGER NOT NULL,
    "ComputedOnLocal" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "DerivedMetrics" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_DerivedMetrics" PRIMARY KEY AUTOINCREMENT,
    "Metric" TEXT NOT NULL,
    "ObservedDateLocal" TEXT NOT NULL,
    "Value" REAL NOT NULL,
    "InputsJson" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "Devices" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Devices" PRIMARY KEY AUTOINCREMENT,
    "Kind" TEXT NOT NULL,
    "Model" TEXT NOT NULL,
    "Firmware" TEXT NULL,
    "ActiveFromLocal" TEXT NOT NULL,
    "ActiveToLocal" TEXT NULL,
    "Notes" TEXT NULL
);
CREATE TABLE "ExcludedPeriods" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ExcludedPeriods" PRIMARY KEY,
    "StartLocal" TEXT NOT NULL,
    "EndLocal" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    "ExcludeFromBaseline" INTEGER NOT NULL,
    "Notes" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "Findings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Findings" PRIMARY KEY AUTOINCREMENT,
    "Key" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "Metric" TEXT NOT NULL,
    "Direction" TEXT NOT NULL,
    "Severity" TEXT NOT NULL,
    "Confidence" REAL NULL,
    "Summary" TEXT NOT NULL,
    "EvidenceJson" TEXT NULL,
    "FirstDetectedLocal" TEXT NOT NULL,
    "LastDetectedLocal" TEXT NOT NULL,
    "ResolvedLocal" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "HeartRate" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_HeartRate" PRIMARY KEY AUTOINCREMENT,
    "Timestamp" TEXT NOT NULL,
    "Bpm" INTEGER NOT NULL,
    "Source" TEXT NULL
);
CREATE TABLE "Interventions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Interventions" PRIMARY KEY,
    "Kind" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Dose" TEXT NULL,
    "StartedOnLocal" TEXT NOT NULL,
    "EndedOnLocal" TEXT NULL,
    "TargetMetric" TEXT NULL,
    "Notes" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "LabPanels" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_LabPanels" PRIMARY KEY,
    "DrawnOnLocal" TEXT NOT NULL,
    "LabName" TEXT NULL,
    "Notes" TEXT NULL,
    "Fasting" INTEGER NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "Meals" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Meals" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "MealType" TEXT NOT NULL,
    "FoodName" TEXT NOT NULL,
    "FdcId" INTEGER NULL,
    "ServingQty" REAL NOT NULL,
    "ServingUnit" TEXT NULL,
    "Calories" REAL NOT NULL,
    "Protein" REAL NOT NULL,
    "Carbs" REAL NOT NULL,
    "Fat" REAL NOT NULL,
    "Fiber" REAL NULL,
    "Source" TEXT NOT NULL,
    "LoggedAt" TEXT NOT NULL
);
CREATE TABLE "Measurements" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Measurements" PRIMARY KEY,
    "Metric" TEXT NOT NULL,
    "Value" REAL NOT NULL,
    "Unit" TEXT NOT NULL,
    "ObservedAtLocal" TEXT NOT NULL,
    "Day" TEXT NOT NULL,
    "Tier" TEXT NOT NULL,
    "Source" TEXT NOT NULL,
    "ContextJson" TEXT NULL,
    "Note" TEXT NULL,
    "LabPanelId" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "Nutrition" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Nutrition" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "Calories" INTEGER NOT NULL,
    "Protein" REAL NOT NULL,
    "Carbs" REAL NOT NULL,
    "Fat" REAL NOT NULL,
    "Fiber" REAL NULL,
    "Sugar" REAL NULL,
    "Sodium" REAL NULL,
    "CalorieGoal" INTEGER NULL,
    "ProteinGoal" REAL NULL,
    "CarbGoal" REAL NULL,
    "FatGoal" REAL NULL,
    "MealsJson" TEXT NULL
);
CREATE TABLE "Observations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Observations" PRIMARY KEY AUTOINCREMENT,
    "Metric" TEXT NOT NULL,
    "Value" REAL NOT NULL,
    "Unit" TEXT NOT NULL,
    "ObservedAtLocal" TEXT NOT NULL,
    "ObservedDateLocal" TEXT NOT NULL,
    "Tier" TEXT NOT NULL,
    "Source" TEXT NOT NULL,
    "SourceRecordId" TEXT NULL,
    "DeviceId" INTEGER NULL,
    "LabPanelId" TEXT NULL,
    "ContextJson" TEXT NULL,
    "BaselineSignature" TEXT NOT NULL,
    "EligibleForBaseline" INTEGER NOT NULL,
    "ValueOriginal" REAL NULL,
    "UnitOriginal" TEXT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "Profiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Profiles" PRIMARY KEY,
    "Name" TEXT NULL,
    "DateOfBirth" TEXT NULL,
    "Age" INTEGER NULL,
    "Weight" REAL NULL,
    "Height" REAL NULL,
    "BiologicalSex" TEXT NULL,
    "Email" TEXT NULL,
    "LockedFields" TEXT NULL,
    "UpdatedAt" TEXT NOT NULL
);
CREATE TABLE "Readiness" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Readiness" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "Score" INTEGER NULL,
    "TemperatureContributor" INTEGER NULL,
    "HrvBalance" INTEGER NULL,
    "RecoveryIndex" INTEGER NULL,
    "RestingHrContributor" INTEGER NULL,
    "ActivityBalance" INTEGER NULL,
    "SleepBalance" INTEGER NULL,
    "Level" TEXT NULL
);
CREATE TABLE "ReferenceRanges" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ReferenceRanges" PRIMARY KEY AUTOINCREMENT,
    "Metric" TEXT NOT NULL,
    "Low" REAL NULL,
    "High" REAL NULL,
    "Unit" TEXT NOT NULL,
    "Sex" TEXT NULL,
    "AgeMin" INTEGER NULL,
    "AgeMax" INTEGER NULL,
    "LabName" TEXT NULL,
    "Notes" TEXT NULL
);
CREATE TABLE "Resilience" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Resilience" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "Level" TEXT NULL,
    "SleepRecovery" INTEGER NULL,
    "DaytimeRecovery" INTEGER NULL,
    "Stress" INTEGER NULL
);
CREATE TABLE "Sleep" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Sleep" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "BedtimeStart" TEXT NOT NULL,
    "BedtimeEnd" TEXT NOT NULL,
    "TotalSleepMinutes" INTEGER NOT NULL,
    "RemMinutes" INTEGER NOT NULL,
    "DeepMinutes" INTEGER NOT NULL,
    "LightMinutes" INTEGER NOT NULL,
    "AwakeMinutes" INTEGER NOT NULL,
    "Score" INTEGER NULL,
    "AvgHrv" REAL NULL,
    "LowestHr" REAL NULL,
    "AvgBreathingRate" REAL NULL,
    "AvgSpo2" REAL NULL,
    "SkinTempDeviation" REAL NULL
);
CREATE TABLE "Spo2" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Spo2" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "Spo2Average" REAL NULL,
    "BreathingDisturbanceIndex" REAL NULL
);
CREATE TABLE "Stress" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Stress" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "StressHighSeconds" INTEGER NULL,
    "RecoveryHighSeconds" INTEGER NULL,
    "DaySummary" TEXT NULL
);
CREATE TABLE "SyncStates" (
    "Source" TEXT NOT NULL CONSTRAINT "PK_SyncStates" PRIMARY KEY,
    "LastSyncedAt" TEXT NULL,
    "LastAttemptAt" TEXT NULL,
    "LastError" TEXT NULL
);
CREATE TABLE "Tokens" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Tokens" PRIMARY KEY AUTOINCREMENT,
    "AccessToken" TEXT NOT NULL,
    "RefreshToken" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "LinkedAt" TEXT NOT NULL,
    "LastSyncedAt" TEXT NULL,
    "LastSyncAttemptAt" TEXT NULL,
    "LastSyncError" TEXT NULL
);
CREATE TABLE "TravelPeriods" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_TravelPeriods" PRIMARY KEY,
    "StartLocal" TEXT NOT NULL,
    "EndLocal" TEXT NOT NULL,
    "HomeTz" TEXT NOT NULL,
    "AwayTz" TEXT NOT NULL,
    "Notes" TEXT NULL
);
CREATE TABLE "Vo2Max" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Vo2Max" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "Vo2Max" REAL NULL
);
CREATE TABLE "WeighIns" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WeighIns" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "WeightKg" REAL NOT NULL,
    "CreatedAt" TEXT NOT NULL
);
CREATE TABLE "Workouts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Workouts" PRIMARY KEY,
    "Day" TEXT NOT NULL,
    "Activity" TEXT NOT NULL,
    "StartTime" TEXT NULL,
    "EndTime" TEXT NULL,
    "Calories" INTEGER NULL,
    "Distance" INTEGER NULL,
    "Intensity" TEXT NULL,
    "Label" TEXT NULL,
    "Source" TEXT NULL
);
CREATE INDEX IX_Baselines_Lookup
    ON Baselines (Metric, BaselineSignature, ComputedOnLocal);
CREATE INDEX "IX_Baselines_Metric_BaselineSignature_ComputedOnLocal" ON "Baselines" ("Metric", "BaselineSignature", "ComputedOnLocal");
CREATE INDEX IX_CardiovascularAge_Day ON CardiovascularAge(Day);
CREATE UNIQUE INDEX IX_Correlations_Identity
    ON Correlations (Driver, Outcome, LagDays, ComputedOnLocal);
CREATE UNIQUE INDEX IX_DerivedMetrics_Identity
    ON DerivedMetrics (Metric, ObservedDateLocal);
CREATE UNIQUE INDEX "IX_DerivedMetrics_Metric_ObservedDateLocal" ON "DerivedMetrics" ("Metric", "ObservedDateLocal");
CREATE INDEX "IX_Findings_Key" ON "Findings" ("Key");
CREATE INDEX IX_Findings_Open ON Findings (ResolvedLocal);
CREATE INDEX "IX_Findings_ResolvedLocal" ON "Findings" ("ResolvedLocal");
CREATE INDEX "IX_HeartRate_Timestamp" ON "HeartRate" ("Timestamp");
CREATE INDEX "IX_Meals_Day" ON "Meals" ("Day");
CREATE INDEX "IX_Meals_MealType" ON "Meals" ("MealType");
CREATE UNIQUE INDEX IX_Measurements_Identity
    ON Measurements (Metric, ObservedAtLocal, Source);
CREATE INDEX IX_Measurements_Window
    ON Measurements (Metric, Day);
CREATE INDEX IX_Nutrition_Day ON Nutrition(Day);
CREATE UNIQUE INDEX IX_Observations_Identity
    ON Observations (Metric, ObservedAtLocal, Source);
CREATE INDEX "IX_Observations_Metric_BaselineSignature_ObservedDateLocal" ON "Observations" ("Metric", "BaselineSignature", "ObservedDateLocal");
CREATE UNIQUE INDEX "IX_Observations_Metric_ObservedAtLocal_Source" ON "Observations" ("Metric", "ObservedAtLocal", "Source");
CREATE INDEX IX_Observations_Window
    ON Observations (Metric, BaselineSignature, ObservedDateLocal);
CREATE INDEX "IX_ReferenceRanges_Metric" ON "ReferenceRanges" ("Metric");
CREATE INDEX IX_Resilience_Day ON Resilience(Day);
CREATE INDEX IX_Spo2_Day ON Spo2(Day);
CREATE INDEX IX_Stress_Day ON Stress(Day);
CREATE INDEX IX_Vo2Max_Day ON Vo2Max(Day);
CREATE INDEX IX_WeighIns_Day ON WeighIns(Day);
CREATE INDEX IX_Workouts_Day ON Workouts(Day);
'''

MAGIC = b'SQLite format 3\x00'
LEAF, INTERIOR = 0x0D, 0x05            # table b-tree pages (index pages are 0x0A / 0x02)


def varint(buf, pos):
    v = 0
    for i in range(8):
        b = buf[pos + i]
        v = (v << 7) | (b & 0x7F)
        if not b & 0x80:
            return v, pos + i + 1
    return (v << 8) | buf[pos + 8], pos + 9


def decode_record(payload):
    """The columns of one row, or ValueError if the bytes are not a record."""
    header_len, p = varint(payload, 0)
    if header_len > len(payload):
        raise ValueError('header past end')
    kinds = []
    while p < header_len:
        t, p = varint(payload, p)
        kinds.append(t)
    out, pos = [], header_len
    for t in kinds:
        if t == 0:
            out.append(None)
        elif 1 <= t <= 6:
            n = (1, 2, 3, 4, 6, 8)[t - 1]
            if pos + n > len(payload):
                raise ValueError('short')
            out.append(int.from_bytes(payload[pos:pos + n], 'big', signed=True))
            pos += n
        elif t == 7:
            if pos + 8 > len(payload):
                raise ValueError('short')
            out.append(struct.unpack('>d', payload[pos:pos + 8])[0])
            pos += 8
        elif t == 8:
            out.append(0)
        elif t == 9:
            out.append(1)
        elif t >= 12:
            n = (t - 12) // 2 if t % 2 == 0 else (t - 13) // 2
            if pos + n > len(payload):
                raise ValueError('short')
            raw = payload[pos:pos + n]
            pos += n
            out.append(bytes(raw) if t % 2 == 0 else raw.decode('utf-8', 'replace'))
        else:
            raise ValueError('reserved serial type')
    return out


class Db:
    def __init__(self, path, page_size=None):
        with open(path, 'rb') as f:
            self.data = f.read()
        head = self.data[:100]
        if head[:16] == MAGIC:
            ps = struct.unpack('>H', head[16:18])[0]
            self.page_size = 65536 if ps == 1 else ps
            self.reserved = head[20]
            self.header_ok = True
        else:
            self.page_size = page_size or 4096
            self.reserved = 0
            self.header_ok = False
        if page_size:
            self.page_size = page_size
        if len(self.data) % self.page_size:
            raise SystemExit(f'File size {len(self.data)} is not a whole number of {self.page_size}-byte pages; '
                             f'pass --page-size (usually 4096).')
        self.pages = len(self.data) // self.page_size
        self.usable = self.page_size - self.reserved

    def page(self, n):
        o = (n - 1) * self.page_size
        return self.data[o:o + self.page_size]

    def is_zero(self, n):
        return not any(self.page(n))

    def kind(self, n):
        """LEAF / INTERIOR if page n is structurally a table b-tree page, else None."""
        if n < 2 or n > self.pages:
            return None
        pg = self.page(n)
        t = pg[0]
        if t not in (LEAF, INTERIOR):
            return None
        hdr = 8 if t == LEAF else 12
        cells = struct.unpack('>H', pg[3:5])[0]
        content = struct.unpack('>H', pg[5:7])[0] or 65536
        if cells == 0 and t == INTERIOR:
            return None
        if hdr + 2 * cells > self.usable or content > self.usable or content < hdr + 2 * cells:
            return None
        for i in range(cells):
            ptr = struct.unpack('>H', pg[hdr + 2 * i:hdr + 2 * i + 2])[0]
            if ptr < hdr + 2 * cells or ptr >= self.usable:
                return None
        return t

    def leaf_rows(self, n):
        """(rowid, columns) for every readable row on leaf page n; unreadable ones are counted."""
        pg = self.page(n)
        cells = struct.unpack('>H', pg[3:5])[0]
        U = self.usable
        max_local = U - 35
        min_local = ((U - 12) * 32 // 255) - 23
        rows, bad = [], 0
        for i in range(cells):
            try:
                pos = struct.unpack('>H', pg[8 + 2 * i:10 + 2 * i])[0]
                plen, pos = varint(pg, pos)
                rowid, pos = varint(pg, pos)
                if plen <= max_local:
                    local = plen
                else:
                    local = min_local + ((plen - min_local) % (U - 4))
                    if local > max_local:
                        local = min_local
                payload = bytes(pg[pos:pos + local])
                if plen > local:
                    nxt = struct.unpack('>I', pg[pos + local:pos + local + 4])[0]
                    left = plen - local
                    seen = set()
                    while left > 0 and nxt and nxt not in seen and 1 <= nxt <= self.pages:
                        seen.add(nxt)
                        op = self.page(nxt)
                        nxt = struct.unpack('>I', op[:4])[0]
                        chunk = op[4:4 + min(left, U - 4)]
                        payload += chunk
                        left -= len(chunk)
                    if left > 0:
                        raise ValueError('overflow chain broken')
                rows.append((rowid, decode_record(payload)))
            except Exception:
                bad += 1
        return rows, bad

    def children(self, n):
        pg = self.page(n)
        cells = struct.unpack('>H', pg[3:5])[0]
        out = []
        for i in range(cells):
            ptr = struct.unpack('>H', pg[12 + 2 * i:14 + 2 * i])[0]
            out.append(struct.unpack('>I', pg[ptr:ptr + 4])[0])
        out.append(struct.unpack('>I', pg[8:12])[0])
        return out

    def walk(self, root):
        """Every row reachable from `root`, as (rows, unreadable_rows, bad_pages)."""
        rows, bad_rows, bad_pages = [], 0, 0
        stack, seen = [root], set()
        while stack:
            n = stack.pop()
            if n in seen:
                continue
            seen.add(n)
            k = self.kind(n)
            if k is None:
                bad_pages += 1
                continue
            if k == LEAF:
                r, b = self.leaf_rows(n)
                rows.extend(r)
                bad_rows += b
            else:
                stack.extend(reversed(self.children(n)))
        return rows, bad_rows, bad_pages, seen


def find_schema(db):
    """The CREATE statements, recovered from schema-table leaves found by shape."""
    best = {}
    for n in range(2, db.pages + 1):
        if db.kind(n) != LEAF:
            continue
        rows, _ = db.leaf_rows(n)
        for _rowid, v in rows:
            if (len(v) == 5 and v[0] in ('table', 'index', 'view', 'trigger') and isinstance(v[1], str)
                    and isinstance(v[2], str) and isinstance(v[3], int) and (v[4] is None or isinstance(v[4], str))):
                key = (v[0], v[1])
                score = (1 if (v[0] != 'table' or db.kind(v[3]) is not None) else 0, len(v[4] or ''))
                if key not in best or score > best[key][0]:
                    best[key] = (score, v)
    return [v for _s, v in best.values()]


def affinity(decl):
    d = (decl or '').upper()
    if 'INT' in d or d in ('BOOLEAN', 'BOOL'):
        return 'I'
    if any(x in d for x in ('REAL', 'FLOA', 'DOUB')):
        return 'R'
    if 'BLOB' in d:
        return 'B'
    if any(x in d for x in ('CHAR', 'TEXT', 'CLOB')) or d == '':
        return 'T'
    return 'N'


def fits(v, aff):
    if v is None:
        return True
    if aff == 'I':
        return isinstance(v, int)
    if aff == 'R':
        return isinstance(v, (int, float))
    if aff == 'T':
        return isinstance(v, str)
    if aff == 'B':
        return isinstance(v, (bytes, str))
    return isinstance(v, (int, float))


def orphan_roots(db, claimed):
    """Table pages that no table's tree reaches and no other leftover page points down to."""
    pages = [n for n in range(2, db.pages + 1) if n not in claimed and db.kind(n) is not None]
    present = set(pages)
    below = set()
    for n in pages:
        if db.kind(n) == INTERIOR:
            below.update(c for c in db.children(n) if c in present)
    return [n for n in pages if n not in below]


def shape_score(rows, cols):
    """How well a set of rows fits a table's columns: (share that fit, share exactly as wide, non-null cells)."""
    n = len(cols)
    if not rows:
        return (0.0, 0.0, 0.0)
    good = exact = nonnull = 0
    for _rowid, vals in rows:
        # A row written before columns were added is shorter than the table; never longer.
        if len(vals) > n or len(vals) < max(1, n - 4):
            continue
        if all(fits(v, cols[i][0]) for i, v in enumerate(vals)):
            good += 1
            exact += (len(vals) == n)
            nonnull += sum(v is not None for v in vals)
    return (good / len(rows), exact / len(rows), nonnull / len(rows))


# Columns added to a table AFTER it was created are appended at the END of an existing database, but a
# fresh database lists them in the model's order. The rows of a database that is older than the
# columns therefore have a different shape than the reference, so those older orders are listed here.
# Taken from the app's history: UserProfile began as Id, Age, Weight, Height, BiologicalSex, Email,
# UpdatedAt, and Name, DateOfBirth and LockedFields were added afterwards, in that order.
LEGACY_ORDERS = {
    'Profiles': ['Id', 'Age', 'Weight', 'Height', 'BiologicalSex', 'Email', 'UpdatedAt',
                 'Name', 'DateOfBirth', 'LockedFields'],
}


def tree_shape(rows):
    """The structure of a set of rows with no values in it: widths and the kind of thing in each column."""
    widths = {}
    for _r, v in rows:
        widths[len(v)] = widths.get(len(v), 0) + 1
    top = max(widths, key=widths.get)
    sig = []
    for i in range(top):
        kind = 'n'
        for _r, v in rows:
            if len(v) > i and v[i] is not None:
                x = v[i]
                kind = 'I' if isinstance(x, int) else 'R' if isinstance(x, float) else 'T' if isinstance(x, str) else 'B'
                break
        sig.append(kind)
    return ''.join(sig), widths


def complete_from_reference(db, out, recovered, claimed, problems):
    """Recreate tables the damaged schema no longer describes, and match leftover page trees to them.

    A table whose CREATE row sat on a destroyed schema page is gone from the schema but not from the
    file: its rows are still on their own pages, which nothing points at any more. Those pages are
    matched to the table they fit by SHAPE (column count and the kind of value in each column), which
    is distinctive for the app's tables. Only tables the file no longer describes are candidates, so
    leftover pages from deleted rows of tables that DO exist are never copied back in.
    """
    statements = [x.strip() for x in REFERENCE_SCHEMA.split(';\n') if x.strip()]
    ref = {}
    for st in statements:
        if st.upper().startswith('CREATE TABLE'):
            name = st.split('(', 1)[0].replace('CREATE TABLE', '').replace('"', '').replace('`', '').strip()
            ref[name] = st
    missing = sorted(n for n in ref if n not in recovered)
    if not missing:
        return [], None, set()

    for name in missing:
        try:
            out.execute(ref[name])
        except sqlite3.Error as e:
            problems.append(f'could not create {name} from the built-in schema: {e}')
    out.commit()

    # Each missing table may be recognised in more than one column order (see LEGACY_ORDERS).
    shapes = {}
    for name in missing:
        info = out.execute(f'PRAGMA table_info("{name}")').fetchall()
        if not info:
            continue
        pk = [c for c in info if c[5]]
        pk_name = pk[0][1] if (len(pk) == 1 and (pk[0][2] or '').upper() == 'INTEGER') else None
        by_name = {c[1]: affinity(c[2]) for c in info}
        orders = [[c[1] for c in info]]
        legacy = LEGACY_ORDERS.get(name)
        if legacy and set(legacy) <= set(by_name) and legacy != orders[0]:
            orders.append(legacy)
        shapes[name] = [([(by_name[n], n) for n in order], order.index(pk_name) if pk_name in order else None)
                        for order in orders]

    trees = []
    for root in orphan_roots(db, claimed):
        rows, bad_rows, _bp, seen = db.walk(root)
        if rows:
            trees.append((root, rows, bad_rows, seen))
    trees.sort(key=lambda t: -len(t[1]))

    taken = {n: [] for n in shapes}
    unmatched = []
    ambiguous = 0
    used = set()
    for root, rows, _bad, seen_pages in trees:
        scored = sorted(((shape_score(rows, cols), n, vi)
                         for n, vs in shapes.items() for vi, (cols, _a) in enumerate(vs)), reverse=True)
        best, name, vi = scored[0]
        if best[0] < 0.9:
            unmatched.append((root, rows))
            continue
        runner = next((x for x in scored[1:] if x[1] != name), None)
        if runner and runner[0][:2] == best[:2] and runner[0][2] >= best[2] * 0.95:
            ambiguous += 1
        taken[name].append((vi, rows))
        used |= seen_pages

    report = []
    for name, variants in shapes.items():
        inserted = seen_rows = 0
        for vi, rows in taken[name]:
            cols, alias = variants[vi]
            names = [c[1] for c in cols]
            for rowid, vals in rows:
                vals = list(vals)
                if alias is not None:
                    while len(vals) <= alias:
                        vals.append(None)
                    vals[alias] = rowid
                vals = vals[:len(names)]
                seen_rows += 1
                cur = out.execute(
                    f'INSERT OR IGNORE INTO "{name}" ({",".join(chr(34) + n + chr(34) for n in names[:len(vals)])}) '
                    f'VALUES ({",".join("?" * len(vals))})', vals)
                inserted += cur.rowcount
        note = f'rebuilt from {len(taken[name])} leftover page trees' if taken[name] else 'no leftover rows matched'
        if any(vi > 0 for vi, _r in taken[name]):
            note += ' (older column order)'
        if seen_rows != inserted:
            note += f'; {seen_rows - inserted} duplicate keys skipped'
        report.append((name, inserted, seen_rows, note))
    out.commit()

    # What could not be placed, described by structure only so it is safe to read aloud or paste.
    leftovers = [(root, len(rows), *tree_shape(rows)) for root, rows in unmatched]
    return report, (unmatched, ambiguous, leftovers), used


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('damaged')
    ap.add_argument('rebuilt')
    ap.add_argument('--page-size', type=int, default=None, help='only if the header is gone AND it is not 4096')
    a = ap.parse_args()

    if os.path.exists(a.rebuilt):
        raise SystemExit(f'{a.rebuilt} already exists; refusing to overwrite it.')

    db = Db(a.damaged, a.page_size)
    print(f'{a.damaged}: {db.pages} pages of {db.page_size} bytes; '
          f'header {"intact" if db.header_ok else "MISSING (page size assumed)"}')

    zero = [n for n in range(1, db.pages + 1) if db.is_zero(n)]
    print(f'pages entirely zero: {len(zero)}' + (f'  {zero[:40]}{" ..." if len(zero) > 40 else ""}' if zero else ''))

    schema = find_schema(db)
    tables = [s for s in schema if s[0] == 'table' and not s[1].startswith('sqlite_') and s[4]]
    if not tables:
        # Every page that described the schema is gone. The built-in reference can still stand in for
        # it (the rows are matched to tables by shape), so carry on rather than give up, but say so:
        # this is the least certain way to rebuild and its result deserves a closer look.
        print('WARNING: no schema rows survive anywhere in this file. Every table will be rebuilt from the '
              'built-in reference by matching leftover pages to it by shape. Check the row counts below '
              'against what you know before trusting it.')
    print(f'schema recovered: {len(tables)} tables, '
          f'{sum(1 for s in schema if s[0] == "index" and s[4])} indexes, '
          f'{sum(1 for s in schema if s[0] in ("view", "trigger"))} views/triggers')

    out = sqlite3.connect(a.rebuilt)
    out.execute('PRAGMA journal_mode=DELETE')
    problems = []

    for _t, name, _tbl, _root, sql in sorted(tables, key=lambda s: s[3]):
        try:
            out.execute(sql)
        except sqlite3.Error as e:
            problems.append(f'could not create table {name}: {e}')
    out.commit()

    claimed, report = set(), []
    for _t, name, _tbl, root, sql in sorted(tables, key=lambda s: s[1]):
        if db.kind(root) is None:
            report.append((name, 0, 0, f'ROOT PAGE {root} IS NOT A TABLE PAGE: rows unreachable'))
            continue
        rows, bad_rows, bad_pages, seen = db.walk(root)
        claimed |= seen
        try:
            cols = out.execute(f'PRAGMA table_info("{name}")').fetchall()
        except sqlite3.Error:
            continue
        pk = [c for c in cols if c[5]]
        alias = pk[0][0] if (len(pk) == 1 and (pk[0][2] or '').upper() == 'INTEGER') else None
        names = [c[1] for c in cols]
        inserted = extra = 0
        before = out.execute(f'SELECT count(*) FROM "{name}"').fetchone()[0]
        for rowid, vals in rows:
            vals = list(vals)
            if alias is not None:
                while len(vals) <= alias:
                    vals.append(None)
                vals[alias] = rowid
            if len(vals) > len(names):
                extra += 1
                vals = vals[:len(names)]
            # Only the columns the row actually has: a row written before a column was added
            # gets that column's DEFAULT from SQLite, which is exactly what it had in life.
            cur = out.execute(
                f'INSERT OR IGNORE INTO "{name}" ({",".join(chr(34) + n + chr(34) for n in names[:len(vals)])}) '
                f'VALUES ({",".join("?" * len(vals))})', vals)
            inserted += cur.rowcount
        after = out.execute(f'SELECT count(*) FROM "{name}"').fetchone()[0]
        if 'AUTOINCREMENT' in sql.upper() and alias is not None:
            # The counter is the highest id EVER issued, which can be above the highest id that
            # survives: delete the newest row and the counter still remembers it. Restarting from
            # the surviving maximum would hand a deleted row's id to a new one. The counter lives
            # in sqlite_sequence's own pages, so it is read back below; this is only the floor.
            mx = out.execute(f'SELECT max("{names[alias]}") FROM "{name}"').fetchone()[0]
            if mx is not None:
                out.execute('INSERT OR REPLACE INTO sqlite_sequence(name, seq) VALUES (?, ?)', (name, mx))
        note = []
        if bad_rows:
            note.append(f'{bad_rows} unreadable rows')
        if bad_pages:
            note.append(f'{bad_pages} unreadable pages')
        if len(rows) != after - before:
            note.append(f'{len(rows) - (after - before)} rows skipped as duplicates/conflicts')
        if extra:
            note.append(f'{extra} rows had more fields than the schema')
        report.append((name, after, len(rows), '; '.join(note)))
    out.commit()

    seq_root = next((s[3] for s in schema if s[0] == 'table' and s[1] == 'sqlite_sequence'), None)
    if seq_root is not None and db.kind(seq_root) is not None:
        for _rowid, v in db.walk(seq_root)[0]:
            if len(v) >= 2 and isinstance(v[0], str) and isinstance(v[1], int):
                have = out.execute('SELECT seq FROM sqlite_sequence WHERE name=?', (v[0],)).fetchone()
                if have is not None and v[1] > have[0]:
                    out.execute('UPDATE sqlite_sequence SET seq=? WHERE name=?', (v[1], v[0]))
        out.commit()

    completed, extra, used = complete_from_reference(db, out, {t[1] for t in tables}, claimed, problems)
    report.extend(completed)
    claimed |= used            # these pages were put back, so they are not "left behind" any more
    if completed:
        print(f'{len(completed)} tables were missing from the damaged schema and were created from the built-in '
              f'reference: {", ".join(c[0] for c in completed)}')

    for typ in ('index', 'view', 'trigger'):
        for _t, name, _tbl, _root, sql in schema:
            if _t == typ and sql:
                try:
                    out.execute(sql)
                except sqlite3.Error as e:
                    problems.append(f'could not create {typ} {name}: {e}')
    if completed:
        for st in [x.strip() for x in REFERENCE_SCHEMA.split(';\n') if x.strip()]:
            if st.upper().startswith('CREATE') and ' INDEX ' in st.upper():
                try:
                    out.execute(st)
                except sqlite3.Error:
                    pass          # already there from the recovered schema, or its table is not one we rebuilt
    out.commit()

    # Table pages nothing points at: the remains of deleted rows or dropped tables. Counted, never copied.
    orphan_pages = [n for n in range(2, db.pages + 1)
                    if db.kind(n) is not None and n not in claimed]
    if extra:
        un, close, leftovers = extra
        print(f'leftover page trees that matched no missing table: {len(un)} '
              f'(about {sum(len(r) for _p, r in un)} row images); matches that were close calls between two tables: {close}')
        for root, n, sig, widths in leftovers[:15]:
            print(f'    page {root}: {n} rows, shape {sig}, widths {dict(sorted(widths.items()))}')
    orphan_rows = 0
    for n in orphan_pages:
        if db.kind(n) == LEAF:
            orphan_rows += len(db.leaf_rows(n)[0])

    print()
    print(f'{"table":34s} {"rebuilt":>9s} {"in tree":>9s}   notes')
    for name, got, seen, note in report:
        print(f'{name:34s} {got:9d} {seen:9d}   {note}')
    print()
    print(f'total rows rebuilt: {sum(r[1] for r in report)}')
    print(f'table pages that no table points at: {len(orphan_pages)} '
          f'(about {orphan_rows} row images; left behind by deleted rows, NOT copied)')
    ic = out.execute('PRAGMA integrity_check').fetchone()[0]
    print(f'integrity_check on the rebuilt file: {ic}')
    for p in problems:
        print('PROBLEM:', p)
    out.close()
    print(f'\nwrote {a.rebuilt}  (the damaged file was not touched)')


if __name__ == '__main__':
    sys.exit(main())
