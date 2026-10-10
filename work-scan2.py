import os, zipfile, re, json
from xml.etree import ElementTree as ET
NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
roots = [r"D:\Program\Cursor\SNTP-ADEPT\MD"]
pat = re.compile(r"СБЦ\s*-?\s*ИИ|ПНИИИС|9-4/84|СК-1976|гидрограф|гидрометео|базовых цен на инженер|ГОССТРОЙ|Госстрой|1999\s*г|Москва\.\s*1999", re.I)

def shared(z):
    if "xl/sharedStrings.xml" not in z.namelist():
        return []
    root = ET.fromstring(z.read("xl/sharedStrings.xml"))
    out = []
    for si in root.findall("m:si", NS):
        texts = [t.text or "" for t in si.findall(".//m:t", NS)]
        out.append("".join(texts))
    return out

found = []
for dirpath, _, filenames in os.walk(roots[0]):
    for fn in filenames:
        if not fn.lower().endswith((".xlsx", ".xlsm")):
            continue
        path = os.path.join(dirpath, fn)
        try:
            z = zipfile.ZipFile(path)
            strings = shared(z)
            z.close()
        except Exception as e:
            found.append({"file": fn, "error": str(e)})
            continue
        hits = []
        for s in strings:
            if s and pat.search(s):
                t = re.sub(r"\s+", " ", s).strip()
                if len(t) > 40:
                    hits.append(t[:1200])
        # unique
        uniq = []
        seen = set()
        for h in hits:
            k = h[:240]
            if k not in seen:
                seen.add(k)
                uniq.append(h)
        if uniq:
            found.append({"file": fn, "hits": uniq[:25]})

out = r"D:\Program\Cursor\SNTP-ADEPT\work-sbc-cites.json"
json.dump(found, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("groups", len(found))