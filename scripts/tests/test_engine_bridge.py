"""Headless integration checks against an explicitly supplied OpenLR2 build."""
import hashlib
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


@unittest.skipUnless(os.environ.get("LAZERRAVE_TEST_ENGINE"), "Set LAZERRAVE_TEST_ENGINE to the built EXE")
class EngineBridgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="LazerRave bridge ")
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        shutil.copy2(os.environ["LAZERRAVE_TEST_ENGINE"], self.directory / "OpenLR2_x64.exe")
        shutil.copy2(os.environ["LAZERRAVE_TEST_FMOD"], self.directory / "fmod.dll")
        config = self.directory / "LR2files/Config"
        config.mkdir(parents=True)
        (self.directory / "LR2files/Database").mkdir()
        self.config = config / "config.xml"
        self.config.write_bytes(b'<config><jukebox></jukebox><player><id>test</id></player></config>\n')
        self.music = self.directory / "Music & songs" / "\u6d4b\u8bd5"
        self.music.mkdir(parents=True)
        self.normal = self.music / "normal.bms"
        self.hyper = self.music / "hyper.bms"
        self.chart(self.normal, "Normal", 3)
        self.chart(self.hyper, "Hyper", 9)

    @staticmethod
    def chart(path, title, level):
        path.write_text(f"#TITLE {title}\n#ARTIST Test\n#BPM 150\n#PLAYLEVEL {level}\n#DIFFICULTY 3\n#00118:0100\n", encoding="ascii")

    def call(self, mode, values=(), success=True, play_options=None):
        root = ET.Element("lazerrave", version="1", mode=mode)
        for key, value in values:
            ET.SubElement(root, key).text = str(value)
        if play_options is not None:
            options = ET.SubElement(root, "play-options")
            for name, value in play_options:
                ET.SubElement(options, "option", name=name, value=str(value))
        request = self.directory / "request \u53c2\u6570.xml"
        ET.ElementTree(root).write(request, encoding="utf-8")
        reply = Path(str(request) + ".reply.xml")
        if reply.exists():
            reply.unlink()
        result = subprocess.run([str(self.directory / "OpenLR2_x64.exe"), "--lazerrave-request", str(request)],
                                cwd=self.directory, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(result.returncode, 0 if success else 2)
        response = ET.parse(reply).getroot()
        self.assertEqual(response.get("status"), "ok" if success else "error")
        return response

    def test_selected_chart_and_session_options(self):
        before = hashlib.sha256(self.config.read_bytes()).digest()
        for chart, speed, offset, arrangement in [(self.normal, 275, -23, 1), (self.hyper, 50, 1000, 2)]:
            reply = self.call("validate", [("chart", chart), ("speed", speed), ("offset", offset), ("arrangement", arrangement)])
            text = reply.findtext("message")
            for part in [str(chart), "keys=7", f"speed={speed}", f"offset={offset}", f"arrangement={arrangement}"]:
                self.assertIn(part, text)
        self.assertEqual(hashlib.sha256(self.config.read_bytes()).digest(), before)
        self.assertFalse((self.directory / "LR2files/Database/song.db").exists())

    def test_sync_readback_and_incremental_changes_preserve_config_and_scores(self):
        before = self.config.read_bytes()
        scores = self.directory / "LR2files/Database/Score/test.db"
        scores.parent.mkdir()
        scores.write_bytes(b"score sentinel")
        reply = self.call("sync", [("root", self.music.parent)])
        self.assertEqual(len(reply.findall("chart")), 2)
        self.assertTrue((self.directory / "LR2files/Database/song.db.before-lazerrave.bak").is_file())
        self.assertEqual(len(self.call("catalog").findall("chart")), 2)
        self.normal.unlink()
        self.chart(self.hyper, "Changed Hyper", 10)
        extra = self.music / "another.bms"
        self.chart(extra, "Another", 12)
        reply = self.call("sync", [("root", self.music.parent)])
        charts = {Path(chart.get("path")).name: chart for chart in reply.findall("chart")}
        self.assertEqual(set(charts), {"hyper.bms", "another.bms"})
        self.assertEqual(charts["hyper.bms"].get("title"), "Changed Hyper")
        self.assertEqual(charts["hyper.bms"].get("level"), "10")
        with sqlite3.connect(self.directory / "LR2files/Database/song.db") as db:
            self.assertEqual(db.execute("PRAGMA integrity_check").fetchone()[0], "ok")
            self.assertEqual(db.execute("SELECT COUNT(*) FROM song").fetchone()[0], 2)
        db.close()
        self.assertEqual(self.config.read_bytes(), before)
        self.assertEqual(scores.read_bytes(), b"score sentinel")

    def test_invalid_requests_fail_without_database_changes(self):
        self.call("validate", [("chart", self.hyper), ("speed", 0)], success=False)
        self.call("play", [("chart", self.music / "missing.bms")], success=False)
        self.call("sync", [("root", self.music / "missing")], success=False)
        self.assertFalse((self.directory / "LR2files/Database/song.db").exists())

    def test_shared_import_keeps_other_roots_and_excludes_pending_downloads(self):
        shared = self.directory / "Shared"
        incoming = shared / ".incoming" / "unfinished"
        incoming.mkdir(parents=True)
        self.chart(incoming / "pending.bms", "Pending", 1)
        roots = [("root", self.music.parent), ("root", shared)]
        response = self.call("sync", roots)
        self.assertEqual(len(response.findall("chart")), 2)
        song = shared / "content-hash"
        song.mkdir()
        self.chart(song / "normal.bms", "Shared Normal", 3)
        self.chart(song / "another.bms", "Shared Another", 8)
        response = self.call("import", roots + [("chart", song / "another.bms")])
        self.assertEqual(len(response.findall("chart")), 4)
        self.assertEqual({Path(row.text) for row in response.findall("root")}, {self.music.parent, shared})
        self.assertEqual(len(self.call("catalog").findall("chart")), 4)
        with sqlite3.connect(self.directory / "LR2files/Database/song.db") as database:
            rows = database.execute("SELECT path FROM song").fetchall()
            self.assertEqual(len(rows), 4)
            self.assertFalse(any(".incoming" in row[0] for row in rows))
        database.close()

    def test_invalid_gameplay_options_fail_without_data_changes(self):
        before = self.config.read_bytes()
        for options in [
            [("unknown", 1)], [("gauge", 6)], [("gauge", -1)], [("gauge", "1.5")],
            [("gauge", "true")], [("gauge", 1), ("gauge", 2)],
            [("hs_min", 1000), ("hs_max", 10)], [("hs_min", 1000)],
        ]:
            with self.subTest(options=options):
                self.call("validate", [("chart", self.normal)], success=False, play_options=options)
        self.assertEqual(self.config.read_bytes(), before)
        self.assertFalse((self.directory / "LR2files/Database/song.db").exists())

    def test_partial_gameplay_options_preserve_classic_defaults(self):
        original = self.call("validate", [("chart", self.normal)])
        baseline = {option.get("name"): option.get("value") for option in original.find("play-options")}
        response = self.call("validate", [("chart", self.normal)], play_options=[("gauge", 1)])
        effective = {option.get("name"): option.get("value") for option in response.find("play-options")}
        self.assertEqual(effective, baseline | {"gauge": "1"})

    def test_invalid_embedding_targets_fail_before_graphics_or_data_changes(self):
        before = self.config.read_bytes()
        for options in [
            [("embed-window", "0"), ("host-process", str(os.getpid()))],
            [("embed-window", "-1"), ("host-process", str(os.getpid()))],
            [("embed-window", "18446744073709551616"), ("host-process", str(os.getpid()))],
            [("embed-window", "1")],
            [("embed-window", "1"), ("host-process", "4294967296")],
            [("embed-window", "1"), ("host-process", str(os.getpid()))],
        ]:
            self.call("validate", [("chart", self.normal)] + options, success=False)
        self.call("catalog", [("embed-window", "1"), ("host-process", str(os.getpid()))], success=False)
        self.call("embed-probe", success=False)
        self.assertEqual(self.config.read_bytes(), before)
        self.assertFalse((self.directory / "LR2files/Database/song.db").exists())

    def test_text_encoding_and_bom_are_consistent_in_metadata(self):
        for encoding in ["cp932", "utf-8", "utf-8-sig"]:
            title = "桜華月"
            self.normal.write_bytes(f"#TITLE {title}\n#ARTIST 日本語\n#BPM 150\n#00118:0100\n".encode(encoding))
            response = self.call("validate", [("chart", self.normal)])
            self.assertIn(f"title={title}", response.findtext("message"))
            self.assertIn("artist=日本語", response.findtext("message"))
            response = self.call("refresh", [("root", self.music.parent)])
            row = next(row for row in response.findall("chart") if Path(row.get("path")) == self.normal)
            self.assertEqual(row.get("title"), title)
        self.normal.write_bytes("#TITLE 中文曲目\n#ARTIST 测试\n#BPM 150\n#00118:0100\n".encode("gb18030"))
        response = self.call("validate", [("chart", self.normal), ("encoding", "gb18030")])
        self.assertIn("title=中文曲目", response.findtext("message"))
        response = self.call("refresh", [("root", self.music.parent), ("encoding", "gb18030")])
        row = next(row for row in response.findall("chart") if Path(row.get("path")) == self.normal)
        self.assertEqual(row.get("title"), "中文曲目")
        self.call("validate", [("chart", self.normal), ("encoding", "invalid")], success=False)

    def test_refresh_repairs_unchanged_duplicate_metadata_without_losing_identity(self):
        self.normal.write_bytes("#TITLE 桜華月\n#ARTIST 日本語\n#BPM 150\n#00118:0100\n".encode("cp932"))
        duplicate = self.music / "duplicate.bms"
        shutil.copy2(self.normal, duplicate)
        self.call("sync", [("root", self.music.parent)])
        database = self.directory / "LR2files/Database/song.db"
        with sqlite3.connect(database) as db:
            db.execute("UPDATE song SET title='broken cache',favorite=3,adddate=123")
            before = db.execute("SELECT path,hash,favorite,adddate FROM song ORDER BY path").fetchall()
        db.close()
        self.assertTrue(all(row.get("title") == "broken cache" for row in self.call("sync", [("root", self.music.parent)]).findall("chart")))
        scores = self.directory / "LR2files/Database/Score/test.db"
        scores.parent.mkdir()
        scores.write_bytes(b"score sentinel")
        replay = self.directory / "LR2files/Replay/test/example.lr2rep"
        replay.parent.mkdir(parents=True)
        replay.write_bytes(b"replay sentinel")
        config_before = self.config.read_bytes()
        response = self.call("refresh", [("root", self.music.parent)])
        self.assertTrue((database.parent / "song.db.before-metadata-repair.bak").is_file())
        rows = {Path(row.get("path")).name: row.get("title") for row in response.findall("chart")}
        self.assertEqual(rows["normal.bms"], "桜華月")
        self.assertEqual(rows["duplicate.bms"], "桜華月")
        with sqlite3.connect(database) as db:
            self.assertEqual(db.execute("SELECT path,hash,favorite,adddate FROM song ORDER BY path").fetchall(), before)
            self.assertEqual(db.execute("PRAGMA integrity_check").fetchone()[0], "ok")
        db.close()
        self.assertEqual(scores.read_bytes(), b"score sentinel")
        self.assertEqual(replay.read_bytes(), b"replay sentinel")
        self.assertEqual(self.config.read_bytes(), config_before)

    def test_settings_roots_replace_classic_roots_and_empty_selection_stays_empty(self):
        config = ET.Element("config")
        ET.SubElement(ET.SubElement(config, "jukebox"), "path").text = str(self.music.parent)
        ET.ElementTree(config).write(self.config, encoding="ascii")
        before = self.config.read_bytes()
        extra = self.directory / "Launcher music"
        extra.mkdir()
        self.chart(extra / "extra.bms", "Extra", 11)
        response = self.call("sync", [("root", extra)])
        self.assertEqual(len(response.findall("chart")), 1)
        self.assertEqual(len(response.findall("root")), 1)
        self.assertEqual(Path(response.findtext("root")), extra)
        database = self.directory / "LR2files/Database/song.db"
        previous = hashlib.sha256(database.read_bytes()).digest()
        self.call("catalog")
        self.assertEqual(hashlib.sha256(database.read_bytes()).digest(), previous)
        empty = self.call("catalog", [("library-source", "settings")])
        self.assertEqual(len(empty.findall("root")), 0)
        self.assertEqual(len(empty.findall("chart")), 0)
        self.assertEqual(hashlib.sha256(database.read_bytes()).digest(), previous)
        response = self.call("sync")
        self.assertEqual(len(response.findall("chart")), 0)
        self.assertEqual(len(response.findall("root")), 0)
        self.assertEqual(len(self.call("catalog").findall("chart")), 0)
        self.assertTrue(self.normal.is_file())
        self.assertTrue((extra / "extra.bms").is_file())
        self.assertEqual(self.config.read_bytes(), before)

    def test_catalog_reads_validated_best_scores_without_modifying_them(self):
        self.call("sync", [("root", self.music.parent)])
        chart_hash = hashlib.md5(self.normal.read_bytes()).hexdigest()
        score_path = self.directory / "LR2files/Database/Score/test.db"
        score_path.parent.mkdir()
        values = [chart_hash, 2, 100, 50, 10, 5, 5, 170, 120, 10, 2, 1, 1, 4, 73, 0, 0]
        password_hash = hashlib.md5(b"").hexdigest()
        signature = "".join(str(values[index]) for index in [10, 2, 6, 4, 3, 5, 7, 1, 8, 11, 12, 13, 9, 14, 15, 16])
        checksum = hashlib.md5((signature + password_hash + chart_hash + "1").encode("ascii")).hexdigest()
        with sqlite3.connect(score_path) as db:
            db.execute("CREATE TABLE score(hash TEXT,clear INTEGER,perfect INTEGER,great INTEGER,good INTEGER,bad INTEGER,poor INTEGER,totalnotes INTEGER,maxcombo INTEGER,minbp INTEGER,playcount INTEGER,clearcount INTEGER,failcount INTEGER,rank INTEGER,rate INTEGER,clear_db INTEGER,op_history INTEGER,scorehash TEXT)")
            db.execute("INSERT INTO score VALUES(" + ",".join("?" for _ in range(18)) + ")", values + [checksum])
        db.close()
        before = score_path.read_bytes()
        response = self.call("catalog")
        normal = next(row for row in response.findall("chart") if Path(row.get("path")) == self.normal)
        self.assertEqual(normal.get("score"), "250")
        hyper = next(row for row in response.findall("chart") if Path(row.get("path")) == self.hyper)
        self.assertIsNone(hyper.get("score"))
        self.assertEqual(score_path.read_bytes(), before)
        with sqlite3.connect(score_path) as db:
            db.execute("UPDATE score SET perfect=101")
        db.close()
        response = self.call("catalog")
        self.assertTrue(all(row.get("score") is None for row in response.findall("chart")))


if __name__ == "__main__":
    unittest.main()
