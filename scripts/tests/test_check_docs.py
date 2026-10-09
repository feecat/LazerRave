from pathlib import Path
import tempfile
import unittest
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_docs import validate


class DocumentationChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="lazerrave-doc-check-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "docs").mkdir()
        self.write("mkdocs.yml", "site_name: Test\nplugins: []\nnav:\n  - Home: index.md\n")
        self.write("docs/index.md", "# Home\n")

    def write(self, name, text):
        (self.root / name).write_text(text, encoding="utf-8")

    def test_valid_local_anchor(self):
        self.write("docs/index.md", "# Home\n\n[Section](#section)\n\n## Section\n")
        self.assertEqual(validate(self.root), [])

    def test_readme_and_index_conflict_is_rejected(self):
        self.write("docs/README.md", "# Contents\n")
        self.write("mkdocs.yml", "site_name: Test\nplugins: []\nnav:\n  - Home: index.md\n  - Contents: README.md\n")
        self.assertTrue(any("conflict" in error for error in validate(self.root)))

    def test_missing_navigation_target_is_rejected(self):
        self.write("mkdocs.yml", "site_name: Test\nplugins: []\nnav:\n  - Missing: absent.md\n")
        self.assertTrue(any("Navigation" in error for error in validate(self.root)))

    def test_broken_path_and_anchor_are_rejected(self):
        self.write("docs/index.md", "# Home\n\n[Missing](absent.md)\n\n[Anchor](#absent)\n")
        errors = validate(self.root)
        self.assertTrue(any("Broken local link" in error for error in errors))
        self.assertTrue(any("Missing heading anchor" in error for error in errors))

    def test_duplicate_navigation_and_unlisted_pages_are_rejected(self):
        self.write("docs/extra.md", "# Extra\n")
        self.write("mkdocs.yml", "site_name: Test\nplugins: []\nnav:\n  - Home: index.md\n  - Duplicate: index.md\n")
        self.assertTrue(any("Navigation" in error for error in validate(self.root)))


if __name__ == "__main__":
    unittest.main()
