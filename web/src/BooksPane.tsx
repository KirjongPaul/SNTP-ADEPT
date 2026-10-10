import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { api, type BookNote, type BookSummary, type FactorVariable, type OfferGroup, type ReferenceBook, type UserBookNote } from "./api";
import type { ProjectCondition } from "./types";
import { volumeClipText, type VolumeClip } from "./volumeClip";

export type BookPlace = { bookId: string; table?: string; item?: string; token: number };

export function BooksPane({
  resetHome = 0,
  findHost = null,
  place = null,
  onInsert,
  conditions,
  estimateConditions,
  estimateSuppressed,
  onToggleCondition,
  onToggleEstimate,
  localIds,
  onToggleLocal
}: {
  resetHome?: number;
  findHost?: HTMLElement | null;
  place?: BookPlace | null;
  onInsert?: (bookId: string, table: string, item: string) => void;
  conditions?: ProjectCondition[];
  estimateConditions?: ProjectCondition[];
  estimateSuppressed?: string[];
  localIds?: string[];
  onToggleCondition?: (variable: FactorVariable) => void;
  onToggleEstimate?: (variable: FactorVariable, on: boolean) => void;
  onToggleLocal?: (variable: FactorVariable) => void;
}) {
  const [list, setList] = useState<BookSummary[]>([]);
  const [book, setBook] = useState<ReferenceBook | null>(null);
  const [variables, setVariables] = useState<FactorVariable[]>([]);
  const [practice, setPractice] = useState<UserBookNote[]>([]);
  const [offers, setOffers] = useState<OfferGroup["offers"]>([]);
  const [report, setReport] = useState<string | null>(null);
  const [origin, setOrigin] = useState<string | null>(null);
  const [anchor, setAnchor] = useState<string | null>(null);
  const [hot, setHot] = useState<{ row: string; table: string; item: string; clip: VolumeClip } | null>(null);
  const [copied, setCopied] = useState("");
  const [err, setErr] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [hit, setHit] = useState({ index: 0, total: 0 });
  const findRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== "f" || !book) return;
      e.preventDefault();
      findRef.current?.focus();
      findRef.current?.select();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [book]);

  useEffect(() => {
    const root = document.querySelector(".book-body");
    if (!(root instanceof HTMLElement)) return;
    clearMarks(root);
    const q = query.trim();
    if (q.length < 2) {
      setHit({ index: 0, total: 0 });
      return () => clearMarks(root);
    }
    markText(root, q);
    const marks = [...root.querySelectorAll("mark.find-hit")];
    const first = marks[0];
    if (first) {
      first.classList.add("is-on");
      const box = first.closest("details");
      if (box instanceof HTMLDetailsElement) box.open = true;
      first.scrollIntoView({ block: "center" });
    }
    setHit({ index: first ? 1 : 0, total: marks.length });
    return () => clearMarks(root);
  }, [query, book, anchor]);

  function moveHit(dir: 1 | -1) {
    const root = document.querySelector(".book-body");
    if (!(root instanceof HTMLElement)) return;
    const marks = [...root.querySelectorAll<HTMLElement>("mark.find-hit")];
    if (marks.length === 0) return;
    const current = marks.findIndex((mark) => mark.classList.contains("is-on"));
    const nextIndex = current < 0 ? (dir > 0 ? 0 : marks.length - 1) : (current + dir + marks.length) % marks.length;
    marks.forEach((mark) => mark.classList.remove("is-on"));
    const next = marks[nextIndex];
    next.classList.add("is-on");
    const box = next.closest("details");
    if (box instanceof HTMLDetailsElement) box.open = true;
    next.scrollIntoView({ block: "center" });
    setHit({ index: nextIndex + 1, total: marks.length });
  }

  function markCopied(row: string) {
    setCopied(row);
    window.setTimeout(() => setCopied((current) => (current === row ? "" : current)), 1600);
  }

  function copyHot() {
    if (!hot) return;
    markCopied(hot.row);
    void navigator.clipboard.writeText(volumeClipText(hot.clip)).catch(() => {
      setCopied("");
      setErr("Буфер не записан");
    });
  }

  useEffect(() => {
    if (!hot || !book) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.repeat) return;
      const target = e.target as HTMLElement | null;
      if (target?.closest("input, textarea, select")) return;
      if (e.key === "Insert" && onInsert) {
        e.preventDefault();
        onInsert(book.id, hot.table, hot.item);
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "c" && !window.getSelection()?.toString()) {
        e.preventDefault();
        copyHot();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [hot, book, onInsert]);

  useEffect(() => {
    api.books().then(setList).catch((e) => setErr(String(e)));
  }, []);

  function closeBook() {
    setBook(null);
    setVariables([]);
    setPractice([]);
    setOffers([]);
    setReport(null);
    setOrigin(null);
    setAnchor(null);
    setQuery("");
    setHit({ index: 0, total: 0 });
    setHot(null);
  }

  useEffect(() => {
    if (!resetHome) return;
    closeBook();
  }, [resetHome]);

  function open(id: string) {
    setAnchor(null);
    api.book(id).then((r) => { setBook(r.book); setReport(r.report ?? null); setVariables(r.variables ?? []); setOrigin(r.codexHref || null); }).catch((e) => setErr(String(e)));
    api.userNotes(id).then(setPractice).catch(() => setPractice([]));
    api.offers().then((groups) => setOffers(groups.find((g) => g.bookId === id)?.offers ?? [])).catch(() => setOffers([]));
  }

  useEffect(() => {
    if (!place?.bookId || book?.id === place.bookId) return;
    open(place.bookId);
  }, [place?.bookId, place?.token]);

  useEffect(() => {
    if (!book || !place || book.id !== place.bookId || !place.table) return;
    let alive = true;
    const run = () => {
      if (!alive) return;
      const table = document.getElementById(`book-table-${place.table}`);
      if (table instanceof HTMLDetailsElement) table.open = true;
      const item = place.item ? document.getElementById(`book-item-${place.table}-${place.item}`) : table;
      if (item) revealBookRow(item);
    };
    const frame = window.requestAnimationFrame(run);
    const later = window.setTimeout(run, 80);
    return () => {
      alive = false;
      window.cancelAnimationFrame(frame);
      window.clearTimeout(later);
    };
  }, [book, place?.bookId, place?.table, place?.item, place?.token]);

  return (
    <div className="books">
      {err && <p className="err">{err}</p>}
      {!book && (
        <table className="ledger">
          <thead>
            <tr><th>Сборник</th><th>Реквизиты</th><th>Статус</th><th className="num">Таблиц</th><th className="num">Пунктов</th><th className="num">Справок</th></tr>
          </thead>
          <tbody>
            {list.map((b) => (
              <tr key={b.id} className="click" onClick={() => open(b.id)}>
                <td>{b.title || b.id}</td>
                <td>{b.orderRef}</td>
                <td>{b.status}</td>
                <td className="num">{b.tables}</td>
                <td className="num">{b.items}</td>
                <td className="num">{b.notes}</td>
              </tr>
            ))}
            {list.length === 0 && <tr><td colSpan={6}>Сборников нет. Каталог читается из папки сборников.</td></tr>}
          </tbody>
        </table>
      )}
      {book && (
        <div className="book-view">
          <nav className="book-toc" aria-label="Содержание сборника">
            <button type="button" onClick={() => jump("book-passport")}>Паспорт</button>
            <button type="button" onClick={() => jump("book-general")}>Общие положения</button>
            {book.generalPart.map((c) => (
              <button key={c.id} type="button" className="sub" onClick={() => jump(`book-clause-${c.id}`)}>{c.id}. {clauseTitle(c.text)}</button>
            ))}
            <button type="button" onClick={() => jump("book-rules")}>Правила расчёта</button>
            {variables.length > 0 && <button type="button" className="factor" onClick={() => jump("book-factors")}>Усложняющие факторы</button>}
            <button type="button" onClick={() => jump("book-tables")}>Таблицы</button>
            {book.tables.filter((t) => t.kind === "price").map((t) => (
              <button key={t.code} type="button" className="sub" onClick={() => jump(`book-table-${t.code}`)}>Таблица {t.code}</button>
            ))}
            {offers.length > 0 && <button type="button" className="audit" onClick={() => jump("book-offers")}>Доп. сметы</button>}
            {report && <button type="button" className="audit" onClick={() => jump("book-report")}>Сверка</button>}
          </nav>
          <div className="book-column">
          <div className="book-body">
            {findHost && createPortal(
              <div className="book-find">
                <input
                  ref={findRef}
                  value={query}
                  placeholder="Найти, Ctrl+F"
                  aria-label="Найти в сборнике"
                  onChange={(e) => setQuery(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Escape") setQuery("");
                    if (e.key === "Enter") {
                      e.preventDefault();
                      moveHit(e.shiftKey ? -1 : 1);
                    }
                  }}
                />
                <button type="button" className="btn" disabled={hit.total === 0} onClick={() => moveHit(-1)}>Вверх</button>
                <button type="button" className="btn" disabled={hit.total === 0} onClick={() => moveHit(1)}>Вниз</button>
                {hit.total > 0 && <span className="count">{hit.index} / {hit.total}</span>}
              </div>,
              findHost
            )}
            <h3 id="book-passport">{book.title}</h3>
            <p className="muted">
              {book.orderRef} · уровень цен {book.priceLevel || "—"} · ФРСН {book.frsnMatch ? "совпало" : "не сверено"} · {book.status}
              {origin && <> · <a className="book-origin" href={origin} target="_blank" rel="noopener">Оригинал в Кодексе</a></>}
            </p>
            <h4 id="book-general">Общие положения</h4>
            {book.generalPart.map((c) => {
              const kind = textKind(c.text);
              return (
                <div key={c.id} id={`book-clause-${c.id}`} className={kind ? `book-clause is-${kind}` : "book-clause"}>
                  {kind && <span className="book-kicker">{kind === "excluded" ? "Не учтено в цене" : "Усложняющий фактор"}</span>}
                  <b>{c.id}.</b> <ClauseText text={c.text} />
                </div>
              );
            })}
            <h4 id="book-rules">Правила расчёта</h4>
            <ul>
              {book.rules.map((r) => (
                <li key={r.id}>
                  <b>{ruleTitle(r.kind)}</b>
                  <span className="muted"> · пункт {r.clauseId || "—"}{r.src?.page ? `, строка ${r.src.page}` : ""}</span>
                  . {r.text}
                </li>
              ))}
            </ul>
            {variables.length > 0 && (
              <section id="book-factors" className="book-factors" aria-label="Усложняющие факторы">
                <h4>Усложняющие факторы</h4>
                <p className="muted">Одно имя переменной. Кнопки задают, куда она попадает: на весь проект, только на открытую смету или только на выбранную строку.</p>
                {variables.map((variable) => {
                  const projectOn = conditions?.some((c) => c.id === variable.id) ?? false;
                  const estimateOn = estimateConditions?.some((c) => c.id === variable.id) ?? false;
                  const suppressed = estimateSuppressed?.includes(variable.id) ?? false;
                  const estimateChecked = estimateOn || (projectOn && !suppressed);
                  const localOn = localIds?.includes(variable.id) ?? false;
                  return (
                    <article key={variable.id} className="factor-var">
                      <header>
                        <b>{variable.alias}</b>
                        <code>{variable.id}</code>
                      </header>
                      <p>{variable.name}</p>
                      <ul>
                        {variable.uses.map((use) => (
                          <li key={use.bookId + use.table + use.item}>
                            {use.bookTitle}: табл. {use.table} п. {use.item}, значение {use.value}
                            {use.priceTables.length > 0 && <span className="muted"> · таблицы цены {use.priceTables.join(", ")}</span>}
                          </li>
                        ))}
                      </ul>
                      <div className="factor-scope">
                        <button
                          type="button"
                          className={projectOn ? "btn is-on" : "btn"}
                          disabled={!onToggleCondition}
                          title="Во всех сметах проекта, где строка из этого сборника"
                          onClick={() => onToggleCondition?.(variable)}
                        >
                          На проект
                        </button>
                        <button
                          type="button"
                          className={estimateChecked ? "btn is-on" : "btn"}
                          disabled={!onToggleEstimate}
                          title={onToggleEstimate ? "Только открытая смета" : "Сначала откройте смету"}
                          onClick={() => onToggleEstimate?.(variable, !estimateChecked)}
                        >
                          На смету
                        </button>
                        <button
                          type="button"
                          className={localOn ? "btn is-on" : "btn"}
                          disabled={!onToggleLocal}
                          title={onToggleLocal ? "Только выбранная строка сметы" : "Сначала выберите строку сметы"}
                          onClick={() => onToggleLocal?.(variable)}
                        >
                          Локально
                        </button>
                      </div>
                    </article>
                  );
                })}
              </section>
            )}
            <h4 id="book-tables">Таблицы параметров цены</h4>
            {book.tables.filter((t) => t.kind === "price").map((t) => {
              const tableNotes = book.notes.filter((n) => n.anchor === `table:${t.code}`);
              return (
                <details key={t.code} id={`book-table-${t.code}`} open={t.code === "3.8" || t.code === "3.1" || place?.table === t.code}>
                  <summary>Таблица {t.code}. {t.name} <span className="muted">{t.items.length}</span></summary>
                  {tableNotes.length > 0 && (
                    <div className="book-notes">
                      <h4>Разъяснения ко всей таблице</h4>
                      <p className="muted">Они относятся к каждому объёму этой таблицы и меняют состав работ либо стоимость.</p>
                      {tableNotes.map((n) => <Note key={n.id} note={n} />)}
                    </div>
                  )}
                  <table className="ledger">
                    <thead>
                      <tr>
                        <th>Пункт</th>
                        <th>Объём работ и расценка</th>
                        <th>Ед.</th>
                        <th className="num">от</th>
                        <th className="num">до</th>
                        <th className="num">а</th>
                        <th className="num">в</th>
                        <th>Разъяснения</th>
                      </tr>
                    </thead>
                    <tbody>
                      {groupsOf(t.items).map((group) => {
                        const key = `item:${t.code}/${group.n}`;
                        const itemNotes = book.notes.filter((n) => n.anchor === key);
                        const open = anchor === key;
                        return (
                          <GroupRows
                            key={t.code + group.n + group.name}
                            code={t.code}
                            marked={place?.table === t.code && place.item === group.n.replace(/\.$/, "")}
                            group={group}
                            notes={itemNotes}
                            practice={practice.filter((n) => n.anchor === key)}
                            onAddPractice={(text, source) => api.addUserNote(book.id, key, text, source).then(() => api.userNotes(book.id).then(setPractice))}
                            onEditPractice={(noteId, text) => api.editUserNote(book.id, noteId, text).then(() => api.userNotes(book.id).then(setPractice))}
                            onDeletePractice={(noteId) => api.deleteUserNote(book.id, noteId).then(() => api.userNotes(book.id).then(setPractice))}
                            open={open}
                            hotRow={hot?.table === t.code && hot.item === group.n ? hot.row : ""}
                            canInsert={!!onInsert}
                            onToggle={() => setAnchor(open ? null : key)}
                            bookId={book.id}
                            onHot={(row, clip) => setHot({ row, table: t.code, item: group.n, clip: { ...clip, bookId: book.id } })}
                            onInsert={() => onInsert?.(book.id, t.code, group.n)}
                            onCopy={(row, clip) => {
                              markCopied(row);
                              void navigator.clipboard.writeText(volumeClipText({ ...clip, bookId: book.id })).catch(() => {
                                setCopied("");
                                setErr("Буфер не записан");
                              });
                            }}
                            copiedRow={copied}
                          />
                        );
                      })}
                    </tbody>
                  </table>
                </details>
              );
            })}
          </div>
          {offers.length > 0 && book && (
            <OfferSheet
              offers={offers}
              onChange={() => api.offers().then((groups) => setOffers(groups.find((g) => g.bookId === book.id)?.offers ?? []))}
            />
          )}
          {report && <AuditNote text={report} />}
          </div>
        </div>
      )}
    </div>
  );
}

