"""Exercise complete package assembly without compiling or opening a game."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


@unittest.skipUnless(os.name == "nt", "Windows package assembly uses robocopy")
class DesktopPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="LazerRave package 测试 ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "resources"
        self.engine = self.root / "engine"
        self.destination = self.root / "package"
        self.desktop = self.root / "LazerRave.exe"
        for path, content in [
            (self.desktop, b"frontend"),
            (self.engine / "OpenLR2_x64.exe", b"engine"),
            (self.engine / "fmod.dll", b"x64 fmod"),
            (self.engine / "ExampleIR.x64.dll", b"plugin"),
            (self.source / "LR2files/Config/config.xml", b"original config"),
            (self.source / "LR2files/Theme/default.csv", b"skin"),
            (self.source / "LR2files/Database/Score/player.db", b"original score"),
            (self.source / "LR2files/Replay/player/chart.lr2rep", b"original replay"),
            (self.source / "BMS/Pack/chart.bms", b"#TITLE Chart"),
        ]:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)

    def package(self, destination=None, success=True):
        script = Path(__file__).resolve().parents[1] / "package-client.ps1"
        result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                                 "-File", str(script), "-DesktopExecutable", str(self.desktop),
                                 "-EnginePackage", str(self.engine), "-RuntimeSource", str(self.source),
                                 "-Destination", str(destination or self.destination)],
                                capture_output=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
        self.assertEqual(result.returncode == 0, success, result.stderr.decode(errors="replace"))

    def test_complete_package_and_rebuild_preserve_player_data(self):
        self.package()
        for relative in ["LazerRave.exe", "OpenLR2_x64.exe", "fmod.dll", "set-window.ps1", "LICENSE", "BMS/Pack/chart.bms"]:
            self.assertTrue((self.destination / relative).is_file(), relative)
        self.assertFalse((self.destination / "README.md").exists())
        self.assertFalse((self.destination / "bga-diagnostics.md").exists())
        self.assertFalse((self.destination / "ExampleIR.x64.dll").exists())
        protected = ["LR2files/Config/config.xml", "LR2files/Database/Score/player.db", "LR2files/Replay/player/chart.lr2rep", "LR2files/Theme/default.csv"]
        for relative in protected:
            (self.destination / relative).write_bytes(b"customized player data")
            (self.source / relative).write_bytes(b"updated source data of a different size")
        extra = self.source / "LR2files/Theme/added.csv"
        extra.write_bytes(b"new resource")
        (self.engine / "OpenLR2_x64.exe").write_bytes(b"updated engine")
        self.package()
        self.assertEqual((self.destination / "OpenLR2_x64.exe").read_bytes(), b"updated engine")
        self.assertEqual((self.destination / "LR2files/Theme/added.csv").read_bytes(), b"new resource")
        for relative in protected:
            self.assertEqual((self.destination / relative).read_bytes(), b"customized player data")

    def test_nested_destination_is_rejected_before_copying(self):
        destination = self.source / "nested package"
        self.package(destination, success=False)
        self.assertFalse(destination.exists())

    def test_pending_sqlite_journal_is_not_silently_discarded(self):
        journal = self.source / "LR2files/Database/song.db-wal"
        journal.write_bytes(b"uncheckpointed database data")
        self.package(success=False)
        self.assertFalse(self.destination.exists())


if __name__ == "__main__":
    unittest.main()
