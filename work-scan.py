import os, sys, zipfile, re, json
from xml.etree import ElementTree as ET

roots = [
    r"D:\Program\Cursor\SNTP-ADEPT\MD",
]
NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
keys = re.compile(r"изыскан|СБЦ|СЦ|геодез|геолог|гидрометео|эколог|базов\w+\s+цен|281/пр|1999|Госстрой", re.I)

def colrow(cell_ref):
    m = re.match(r"([A-Z]+)(\d+)", cell_ref or "")
    return m.group(1) if m else "", m.group(2) if m else ""

def read_shared(z):
    if "xl/sharedStrings.xml" not in z.namelist():
        return []
    root = ET.fromstring(z.read("xl/sharedStrings.xml"))
    out = []
    for si in root.findall("m:si", NS):
        texts = [t.text or "" for t in si.findall(".//m:t", NS)]
        out.append("".join(texts))
    return out

def sheet_names(z):
    # workbook + rels
    wb = ET.fromstring(z.read("xl/workbook.xml"))
    rels = ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))
    relmap = {}
    for rel in rels:
        relmap[rel.attrib.get("Id")] = rel.attrib.get("Target")
    names = []
    for sh in wb.findall("m:sheets/m:sheet", NS):
        rid = sh.attrib.get("{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id")
        target = relmap.get(rid, "")
        if target and not target.startswith("xl/"):
            target = "xl/" + target.lstrip("/")
        names.append((sh.attrib.get("name"), target))
    return names

hits = []
files = []
for root_dir in roots:
    for dirpath, _, filenames in os.walk(root_dir):
        for fn in filenames:
            low = fn.lower()
            if low.endswith((".xlsx", ".xlsm")):
                files.append(os.path.join(dirpath, fn))

report = []
for path in files:
    entry = {"file": os.path.basename(path), "path": path, "sheets": [], "hits": []}
    try:
        z = zipfile.ZipFile(path)
    except Exception as e:
        entry["error"] = str(e)
        report.append(entry)
        continue
    try:
        shared = read_shared(z)
        sheets = sheet_names(z)
        entry["sheets"] = [n for n,_ in sheets]
        # scan shared strings first for keywords
        interesting = []
        for i, s in enumerate(shared):
            if s and keys.search(s):
                interesting.append(s.replace("\n"," ").strip()[:400])
        # also inline strings in sheets if few shared hits - still scan sheet cells that are inline
        # limit interesting unique
        uniq = []
        seen = set()
        for s in interesting:
            key = s[:180]
            if key not in seen:
                seen.add(key)
                uniq.append(s)
        entry["hit_count"] = len(interesting)
        entry["hits"] = uniq[:80]
    except Exception as e:
        entry["error"] = str(e)
    finally:
        z.close()
    report.append(entry)

out = r"D:\Program\Cursor\SNTP-ADEPT\work-xlsx-hits.json"
with open(out, "w", encoding="utf-8") as f:
    json.dump(report, f, ensure_ascii=False, indent=2)
print("files", len(files), "out", out)
for e in report:
    print(e["file"], "sheets", len(e.get("sheets") or []), "hits", e.get("hit_count"), "err", e.get("error"))