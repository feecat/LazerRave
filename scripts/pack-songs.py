"""Create one maximum-deflate ZIP per BMS song without modifying its source."""
import argparse
import csv
import sys
import hashlib
import os
from pathlib import Path
import zipfile

CHARTS = {".bms", ".bme", ".bml", ".pms"}
RESOURCES = CHARTS | {".wav", ".ogg", ".mp3", ".flac", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".avi", ".mpg", ".mpeg", ".mp4", ".wmv", ".webm", ".txt"}


def resources(song):
    for directory, folders, files in os.walk(song, followlinks=False):
        for name in folders + files:
            path = Path(directory) / name
            if path.is_symlink() or path.is_junction():
                raise ValueError(f"Linked resource is not supported: {path}")
        for name in sorted(files):
            path = Path(directory) / name
            if path.suffix.lower() in RESOURCES:
                yield path


def existing_pack(song, output):
    target = output / (song.name + ".zip")
    if not target.is_file():
        return None
    files = sorted(resources(song))
    if any(file.stat().st_mtime > target.stat().st_mtime for file in files):
        return None
    expected = {file.relative_to(song.parent).as_posix(): file.stat().st_size for file in files}
    try:
        with zipfile.ZipFile(target) as archive:
            if {entry.filename: entry.file_size for entry in archive.infolist()} != expected or archive.testzip():
                return None
    except (OSError, zipfile.BadZipFile):
        return None
    with target.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    print(f"Reused {target.name}: existing ZIP verified.", flush=True)
    return target.name, target.stat().st_size, digest


def pack(song, output):
    files = sorted(resources(song))
    if not any(file.suffix.lower() in CHARTS for file in files):
        raise ValueError(f"No BMS charts in {song}")
    total = sum(file.stat().st_size for file in files)
    if len(files) > 10000 or total > 512 * 1024 * 1024:
        raise ValueError(f"Song exceeds the server limits: {song.name}")
    target = output / (song.name + ".zip")
    temporary = target.with_suffix(".zip.part")
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9, strict_timestamps=False) as archive:
            for file in files:
                name = file.relative_to(song.parent).as_posix()
                if len(name) > 240:
                    raise ValueError(f"Resource path exceeds the server limit: {name}")
                archive.write(file, name)
        if temporary.stat().st_size > 128 * 1024 * 1024:
            raise ValueError(f"ZIP exceeds 128 MiB: {song.name}")
        with zipfile.ZipFile(temporary) as archive:
            bad = archive.testzip()
            if bad:
                raise ValueError(f"ZIP checksum failed: {bad}")
        temporary.replace(target)
        with target.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        size = target.stat().st_size
        print(f"{target.name}: {len(files)} files, {size / 1048576:.2f} MiB, SHA256 {digest}", flush=True)
        return target.name, size, digest
    finally:
        temporary.unlink(missing_ok=True)


def write_manifest(output, rows):
    temporary = output / "manifest.tsv.part"
    with temporary.open("w", encoding="utf-8", newline="") as manifest:
        writer = csv.writer(manifest, delimiter="\t")
        writer.writerow(["filename", "size_bytes", "sha256"])
        writer.writerows(rows)
    temporary.replace(output / "manifest.tsv")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "out" / "song-packs")
    selection = parser.add_mutually_exclusive_group()
    selection.add_argument("--limit", type=int, default=3, help="Number of songs; default is a three-song sample.")
    selection.add_argument("--all", action="store_true", help="Package every song folder in the source library.")
    parser.add_argument("--resume", action="store_true", help="Reuse unchanged existing ZIPs after entry and CRC checks.")
    parser.add_argument("--song", action="append", help="Exact song folder name; repeat to choose specific songs.")
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    if (source / "Files").is_dir():
        source /= "Files"
    output = args.output.resolve()
    if output == source or output.is_relative_to(source):
        parser.error("Output must be outside the source song library.")
    if args.limit < 1:
        parser.error("The sample limit must be positive.")
    if args.song:
        songs = [source / name for name in args.song]
        if any(song.parent != source or not song.is_dir() for song in songs):
            parser.error("Each song must name an existing direct child folder.")
    else:
        songs = sorted((path for path in source.iterdir() if path.is_dir()), key=lambda p: p.name)
        if not args.all:
            songs = songs[:args.limit]
    output.mkdir(parents=True, exist_ok=True)
    rows, failures = [], []
    write_manifest(output, rows)
    for index, song in enumerate(songs, 1):
        print(f"[{index}/{len(songs)}] {song.name}", flush=True)
        try:
            row = existing_pack(song, output) if args.resume else None
            rows.append(row or pack(song, output))
            write_manifest(output, rows)
        except (OSError, ValueError, zipfile.BadZipFile) as error:
            failures.append((song.name, str(error)))
            print(f"FAILED: {song.name}: {error}", flush=True)
    with (output / "failures.tsv").open("w", encoding="utf-8", newline="") as report:
        writer = csv.writer(report, delimiter="\t")
        writer.writerow(["song", "error"])
        writer.writerows(failures)
    print(f"Created or verified {len(rows)} song packs; {len(failures)} failures. Source files retained.", flush=True)
    return 1 if failures else 0



if __name__ == "__main__":
    sys.exit(main())