function OfferSheet({ offers, onChange }: { offers: OfferGroup["offers"]; onChange: () => Promise<void> }) {
  return (
    <section id="book-offers" className="book-offers" aria-label="Дополнительные сметы">
      <p className="book-audit-kicker">Справочник</p>
      <h4>Дополнительные сметы</h4>
      <p className="muted">Служебные названия. В текст сборника не входят. Их предлагают при создании локальной сметы. Правка общая для всех объектов.</p>
      <table className="ledger">
        <thead>
          <tr>
            <th>Пункт</th>
            <th>Приказ и название сметы</th>
            <th>Предлагать</th>
          </tr>
        </thead>
        <tbody>
          {offers.map((offer) => (
            <OfferRow key={offer.id} offer={offer} onChange={onChange} />
          ))}
        </tbody>
      </table>
    </section>
  );
}

function OfferRow({ offer, onChange }: { offer: OfferGroup["offers"][number]; onChange: () => Promise<void> }) {
  const [title, setTitle] = useState(offer.title);
  const [editing, setEditing] = useState(false);
  useEffect(() => setTitle(offer.title), [offer.title]);
  return (
    <tr className={offer.hidden ? "is-hidden" : ""}>
      <td className="point">{offer.clauseId}{offer.mark ? ` ${offer.mark})` : ""}</td>
      <td>
        <div className="muted order">{offer.text}</div>
        {editing ? (
          <label>
            <span className="offer-label">Смета</span>
            <input
              className="offer-name"
              value={title}
              aria-label={`Название сметы ${offer.clauseId} ${offer.mark}`}
              onChange={(e) => setTitle(e.target.value)}
            />
            <span className="factor-scope">
              <button
                type="button"
                className="btn"
                disabled={!title.trim() || title.trim() === offer.title}
                onClick={() => {
                  const next = title.trim();
                  void api.nameOffer(offer.id, next || offer.shortTitle).then(() => {
                    setEditing(false);
                    return onChange();
                  });
                }}
              >
                Сохранить
              </button>
              <button type="button" className="btn" onClick={() => { setTitle(offer.title); setEditing(false); }}>Отмена</button>
            </span>
          </label>
        ) : (
          <div className="offer-title">
            <span className="offer-label">Смета</span>
            {(offer.history ?? []).length > 0 && <WhoEdited history={offer.history} />}
            <b>{offer.title}</b>
            <button type="button" className="btn" onClick={() => setEditing(true)}>Редактировать</button>
          </div>
        )}
      </td>
      <td>
        <input
          type="checkbox"
          checked={!offer.hidden}
          aria-label={`Предлагать ${offer.title}`}
          onChange={() => void api.hideOffer(offer.id, !offer.hidden).then(onChange)}
        />
      </td>
    </tr>
  );
}

