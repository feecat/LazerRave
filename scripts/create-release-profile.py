"""Emit a fresh OpenLR2 player database as base64, without reading player data."""
import base64
import hashlib
from pathlib import Path
import re
import sqlite3


def create_profile():
    repository = Path(__file__).resolve().parents[1]
    source = (repository / "src/OpenLR2/LR2/LR2_songmanage.cpp").read_text(encoding="utf-8")
    connection = sqlite3.connect(":memory:")
    try:
        for table in ("player", "score"):
            match = re.search(r'SQL_Run\("(CREATE TABLE ' + table + r'\([^"\n]+\))",', source)
            if not match:
                raise RuntimeError(f"OpenLR2 {table} schema could not be found")
            connection.execute(match.group(1))
        columns = [row[1] for row in connection.execute("PRAGMA table_info(player)")]
        values = dict.fromkeys(columns, 0)
        password_hash = hashlib.md5(b"").hexdigest()
        values.update(id="Player", name="Player", hash=password_hash, irid="", irhash="",
                      systemversion=1, trial=1)
        prefix = ("bad", "clear", "combo", "fail", "good", "great", "maxcombo", "perfect",
                  "playcount", "playtime", "poor")
        suffix = ("grade_9", "grade_10", "grade_14", "grade_5", "grade_7", "gradeversion",
                  "trial", "trialversion", "systemversion", "option")
        checksum = "".join(str(values[key]) for key in prefix) + password_hash
        checksum += "".join(str(values[key]) for key in suffix) + "1"
        values["scorehash"] = hashlib.md5(checksum.encode("ascii")).hexdigest()
        connection.execute("INSERT INTO player VALUES (" + ",".join("?" for _ in columns) + ")",
                           [values[column] for column in columns])
        connection.commit()
        return connection.serialize()
    finally:
        connection.close()


if __name__ == "__main__":
    print(base64.b64encode(create_profile()).decode("ascii"))
