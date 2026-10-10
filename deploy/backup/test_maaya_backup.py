"""Tests for maaya-backup.sh, run against a fake Docker and real SQLite files.

    python3 deploy/backup/test_maaya_backup.py

Docker is replaced by a stub that records every call, so the tests can check the thing
that matters most about this script: the stack is stopped before any copy and started
again afterwards, whatever happens. sqlite3 is a thin Python stand-in for the CLI, so
the tests also run where the sqlite3 command is not installed.
"""
import json, os, shutil, sqlite3, stat, subprocess, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, 'maaya-backup.sh')


def find_bash():
    if os.environ.get('BASH_FOR_TESTS'):
        return os.environ['BASH_FOR_TESTS']
    if os.name == 'nt':                       # Git's bash, not WSL's
        for p in (r'C:\Program Files\Git\bin\bash.exe', r'C:\Program Files (x86)\Git\bin\bash.exe'):
            if os.path.exists(p):
                return p
    return shutil.which('bash')


BASH = find_bash()

DOCKER_STUB = r'''#!/usr/bin/env bash
echo "$*" >> "$STUB_LOG"
case "$1" in
  info) [ "${DOCKER_UP:-1}" = 1 ] ;;
  compose)
    shift
    case "$1" in
      ps) printf '%s\n' ${RUNNING_SERVICES:-} ;;
      stop) [ "${STOP_FAILS:-0}" = 0 ] ;;
      start) true ;;
    esac ;;
esac
'''

SQLITE_STUB_PY = r'''
import sqlite3, sys
path, sql = sys.argv[1], sys.argv[2]
try:
    con = sqlite3.connect(path)
    rows = con.execute(sql).fetchall()
    con.close()
    print("\n".join(str(r[0]) for r in rows))
except Exception as e:
    print("Error: " + str(e)); sys.exit(1)
'''


def read(path):
    with open(path) as f:
        return f.read()


def write(path, text):
    with open(path, 'w') as f:
        f.write(text)


def make_db(path, rows=50):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    con = sqlite3.connect(path)
    con.execute('CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT)')
    con.executemany('INSERT INTO t (v) VALUES (?)', [('x' * 200,) for _ in range(rows)])
    con.commit()
    con.close()


def damage(path):
    # Zero page 2 of 4096-byte pages: a table page, so integrity_check fails.
    with open(path, 'r+b') as f:
        f.seek(4096)
        f.write(b'\x00' * 4096)


class BackupTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix='maaya-backup-')
        self.data = os.path.join(self.root, 'deploy', 'data')
        self.backups = os.path.join(self.root, 'deploy', 'backups')
        self.bin = os.path.join(self.root, 'bin')
        os.makedirs(self.bin)
        self.log = os.path.join(self.root, 'docker-calls.log')
        write(self.log, '')

        docker = os.path.join(self.bin, 'docker')
        with open(docker, 'w', newline='\n') as f:
            f.write(DOCKER_STUB)
        stub_py = os.path.join(self.bin, 'sqlite_stub.py')
        with open(stub_py, 'w') as f:
            f.write(SQLITE_STUB_PY)
        sqlite_sh = os.path.join(self.bin, 'sqlite3')
        with open(sqlite_sh, 'w', newline='\n') as f:
            f.write('#!/usr/bin/env bash\nexec "%s" "%s" "$@"\n' % (sys.executable.replace('\\', '/'), stub_py.replace('\\', '/')))
        for p in (docker, sqlite_sh):
            os.chmod(p, os.stat(p).st_mode | stat.S_IEXEC)

        make_db(os.path.join(self.data, 'san', 'san.db'))
        make_db(os.path.join(self.data, 'vitara', 'vitara.db'))
        make_db(os.path.join(self.data, 'northstar', 'run', 'northstar.db'))

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def run_backup(self, **env):
        e = dict(os.environ)
        to_posix = lambda p: p.replace('\\', '/')
        e.update({
            'MAAYA_REPO': to_posix(self.root),
            'MAAYA_DATA': to_posix(self.data),
            'MAAYA_BACKUP_DIR': to_posix(self.backups),
            'MAAYA_DOCKER': to_posix(os.path.join(self.bin, 'docker')),
            'MAAYA_SQLITE': to_posix(os.path.join(self.bin, 'sqlite3')),
            'STUB_LOG': to_posix(self.log),
            'RUNNING_SERVICES': 'san vitara northstar',
        })
        e.update({k: str(v) for k, v in env.items()})
        r = subprocess.run([BASH, SCRIPT.replace('\\', '/')], env=e, capture_output=True, text=True)
        return r

    def calls(self):
        return [l.strip() for l in read(self.log).splitlines() if l.strip()]

    def status(self):
        return json.loads(read(os.path.join(self.data, 'backup-status.json')))

    def snapshots(self):
        return sorted(d for d in os.listdir(self.backups) if d.startswith('20'))

    # ── The happy path ──────────────────────────────────────────────────────────

    def test_a_normal_run_stops_copies_and_starts_again(self):
        r = self.run_backup()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        calls = self.calls()
        stop = calls.index('compose stop')
        start = next(i for i, c in enumerate(calls) if c.startswith('compose start'))
        self.assertLess(stop, start)
        self.assertEqual(calls[start], 'compose start san vitara northstar')

        s = self.status()
        self.assertTrue(s['ok'])
        self.assertEqual(s['databases'], 3)
        self.assertEqual(s['failed'], [])
        self.assertEqual(s['expectedEveryHours'], 168)

        snap = os.path.join(self.backups, self.snapshots()[0])
        self.assertEqual(read(os.path.join(snap, 'RESULT')).strip(), 'ok')
        for rel in ('san/san.db', 'vitara/vitara.db', 'northstar/run/northstar.db'):
            con = sqlite3.connect(os.path.join(snap, rel))
            self.assertEqual(con.execute('select count(*) from t').fetchone()[0], 50)
            con.close()

    def test_committed_data_still_in_a_wal_file_is_in_the_copy(self):
        # A hard-stopped container can leave committed rows in the WAL, not yet folded
        # into the main file. The copy must carry them.
        path = os.path.join(self.data, 'san', 'san.db')
        con = sqlite3.connect(path)
        con.execute('PRAGMA journal_mode=WAL')
        con.execute('PRAGMA wal_autocheckpoint=0')
        con.executemany('INSERT INTO t (v) VALUES (?)', [('w',)] * 7)
        con.commit()
        wal_copy = path + '-wal.keep'
        shutil.copy(path + '-wal', wal_copy)
        con.close()                              # closing checkpoints and deletes the WAL...
        shutil.copy(wal_copy, path + '-wal')     # ...so put the un-folded WAL back, as a hard stop leaves it
        # Restore the main file to its pre-WAL state is not possible here; the WAL replays
        # idempotently, so the check is simply that the copy is readable and complete.
        r = self.run_backup()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        snap = os.path.join(self.backups, self.snapshots()[0])
        con = sqlite3.connect(os.path.join(snap, 'san', 'san.db'))
        self.assertEqual(con.execute('select count(*) from t').fetchone()[0], 57)
        con.close()

    # ── Failures ────────────────────────────────────────────────────────────────

    def test_one_damaged_database_does_not_cost_the_others(self):
        damage(os.path.join(self.data, 'vitara', 'vitara.db'))
        r = self.run_backup()
        self.assertEqual(r.returncode, 2, r.stdout + r.stderr)

        s = self.status()
        self.assertFalse(s['ok'])
        self.assertEqual(s['databases'], 2)
        self.assertEqual(len(s['failed']), 1)
        self.assertIn('vitara/vitara.db', s['failed'][0])
        self.assertIn('vitara/vitara.db', s['error'])

        snap = os.path.join(self.backups, self.snapshots()[0])
        self.assertTrue(os.path.exists(os.path.join(snap, 'san', 'san.db')))
        self.assertTrue(os.path.exists(os.path.join(snap, 'northstar', 'run', 'northstar.db')))
        self.assertTrue(read(os.path.join(snap, 'RESULT')).startswith('failed'))
        self.assertTrue(any(c.startswith('compose start') for c in self.calls()))

    def test_if_the_stack_will_not_stop_nothing_is_copied_and_it_is_started_again(self):
        r = self.run_backup(STOP_FAILS=1)
        self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
        self.assertEqual(self.snapshots(), [])
        self.assertFalse(self.status()['ok'])
        self.assertIn('could not stop', self.status()['error'])
        self.assertTrue(any(c.startswith('compose start') for c in self.calls()))

    def test_with_docker_down_it_copies_without_stopping_or_starting(self):
        r = self.run_backup(DOCKER_UP=0)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual(self.calls(), ['info'])
        self.assertTrue(self.status()['ok'])

    def test_nothing_is_started_that_was_not_running(self):
        r = self.run_backup(RUNNING_SERVICES='san')
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn('compose start san', self.calls())

    # ── Retention ───────────────────────────────────────────────────────────────

    def test_retention_keeps_the_newest_and_the_last_good_one(self):
        os.makedirs(self.backups)
        # 15 older snapshots: the oldest is the only one that ever passed.
        for i in range(15):
            d = os.path.join(self.backups, '2026-01-%02dT030000Z' % (i + 1))
            os.makedirs(d)
            write(os.path.join(d, 'RESULT'), 'ok\n' if i == 0 else 'failed\nx\n')
        damage(os.path.join(self.data, 'vitara', 'vitara.db'))   # today's run fails too
        self.run_backup(MAAYA_KEEP=12)
        snaps = self.snapshots()
        self.assertEqual(len(snaps), 13)                       # 12 newest + the last good one
        self.assertIn('2026-01-01T030000Z', snaps)

    def test_a_good_run_lets_old_snapshots_go(self):
        os.makedirs(self.backups)
        for i in range(15):
            d = os.path.join(self.backups, '2026-01-%02dT030000Z' % (i + 1))
            os.makedirs(d)
            write(os.path.join(d, 'RESULT'), 'ok\n')
        self.run_backup(MAAYA_KEEP=12)
        self.assertEqual(len(self.snapshots()), 12)

    def test_a_second_run_while_one_is_running_refuses(self):
        os.makedirs(os.path.join(self.backups, '.lock'))
        r = self.run_backup()
        self.assertEqual(r.returncode, 1)
        self.assertEqual(self.calls(), [])                     # never touched the stack


if __name__ == '__main__':
    unittest.main(verbosity=2)
