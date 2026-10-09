"""Verify release privacy and fresh native profiles without launching the game."""
import hashlib
import os
from pathlib import Path
import sqlite3
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

REPOSITORY = Path(__file__).resolve().parents[2]


@unittest.skipUnless(os.name == "nt", "Windows release packaging")
class ReleasePackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="LazerRave release 测试 ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.app = self.root / "app"
        self.app.mkdir()
        executable = REPOSITORY / "out/app/LazerRave.exe"
        self.assertTrue(executable.exists(), "Publish the current Release client first")
        os.link(executable, self.app / executable.name)
        self.manifest = self.root / "native.txt"
        self.manifest.write_text("native.dll\n", encoding="utf-8")
        for relative in ["OpenLR2_x64.exe", "fmod.dll", "native.dll", "set-window.ps1",
                         "Resources/osu.Game.Resources.dll", "Localization/zh-Hans/example.resources.dll",
                         "LR2files/Sound/lr2.lr2ss", "LR2files/Config/black.bmp", "LR2files/Config/white.bmp",
                         "LR2files/Config/foon.bmp", "LR2files/Config/muon.wav", "LR2files/Config/title.bmp",
                         "LR2files/Config/optionstr.csv", "LR2files/Config/keyconfig_def.xml",
                         "LR2files/Config/keyconfig_5_def.xml", "LR2files/Config/keyconfig_p_def.xml",
                         "LR2files/Config/midi_def.xml", "LR2files/Config/sample_5.bme",
                         "LR2files/Config/sample_7.bme", "LR2files/Config/sample_9.pms",
                         "LR2files/Config/sample_10.bme", "LR2files/Config/sample_14.bme"]:
            self.write(relative, b"release resource")
        for tree in ("Bgm", "Mouse", "Movie"):
            (self.app / "LR2files" / tree).mkdir()
        for config_name in ("config.xml", "openlr2-config.xml"):
            config = ET.parse(REPOSITORY / "res/release" / config_name)
            for element in config.findall("./skin/*"):
                if element.text and element.text.startswith("LR2files"):
                    self.write(element.text.replace("\\", "/"), b"default skin")

    def write(self, relative, content):
        path = self.app / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def package(self, success=True, destination=None):
        result = subprocess.run([
            "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            str(REPOSITORY / "scripts/package-release.ps1"), "-ApplicationDirectory", str(self.app),
            "-DestinationDirectory", str(destination or self.root / "releases"),
            "-NativeManifest", str(self.manifest)], capture_output=True, timeout=120,
            creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(result.returncode == 0, success, result.stderr.decode(errors="replace"))

    def test_archive_excludes_private_data_and_has_playable_fresh_profile(self):
        private_files = ["userdata/cloud-session.bin", "userdata/settings.toml", "BMS/private/chart.bms",
                         "Shared/private/chart.bms", "LR2files/Database/Score/Player.db",
                         "LR2files/Config/config.xml", "LR2files/Config/openlr2-config.xml",
                         "LR2files/Config/lazerrave-roots.xml", "LR2files/Replay/private.lr2rep",
                         "cache/private", "logs/private.log", "Log.txt", "nowstate.json",
                         "ExampleIR.x64.dll", "README.md", "bga-diagnostics.md"]
        for relative in private_files:
            self.write(relative, b"PRIVATE PLAYER DATA")
        self.package()
        zip_path = next((self.root / "releases").glob("*.zip"))
        expected_hash = Path(str(zip_path) + ".sha256").read_text().split()[0]
        self.assertEqual(expected_hash, hashlib.sha256(zip_path.read_bytes()).hexdigest())
        with zipfile.ZipFile(zip_path) as archive:
            prefix = zip_path.stem + "/"
            names = {name.removeprefix(prefix) for name in archive.namelist()}
            generated = {"userdata/settings.toml", "LR2files/Database/Score/Player.db",
                         "LR2files/Config/config.xml", "LR2files/Config/openlr2-config.xml"}
            self.assertFalse((set(private_files) - generated) & names)
            for relative in generated:
                self.assertNotIn(b"PRIVATE PLAYER DATA", archive.read(prefix + relative))
            for name in ("LazerRave.exe", "OpenLR2_x64.exe", "fmod.dll", "native.dll",
                         "Resources/osu.Game.Resources.dll", "licenses/dxlib-NOTICE.txt", "BMS/", "Shared/"):
                self.assertIn(name, names)
            connection = sqlite3.connect(":memory:")
            try:
                connection.deserialize(archive.read(prefix + "LR2files/Database/Score/Player.db"))
                self.assertEqual(connection.execute("SELECT COUNT(*) FROM score").fetchone()[0], 0)
                row = connection.execute("SELECT id,hash,systemversion,trial,scorehash FROM player").fetchone()
                password_hash = hashlib.md5(b"").hexdigest()
                self.assertEqual(row[:4], ("Player", password_hash, 1, 1))
                self.assertEqual(row[4], hashlib.md5(("0" * 11 + password_hash + "00000010101").encode()).hexdigest())
            finally:
                connection.close()
        for relative in private_files:
            self.assertEqual((self.app / relative).read_bytes(), b"PRIVATE PLAYER DATA")

    def test_missing_dependency_fails_before_archive_creation(self):
        (self.app / "native.dll").unlink()
        self.package(success=False)
        self.assertFalse((self.root / "releases").exists())

    def test_version_mismatch_fails_before_archive_creation(self):
        (self.app / "LazerRave.exe").unlink()
        self.write("LazerRave.exe", b"unversioned executable")
        self.package(success=False)
        self.assertFalse((self.root / "releases").exists())

    def test_nested_release_destination_is_rejected(self):
        self.package(success=False, destination=self.app / "releases")
        self.assertFalse((self.app / "releases").exists())


if __name__ == "__main__":
    unittest.main()
