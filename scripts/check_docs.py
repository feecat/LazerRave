"""Validate MkDocs inputs and links without building or serving a site."""

from __future__ import annotations

import argparse
from collections import Counter
from html.parser import HTMLParser
import logging
from pathlib import Path
import sys
from urllib.parse import unquote, urlsplit

import markdown
from mkdocs.config import load_config
from mkdocs.structure.files import get_files


class References(HTMLParser):
    def __init__(self, text: str):
        super().__init__()
        self.links: list[str] = []
        self.ids: set[str] = set()
        self.feed(markdown.markdown(text, extensions=["tables", "fenced_code", "toc"]))

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if "id" in attrs:
            self.ids.add(attrs["id"])
        if tag in {"a", "img"}:
            destination = attrs.get("href" if tag == "a" else "src")
            if destination:
                self.links.append(destination)


class Warnings(logging.Handler):
    def __init__(self):
        super().__init__(logging.WARNING)
        self.messages: list[str] = []

    def emit(self, record):
        self.messages.append(record.getMessage())


def nav_paths(value):
    if isinstance(value, list):
        for item in value:
            yield from nav_paths(item)
    elif isinstance(value, dict):
        for item in value.values():
            yield from nav_paths(item)
    elif isinstance(value, str) and not urlsplit(value).scheme:
        yield value


def validate(root: Path) -> list[str]:
    root = root.resolve()
    errors: list[str] = []
    warnings = Warnings()
    logger = logging.getLogger("mkdocs")
    logger.addHandler(warnings)
    try:
        config = load_config(config_file=str(root / "mkdocs.yml"), strict=True)
        files = get_files(config)
    except Exception as error:
        return [f"Configuration failed: {error}"]
    finally:
        logger.removeHandler(warnings)
    errors.extend(warnings.messages)
    pages = [f for f in files if f.is_documentation_page() and f.inclusion.is_included()]
    actual = Counter(f.src_uri for f in pages)
    navigation = Counter(nav_paths(config["nav"]))
    if navigation != actual:
        errors.append(f"Navigation differs from included pages: {sorted((navigation - actual).elements())}; unlisted: {sorted((actual - navigation).elements())}")
    if len({f.dest_uri for f in pages}) != len(pages):
        errors.append("Documentation output paths conflict")
    docs = Path(config["docs_dir"])
    for path in docs.rglob("*"):
        if path.is_file() and path.suffix.lower() in {".json", ".jsonl", ".log"}:
            errors.append(f"Diagnostic/structured data is not a documentation asset: {path.relative_to(root)}")
    sources = [Path(f.abs_src_path) for f in pages]
    if (root / "README.md").is_file():
        sources.append(root / "README.md")
    parsed = {}
    for path in sources:
        try:
            parsed[path.resolve()] = References(path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError) as error:
            errors.append(f"Cannot read {path}: {error}")
    for path, references in parsed.items():
        for link in references.links:
            url = urlsplit(link)
            if url.scheme or url.netloc:
                continue
            target = (path.parent / unquote(url.path)).resolve() if url.path else path
            if not target.is_relative_to(root) or not target.exists():
                errors.append(f"Broken local link: {path.relative_to(root)} -> {link}")
                continue
            if url.fragment and target.suffix.lower() == ".md":
                fragment = unquote(url.fragment)
                target_refs = parsed.get(target) or References(target.read_text(encoding="utf-8"))
                if fragment not in target_refs.ids:
                    errors.append(f"Missing heading anchor: {path.relative_to(root)} -> {link}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    errors = validate(args.root)
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    if errors:
        return 1
    print("PASS: MkDocs configuration, included navigation, output paths, local links and anchors")
    print("No documentation site generated.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
