import { useEffect, useMemo, useState } from "react";
import { api } from "./api";
import { bookLabels, bookOfHit, BOOKS, inSection, sectionFor, SECTIONS, type PirSection } from "./sections";
import type { Estimate } from "./types";

type Hit = {
  status: string;
  code: string;
  name: string;
  priceA?: number;
  priceB?: number;
  hasPrices: boolean;
  source: string;
  kind?: string;
  doc?: string;
};

export function Composer({
  estimate,
  onInsert
}: {
  estimate: Estimate;
  onInsert: (hit: Hit) => void;
}) {
  const initial = sectionFor(estimate.number, estimate.title) ?? sectionFor(estimate.chapterCaption, estimate.title);
  const [section, setSection] = useState<PirSection | undefined>(initial);
  const [open, setOpen] = useState(false);
  const [wider, setWider] = useState(false);
  const [q, setQ] = useState("");
  const [hits, setHits] = useState<Hit[]>([]);
  const [hi, setHi] = useState(0);
  const [show, setShow] = useState(false);

  useEffect(() => {
    setSection(sectionFor(estimate.number, estimate.title) ?? sectionFor(estimate.chapterCaption, estimate.title));
  }, [estimate.id, estimate.number, estimate.chapterCaption]);

  const pool = useMemo(() => (wider ? hits : hits.filter((h) => inSection(h, section))), [hits, wider, section]);

  useEffect(() => {
    if (!q.trim()) {
      setHits([]);
      return;
    }
    const t = setTimeout(() => {
      api
        .searchLine(q)
        .then((r) => setHits(r.hits))
        .catch(() => setHits([]));
    }, 180);
    return () => clearTimeout(t);
  }, [q]);

  const labels = bookLabels(section);
  const empty =
    q.trim() && hits.length > 0 && pool.length === 0
      ? "В группе раздела нет совпадений. Откройте «Все сборники» или смените раздел."
      : q.trim() && pool.length === 0
        ? "Нет совпадений."
        : "";

  return (
    <div className="composer">
      <button
        className="composer-sec"
        type="button"
        aria-expanded={open}
        title="Раздел задаёт группу справочников"
        onClick={() => setOpen((v) => !v)}
      >
        <span className="n">{section?.n ?? "—"}</span>
        <span>{section?.short ?? "раздел"}</span>
      </button>
      <div className="composer-field">
        <input
          value={q}
          placeholder={wider ? "Искать во всех сборниках" : `Искать в ${labels}`}
          autoComplete="off"
          onChange={(e) => {
            setQ(e.target.value);
            setHi(0);
            setShow(true);
          }}
          onFocus={() => setShow(true)}
          onBlur={() => setTimeout(() => setShow(false), 160)}
          onKeyDown={(e) => {
            if (e.key === "ArrowDown") {
              e.preventDefault();
              setHi((i) => Math.min(pool.length - 1, i + 1));
            } else if (e.key === "ArrowUp") {
              e.preventDefault();
              setHi((i) => Math.max(0, i - 1));
            } else if (e.key === "Enter" && pool[hi]) {
              e.preventDefault();
              onInsert(pool[hi]);
              setQ("");
              setShow(false);
            } else if (e.key === "Escape") {
              setShow(false);
              setOpen(false);
            }
          }}
        />
        {show && (pool.length > 0 || empty) && (
          <div className="compose-hits" role="listbox">
            {empty && <div className="compose-empty">{empty}</div>}
            {pool.slice(0, 8).map((h, i) => (
              <button
                key={h.code + i}
                type="button"
                className={i === hi ? "compose-hit is-on" : "compose-hit"}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => {
                  onInsert(h);
                  setQ("");
                  setShow(false);
                }}
              >
                <code>{h.code}</code>
                <span className="nm">{h.name}</span>
                <span className="bk">{BOOKS[bookOfHit(h)]?.short ?? h.source}</span>
              </button>
            ))}
          </div>
        )}
      </div>
      <button type="button" className={wider ? "is-on" : ""} onClick={() => setWider((v) => !v)}>
        {wider ? "Группа раздела" : "Все сборники"}
      </button>
      {open && (
        <div className="sec-pop" role="listbox">
          <div className="sec-pop-head">Раздел задаёт группу справочников</div>
          {SECTIONS.map((sec) => (
            <button
              key={sec.id}
              type="button"
              className={sec.id === section?.id ? "is-on" : ""}
              onClick={() => {
                setSection(sec);
                setOpen(false);
                setWider(false);
              }}
            >
              <span className="n">{sec.n}</span>
              <span>{sec.name}</span>
              <small>{bookLabels(sec)}</small>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
