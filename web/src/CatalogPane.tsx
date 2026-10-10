import { Fragment, useEffect, useState } from "react";
import { api, type FactorVariable, type LaborCatalog } from "./api";
import { BooksPane } from "./BooksPane";
import type { ProjectCondition } from "./types";

const kinds = [
  ["rates", "Ставки"],
  ["koeffs", "Коэффициенты"],
  ["indexes", "Индексы"],
  ["rpds", "РПД"],
  ["gazprom", "ПАО «Газпром»"],
  ["labor3p", "Сметы 3П"],
  ["books", "Сборники"],
  ["refresh", "Обновление справочников"]
] as const;

export function CatalogPane({
  onInsertBook,
  conditions,
  estimateConditions,
  estimateSuppressed,
  onToggleCondition,
  onToggleEstimate,
  localIds,
  onToggleLocal
}: {
  onInsertBook?: (bookId: string, table: string, item: string) => void;
  conditions?: ProjectCondition[];
  estimateConditions?: ProjectCondition[];
  estimateSuppressed?: string[];
  localIds?: string[];
  onToggleCondition?: (variable: FactorVariable) => void;
  onToggleEstimate?: (variable: FactorVariable, on: boolean) => void;
  onToggleLocal?: (variable: FactorVariable) => void;
}) {
  const [kind, setKind] = useState<(typeof kinds)[number][0]>("rates");
  const [booksHome, setBooksHome] = useState(0);
  const [findHost, setFindHost] = useState<HTMLDivElement | null>(null);
  const [nodes, setNodes] = useState<{ name: string; count: number }[]>([]);
  const [gazprom, setGazprom] = useState<{ identifier: string; caption: string; value?: number; source?: string }[]>([]);
  const [labor, setLabor] = useState<LaborCatalog[]>([]);
  const [laborId, setLaborId] = useState("sntp-groups");
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    if (kind === "books" || kind === "labor3p" || kind === "refresh") return;
    api.catalogGroups(kind).then((g) => setNodes(g.nodes)).catch((e) => setErr(String(e)));
    if (kind === "gazprom") api.catalogGazprom().then(setGazprom).catch(() => setGazprom([]));
  }, [kind]);

  useEffect(() => {
    if (kind !== "labor3p") return;
    api.laborCatalogs().then((list) => {
      setLabor(list);
      setLaborId((cur) => list.some((c) => c.id === cur) ? cur : list[0]?.id ?? "");
    }).catch((e) => setErr(String(e)));
  }, [kind]);

  return (
    <section className="pane catalog-pane">
      <div className="catalog-kinds">
        {kinds.map(([id, label]) => (
          <button
            key={id}
            type="button"
            className={kind === id ? "on" : ""}
            onClick={() => {
              if (id === "books" && kind === "books") setBooksHome((n) => n + 1);
              setKind(id);
            }}
          >
            {label}
          </button>
        ))}
        {kind === "books" && <div className="book-find-slot" ref={setFindHost} />}
      </div>
      {err && <p className="err">{err}</p>}
      {kind === "books" && (
        <BooksPane
          resetHome={booksHome}
          findHost={findHost}
          onInsert={onInsertBook}
          conditions={conditions}
          estimateConditions={estimateConditions}
          estimateSuppressed={estimateSuppressed}
          onToggleCondition={onToggleCondition}
          onToggleEstimate={onToggleEstimate}
          localIds={localIds}
          onToggleLocal={onToggleLocal}
        />
      )}
      {kind === "labor3p" && (
        <LaborCatalogView books={labor} selected={laborId} onSelect={setLaborId} onSaved={setLabor} onError={setErr} />
      )}
      {kind === "refresh" && <CatalogRefresh onError={setErr} />}
      {kind !== "books" && kind !== "labor3p" && kind !== "refresh" && <ul className="catalog-tree">
        {nodes.map((n) => (
          <li key={n.name}>
            {n.name} <span className="muted">{n.count}</span>
          </li>
        ))}
        {nodes.length === 0 && <li className="muted">Групп нет. Синхронизируйте .apr или снимите «Базы ПИР».</li>}
      </ul>}
      {kind === "gazprom" && (
        <table className="grid">
          <thead>
            <tr>
              <th>Код</th>
              <th>Наименование</th>
              <th>Значение</th>
              <th>Источник</th>
            </tr>
          </thead>
          <tbody>
            {gazprom.map((g) => (
              <tr key={g.identifier + (g.source ?? "")}>
                <td>{g.identifier}</td>
                <td>{g.caption}</td>
                <td>{g.value ?? "—"}</td>
                <td>{g.source}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

function a4Sheets(format?: string) {
  const normalized = (format ?? "").trim().toUpperCase().replace(/А/g, "A");
  const match = normalized.match(/^A(\d)$/);
  if (!match) return null;
  const sheets = 2 ** (4 - Number(match[1]));
  return sheets > 0 ? sheets : null;
}

function formatToA4(hours: number, format?: string) {
  const sheets = a4Sheets(format);
  if (sheets == null || hours == null || Number.isNaN(hours)) return "—";
  return (hours / sheets).toLocaleString("ru-RU", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function wageRates(base: number) {
  if (!base) return null;
  const salary = base * 40 * 0.36 * 0.9234 * 0.95 / 1.15;
  return {
    day: base,
    hour: Math.round((base / 8) * 100) / 100,
    salary: Math.round(salary * 100) / 100
  };
}

function money2(value: number | null) {
  if (value == null) return "—";
  return value.toLocaleString("ru-RU", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function linkedGroups(books: LaborCatalog[], wageId: string, postId: string) {
  const titles: string[] = [];
  for (const book of books) {
    if (book.kind !== "group") continue;
    for (const post of book.posts) {
      if (post.links?.[wageId] === postId) titles.push(post.title);
    }
  }
  return titles;
}

function CatalogRefresh({ onError }: { onError: (message: string) => void }) {
  const [cat, setCat] = useState<{ koeffs: number; indexes: number; rates: number; rpds: number } | null>(null);
  const [busy, setBusy] = useState("");
  useEffect(() => {
    api.catalogStatus().then(setCat).catch(() => setCat(null));
  }, []);
  return (
    <div className="card">
      <h3>Внешние источники</h3>
      <p className="muted">
        {cat
          ? `В SNTP_PPO: коэф. ${cat.koeffs}, индексы ${cat.indexes}, ставки ${cat.rates}, РПД ${cat.rpds}`
          : "Справочники читаются из внешних файлов, в MySQL Адепт не пишутся."}
      </p>
      <div className="analog-bar">
        <button
          type="button"
          className="btn"
          disabled={busy.length > 0}
          onClick={() => {
            setBusy("apr");
            api.catalogSync()
              .then(() => api.catalogStatus())
              .then(setCat)
              .catch((e) => onError(e instanceof Error ? e.message : String(e)))
              .finally(() => setBusy(""));
          }}
        >
          {busy === "apr" ? "Синхронизация…" : "Синхронизировать .apr"}
        </button>
        <button
          type="button"
          className="btn"
          disabled={busy.length > 0}
          onClick={() => {
            setBusy("fgis");
            api.catalogSyncFgis()
              .catch((e) => onError(e instanceof Error ? e.message : String(e)))
              .finally(() => setBusy(""));
          }}
        >
          {busy === "fgis" ? "Синхронизация…" : "Синхронизировать ФГИС ЦС"}
        </button>
      </div>
    </div>
  );
}

function LaborCatalogView({
  books,
  selected,
  onSelect,
  onSaved,
  onError
}: {
  books: LaborCatalog[];
  selected: string;
  onSelect: (id: string) => void;
  onSaved: (books: LaborCatalog[]) => void;
  onError: (message: string) => void;
}) {
  const book = books.find((b) => b.id === selected) ?? books[0];
  const wages = books.filter((b) => !b.kind || b.kind === "wage");
  const wageKey = wages.map((w) => w.id).join("\n");
  const [folded, setFolded] = useState<Record<string, boolean>>({});
  const [orderId, setOrderId] = useState("");
  useEffect(() => setFolded({}), [book?.id]);
  useEffect(() => {
    if (book?.kind !== "group") return;
    const ids = wageKey ? wageKey.split("\n") : [];
    setOrderId((cur) => {
      if (cur && ids.includes(cur)) return cur;
      if (book.rateCatalogId && ids.includes(book.rateCatalogId)) return book.rateCatalogId;
      return ids[0] ?? "";
    });
  }, [book?.id, book?.kind, book?.rateCatalogId, wageKey]);
  if (!book) return <p className="muted">Справочники 3П не найдены.</p>;
  const hours = book.kind === "hours";
  const wage = !book.kind || book.kind === "wage";
  const group = book.kind === "group";
  const index = book.kind === "index";
  const order = wages.find((w) => w.id === orderId) ?? wages[0];
  const sections = groupPosts(book.posts);
  const cols = 2 + (hours ? 4 : wage ? 4 : group ? 4 : index ? 1 : 0);
  const saveLink = (groupPostId: string, wagePostId: string) => {
    if (!order) return;
    api.assignLaborLink(book.id, groupPostId, order.id, wagePostId).then(onSaved).catch((e) => onError(e instanceof Error ? e.message : String(e)));
  };
  return (
    <div className="labor-catalog">
      <div className="labor-book-row">
        {books.map((b) => (
          <button key={b.id} type="button" className={b.id === book.id ? "on" : ""} onClick={() => onSelect(b.id)}>
            {b.title}
          </button>
        ))}
      </div>
      <div className="labor-catalog-meta">
        {book.customer && <span className="chip">{book.customer}</span>}
        {book.order && <span>{book.order.replace(/,?\s*MD\/\S+/g, "")}</span>}
        <span className="muted">{book.posts.length} поз. · {sections.length} разд.</span>
        {(wage || group) && <span className="muted">Чел/день — ставка из приказа. Чел/час = чел/день / 8. Зарплата справочно: чел/день × 40 × 0,36 × 0,9234 × 0,95 / 1,15.</span>}
      </div>
      <table className={group ? "grid labor-grid is-map" : "grid labor-grid"}>
        {group && (
          <colgroup>
            <col className="labor-col-num" />
            <col className="labor-col-name" />
            <col className="labor-col-map" />
            <col className="labor-col-money" />
            <col className="labor-col-money" />
            <col className="labor-col-money" />
          </colgroup>
        )}
        <thead>
          <tr>
            <th>№</th>
            <th>Наименование</th>
            {hours && <th>Марка</th>}
            {hours && <th>Формат</th>}
            {hours && <th className="num">Чел·ч</th>}
            {hours && <th className="num">к А4</th>}
            {wage && <th>Группа</th>}
            {group && (
              <th className="labor-map">
                <select aria-label="Приказ" value={order?.id ?? ""} onChange={(e) => setOrderId(e.target.value)}>
                  {wages.map((w) => (
                    <option key={w.id} value={w.id}>{w.title}</option>
                  ))}
                </select>
              </th>
            )}
            {group && <th className="num" title="Дневная ставка из приказа">Чел/день</th>}
            {group && <th className="num" title="Ставка чел/день / 8">Чел/час</th>}
            {group && <th className="num" title="Справочно: ставка чел/день × 40 × 0,36 × 0,9234 × 0,95 / 1,15">Зарплата</th>}
            {index && <th className="num">Индекс</th>}
            {wage && <th className="num" title="Дневная ставка из приказа">Ставка чел/день</th>}
            {wage && <th className="num" title="Ставка чел/день / 8">Ставка чел/час</th>}
            {wage && <th className="num" title="Справочно: ставка чел/день × 40 × 0,36 × 0,9234 × 0,95 / 1,15">Зарплата</th>}
          </tr>
        </thead>
        <tbody>
          {sections.map((section) => {
            const closed = folded[section.name];
            return (
              <Fragment key={section.name}>
                <tr className="labor-section">
                  <td colSpan={cols}>
                    <button
                      type="button"
                      className="labor-fold"
                      aria-expanded={!closed}
                      onClick={() => setFolded((cur) => ({ ...cur, [section.name]: !cur[section.name] }))}
                    >
                      <span aria-hidden="true">{closed ? "▸" : "▾"}</span>
                      {section.name}
                      <span className="muted">{section.posts.length}</span>
                    </button>
                  </td>
                </tr>
                {!closed && section.posts.map((p) => {
                  const linked = group && order ? order.posts.find((rate) => rate.id === p.links?.[order.id]) : undefined;
                  const rates = group ? (linked ? wageRates(linked.daily) : null) : wage ? wageRates(p.daily) : null;
                  return (
                  <tr key={p.id}>
                    <td className="num">{p.id}</td>
                    <td>{wage && p.grade ? `${p.title}, ${p.grade}` : p.title}</td>
                    {wage && <td>{linkedGroups(books, book.id, p.id).join(", ") || "—"}</td>}
                    {group && order && (
                      <td className="labor-map">
                        <select aria-label={`Сопоставление ${p.title}`} value={p.links?.[order.id] ?? ""} onChange={(e) => saveLink(p.id, e.target.value)}>
                          <option value="">—</option>
                          {groupPosts(order.posts).map((block) => (
                            <optgroup key={block.name} label={block.name}>
                              {block.posts.map((rate) => (
                                <option key={rate.id} value={rate.id}>
                                  {rate.grade ? `${rate.title}, ${rate.grade}` : rate.title}
                                </option>
                              ))}
                            </optgroup>
                          ))}
                        </select>
                      </td>
                    )}
                    {(group || wage) && (
                      <>
                        <td className="num">{rates ? rates.day.toLocaleString("ru-RU") : "—"}</td>
                        <td className="num">{money2(rates?.hour ?? null)}</td>
                        <td className="num">{money2(rates?.salary ?? null)}</td>
                      </>
                    )}
                    {hours && <td>{p.grade}</td>}
                    {hours && <td>{p.format || "—"}</td>}
                    {hours && <td className="num">{p.daily || "—"}</td>}
                    {hours && (
                      <td className="num" title={a4Sheets(p.format) ? `${p.format} = ${a4Sheets(p.format)} лист. А4` : undefined}>
                        {formatToA4(p.daily, p.format)}
                      </td>
                    )}
                    {index && <td className="num">{p.daily ? p.daily.toLocaleString("ru-RU", { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : "—"}</td>}
                  </tr>
                  );
                })}
              </Fragment>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function groupPosts(posts: LaborCatalog["posts"]) {
  const sections: { name: string; posts: LaborCatalog["posts"] }[] = [];
  for (const post of posts) {
    const name = post.group || "Без раздела";
    const last = sections[sections.length - 1];
    if (last?.name === name) last.posts.push(post);
    else sections.push({ name, posts: [post] });
  }
  return sections;
}