function WhoEdited({ history }: { history: { at: string; userName: string; title: string }[] }) {
  const tip = [...history].reverse().map((rev) => {
    const when = rev.at ? rev.at.slice(0, 16).replace("T", " ") : "";
    return `${rev.userName || "—"}${when ? `, ${when}` : ""}: ${rev.title || "название сброшено"}`;
  }).join("\n");
  return (
    <button type="button" className="offer-who" title={tip} aria-label={tip}>
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
        <circle cx="12" cy="8" r="3.2" />
        <path d="M5 19.2c1.4-3 3.8-4.5 7-4.5s5.6 1.5 7 4.5" />
      </svg>
    </button>
  );
}

function AuditNote({ text }: { text: string }) {
  const blocks = text.replace(/\r\n/g, "\n").trim().split(/\n{2,}/).filter((block, i) => !(i === 0 && block.startsWith("# ")));
  return (
    <aside id="book-report" className="book-audit" aria-label="Сверка сборника">
      <p className="book-audit-kicker">Сверка</p>
      <h4>Факт проверки</h4>
      <p className="book-audit-lead">Служебная справка. В текст сборника не входит: здесь записано, что совпало при вводе и повторном чтении.</p>
      {blocks.map((block, i) => <p key={i}>{linkText(block)}</p>)}
    </aside>
  );
}

