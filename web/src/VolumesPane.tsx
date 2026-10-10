import { useEffect, useState } from "react";
import { api, type ReferenceBook } from "./api";
import { NotesHelp } from "./BooksPane";
import type { PirDocument, SurveyParameter, SurveySheet, WorkVolume } from "./types";

export function VolumesPane({
  projectId,
  est,
  survey,
  onDoc,
  onError,
  onOpenBook
}: {
  projectId: string;
  est: { id: string; volumes?: WorkVolume[] };
  survey?: SurveySheet;
  onDoc: (doc: PirDocument) => void;
  onError: (message: string) => void;
  onOpenBook?: (place: { bookId: string; table: string; item?: string }) => void;
}) {
  const volumes = est.volumes ?? [];
  const [picker, setPicker] = useState(false);
  const [edited, setEdited] = useState<SurveyParameter | null>(null);
  useEffect(() => {
    if (volumes.length > 0) return;
    api.deriveVolumes(projectId, est.id).then(onDoc).catch((e) => onError(e instanceof Error ? e.message : String(e)));
  }, [est.id]);

  function save(vol: WorkVolume, quantity: number) {
    api.patchVolume(projectId, est.id, vol.id, { quantity }).then(onDoc).catch((e) => onError(e instanceof Error ? e.message : String(e)));
  }

  return (
    <div className="volumes">
      <div className="desk-cmds">
        <button type="button" className="btn" onClick={() => setPicker((v) => !v)}>Из сборника</button>
        <button type="button" className="btn" onClick={() => api.deriveVolumes(projectId, est.id).then(onDoc).catch((e) => onError(String(e)))}>
          Собрать из сметы
        </button>
        <button type="button" className="btn primary" onClick={() => api.applyVolumes(projectId, est.id).then(onDoc).catch((e) => onError(String(e)))}>
          Обновить к смете
        </button>
        <button type="button" className="btn" onClick={() => api.buildSurvey(projectId).then(onDoc).catch((e) => onError(e instanceof Error ? e.message : String(e)))}>
          Сформировать объёмы по изысканиям
        </button>
      </div>
      <p className="muted">Количество выбирает полосу пункта: в пределах, ниже или выше диапазона. Строка с единицей «объект» берётся укрупнённо и от величины показателя не зависит. «Открыть в справочник» показывает эту таблицу в аналогах.</p>
      {picker && (
        <BookVolumePicker
          onError={onError}
          onAdd={(bookId, table, item) => {
            api.addVolumeFromBook(projectId, est.id, bookId, table, item).then((d) => { setPicker(false); onDoc(d); }).catch((e) => onError(e instanceof Error ? e.message : String(e)));
          }}
        />
      )}
      <table className="ledger">
        <thead>
          <tr><th>Показатель</th><th>Ед.</th><th className="num">Количество</th><th>Сборник</th><th>Статус</th></tr>
        </thead>
        <tbody>
          {volumes.map((vol) => (
            <VolumeRow key={vol.id} vol={vol} onCommit={(q) => save(vol, q)} onOpenBook={onOpenBook} />
          ))}
          {volumes.length === 0 && <tr><td colSpan={5}>В смете нет показателей.</td></tr>}
        </tbody>
      </table>
      {survey && (
        <SurveyTable
          survey={survey}
          onEdit={setEdited}
        />
      )}
      {edited && (
        <SurveyDialog
          row={survey?.parameters.find((p) => p.id === edited.id) ?? edited}
          onClose={() => setEdited(null)}
          onSave={(body) => {
            api.patchSurvey(projectId, edited.id, body)
              .then((d) => { onDoc(d); setEdited(null); })
              .catch((e) => onError(e instanceof Error ? e.message : String(e)));
          }}
        />
      )}
    </div>
  );
}

