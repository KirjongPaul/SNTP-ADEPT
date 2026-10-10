import { useEffect, useState } from "react";
import { api, type SourceFile } from "./api";

function fileStamp(iso: string) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

export function SourcePane({
  projectId,
  onError
}: {
  projectId?: string | null;
  onError: (message: string) => void;
}) {
  const [items, setItems] = useState<SourceFile[]>([]);
  const [over, setOver] = useState(false);

  function reload(id: string) {
    api.sources(id).then((c) => setItems(c.items ?? [])).catch((e) => onError(e instanceof Error ? e.message : String(e)));
  }

  useEffect(() => {
    if (!projectId) {
      setItems([]);
      return;
    }
    reload(projectId);
  }, [projectId]);

  function take(files: File[]) {
    if (!projectId || files.length === 0) return;
    api.addSources(projectId, files).then((c) => setItems(c.items ?? [])).catch((e) => onError(e instanceof Error ? e.message : String(e)));
  }

  return (
    <div
      className={over ? "source-pane is-over" : "source-pane"}
      onDragOver={(e) => {
        if (![...e.dataTransfer.types].includes("Files")) return;
        e.preventDefault();
        e.stopPropagation();
        setOver(true);
      }}
      onDragLeave={() => setOver(false)}
      onDrop={(e) => {
        if (![...e.dataTransfer.types].includes("Files")) return;
        e.preventDefault();
        e.stopPropagation();
        setOver(false);
        take([...e.dataTransfer.files]);
      }}
    >
      <div className="source-bar">
        <label className="btn">
          Добавить файлы
          <input
            type="file"
            multiple
            hidden
            onChange={(e) => {
              const files = [...(e.target.files ?? [])];
              e.target.value = "";
              take(files);
            }}
          />
        </label>
        <button
          type="button"
          className="btn"
          disabled={!projectId}
          onClick={() => projectId && api.revealSources(projectId).catch((e) => onError(e instanceof Error ? e.message : String(e)))}
        >
          Открыть папку
        </button>
      </div>
      <p className="muted">Задание, технические требования, программа изысканий и другие документы. В смету не переносятся: откройте файл в своём приложении.</p>
      {items.length === 0 && <p>В каталоге пока нет файлов. Перетащите их сюда.</p>}
      <ul className="source-list">
        {items.map((item) => (
          <li key={item.name}>
            <button
              type="button"
              title="Открыть в приложении"
              onClick={() => projectId && api.openSource(projectId, item.name).catch((e) => onError(e instanceof Error ? e.message : String(e)))}
            >
              <b>{item.name}</b>
              <span className="muted">{item.kind} · {fileStamp(item.modified)}</span>
            </button>
            <button
              type="button"
              className="btn danger"
              onClick={() => projectId && api.removeSource(projectId, item.name).then((c) => setItems(c.items ?? [])).catch((e) => onError(e instanceof Error ? e.message : String(e)))}
            >
              Удалить
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