function linkText(text: string) {
  return text.split(/(https?:\/\/\S+)/).map((part) =>
    part.startsWith("http") ? <a key={part} href={part} target="_blank" rel="noreferrer">{part}</a> : part
  );
}

export function NotesHelp({ raw }: { raw?: string }) {
  const [notes, setNotes] = useState<BookNote[]>([]);
  const [practice, setPractice] = useState<UserBookNote[]>([]);
  useEffect(() => {
    const anchor = anchorFrom(raw);
    if (!raw || !anchor || !/849/.test(raw)) {
      setNotes([]);
      setPractice([]);
      return;
    }
    api.bookNotes("nz-849-2023", anchor).then(setNotes).catch(() => setNotes([]));
    api.userNotes("nz-849-2023", anchor).then(setPractice).catch(() => setPractice([]));
  }, [raw]);
  if (notes.length === 0 && practice.length === 0) return null;
  return (
    <div className="book-notes">
      {notes.length > 0 && <h4>Разъяснения</h4>}
      {notes.map((n) => <Note key={n.id} note={n} />)}
      {practice.length > 0 && <h4>Опыт СНТП</h4>}
      {practice.map((n) => <p key={n.id}><b>{n.userName}.</b> {n.source ? <span className="muted">{n.source}. </span> : null}{n.text}</p>)}
    </div>
  );
}

