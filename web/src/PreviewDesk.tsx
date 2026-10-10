import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { api, type AnalogCard, type AnalogDetail } from "./api";
import { AnalogPane } from "./AnalogPane";
import { SourcePane } from "./SourcePane";
import { BooksPane, type BookPlace } from "./BooksPane";
import {
  estimateDragText,
  putDrag,
  rateDragText,
  sendPreview,
  type EstimateDrag,
  type RateDrag
} from "./previewBridge";
import type { PirDocument, ProjectListItem } from "./types";
import { useUiZoom, ZoomControl } from "./uiZoom";

function clampPreviewWidth(width: number) {
  return Math.round(Math.min(1200, Math.max(320, width)));
}

type ColKind = "project" | "analogs";

type ColSlot = {
  estId: string | null;
  rateId: string | null;
  listScroll: number;
  sheetScroll: number;
};

function emptySlot(estId: string | null = null): ColSlot {
  return { estId, rateId: null, listScroll: 0, sheetScroll: 0 };
}

function money(n?: number | null) {
  if (n == null || Number.isNaN(n)) return "—";
  return n.toLocaleString("ru-RU", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

export function PreviewDesk({
  detached,
  hostProjectId,
  hostEstimateId,
  onAccepted,
  focus,
  bookPlace = null,
  collapsed = false,
  onCollapsedChange,
  onClose,
  onDetach,
  onWidth
}: {
  detached?: boolean;
  hostProjectId?: string | null;
  hostEstimateId?: string | null;
  onAccepted?: (doc: PirDocument) => void;
  focus?: { projectId: string; estimateId?: string } | null;
  bookPlace?: BookPlace | null;
  collapsed?: boolean;
  onCollapsedChange?: (collapsed: boolean) => void;
  onClose?: () => void;
  onDetach?: () => void;
  onWidth?: (width: number) => void;
}) {
  const paneRef = useRef<HTMLElement>(null);
  const sheetRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(720);
  const [mode, setMode] = useState<"projects" | "books" | "analogs">(bookPlace?.bookId ? "books" : "projects");
  const [list, setList] = useState<ProjectListItem[]>([]);
  const [err, setErr] = useState<string | null>(null);
  const [projectId, setProjectId] = useState<string | null>(focus?.projectId ?? null);
  const [doc, setDoc] = useState<PirDocument | null>(null);
  const [estId, setEstId] = useState<string | null>(focus?.estimateId ?? null);
  const [rateId, setRateId] = useState<string | null>(null);
  const [sources, setSources] = useState(false);
  const [colTab, setColTab] = useState<ColKind>("project");
  const [analogs, setAnalogs] = useState<AnalogCard[]>([]);
  const [openAnalog, setOpenAnalog] = useState<string | null>(null);
  const [analogRead, setAnalogRead] = useState<Record<string, NonNullable<AnalogDetail["read"]>>>({});
  const [zoom, setZoom] = useUiZoom(!!detached);
  const radioName = useId();
  const slots = useRef<Record<ColKind, ColSlot>>({
    project: emptySlot(focus?.estimateId ?? null),
    analogs: emptySlot()
  });
  const colTabRef = useRef<ColKind>("project");
  const colRefs = useRef<Partial<Record<ColKind, HTMLDivElement | null>>>({});
  const restoring = useRef(false);
  colTabRef.current = colTab;

  useEffect(() => {
    api.list().then((items) => { setList(items); setErr(null); }).catch((e) => setErr(e instanceof Error ? e.message : String(e)));
    api.mySettings().then((s) => {
      if (s.previewWidth) setWidth(clampPreviewWidth(s.previewWidth));
    }).catch(() => {});
  }, []);

  useEffect(() => { onWidth?.(width); }, [width, onWidth]);

  useEffect(() => {
    if (!detached) return;
    let timer = 0;
    const onResize = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => {
        const next = clampPreviewWidth(window.innerWidth);
        setWidth(next);
        api.saveMySettings({ previewWidth: next }).catch(() => {});
      }, 400);
    };
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, [detached]);

  useEffect(() => {
    if (detached || collapsed) return;
    const onDown = (e: MouseEvent) => {
      const target = e.target as Node | null;
      if (!target || paneRef.current?.contains(target)) return;
      if (target instanceof Element && target.closest(".preview-cmds")) return;
      onCollapsedChange?.(true);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCollapsedChange?.(true);
    };
    window.addEventListener("mousedown", onDown);
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("mousedown", onDown);
      window.removeEventListener("keydown", onKey);
    };
  }, [detached, collapsed, onCollapsedChange]);

  useEffect(() => {
    if (!focus?.projectId) return;
    setMode("projects");
    setProjectId(focus.projectId);
    slots.current.project.estId = focus.estimateId ?? null;
    slots.current.project.rateId = null;
    if (colTabRef.current === "project") setEstId(focus.estimateId ?? null);
  }, [focus?.projectId, focus?.estimateId]);

  useEffect(() => {
    if (bookPlace?.bookId) setMode("books");
  }, [bookPlace?.token]);

  useEffect(() => {
    if (!projectId) {
      setDoc(null);
      return;
    }
    let live = true;
    api.get(projectId).then((d) => {
      if (!live) return;
      setErr(null);
      setDoc(d);
      setEstId((cur) => {
        const prev = slots.current.project.estId ?? (colTabRef.current === "project" ? cur : null);
        const next = prev && d.estimates.some((e) => e.id === prev || e.number === prev) ? prev : d.estimates[0]?.id ?? null;
        if (slots.current.project.estId !== next) slots.current.project.rateId = null;
        slots.current.project.estId = next;
        return colTabRef.current === "project" ? next : cur;
      });
    }).catch((e) => setErr(e instanceof Error ? e.message : String(e)));
    return () => { live = false; };
  }, [projectId]);

  useEffect(() => {
    setOpenAnalog(null);
    slots.current.analogs = emptySlot();
    if (colTabRef.current !== "project") {
      setEstId(null);
      setRateId(null);
    }
    if (!projectId) {
      setAnalogs([]);
      return;
    }
    let live = true;
    api.analogs(projectId).then((c) => {
      if (!live) return;
      setErr(null);
      setAnalogs((c.items ?? []).filter((item) => item.status === "recognized" || item.status === "imported"));
    }).catch((e) => setErr(e instanceof Error ? e.message : String(e)));
    return () => { live = false; };
  }, [projectId]);

  function toggleAnalog(id: string) {
    setOpenAnalog((cur) => cur === id ? null : id);
    if (!projectId || analogRead[id]) return;
    api.analog(projectId, id).then((d) => {
      if (d.read) setAnalogRead((cur) => ({ ...cur, [id]: d.read! }));
    }).catch((e) => setErr(e instanceof Error ? e.message : String(e)));
  }

  const project = list.find((p) => p.id === projectId);
  const est = doc?.estimates.find((e) => e.id === estId || e.number === estId) ?? null;
  const otherName = project?.name || doc?.project.objectName || "Проект";
  const analogPick = colTab === "project" || !estId?.includes("/") ? null : (() => {
    const cut = estId.indexOf("/");
    const analogId = estId.slice(0, cut);
    const estimateId = estId.slice(cut + 1);
    return { analogId, estimateId, estimate: analogRead[analogId]?.estimates.find((row) => row.id === estimateId) ?? null };
  })();
  const sheetRows = (colTab === "project" ? est?.positions : analogPick?.estimate?.positions)?.filter((p) => p.kind === "section" || p.kind === "work" || p.kind === "text") ?? [];
  const sheetTitle = colTab === "project" ? est?.title : analogPick?.estimate?.title;
  const sheetKey = `${colTab}:${colTab === "project" ? est?.id ?? "" : analogPick?.estimateId ?? ""}`;
  const selectedRate = sheetRows.find((p) => p.id === rateId && (p.kind === "work" || p.kind === "text")) ?? null;
  const sheetRowsRef = useRef(sheetRows);
  sheetRowsRef.current = sheetRows;

  useEffect(() => {
    const remembered = slots.current[colTabRef.current].rateId;
    const rows = sheetRowsRef.current;
    if (rows.length === 0) return;
    if (remembered && rows.some((p) => p.id === remembered)) return;
    const first = rows.find((p) => p.kind === "work" || p.kind === "text");
    setRateId(first?.id ?? null);
  }, [sheetKey]);

  useEffect(() => {
    const wrap = sheetRef.current;
    if (!wrap) return;
    let acc = 0;
    const onWheel = (e: WheelEvent) => {
      if (e.ctrlKey || document.querySelector(".palette, .modal")) return;
      e.preventDefault();
      const px = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaMode === 2 ? e.deltaY * wrap.clientHeight : e.deltaY;
      acc += px;
      let steps = 0;
      while (acc >= 36) { steps += 1; acc -= 36; }
      while (acc <= -36) { steps -= 1; acc += 36; }
      if (!steps) return;
      const rows = sheetRowsRef.current;
      if (rows.length === 0) return;
      setRateId((cur) => {
        const at = Math.max(0, rows.findIndex((p) => p.id === cur));
        const next = rows[Math.min(rows.length - 1, Math.max(0, at + steps))];
        return next?.id ?? cur;
      });
    };
    wrap.addEventListener("wheel", onWheel, { passive: false });
    return () => wrap.removeEventListener("wheel", onWheel);
  }, [sheetKey]);

  useEffect(() => {
    if (restoring.current) return;
    const wrap = sheetRef.current;
    const row = wrap?.querySelector(`tr[data-pos="${rateId}"]`);
    if (!(row instanceof HTMLElement) || !wrap) return;
    const rowBox = row.getBoundingClientRect();
    const wrapBox = wrap.getBoundingClientRect();
    if (rowBox.top < wrapBox.top) wrap.scrollTop -= wrapBox.top - rowBox.top;
    else if (rowBox.bottom > wrapBox.bottom) wrap.scrollTop += rowBox.bottom - wrapBox.bottom;
  }, [rateId]);

  useLayoutEffect(() => {
    restoring.current = true;
    const slot = slots.current[colTab];
    const listEl = colRefs.current[colTab];
    if (listEl) listEl.scrollTop = slot.listScroll;
    if (sheetRef.current) sheetRef.current.scrollTop = slot.sheetScroll;
    const timer = window.setTimeout(() => { restoring.current = false; }, 0);
    return () => window.clearTimeout(timer);
  }, [colTab]);

  function switchTab(next: ColKind) {
    if (next === colTab) return;
    const slot = slots.current[colTab];
    slot.estId = estId;
    slot.rateId = rateId;
    slot.listScroll = colRefs.current[colTab]?.scrollTop ?? slot.listScroll;
    slot.sheetScroll = sheetRef.current?.scrollTop ?? slot.sheetScroll;
    const incoming = slots.current[next];
    setEstId(incoming.estId);
    setRateId(incoming.rateId);
    setColTab(next);
  }

  function copyEstimate(payload: EstimateDrag) {
    sendPreview({ type: "copy-estimate", payload });
  }

  function copyRate(payload: RateDrag) {
    sendPreview({ type: "copy-rate", payload });
  }

  function onPreviewGrip(e: { button: number; preventDefault: () => void; stopPropagation: () => void; clientX: number }) {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    const startX = e.clientX;
    const startW = paneRef.current?.getBoundingClientRect().width ?? width;
    const move = (ev: PointerEvent) => setWidth(clampPreviewWidth(startW - (ev.clientX - startX)));
    const up = (ev: PointerEvent) => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      const next = clampPreviewWidth(startW - (ev.clientX - startX));
      setWidth(next);
      api.saveMySettings({ previewWidth: next }).catch(() => {});
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  }

  function chooseProjectEstimate(id: string) {
    slots.current.project.estId = id;
    setSources(false);
    setEstId(id);
  }

  function chooseAnalogEstimate(analogId: string, estimateId: string) {
    const key = `${analogId}/${estimateId}`;
    slots.current.analogs.estId = key;
    slots.current.analogs.rateId = null;
    setSources(false);
    setEstId(key);
    setRateId(null);
  }

  function analogList() {
    return (
      <ul className="preview-list preview-analogs">
        {analogs.length === 0 && <li className="muted">Распознанных аналогов нет.</li>}
        {analogs.map((item) => {
          const title = item.nameEdited && item.systemName.trim() ? item.systemName.trim() : (item.sourceName || item.systemName);
          const open = openAnalog === item.id;
          const estimates = analogRead[item.id]?.estimates ?? [];
          const format = item.format === "grand" ? "Grand" : item.format === "excel" ? "Excel" : item.format;
          return (
            <li key={item.id} className="preview-analog">
              <div className="preview-analog-head">
                <button type="button" className={open ? "on" : ""} onClick={() => toggleAnalog(item.id)}>
                  <span className={open ? "preview-caret is-open" : "preview-caret"} aria-hidden="true" />
                  <span className="ttl">{title}</span>
                  <span className="muted">{format}</span>
                </button>
                <button
                  type="button"
                  className="preview-send"
                  title={item.format === "grand" ? "Открыть файл Grand" : "Открыть файл в Excel"}
                  onClick={() => {
                    if (!projectId) return;
                    api.openAnalog(projectId, item.id).catch((err) => setErr(String(err)));
                  }}
                >
                  Открыть
                </button>
                <button
                  type="button"
                  className="preview-send"
                  title="Открыть каталог и показать этот файл"
                  onClick={() => {
                    if (!projectId) return;
                    api.revealAnalogFile(projectId, item.id).catch((err) => setErr(String(err)));
                  }}
                >
                  Каталог
                </button>
              </div>
              {open && (
                <ul className="preview-analog-est">
                  {estimates.length === 0 && <li className="muted">Смет в файле нет.</li>}
                  {estimates.map((row) => {
                    const key = `${item.id}/${row.id}`;
                    return (
                      <li key={row.id}>
                        <button
                          type="button"
                          className={estId === key ? "on" : ""}
                          onClick={() => chooseAnalogEstimate(item.id, row.id)}
                        >
                          <span className="num">{row.number}</span>
                          <span className="ttl">{row.title || row.id}</span>
                          <span className="sum">{money(row.engineAmount ?? row.excelAmount)}</span>
                        </button>
                        <button
                          type="button"
                          className="preview-send"
                          title="Перенести эту смету в рабочие сметы проекта"
                          onClick={() => {
                            if (!projectId) return;
                            api.acceptAnalogEstimates(projectId, item.id, [row.id]).then((d) => onAccepted?.(d)).catch((err) => setErr(String(err)));
                          }}
                        >
                          В проект
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </li>
          );
        })}
      </ul>
    );
  }

  return (
    <section
      ref={paneRef}
      className={detached ? "preview-app" : `preview-pane${collapsed ? " is-collapsed" : ""}`}
      aria-label="Окно аналогов"
      style={!detached && !collapsed ? { width } : undefined}
    >
      {!detached && !collapsed && (
        <div className="preview-grip" title="Потяните, чтобы изменить ширину" onPointerDown={onPreviewGrip} />
      )}
      {!detached && (
        <button type="button" className="preview-strip" title="Развернуть сборники и аналоги" onClick={() => onCollapsedChange?.(false)}>
          Сборники и аналоги
        </button>
      )}
      <header className="preview-head">
        <div>
          <div className="preview-tag">Аналоги</div>
          <strong>Аналоги и сборники</strong>
        </div>
        <div className="preview-cmds">
          <button type="button" className={mode === "projects" ? "on" : ""} onClick={() => setMode("projects")}>Проекты</button>
          <button type="button" className={mode === "analogs" ? "on" : ""} onClick={() => setMode("analogs")}>Аналоги</button>
          <button type="button" className={mode === "books" ? "on" : ""} onClick={() => setMode("books")}>Сборники</button>
          {onDetach && <button type="button" onClick={onDetach}>Открепить</button>}
          {onClose && <button type="button" onClick={onClose}>Закрыть</button>}
        </div>
      </header>
      {mode === "projects" ? (
        <div className="preview-note preview-note-bar" title="Сверху выберите проект и смету. Расценку из нижней сметы перетащите в смету открытого проекта. «В смету» делает то же из отдельного окна.">
          <h3>Проекты</h3>
          <div className="preview-radios">
            <label>
              <input type="radio" name={radioName} checked={colTab === "project"} onChange={() => switchTab("project")} />
              Проектные сметы
            </label>
            <label>
              <input type="radio" name={radioName} checked={colTab === "analogs"} onChange={() => switchTab("analogs")} />
              <span>Сметы из аналогов <span className="preview-radio-src">Excel, Grand</span></span>
            </label>
          </div>
        </div>
      ) : (
        <p className="preview-note">
          Сверху выберите проект и смету. Расценку из нижней сметы перетащите в смету открытого проекта. «В смету» делает то же из отдельного окна.
        </p>
      )}
      {err && <p className="err">{err}</p>}
      {mode === "books" ? (
        <div className="preview-books">
          <BooksPane place={bookPlace} />
        </div>
      ) : mode === "analogs" ? (
        <div className="preview-books">
          <AnalogPane
            projectId={projectId ?? hostProjectId}
            acceptProjectId={hostProjectId}
            acceptEstimateId={hostEstimateId}
            onAccepted={onAccepted}
            onError={setErr}
          />
        </div>
      ) : (
        <div className="preview-body">
          <div className="preview-nav">
            <div className="preview-col">
              <ul className="preview-list">
                {list.map((p) => (
                  <li key={p.id}>
                    <button type="button" className={[p.id === projectId && !sources ? "on" : "", p.id === hostProjectId ? "is-host" : ""].filter(Boolean).join(" ")} title={p.id === hostProjectId ? "Текущий проект" : undefined} onClick={() => {
                      slots.current.project.estId = null;
                      slots.current.project.rateId = null;
                      if (colTabRef.current === "project") setEstId(null);
                      setProjectId(p.id);
                      setSources(false);
                    }}>
                      <span className="preview-host-slot">
                        {p.id === hostProjectId && (
                          <svg className="preview-host" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" aria-hidden="true">
                            <path d="M5 12.5 10 17.5 19 7" />
                          </svg>
                        )}
                      </span>
                      <span className="ttl">{p.name}</span>
                      <span className="muted">{p.estimateCount ?? 0}</span>
                    </button>
                    <button
                      type="button"
                      className={p.id === projectId && sources ? "preview-send on" : "preview-send"}
                      title="Исходные данные для расчёта стоимости"
                      onClick={() => { setProjectId(p.id); setSources(true); }}
                    >
                      ИД
                    </button>
                  </li>
                ))}
              </ul>
            </div>
            <div className="preview-col-stack">
            <div className="preview-col" hidden={colTab !== "project"} ref={(node) => { colRefs.current.project = node; }}>
                  <h3>{otherName}</h3>
                  <ul className="preview-list">
                    {(project?.estimates ?? []).map((e) => {
                      const payload: EstimateDrag = {
                        sourceProjectId: project!.id,
                        sourceEstimateId: e.id || e.number,
                        title: e.title,
                        projectName: project!.name
                      };
                      return (
                        <li key={e.id || e.number}>
                          <button
                            type="button"
                            draggable
                            className={est?.id === e.id ? "on" : ""}
                            title="Перетащите в локальные сметы рабочего окна"
                            onClick={() => chooseProjectEstimate(e.id || e.number)}
                            onDragStart={(ev) => putDrag(ev.dataTransfer, estimateDragText(payload))}
                          >
                            <span className="num">{e.number}</span>
                            <span className="ttl">{e.title}</span>
                            <span className="sum">{money(e.amount)}</span>
                          </button>
                          <button type="button" className="preview-send" title="Скопировать смету в текущий проект" onClick={() => copyEstimate(payload)}>В проект</button>
                        </li>
                      );
                    })}
                  </ul>
            </div>
            <div className="preview-col" hidden={colTab !== "analogs"} ref={(node) => { colRefs.current.analogs = node; }}>
              {analogList()}
            </div>
            </div>
          </div>
          <div className="preview-sheet">
            {sources && projectId ? (
              <>
                <h3>Исходные данные</h3>
                <SourcePane projectId={projectId} onError={setErr} />
              </>
            ) : null}
            {!sources && <h3>{sheetTitle || (colTab === "project" ? "Локальная смета" : "Смета аналога")}</h3>}
            {!sources && !(colTab === "project" ? est : analogPick?.estimate) && <p className="muted">Выберите смету сверху. Расценку перетащите в смету открытого проекта или скопируйте кнопкой внизу.</p>}
            {!sources && (colTab === "project" ? est : analogPick?.estimate) && (
              <>
                <div className="preview-sheet-scroll" ref={sheetRef}>
                  <table className="ledger preview-estimate">
                    <thead>
                      <tr>
                        <th>№</th>
                        <th>Наименование</th>
                        <th className="num">Кол.</th>
                        <th className="num">Сумма</th>
                      </tr>
                    </thead>
                    <tbody>
                      {sheetRows.map((p) => {
                        const draggable = colTab === "project" && !!est && (p.kind === "work" || p.kind === "text");
                        return (
                          <tr
                            key={p.id}
                            data-pos={p.id}
                            className={[p.kind === "section" ? "sec" : "", p.id === rateId ? "sel" : ""].filter(Boolean).join(" ") || undefined}
                            draggable={draggable}
                            title={draggable ? "Перетащите в смету открытого проекта" : undefined}
                            onClick={() => { slots.current[colTab].rateId = p.id; setRateId(p.id); }}
                            onDragStart={(ev) => {
                              if (!draggable || !est || !doc) return;
                              slots.current.project.rateId = p.id;
                              setRateId(p.id);
                              putDrag(ev.dataTransfer, rateDragText({
                                sourceProjectId: doc.id,
                                sourceEstimateId: est.id,
                                sourcePositionId: p.id,
                                name: p.name
                              }));
                            }}
                          >
                            <td className="num">{p.number}</td>
                            <td>{p.name}</td>
                            <td className="num">{p.kind === "work" ? p.quantity ?? "—" : ""}</td>
                            <td className="num">{p.kind === "work" ? money(p.engineAmount ?? p.excelAmount) : ""}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
                <div className="preview-sheet-foot">
                  <button
                    type="button"
                    className="btn"
                    disabled={!selectedRate || (colTab === "project" ? !doc || !est : !hostProjectId || !hostEstimateId || !analogPick)}
                    title={colTab === "project" ? "Скопировать расценку в смету открытого проекта" : "Перенести расценку в открытую смету проекта"}
                    onClick={() => {
                      if (!selectedRate) return;
                      if (colTab === "project") {
                        if (!doc || !est) return;
                        copyRate({
                          sourceProjectId: doc.id,
                          sourceEstimateId: est.id,
                          sourcePositionId: selectedRate.id,
                          name: selectedRate.name
                        });
                        return;
                      }
                      if (!hostProjectId || !hostEstimateId || !analogPick) return;
                      api.acceptAnalogPositions(hostProjectId, analogPick.analogId, hostEstimateId, [selectedRate.id]).then((d) => onAccepted?.(d)).catch((err) => setErr(String(err)));
                    }}
                  >
                    В смету
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      )}
      {detached && (
        <footer className="statusbar">
          <span className="status-side">Аналоги</span>
          <ZoomControl zoom={zoom} onZoom={setZoom} />
        </footer>
      )}
    </section>
  );
}