function SurveyTable({
  survey,
  onEdit
}: {
  survey: SurveySheet;
  onEdit: (row: SurveyParameter) => void;
}) {
  return (
    <div className="survey-sheet">
      <h3>{survey.title}</h3>
      <p className="muted">Геология линейного газопровода. Длину, заложение, промерзание, диаметр и категорию грунта задаёт проект. Расстояние и глубину скважины считает таблица 7.2 СП 446.1325800.2019. Погонные метры уходят в таблицу 14 НЗ 281/пр.</p>
      <table className="ledger">
        <thead>
          <tr><th>Параметр</th><th>Ед.</th><th className="num">Значение</th><th>Откуда</th><th>Норма</th></tr>
        </thead>
        <tbody>
          {survey.parameters.map((row) => (
            <tr key={row.id} className="click" onClick={() => onEdit(row)}>
              <td>{row.name}{row.manual ? " · вручную" : ""}</td>
              <td>{row.unit || "—"}</td>
              <td className="num">{showQuantity(row)}</td>
              <td>{row.origin === "user" ? "проект" : "правило"}</td>
              <td>{row.normClause ? `п. ${row.normClause}` : "—"}{row.normTable ? `, табл. ${row.normTable}` : ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SurveyDialog({
  row,
  onClose,
  onSave
}: {
  row: SurveyParameter;
  onClose: () => void;
  onSave: (body: { quantity?: number; text?: string }) => void;
}) {
  const [value, setValue] = useState(row.text ?? (row.quantity == null ? "" : String(row.quantity)));
  const href = row.normNd ? `http://techexpert/docs/d?nd=${row.normNd}` : null;
  return (
    <div className="modal" onClick={onClose}>
      <form
        className="dialog wide"
        onClick={(e) => e.stopPropagation()}
        onSubmit={(e) => {
          e.preventDefault();
          if (row.id === "category") onSave({ text: value.trim() });
          else {
            const n = Number(value.replace(",", "."));
            if (!Number.isFinite(n)) return;
            onSave({ quantity: n });
          }
        }}
      >
        <h2>{row.name}</h2>
        <p className="muted">{row.why}</p>
        {href && (
          <p className="muted">
            <a className="book-origin" href={href} target="_blank" rel="noopener">
              Оригинал в Кодексе{row.normClause ? `, п. ${row.normClause}` : ""}
            </a>
          </p>
        )}
        {row.quote && <blockquote className="survey-quote">{row.quote}</blockquote>}
        <label>
          {row.id === "category" ? "Категория" : "Число"}
          <input value={value} onChange={(e) => setValue(e.target.value)} autoFocus />
        </label>
        {row.uses.length > 0 && (
          <div>
            {row.uses.map((use) => (
              <div key={use.table + (use.item ?? "")}>{use.orderRef}, табл. {use.table}{use.item ? ` п. ${use.item}` : ""}. {use.note}</div>
            ))}
          </div>
        )}
        <div className="survey-protocol">
          {row.protocol.map((line) => <div key={line}>{line}</div>)}
        </div>
        <div className="dialog-actions">
          <button type="button" onClick={onClose}>Закрыть</button>
          <button type="submit" className="primary">Записать</button>
        </div>
      </form>
    </div>
  );
}

function showQuantity(row: SurveyParameter) {
  if (row.text) return row.text;
  if (row.quantity == null) return "—";
  const from = formatQty(row.quantity);
  if (row.quantityTo == null || row.quantityTo === row.quantity) return from;
  return `${from}–${formatQty(row.quantityTo)}`;
}

function formatQty(n: number) {
  return n.toLocaleString("ru-RU", { maximumFractionDigits: 3 });
}

function VolumeRow({
  vol,
  onCommit,
  onOpenBook
}: {
  vol: WorkVolume;
  onCommit: (quantity: number) => void;
  onOpenBook?: (place: { bookId: string; table: string; item?: string }) => void;
}) {
  const [value, setValue] = useState(vol.quantity == null ? "" : String(vol.quantity));
  useEffect(() => setValue(vol.quantity == null ? "" : String(vol.quantity)), [vol.quantity]);
  const raw = vol.table ? `Таблица ${vol.table}. п.${vol.item ?? ""} ${vol.bookId ?? ""}` : "";
  const pick = vol.pick;
  const warn = !pick && vol.protocol.length > 0;
  return (
    <>
      <tr>
        <td>
          <div className="vol-title">
            <span>{vol.name}</span>
            {vol.bookId && vol.table && onOpenBook && (
              <button
                type="button"
                className="btn"
                onClick={() => onOpenBook({ bookId: vol.bookId!, table: vol.table!, item: vol.item })}
              >
                Открыть в справочник
              </button>
            )}
          </div>
        </td>
        <td>{vol.unit || "—"}</td>
        <td className="num">
          <input
            value={value}
            onChange={(e) => setValue(e.target.value)}
            onBlur={(e) => {
              const n = Number(e.currentTarget.value.replace(",", "."));
              if (Number.isFinite(n) && n !== vol.quantity) onCommit(n);
            }}
          />
        </td>
        <td>{vol.bookId || "—"} {vol.table ? `табл. ${vol.table}` : ""} {vol.item ? `п.${vol.item}` : ""}</td>
        <td>{statusLabel(vol.status)}</td>
      </tr>
      {pick && (
        <tr>
          <td colSpan={5}>
            п.{pick.item} · {bandSpan(pick.xmin)}…{bandSpan(pick.xmax)} {pick.unit ?? vol.unit ?? ""} · {kindLabel(pick.kind)}
            {pick.a != null ? ` · а ${bandSpan(pick.a)}` : ""}
            {pick.b != null ? ` · в ${bandSpan(pick.b)}` : ""}
            <NotesHelp raw={raw.includes("849") || vol.bookId === "nz-849-2023" ? `НЗ №849/пр Таблица ${vol.table}. п.${vol.item}` : raw} />
          </td>
        </tr>
      )}
      {warn && (
        <tr>
          <td colSpan={5}>
            {vol.protocol.map((line) => <div key={line}>{line}</div>)}
          </td>
        </tr>
      )}
    </>
  );
}

function bandSpan(n?: number) {
  if (n == null || Number.isNaN(n)) return "—";
  return n.toLocaleString("ru-RU", { maximumFractionDigits: 3 });
}

function kindLabel(kind: string) {
  if (kind === "within") return "в пределах диапазона";
  if (kind === "below") return "ниже диапазона";
  if (kind === "below-half") return "ниже диапазона, Кпон";
  if (kind === "above") return "выше диапазона";
  if (kind === "lump") return "укрупнённо";
  return kind;
}

function BookVolumePicker({
  onAdd,
  onError
}: {
  onAdd: (bookId: string, table: string, item: string) => void;
  onError: (message: string) => void;
}) {
  const [book, setBook] = useState<ReferenceBook | null>(null);
  const [origin, setOrigin] = useState<string | null>(null);
  useEffect(() => {
    api.books()
      .then((list) => (list[0] ? api.book(list[0].id) : Promise.reject(new Error("Сборников нет"))))
      .then((r) => { setBook(r.book); setOrigin(r.codexHref || null); })
      .catch((e) => onError(e instanceof Error ? e.message : String(e)));
  }, []);
  if (!book) return <p className="muted">Читаю сборник…</p>;
  const rows = book.tables
    .filter((t) => t.kind === "price")
    .flatMap((t) => {
      const seen = new Set<string>();
      return t.items.filter((item) => {
        if (seen.has(item.n)) return false;
        seen.add(item.n);
        return true;
      }).map((item) => ({ table: t.code, item }));
    });
  return (
    <div className="book-notes volume-picker">
      <h4>Объёмы из {book.orderRef}</h4>
      {origin && <p className="muted"><a className="book-origin" href={origin} target="_blank" rel="noopener">Оригинал в Кодексе</a></p>}
      <ul>
        {rows.map((row) => (
          <li key={row.table + row.item.n}>
            <button type="button" onClick={() => onAdd(book.id, row.table, row.item.n)}>
              Табл. {row.table} п. {row.item.n}. {row.item.name}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

function statusLabel(status: string) {
  if (status === "book-not-entered") return "сборник ещё не введён";
  if (status === "group") return "группа пунктов";
  if (status === "updated") return "расценка подобрана";
  if (status === "from-book") return "из сборника, задайте количество";
  return "из сметы";
}