function PracticeBox({
  notes,
  onAdd,
  onEdit,
  onDelete
}: {
  notes: UserBookNote[];
  onAdd: (text: string, source: string) => Promise<void>;
  onEdit: (noteId: string, text: string) => Promise<void>;
  onDelete: (noteId: string) => Promise<void>;
}) {
  const [text, setText] = useState("");
  const [source, setSource] = useState("");
  const [who, setWho] = useState("");
  const [whoError, setWhoError] = useState("");
  const [composing, setComposing] = useState(false);
  useEffect(() => {
    if (!composing || who || whoError) return;
    api.me().then((me) => {
      setWho(me.userName ?? "");
      setWhoError(me.resolved ? "" : `ФИО не найдено для ${me.account}. Запись не сохранится, пока SQL не сопоставит учётную запись с EXECUTERS.`);
    }).catch(() => setWhoError("Не удалось узнать ФИО"));
  }, [composing, who, whoError]);
  return (
    <aside className="practice">
      <p className="book-kicker">Опыт СНТП</p>
      <p className="muted">Не текст приказа. Заметку видят все, кто открывает сборник. Автор — ФИО из справочника сотрудников.</p>
      {notes.map((n) => <PracticeItem key={n.id} note={n} onEdit={onEdit} onDelete={onDelete} />)}
      {composing ? (
        <>
          {who && <p>Автор: <b>{who}</b></p>}
          {whoError && <p className="err">{whoError}</p>}
          <label>
            Откуда опыт
            <input lang="ru" spellCheck value={source} placeholder="Например, проверка смет ПАО «Газпром»" onChange={(e) => setSource(e.target.value)} />
          </label>
          <label>
            Уточнение
            <textarea lang="ru" spellCheck value={text} onChange={(e) => setText(e.target.value)} />
          </label>
          <div className="factor-scope">
            <button
              type="button"
              className="btn"
              disabled={!text.trim() || !who}
              onClick={() => {
                void onAdd(text.trim(), source.trim()).then(() => { setText(""); setComposing(false); });
              }}
            >
              Оставить уточнение
            </button>
            <button type="button" className="btn" onClick={() => { setText(""); setComposing(false); }}>Отмена</button>
          </div>
        </>
      ) : (
        <button type="button" className="btn" onClick={() => setComposing(true)}>Добавить уточнение</button>
      )}
    </aside>
  );
}

function PracticeItem({
  note,
  onEdit,
  onDelete
}: {
  note: UserBookNote;
  onEdit: (noteId: string, text: string) => Promise<void>;
  onDelete: (noteId: string) => Promise<void>;
}) {
  const [editing, setEditing] = useState(false);
  const [text, setText] = useState(note.text);
  useEffect(() => setText(note.text), [note.text]);
  const when = (note.updatedAt || note.createdAt || "").slice(0, 16).replace("T", " ");
  return (
    <article className="practice-item">
      <p className="muted">{note.userName}{when ? ` · ${when}` : ""}{note.source ? ` · ${note.source}` : ""}</p>
      {editing ? (
        <>
          <textarea lang="ru" spellCheck value={text} onChange={(e) => setText(e.target.value)} />
          <div className="factor-scope">
            <button type="button" className="btn" disabled={!text.trim() || text.trim() === note.text} onClick={() => void onEdit(note.id, text.trim()).then(() => setEditing(false))}>Сохранить</button>
            <button type="button" className="btn" onClick={() => { setText(note.text); setEditing(false); }}>Отмена</button>
          </div>
        </>
      ) : (
        <>
          <p>{note.text}</p>
          <div className="factor-scope">
            <button type="button" className="btn" onClick={() => setEditing(true)}>Править</button>
            <button type="button" className="btn" onClick={() => void onDelete(note.id)}>Удалить</button>
          </div>
        </>
      )}
      {(note.history ?? []).length > 0 && (
        <details>
          <summary>Хронология, {note.history.length}</summary>
          <ol>
            {[...note.history].reverse().map((rev) => (
              <li key={rev.at + rev.text.slice(0, 24)}>
                <span className="muted">{rev.userName} · {rev.at.slice(0, 16).replace("T", " ")}</span>
                <div>{rev.text}</div>
              </li>
            ))}
          </ol>
        </details>
      )}
    </article>
  );
}

