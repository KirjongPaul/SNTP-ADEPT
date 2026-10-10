import { useEffect, useState } from "react";
import { api, type AnalogCard, type AnalogDetail } from "./api";
import { SourcePane } from "./SourcePane";
import type { PirDocument } from "./types";

function analogStatus(status: string) {
  if (status === "imported") return "импортирован";
  if (status === "recognized") return "распознан";
  return "не распознан";
}

function fileStamp(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

export function AnalogPane({
  projectId,
  acceptProjectId,
  acceptEstimateId,
  onAccepted,
  onError
}: {
  projectId?: string | null;
  acceptProjectId?: string | null;
  acceptEstimateId?: string | null;
  onAccepted?: (doc: PirDocument) => void;
  onError: (message: string) => void;
}) {
  const [items, setItems] = useState<AnalogCard[]>([]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [detail, setDetail] = useState<AnalogDetail | null>(null);
  const [estimates, setEstimates] = useState<string[]>([]);
  const [positions, setPositions] = useState<string[]>([]);
  const [over, setOver] = useState(0);
  const [busy, setBusy] = useState(false);

  function reload(id: string) {
    api.analogs(id).then((c) => setItems(c.items ?? [])).catch((e) => onError(e instanceof Error ? e.message : String(e)));
  }

  useEffect(() => {
    const isFile = (e: DragEvent) => [...(e.dataTransfer?.types ?? [])].includes("Files");
    const guard = (e: DragEvent) => {
      if (!isFile(e)) return;
      e.preventDefault();
    };
    window.addEventListener("dragover", guard);
    window.addEventListener("drop", guard);
    return () => {
      window.removeEventListener("dragover", guard);
      window.removeEventListener("drop", guard);
    };
  }, []);

  useEffect(() => {
    if (!projectId) {
      setItems([]);
      return;
    }
    reload(projectId);
  }, [projectId]);

  function take(files: File[]) {
    if (!projectId || files.length === 0) return;
    setBusy(true);
    const next = files.slice();
    const step = () => {
      const file = next.shift();
      if (!file) {
        setBusy(false);
        reload(projectId);
        return;
      }
      api.importAnalog(projectId, file).then(step).catch((e) => {
        onError(e instanceof Error ? e.message : String(e));
        step();
      });
    };
    step();
  }

  function show(id: string) {
    if (!projectId) return;
    setOpenId(id);
    setEstimates([]);
    setPositions([]);
    api.analog(projectId, id).then(setDetail).catch((e) => onError(e instanceof Error ? e.message : String(e)));
  }

  if (!projectId) return <p className="muted">Откройте проект. Импорт кладёт файл в его аналоги, а не создаёт новый объект.</p>;

  const read = detail?.read;
  return (
    <div
      className={over > 0 ? "analog-drop is-over" : "analog-drop"}
      onDragEnter={(e) => {
        if (![...e.dataTransfer.types].includes("Files")) return;
        e.preventDefault();
        setOver((n) => n + 1);
      }}
      onDragOver={(e) => {
        if (![...e.dataTransfer.types].includes("Files")) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = "copy";
      }}
      onDragLeave={() => setOver((n) => Math.max(0, n - 1))}
      onDrop={(e) => {
        if (![...e.dataTransfer.types].includes("Files")) return;
        e.preventDefault();
        e.stopPropagation();
        setOver(0);
        take([...e.dataTransfer.files]);
      }}
    >
      <details className="source-fold">
        <summary>Каталог ИД</summary>
        <SourcePane projectId={projectId} onError={onError} />
      </details>
      <div className="analog-bar">
        <button
          type="button"
          className="btn"
          disabled={!projectId || busy}
          title="Открыть папку исходных данных этого проекта"
          onClick={() => {
            if (!projectId) return;
            setBusy(true);
            api.pickAnalogs(projectId)
              .then((c) => setItems(c.items ?? []))
              .catch((e) => onError(e instanceof Error ? e.message : String(e)))
              .finally(() => setBusy(false));
          }}
        >
          Выбрать с диска
        </button>
        <button type="button" className="btn" onClick={() => api.revealAnalogs(projectId).catch((e) => onError(String(e)))}>Открыть папку</button>
        {busy && <span className="muted">Разбор…</span>}
      </div>
      <p className="muted">Перетащите Excel или Grand. После сброса видны закладки и статус. «Импортировать» распознаёт файл и пишет уточнения в рельс. Сметы в проект эта кнопка не копирует.</p>
      {items.length === 0 && <p>Аналогов пока нет.</p>}
      <ul className="analog-list">
        {items.map((item) => (
          <li key={item.id}>
            <div className="analog-row">
              <div className="analog-title">
                <input
                  className="analog-name"
                  aria-label="Название аналога"
                  value={item.systemName}
                  onChange={(e) => {
                    const systemName = e.target.value;
                    setItems((cur) => cur.map((row) => row.id === item.id ? { ...row, systemName } : row));
                  }}
                  onBlur={() => {
                    const systemName = item.systemName.trim();
                    if (!systemName) return;
                    api.renameAnalog(projectId, item.id, systemName).catch((e) => onError(e instanceof Error ? e.message : String(e)));
                  }}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") e.currentTarget.blur();
                  }}
                />
                <button type="button" className={openId === item.id ? "on" : ""} onClick={() => show(item.id)}>
                  <span className="analog-meta muted">
                    <span title={item.sourceName}>{item.sourceName}</span>
                    {item.modified && <span>изменён {fileStamp(item.modified)}</span>}
                    {item.format === "excel" && <span>закладок {item.sheets?.length ?? 0}</span>}
                    {item.format === "grand" && (item.facts ?? []).map((fact) => (
                      <span key={fact.label}>{fact.label.toLocaleLowerCase("ru-RU")} {fact.value}</span>
                    ))}
                    {item.stale && <span>файл изменился</span>}
                    <span>{analogStatus(item.status)}</span>
                  </span>
                </button>
              </div>
              <button
                type="button"
                className="btn"
                disabled={busy}
                title="Распознать файл ещё раз. Сметы в рабочие сметы не копируются"
                onClick={() => {
                  setBusy(true);
                  api.reimportAnalog(projectId, item.id)
                    .then((r) => {
                      setItems(r.items ?? []);
                      if (r.document) onAccepted?.(r.document);
                      show(item.id);
                    })
                    .catch((e) => onError(e instanceof Error ? e.message : String(e)))
                    .finally(() => setBusy(false));
                }}
              >
                Импортировать
              </button>
              <button type="button" className="btn" onClick={() => api.openAnalog(projectId, item.id).catch((e) => onError(String(e)))}>Открыть</button>
              <button
                type="button"
                className="btn danger"
                onClick={() => api.deleteAnalog(projectId, item.id).then((c) => {
                  setItems(c.items ?? []);
                  if (openId === item.id) { setOpenId(null); setDetail(null); }
                }).catch((e) => onError(String(e)))}
              >
                Удалить
              </button>
              {item.stale && <span className="muted">файл на диске новее</span>}
            </div>
            {item.format === "excel" && (item.sheets?.length ?? 0) > 0 && (
              <details className="analog-sheets">
                <summary>Закладки · {item.sheets!.length}</summary>
                <ul>
                  {item.sheets!.map((sheet) => <li key={sheet}>{sheet}</li>)}
                </ul>
              </details>
            )}
            {item.format === "grand" && (item.parts?.length ?? 0) > 0 && (
              <details className="analog-sheets">
                <summary>Разделы · {item.parts!.length}</summary>
                <ul>
                  {item.parts!.map((part) => <li key={part}>{part}</li>)}
                </ul>
              </details>
            )}
          </li>
        ))}
      </ul>
      {read && acceptProjectId && (
        <div className="analog-read">
          <div className="analog-bar">
            <button
              type="button"
              className="btn primary"
              disabled={!acceptProjectId}
              onClick={() => api.acceptAnalogEstimates(acceptProjectId, openId!, estimates).then(onAccepted).catch((e) => onError(String(e)))}
            >
              {estimates.length ? "Принять отмеченные сметы" : "Принять все сметы"}
            </button>
            <button
              type="button"
              className="btn"
              disabled={!acceptEstimateId || positions.length === 0}
              onClick={() => api.acceptAnalogPositions(acceptProjectId, openId!, acceptEstimateId!, positions).then(onAccepted).catch((e) => onError(String(e)))}
            >
              Принять расценки
            </button>
          </div>
          {read.estimates.map((est) => (
            <section key={est.id}>
              <label>
                <input
                  type="checkbox"
                  checked={estimates.includes(est.id)}
                  onChange={() => setEstimates((cur) => cur.includes(est.id) ? cur.filter((x) => x !== est.id) : [...cur, est.id])}
                />
                {est.title || est.id}
              </label>
              <ul>
                {est.positions.filter((p) => p.kind === "work" || p.kind === "text").map((p) => {
                  const score = read.scores.find((s) => s.positionId === p.id);
                  return (
                    <li key={p.id}>
                      <label>
                        <input
                          type="checkbox"
                          checked={positions.includes(p.id)}
                          onChange={() => setPositions((cur) => cur.includes(p.id) ? cur.filter((x) => x !== p.id) : [...cur, p.id])}
                        />
                        {p.name}
                      </label>
                      {score && <span className="muted"> {Math.round(score.confidence * 100)} % · {score.note}</span>}
                    </li>
                  );
                })}
              </ul>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
