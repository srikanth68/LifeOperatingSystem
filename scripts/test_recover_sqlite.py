#!/usr/bin/env python3
"""Tests for scripts/recover-sqlite.py.   Run:  python3 scripts/test_recover_sqlite.py

Each test builds its own healthy database, destroys it the way the real one was destroyed (zeroing
whole 4096-byte pages, always page 1 and sometimes the pages that describe the schema), runs the tool
and compares the rebuilt file with the healthy one, row for row, by column NAME (a rebuilt table may
list its columns in a different physical order than an old one).

Nothing here touches a real database, and no file outside a temporary directory is written.
"""
import hashlib
import importlib.util
import os
import random
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(HERE, 'recover-sqlite.py')

spec = importlib.util.spec_from_file_location('recover_sqlite', TOOL)
rec = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rec)

PAGE = 4096


def affinity_value(aff, name, i):
    """A plausible, unique-per-row value for a column of this kind."""
    if aff == 'I':
        return i
    if aff == 'R':
        return i * 0.25
    return f'{name[:10]}-{i:06d}'


class Base(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp(prefix='recover_test_')
        self.addCleanup(shutil.rmtree, self.dir, ignore_errors=True)

    def path(self, name):
        return os.path.join(self.dir, name)

    @staticmethod
    def read_bytes(p):
        with open(p, 'rb') as f:
            return f.read()

    # -- building --------------------------------------------------------------------------------

    def reference_db(self, name='good.db'):
        """An empty database with the app's whole schema, in a single plain file."""
        p = self.path(name)
        c = sqlite3.connect(p)
        c.execute('PRAGMA journal_mode=DELETE')
        c.executescript(rec.REFERENCE_SCHEMA)
        c.commit()
        return p, c

    def fill(self, c, tables, rows):
        for t in tables:
            info = c.execute(f'PRAGMA table_info("{t}")').fetchall()
            pk = [x for x in info if x[5]]
            alias = pk[0][1] if len(pk) == 1 and (pk[0][2] or '').upper() == 'INTEGER' else None
            names = [x[1] for x in info if x[1] != alias]
            affs = {x[1]: rec.affinity(x[2]) for x in info}
            for i in range(rows):
                c.execute(f'INSERT INTO "{t}" ({",".join(chr(34) + n + chr(34) for n in names)}) '
                          f'VALUES ({",".join("?" * len(names))})',
                          [affinity_value(affs[n], n, i) for n in names])
        c.commit()

    # -- damaging --------------------------------------------------------------------------------

    def wipe(self, src, pages, name='damaged.db'):
        dst = self.path(name)
        shutil.copy(src, dst)
        with open(dst, 'r+b') as f:
            for n in pages:
                f.seek((n - 1) * PAGE)
                f.write(b'\x00' * PAGE)
        return dst

    def schema_pages_for(self, good, tables):
        """The pages holding the CREATE rows of these tables (they are leaves, so they survive page 1)."""
        db = rec.Db(good)
        found = set()
        for n in range(2, db.pages + 1):
            if db.kind(n) == rec.LEAF:
                for _rid, v in db.leaf_rows(n)[0]:
                    if len(v) == 5 and v[0] == 'table' and v[1] in tables:
                        found.add(n)
        return sorted(found)

    # -- running and checking --------------------------------------------------------------------

    def run_tool(self, damaged, name='rebuilt.db'):
        out = self.path(name)
        r = subprocess.run([sys.executable, TOOL, damaged, out], capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        return out, r.stdout

    def assert_same(self, good, rebuilt, tables=None):
        a, b = sqlite3.connect(good), sqlite3.connect(rebuilt)
        self.addCleanup(a.close)
        self.addCleanup(b.close)
        names = tables or [x[0] for x in a.execute(
            "select name from sqlite_master where type='table' and name not like 'sqlite_%' order by 1")]
        for t in names:
            cols = sorted(x[1] for x in a.execute(f'pragma table_info("{t}")'))
            q = f'select {",".join(chr(34) + c + chr(34) for c in cols)} from "{t}"'
            self.assertEqual(sorted(a.execute(q).fetchall(), key=repr),
                             sorted(b.execute(q).fetchall(), key=repr), f'table {t} differs')


class TheFormat(Base):
    def test_a_varint_round_trips_at_every_length(self):
        for value, enc in [(0, b'\x00'), (127, b'\x7f'), (128, b'\x81\x00'), (16384, b'\x81\x80\x00')]:
            self.assertEqual(rec.varint(enc, 0), (value, len(enc)))

    def test_every_storage_class_decodes(self):
        c = sqlite3.connect(self.path('t.db'))
        c.execute('CREATE TABLE t (a, b, c, d, e)')
        c.execute('INSERT INTO t VALUES (NULL, 7, 1.5, ?, ?)', ('text', b'\x00\x01'))
        c.execute('INSERT INTO t VALUES (0, 1, -9000000000, "", 8)')
        c.commit(); c.close()
        db = rec.Db(self.path('t.db'))
        rows = [v for n in range(2, db.pages + 1) if db.kind(n) == rec.LEAF for _r, v in db.leaf_rows(n)[0]]
        self.assertIn([None, 7, 1.5, 'text', b'\x00\x01'], rows)
        self.assertIn([0, 1, -9000000000, '', 8], rows)


class PageOneWiped(Base):
    def test_a_realistic_schema_survives_page_one_and_every_table_comes_back(self):
        good, c = self.reference_db()
        self.fill(c, ['Sleep', 'Readiness', 'Activity', 'Stress', 'Measurements', 'Observations'], 400)
        c.close()

        out, text = self.run_tool(self.wipe(good, [1]))

        self.assertNotIn('were missing from the damaged schema', text)      # the schema pages were fine
        self.assert_same(good, out)
        conn = sqlite3.connect(out)
        self.addCleanup(conn.close)
        self.assertEqual(conn.execute('PRAGMA integrity_check').fetchone()[0], 'ok')

    def test_the_damaged_file_is_never_written_to(self):
        good, c = self.reference_db()
        self.fill(c, ['Sleep'], 50); c.close()
        damaged = self.wipe(good, [1])
        digest = lambda: hashlib.sha256(self.read_bytes(damaged)).hexdigest()
        before = digest()

        self.run_tool(damaged)

        self.assertEqual(digest(), before)

    def test_it_refuses_to_overwrite_an_existing_output(self):
        good, c = self.reference_db(); c.close()
        damaged = self.wipe(good, [1])
        existing = self.path('already.db')
        with open(existing, 'wb') as f:
            f.write(b'precious')

        r = subprocess.run([sys.executable, TOOL, damaged, existing], capture_output=True, text=True)

        self.assertNotEqual(r.returncode, 0)
        self.assertEqual(self.read_bytes(existing), b'precious')


class SchemaPagesWipedToo(Base):
    """What happened on the real machine: page 1 AND some of the pages describing tables."""

    def test_tables_whose_definitions_are_gone_come_back_by_shape(self):
        good, c = self.reference_db()
        self.fill(c, ['Activity', 'Readiness', 'Sleep', 'Stress', 'Profiles', 'Tokens', 'Measurements'], 300)
        c.close()
        gone = ['Activity', 'Readiness', 'Sleep', 'Stress', 'Profiles', 'Tokens']

        out, text = self.run_tool(self.wipe(good, [1, *self.schema_pages_for(good, gone)]))

        self.assertIn('were missing from the damaged schema', text)
        self.assert_same(good, out)

    def test_even_with_every_schema_page_gone_it_still_rebuilds_everything(self):
        good, c = self.reference_db()
        self.fill(c, ['Sleep', 'Readiness', 'Activity', 'Stress', 'Observations', 'Measurements'], 200)
        c.close()
        db = rec.Db(good)
        every = [n for n in range(2, db.pages + 1) if db.kind(n) == rec.LEAF and any(
            len(v) == 5 and v[0] in ('table', 'index') for _r, v in db.leaf_rows(n)[0])]

        out, text = self.run_tool(self.wipe(good, [1, *every]))

        self.assertIn('WARNING: no schema rows survive', text)
        self.assert_same(good, out)

    def test_rows_that_were_deleted_do_not_come_back_from_a_table_that_still_exists(self):
        good, c = self.reference_db()
        self.fill(c, ['Measurements', 'Sleep'], 600)
        c.execute("DELETE FROM Measurements WHERE Id > 'Id-000100'")
        c.commit(); c.close()
        out, _ = self.run_tool(self.wipe(good, [1]))
        self.assert_same(good, out, ['Measurements'])      # the deleted rows stay deleted


class OlderColumnOrder(Base):
    def test_a_profile_from_before_columns_were_appended_is_read_in_the_right_order(self):
        good, c = self.reference_db()
        # An old database: the table began with seven columns and three more were ALTERed on the end.
        c.execute('DROP TABLE Profiles')
        c.execute("CREATE TABLE Profiles (Id TEXT PRIMARY KEY, Age INTEGER, Weight REAL, Height REAL, "
                  "BiologicalSex TEXT, Email TEXT, UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00')")
        for col in ('Name', 'DateOfBirth', 'LockedFields'):
            c.execute(f'ALTER TABLE Profiles ADD COLUMN {col} TEXT')
        c.execute("INSERT INTO Profiles VALUES ('default', 42, 76.1, 1.651, 'male', NULL, '2026-10-07', "
                  "'Demo', '1984-06-15', 'height,sex')")
        self.fill(c, ['Sleep', 'Readiness', 'Activity', 'Stress'], 100)
        c.commit(); c.close()
        gone = ['Profiles', 'Sleep', 'Readiness', 'Activity', 'Stress']

        out, text = self.run_tool(self.wipe(good, [1, *self.schema_pages_for(good, gone)]))

        conn = sqlite3.connect(out)
        self.addCleanup(conn.close)
        row = conn.execute(
            'select Id, Age, Height, BiologicalSex, Name, DateOfBirth, LockedFields from Profiles').fetchall()
        self.assertEqual(row, [('default', 42, 1.651, 'male', 'Demo', '1984-06-15', 'height,sex')])
        self.assertIn('older column order', text)
        self.assert_same(good, out, ['Sleep', 'Readiness', 'Activity', 'Stress'])


class HardCases(Base):
    def setUp(self):
        super().setUp()
        random.seed(5)

    def test_big_rows_deep_trees_added_columns_and_the_autoincrement_counter(self):
        good, c = self.reference_db()
        c.executescript("""
            CREATE TABLE Notes (Id TEXT PRIMARY KEY, Body TEXT, Blob BLOB);
            CREATE TABLE Readings (Id INTEGER PRIMARY KEY AUTOINCREMENT, Metric TEXT NOT NULL, Value REAL, At TEXT);
            CREATE TABLE Wide (K TEXT PRIMARY KEY, A INT, B TEXT);
        """)
        for i in range(30):        # rows far larger than a page spill onto overflow pages
            c.execute('INSERT INTO Notes VALUES (?,?,?)',
                      (f'n{i:03d}', 'x' * random.randint(5000, 30000) + f'#{i}', os.urandom(random.randint(0, 9000))))
        for i in range(3000):
            v = random.choice([None, random.random() * 100, random.randint(-10 ** 12, 10 ** 12), 0, 1])
            c.execute('INSERT INTO Readings (Metric, Value, At) VALUES (?,?,?)', ('hrv', v, f'2026-01-{i % 28 + 1:02d}'))
        for i in range(200):       # rows written BEFORE two columns existed are physically shorter
            c.execute('INSERT INTO Wide (K, A) VALUES (?,?)', (f'w{i}', i))
        c.execute("ALTER TABLE Wide ADD COLUMN C TEXT DEFAULT 'late'")
        c.execute('ALTER TABLE Wide ADD COLUMN D INTEGER NOT NULL DEFAULT 7')
        c.execute("DELETE FROM Readings WHERE Id % 3 = 0")     # the newest id is deleted: the counter must still know it
        c.commit()
        last_issued = c.execute("select seq from sqlite_sequence where name='Readings'").fetchone()[0]
        c.close()

        out, _ = self.run_tool(self.wipe(good, [1]))

        self.assert_same(good, out, ['Notes', 'Readings', 'Wide'])
        b = sqlite3.connect(out)
        self.addCleanup(b.close)
        self.assertEqual(b.execute("select seq from sqlite_sequence where name='Readings'").fetchone()[0], last_issued)
        b.execute("INSERT INTO Readings (Metric, Value, At) VALUES ('x', 1, 'y')")
        self.assertEqual(b.execute('select max(Id) from Readings').fetchone()[0], last_issued + 1)    # no reused id


if __name__ == '__main__':
    unittest.main(verbosity=2)