const ruleNames: Record<string, string> = {
  "range-select": "В пределах полосы",
  "interpolate-below-min": "Ниже минимума",
  "extrapolate-above-max": "Выше максимума",
  "kpon-below-half": "Понижающий коэффициент",
  "factor-when": "Коэффициент по условию",
  "share-of-position": "Доля от другой строки"
};

function revealBookRow(el: HTMLElement) {
  let node: HTMLElement | null = el.parentElement;
  while (node) {
    const style = getComputedStyle(node);
    if (/(auto|scroll)/.test(style.overflowY) && node.scrollHeight > node.clientHeight + 4) {
      const top = el.getBoundingClientRect().top - node.getBoundingClientRect().top + node.scrollTop - 12;
      node.scrollTo({ top: Math.max(0, top) });
      return;
    }
    node = node.parentElement;
  }
  el.scrollIntoView({ block: "start" });
}

function jump(id: string) {
  const el = document.getElementById(id);
  if (el instanceof HTMLDetailsElement) el.open = true;
  if (el) revealBookRow(el);
}

function ruleTitle(kind: string) {
  return ruleNames[kind] ?? kind;
}

function clauseTitle(text: string) {
  const cut = text.split(/[.;]/)[0] ?? text;
  return cut.length > 72 ? cut.slice(0, 72) + "…" : cut;
}

type BookItemRow = ReferenceBook["tables"][number]["items"][number];

/** То же правило, что BookGroupNaming.From: метка «объём работ» только в начале, затем «п. N.». */
function volumeName(raw: string) {
  let s = raw.trim();
  const fold = s.replace(/ё/g, "е").replace(/Ё/g, "Е").toLowerCase();
  const label = "объем работ";
  if (fold.startsWith(label)) s = s.slice(label.length).trimStart();
  s = s.replace(/^[Пп]\.\s*[\d.]+\.?\s*/, "");
  return s.replace(/[:：]\s*$/, "").trim();
}

/** То же правило, что BookGroupNaming.IsLump: единица «объект» и нет параметра «в». */
function isLumpBand(item: { unit?: string; b?: number | null }) {
  if (item.b != null) return false;
  return (item.unit ?? "").toLowerCase().includes("объект");
}

function selectKind(bands: { unit?: string; b?: number | null }[]) {
  const lump = bands.filter(isLumpBand).length;
  if (lump === 0) return "indicator";
  if (bands.length > 0 && lump === bands.length) return "lump";
  return lump > 0 ? "mixed" : "indicator";
}

function selectLabel(kind: string) {
  if (kind === "lump") return "укрупнённо";
  if (kind === "mixed") return "по показателю, строка «объект» — укрупнённо";
  return "по показателю";
}

function groupsOf(items: BookItemRow[]) {
  const groups: { n: string; name: string; bands: BookItemRow[] }[] = [];
  for (const item of items) {
    const last = groups[groups.length - 1];
    if (last && last.n === item.n && last.name === item.name) last.bands.push(item);
    else groups.push({ n: item.n, name: item.name, bands: [item] });
  }
  return groups;
}

function bandFaults(bands: BookItemRow[]) {
  const faults = new Map<number, "overlap" | "gap">();
  const notes: string[] = [];
  const ranked = bands
    .map((item, index) => ({ item, index }))
    .filter((row) => row.item.xmin != null || row.item.xmax != null)
    .sort((a, b) => (a.item.xmin ?? Number.NEGATIVE_INFINITY) - (b.item.xmin ?? Number.NEGATIVE_INFINITY)
      || (a.item.xmax ?? Number.POSITIVE_INFINITY) - (b.item.xmax ?? Number.POSITIVE_INFINITY));
  for (let i = 1; i < ranked.length; i++) {
    const prev = ranked[i - 1].item;
    const next = ranked[i].item;
    if (prev.xmax == null || next.xmin == null) continue;
    const prevPoint = prev.xmin != null && prev.xmin === prev.xmax;
    const nextPoint = next.xmin != null && next.xmin === next.xmax;
    if (prevPoint && nextPoint) continue;
    if (next.xmin < prev.xmax) {
      faults.set(ranked[i - 1].index, "overlap");
      faults.set(ranked[i].index, "overlap");
      notes.push(`Полосы ${prev.xmin ?? "—"}…${prev.xmax} и ${next.xmin}…${next.xmax ?? "—"} пересекаются. Это ошибка ввода.`);
    } else if (next.xmin > prev.xmax) {
      faults.set(ranked[i - 1].index, faults.get(ranked[i - 1].index) ?? "gap");
      faults.set(ranked[i].index, faults.get(ranked[i].index) ?? "gap");
      notes.push(`Между ${prev.xmax} и ${next.xmin} дыра: для этого объёма нет расценки.`);
    }
  }
  return { faults, notes };
}

