#!/usr/bin/env python3
"""Export the TITHE content tables to Unreal-Engine-ready data files.

The whole game is data-driven off embedded JSON string constants in
DofusSlice.Core/Content/Tithe/TitheTables.cs. For a fresh Unreal rebuild we want that
balance-tuned data as (a) clean JSON (faithful, Unreal DataTable JSON-import shape:
`[{"Name": <id>, ...fields}]`), and (b) flat CSV for the simple stat tables (import as a
DataTable, or edit in a spreadsheet). Nested tables (skills' effects/ranks, sets' tiers)
stay JSON only.

    python3 tools/export_unreal_datatables.py
    # writes docs/unreal-port/data/*.json and *.csv
"""
import csv
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "DofusSlice/DofusSlice.Core/Content/Tithe/TitheTables.cs"
OUT = ROOT / "docs/unreal-port/data"

# const name in TitheTables.cs -> (output basename, key field to use as the DataTable row "Name")
TABLES = {
    "ClassesJson":   ("classes", "id"),
    "MobsJson":      ("mobs", "id"),
    "SkillsJson":    ("skills", "key"),
    "EssencesJson":  ("essences", "name"),
    "ItemsJson":     ("items", "id"),
    "SetsJson":      ("sets", "id"),
}
# tables to ALSO flatten to CSV (top-level scalar fields only; arrays/objects are skipped
# with a note, since CSV can't hold them cleanly — those live in the JSON).
CSV_TABLES = {"classes", "mobs", "items", "essences"}


def extract_blocks(text):
    # Yield (const_name, parsed_json) for each raw-string-literal JSON table in TitheTables.cs.
    pat = re.compile(r'public\s+const\s+string\s+(\w+)\s*=\s*"""(.*?)"""', re.DOTALL)
    for m in pat.finditer(text):
        name, body = m.group(1), m.group(2)
        if name not in TABLES:
            continue
        yield name, json.loads(body)


def to_datatable_json(rows, key):
    """Unreal DataTable JSON import shape: an array of objects each with a 'Name'."""
    out = []
    for r in rows:
        row = {"Name": str(r.get(key, ""))}
        row.update(r)
        out.append(row)
    return out


def flat_csv(rows):
    """Union of scalar columns across rows; list/dict fields are dropped (kept in JSON)."""
    cols, skipped = [], set()
    for r in rows:
        for k, v in r.items():
            if isinstance(v, (list, dict)):
                skipped.add(k)
            elif k not in cols:
                cols.append(k)
    return cols, skipped


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    text = SRC.read_text()
    made = []
    for name, rows in extract_blocks(text):
        base, key = TABLES[name]
        # JSON (faithful + Unreal DataTable-import shaped)
        (OUT / f"{base}.json").write_text(json.dumps(to_datatable_json(rows, key), indent=2))
        made.append(f"{base}.json ({len(rows)} rows)")
        # CSV for the flat tables
        if base in CSV_TABLES:
            cols, skipped = flat_csv(rows)
            with (OUT / f"{base}.csv").open("w", newline="") as f:
                w = csv.writer(f)
                w.writerow(["Name"] + cols)
                for r in rows:
                    w.writerow([r.get(key, "")] + [r.get(c, "") for c in cols])
            note = f" (dropped nested cols → see JSON: {sorted(skipped)})" if skipped else ""
            made.append(f"{base}.csv{note}")
    print("wrote to", OUT)
    for m in made:
        print("  -", m)


if __name__ == "__main__":
    main()