function GroupRows({
  code,
  group,
  notes,
  practice,
  onAddPractice,
  onEditPractice,
  onDeletePractice,
  open,
  marked = false,
  hotRow,
  canInsert,
  onToggle,
  onHot,
  onInsert,
  onCopy,
  copiedRow,
  bookId
}: {
  code: string;
  bookId: string;
  group: { n: string; name: string; bands: BookItemRow[] };
  notes: BookNote[];
  practice: UserBookNote[];
  onAddPractice: (text: string, source: string) => Promise<void>;
  onEditPractice: (noteId: string, text: string) => Promise<void>;
  onDeletePractice: (noteId: string) => Promise<void>;
  open: boolean;
  marked?: boolean;
  hotRow: string;
  canInsert: boolean;
  onToggle: () => void;
  onHot: (row: string, clip: VolumeClip) => void;
  onInsert: () => void;
  onCopy: (row: string, clip: VolumeClip) => void;
  copiedRow: string;
}) {
  const noteLabel = notes.length && practice.length
    ? `Разъяснения · ${notes.length} · опыт ${practice.length}`
    : practice.length
      ? `Опыт · ${practice.length}`
      : notes.length
        ? `Разъяснения · ${notes.length}`
        : "Разъяснения";
  const noteCell = (
    <button
      type="button"
      className="btn book-note-btn"
      aria-expanded={open}
      title="Примечания приказа и опыт проверки смет. Опыт пишут пользователи, в текст сборника он не входит"
      onClick={onToggle}
    >
      {noteLabel}
    </button>
  );
  const span = bandFaults(group.bands);
  const volumeTitle = volumeName(group.name);
  const mode = selectKind(group.bands);
  const volumeHeader = (
    <tr id={`book-item-${code}-${group.n.replace(/\.$/, "")}`} className={marked ? "book-group is-target" : "book-group"}>
      <td>
        {marked && (
          <svg className="book-check" width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M3 8.2 6.2 11.5 13 4.5" fill="none" stroke="currentColor" strokeWidth="2" />
          </svg>
        )}
        {group.n}
      </td>
      <td colSpan={6}>
        <span className="book-kind">Объём работ</span>
        п. {group.n}. {group.name}
        {marked && <span className="book-picked">Выбран</span>}
        <span className="book-volume-meta">Имя объёма: {volumeTitle || group.name} · {selectLabel(mode)}</span>
      </td>
      <td>{noteCell}</td>
    </tr>
  );
  return (
    <>
      {volumeHeader}
      {span.notes.length > 0 && (
        <tr className="book-span-warn">
          <td />
          <td colSpan={7}>{span.notes.join(" ")}</td>
        </tr>
      )}
      {group.bands.map((item, i) => {
        const row = `${code}/${group.n}/${item.xmin ?? ""}/${item.xmax ?? ""}/${i}`;
        const clip: VolumeClip = {
          bookId,
          table: code,
          item: group.n,
          name: group.name,
          unit: item.unit,
          xmin: item.xmin,
          xmax: item.xmax,
          a: item.a,
          b: item.b
        };
        const copied = copiedRow === row;
        return (
        <tr
          key={row}
          className={["book-band", hotRow === row ? "is-hot" : "", copied ? "is-copied" : "", span.faults.get(i) ? `is-${span.faults.get(i)}` : ""].filter(Boolean).join(" ")}
          title="Перетащите на номер сметы слева. Ctrl+C копирует объём в буфер."
          draggable
          onMouseEnter={() => onHot(row, clip)}
          onDragStart={(ev) => {
            if ((ev.target as HTMLElement).closest("button")) {
              ev.preventDefault();
              return;
            }
            const text = volumeClipText(clip);
            ev.dataTransfer.setData("text/plain", text);
            ev.dataTransfer.setData("application/x-sntp-volume", text);
            ev.dataTransfer.effectAllowed = "copy";
          }}
        >
          <td />
          <td><span className="book-kind">Расценка</span></td>
          <td>{item.unit}{isLumpBand(item) && <span className="book-lump">укрупнённо</span>}</td>
          <td className="num">{item.xmin ?? "—"}</td>
          <td className="num">{item.xmax ?? "—"}</td>
          <td className="num">{item.a ?? "—"}</td>
          <td className="num">{item.b ?? "—"}</td>
          <td>
            <button
              type="button"
              className="btn"
              disabled={!canInsert}
              title={canInsert ? "Вставить объём в открытую смету. Insert — то же, пока строка под курсором" : "Сначала откройте смету"}
              onClick={(ev) => {
                ev.stopPropagation();
                onInsert();
              }}
            >
              В смету
            </button>
            <button
              type="button"
              className={copied ? "btn is-copied" : "btn"}
              title="Скопировать объём в буфер. Ctrl+C — то же, пока строка под курсором. В другой смете вставьте через Ctrl+V"
              onClick={(ev) => {
                ev.stopPropagation();
                onCopy(row, clip);
              }}
            >
              {copied ? "В буфере" : "В буфер"}
            </button>
          </td>
        </tr>
        );
      })}
      {open && (
        <tr>
          <td colSpan={8}>
            <div className="book-notes">
              <h4>Разъяснения к объёму</h4>
              {notes.length > 0 ? (
                <>
                  <p className="muted">Эти условия меняют состав работ и стоимость расценки.</p>
                  {notes.map((n) => <Note key={n.id} note={n} />)}
                </>
              ) : (
                <p>Официальных разъяснений нет.</p>
              )}
            </div>
            <PracticeBox notes={practice} onAdd={onAddPractice} onEdit={onEditPractice} onDelete={onDeletePractice} />
          </td>
        </tr>
      )}
    </>
  );
}

function Note({ note }: { note: BookNote }) {
  const kind = textKind(note.quote);
  return (
    <div className={kind ? `book-note is-${kind}` : "book-note"}>
      {kind && <span className="book-kicker">{kind === "excluded" ? "Не учтено в цене" : "Усложняющий фактор"}</span>}
      {note.url ? <a href={note.url} target="_blank" rel="noreferrer">{note.letterRef}</a> : <b>{note.letterRef}</b>}
      {note.page ? ` · строка ${note.page}` : ""}
      {note.file ? <span className="muted"> · {note.file}</span> : null}
      <ClauseText text={note.quote} />
    </div>
  );
}

function textKind(text: string): "excluded" | "factor" | "" {
  const value = text.toLowerCase();
  if (/не\s+учт/.test(value)) return "excluded";
  if (/усложня/.test(value)) return "factor";
  if (/корректирующ[а-яё]*\s+коэффициент/.test(value) && !/приведены значения параметров/.test(value)) return "factor";
  return "";
}

function ClauseText({ text }: { text: string }) {
  const lines = text.split("\n").map((s) => s.trim()).filter(Boolean);
  const bullets = lines.filter((l) => l.startsWith("-"));
  if (bullets.length >= 2 && bullets.length >= lines.length - 1) {
    const lead = lines[0].startsWith("-") ? "" : lines[0];
    return (
      <>
        {lead ? <span>{lead}</span> : null}
        <ul className="book-points">
          {bullets.map((l) => <li key={l}>{l.replace(/^-\s*/, "")}</li>)}
        </ul>
      </>
    );
  }
  const parts = splitPoints(text);
  if (!parts) return <>{text}</>;
  const list = (
    <ul className="book-points">
      {parts.items.map((item) => <li key={item.slice(0, 48)}>{item}</li>)}
    </ul>
  );
  return (
    <>
      <span>{parts.lead}</span>
      {list}
    </>
  );
}

function clearMarks(root: HTMLElement) {
  root.querySelectorAll("mark.find-hit").forEach((mark) => {
    const parent = mark.parentNode;
    if (!parent) return;
    parent.replaceChild(document.createTextNode(mark.textContent ?? ""), mark);
    parent.normalize();
  });
}

function markText(root: HTMLElement, query: string) {
  const q = query.toLowerCase();
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  const nodes: Text[] = [];
  let node = walker.nextNode();
  while (node) {
    const parent = (node as Text).parentElement;
    if (parent && !parent.closest("input, textarea, script, mark") && (node.textContent ?? "").toLowerCase().includes(q))
      nodes.push(node as Text);
    node = walker.nextNode();
  }
  nodes.forEach((text) => {
    const value = text.textContent ?? "";
    const lower = value.toLowerCase();
    const frag = document.createDocumentFragment();
    let from = 0;
    let at = lower.indexOf(q);
    while (at >= 0) {
      frag.append(value.slice(from, at));
      const mark = document.createElement("mark");
      mark.className = "find-hit";
      mark.textContent = value.slice(at, at + q.length);
      frag.append(mark);
      from = at + q.length;
      at = lower.indexOf(q, from);
    }
    frag.append(value.slice(from));
    text.parentNode?.replaceChild(frag, text);
  });
}

function splitPoints(text: string) {
  const normalized = text.replace(/\sж по\s/g, " ж) по ");
  const marks: { index: number; length: number; mark: string }[] = [];
  const re = /(?:^|[\n;]|\s)([а-яё])\)\s+/gi;
  let match: RegExpExecArray | null;
  while ((match = re.exec(normalized))) {
    marks.push({ index: match.index, length: match[0].length, mark: match[1] });
  }
  if (marks.length < 2) return null;
  const items = marks.map((mark, i) => {
    const start = mark.index + mark.length;
    const end = i + 1 < marks.length ? marks[i + 1].index : normalized.length;
    return `${mark.mark}) ${normalized.slice(start, end).replace(/[;\s]+$/, "")}`;
  });
  return { lead: normalized.slice(0, marks[0].index).trim(), items };
}

function anchorFrom(raw?: string) {
  if (!raw) return null;
  const table = raw.match(/[Тт]абл(?:ица|\.)\s*(\d+(?:\.\d+)*)/)?.[1];
  const item = raw.match(/п(?:ункт|\.)\s*(\d+(?:\.\d+)*)/i)?.[1]?.replace(/\.$/, "");
  if (table && item) return `item:${table}/${item}`;
  if (table) return `table:${table}`;
  return null;
}
