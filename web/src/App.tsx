import { Fragment, useEffect, useMemo, useRef, useState } from "react";
import { api, type LaborCatalog, type OfferGroup } from "./api";
import { CatalogPane } from "./CatalogPane";
import { NotesHelp } from "./BooksPane";
import { clipFromTransfer, isVolumeDrag, readVolumeClip, type VolumeClip } from "./volumeClip";
import { PreviewDesk } from "./PreviewDesk";
import type { BookPlace } from "./BooksPane";
import { estimateDragText, estimateMark, listenPreview, putDrag, rateMark, readMarked, type EstimateDrag, type RateDrag } from "./previewBridge";
import { AnalogPane } from "./AnalogPane";
import { VolumesPane } from "./VolumesPane";
import { useUiZoom, ZoomControl } from "./uiZoom";
import { Composer } from "./Composer";
import { bookLabels, sectionFor, SECTIONS } from "./sections";
import type { Estimate, LaborModel, PirDocument, Position, ProjectListItem } from "./types";

type Tab = "smets" | "svod" | "issues" | "import" | "export" | "catalog";

const TABS: Tab[] = ["smets", "svod", "issues", "import", "export", "catalog"];

function isTab(value: string | undefined): value is Tab {
  return !!value && TABS.includes(value as Tab);
}

type DeskMark = { projectId: string; estId?: string; tab?: Tab };

type DeskPlace = { projectId: string | null; estId: string | null; tab: Tab; estView: "grid" | "volumes" };

function samePlace(a: DeskPlace, b: DeskPlace) {
  return a.projectId === b.projectId && a.estId === b.estId && a.tab === b.tab && a.estView === b.estView;
}

function readHash(): DeskMark | null {
  const raw = location.hash.replace(/^#\/?/, "");
  if (!raw) return null;
  const [projectId, estId, tab] = raw.split("/");
  if (!projectId) return null;
  return {
    projectId: decodeURIComponent(projectId),
    estId: estId ? decodeURIComponent(estId) : undefined,
    tab: isTab(tab) ? tab : undefined
  };
}

function readStoredDesk(): DeskMark | null {
  try {
    const value = JSON.parse(localStorage.getItem("sntp.desk") || "");
    if (!value || typeof value.projectId !== "string") return null;
    return {
      projectId: value.projectId,
      estId: typeof value.estId === "string" && value.estId ? value.estId : undefined,
      tab: isTab(value.tab) ? value.tab : undefined
    };
  } catch {
    return null;
  }
}

function writeDesk(projectId: string, estId: string | null, tab: Tab) {
  const mark = { projectId, estId: estId ?? "", tab };
  localStorage.setItem("sntp.desk", JSON.stringify(mark));
  const hash = `#/${encodeURIComponent(projectId)}/${encodeURIComponent(mark.estId)}/${tab}`;
  if (location.hash !== hash) history.replaceState(null, "", hash);
}

function clearDesk() {
  localStorage.removeItem("sntp.desk");
  if (location.hash.startsWith("#/")) history.replaceState(null, "", location.pathname + location.search);
}

const RAIL_MIN = 36;
const RAIL_MAX = 420;
const RAIL_LINE = 56;
const RAIL_DEFAULT = 248;

function storedRailWidth() {
  try {
    const n = Number(localStorage.getItem("sntp.railPx"));
    if (Number.isFinite(n) && n >= RAIL_MIN && n <= RAIL_MAX) return Math.round(n);
  } catch {
    /* ignore */
  }
  return RAIL_MIN;
}

function NavIcon({ id }: { id: Tab }) {
  const p = { width: 16, height: 16, viewBox: "0 0 24 24", fill: "none", stroke: "currentColor", strokeWidth: 1.7, "aria-hidden": true } as const;
  if (id === "svod") return <svg {...p}><path d="M4 6h16M4 12h16M4 18h10" /></svg>;
  if (id === "smets") return <svg {...p}><rect x="6" y="3" width="12" height="18" /><path d="M9 8h6M9 12h6M9 16h4" /></svg>;
  if (id === "catalog") return <svg {...p}><path d="M4 5.5h6.5v14H4zM13.5 5.5H20v14h-6.5z" /></svg>;
  if (id === "import") return <svg {...p}><path d="M12 4v10M8 10l4 4 4-4M5 19h14" /></svg>;
  if (id === "export") return <svg {...p}><path d="M12 14V4M8 8l4-4 4 4M5 19h14" /></svg>;
  return <svg {...p}><path d="M12 3.5 3.2 20h17.6L12 3.5z" /><path d="M12 10v4.2M12 16.8v.6" /></svg>;
}

function FolderIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
      <path d="M3 6.5h6l2 2h10v10.5H3z" />
    </svg>
  );
}

function ReloadIcon() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
      <path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3" />
      <path d="M19.5 4v4.5H15" />
    </svg>
  );
}

declare global {
  interface Window {
    sntpHotkey?: (cmd: string) => void;
  }
}

function money(n?: number) {
  if (n == null || Number.isNaN(n)) return "—";
  return n.toLocaleString("ru-RU", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function isSurveyForm(form?: string) {
  return form === "form2PSurveyNz" || form === "form2PSurveySbc";
}

function lineMoney(est?: Estimate) {
  if (est?.heading) return { survey: 0, design: 0, total: 0 };
  const amount = est?.engineAmount ?? est?.excelAmount ?? 0;
  return isSurveyForm(est?.form)
    ? { survey: amount, design: 0, total: amount }
    : { survey: 0, design: amount, total: amount };
}

type SvodNode = {
  key: string;
  title: string;
  code: string;
  est?: Estimate;
  survey: number;
  design: number;
  total: number;
  heading?: boolean;
  children: SvodNode[];
};

function svodTree(rows: { code: string; title: string; ref?: string }[], estimates: Estimate[]): SvodNode[] {
  const roots: SvodNode[] = [];
  const stack: { depth: number; children: SvodNode[] }[] = [{ depth: -1, children: roots }];
  rows.forEach((row, index) => {
    const est = estimates.find((e) => e.id === row.ref || e.number === row.code);
    const moneyLine = lineMoney(est);
    const node: SvodNode = {
      key: est?.id ?? `${row.code}-${index}`,
      title: est?.title || row.title,
      code: est?.number || row.code,
      est,
      heading: !!est?.heading,
      ...moneyLine,
      children: []
    };
    const depth = est?.outlineDepth ?? 0;
    while (stack.length > 1 && stack[stack.length - 1].depth >= depth) stack.pop();
    stack[stack.length - 1].children.push(node);
    stack.push({ depth, children: node.children });
  });
  return roots;
}

function visibleSvodIds(nodes: SvodNode[], fold: Record<string, boolean>, stageHidden: boolean): string[] {
  if (fold["svod-object"]) return [];
  if (!stageHidden && fold["svod-stage"]) return [];
  const ids: string[] = [];
  const walk = (list: SvodNode[]) => {
    for (const node of list) {
      if (node.est) ids.push(node.est.id);
      if (node.heading && fold[node.key]) continue;
      if (node.children.length) walk(node.children);
    }
  };
  walk(nodes);
  return ids;
}

function roll(nodes: SvodNode[]) {
  return nodes.reduce(
    (acc, node) => {
      const kids = roll(node.children);
      return {
        survey: acc.survey + node.survey + kids.survey,
        design: acc.design + node.design + kids.design,
        total: acc.total + node.total + kids.total
      };
    },
    { survey: 0, design: 0, total: 0 }
  );
}

export type ThemeMode = "light" | "dark";
export type ThemeStyle = "kraft" | "shadcn" | "sharepoint";

export const THEME_STYLES: { id: ThemeStyle; title: string; desc: string }[] = [
  { id: "kraft", title: "Инженерный стол", desc: "Бумага ватмана, терракота и сепия" },
  { id: "shadcn", title: "Чертёжный кабинет", desc: "Холодный графит, индиго и бирюза" },
  { id: "sharepoint", title: "Проектный офис", desc: "Тёплый серый, морская волна, Segoe UI" }
];

export function App() {
  const [doc, setDoc] = useState<PirDocument | null>(null);
  const [currentProject, setCurrentProject] = useState<{ id: string; name: string } | null>(null);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewCollapsed, setPreviewCollapsed] = useState(false);
  const previewWidthRef = useRef(720);
  const [previewFocus, setPreviewFocus] = useState<{ projectId: string; estimateId?: string } | null>(null);
  const [bookPlace, setBookPlace] = useState<BookPlace | null>(null);
  /** Растёт при уходе на главную: ответы, начатые до этого, проект не открывают. */
  const session = useRef(0);
  const docRef = useRef<PirDocument | null>(null);
  const [list, setList] = useState<ProjectListItem[]>([]);
  const [tab, setTab] = useState<Tab>("smets");
  const [svodFold, setSvodFold] = useState<Record<string, boolean>>({});
  const [svodSel, setSvodSel] = useState<string[]>([]);
  const [svodCur, setSvodCur] = useState<string | null>(null);
  const svodAnchor = useRef<string | null>(null);
  const placeSvodRef = useRef<(index: number, mode: "select" | "extend" | "move") => void>(() => {});
  const toggleSvodRef = useRef<() => void>(() => {});
  const [svodMenu, setSvodMenu] = useState(false);
  const [svodFocus, setSvodFocus] = useState<string | null>(null);
  const [canBack, setCanBack] = useState(false);
  const [canForward, setCanForward] = useState(false);
  const historyRef = useRef<DeskPlace[]>([{ projectId: null, estId: null, tab: "smets", estView: "grid" }]);
  const histIndex = useRef(0);
  const placeRef = useRef<DeskPlace>(historyRef.current[0]);
  const restoringPlace = useRef(false);
  const pendingPlace = useRef<DeskPlace | null>(null);
  const historyReady = useRef(false);
  const absorbedBoot = useRef(false);
  const jumpRef = useRef<(delta: -1 | 1) => void>(() => {});
  const [estId, setEstId] = useState<string | null>(null);
  const [posId, setPosId] = useState<string | null>(null);
  const [estView, setEstView] = useState<"grid" | "volumes">("grid");
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const runSeq = useRef(0);
  const [showNewObject, setShowNewObject] = useState(false);
  const [showNewLocal, setShowNewLocal] = useState(false);
  const [paste, setPaste] = useState<{ text?: string; html?: string; rows?: { kind: string; cells: string[] }[] } | null>(null);
  const [pasteTune, setPasteTune] = useState<string | null>(null);
  const [pasteForce, setPasteForce] = useState(false);
  const [mapFrom, setMapFrom] = useState("Кст");
  const [mapTo, setMapTo] = useState("Кст-стесн");
  const [mapRole, setMapRole] = useState("constraint");
  const [theme, setTheme] = useState<ThemeMode>(() => (localStorage.getItem("sntp.theme") === "dark" ? "dark" : "light"));
  const [themeStyle, setThemeStyle] = useState<ThemeStyle>(() => {
    const s = localStorage.getItem("sntp.themeStyle");
    return s === "shadcn" || s === "sharepoint" ? s : "kraft";
  });
  const [themeMenuOpen, setThemeMenuOpen] = useState(false);
  const themePickerRef = useRef<HTMLDivElement>(null);
  const [palette, setPalette] = useState(false);
  const [paletteQ, setPaletteQ] = useState("");
  const [zoom, setZoom] = useUiZoom();
  const [railWidth, setRailWidth] = useState(storedRailWidth);
  const railWidthRef = useRef(railWidth);
  const [treeTip, setTreeTip] = useState<{
    id: string;
    number: string;
    form: string;
    title: string;
    sum: string;
    top: number;
    left: number;
    mode: "open" | "before" | "child" | "rate";
  } | null>(null);
  const [rateLanded, setRateLanded] = useState("");
  const [gridDrop, setGridDrop] = useState(false);
  const putVolumeRef = useRef<(targetId: string, clip: VolumeClip, openVolumes: boolean) => void>(() => {});
  const tabRef = useRef(tab);
  tabRef.current = tab;

  function showTreeTip(el: HTMLElement, item: { id: string; number?: string; title: string; form?: string; engineAmount?: number; excelAmount?: number }, mode: "open" | "before" | "child" | "rate") {
    const zoom = Number(getComputedStyle(document.documentElement).getPropertyValue("--ui-zoom")) || 1;
    const rect = el.getBoundingClientRect();
    const rail = el.closest(".rail-tree")?.getBoundingClientRect();
    const edge = rail?.right ?? rect.right;
    setTreeTip({
      id: item.id,
      number: item.number ?? "",
      form: estimateFormMark(item.form ?? ""),
      title: item.title,
      sum: money(item.engineAmount ?? item.excelAmount),
      top: Math.min(rect.top / zoom, window.innerHeight / zoom - 92),
      left: edge / zoom + 8,
      mode
    });
  }

  function rememberRail(width: number) {
    const next = Math.round(Math.min(RAIL_MAX, Math.max(RAIL_MIN, width)));
    railWidthRef.current = next;
    setRailWidth(next);
    try {
      localStorage.setItem("sntp.railPx", String(next));
      if (next > RAIL_LINE) localStorage.setItem("sntp.railOpen", String(next));
    } catch {
      /* ignore */
    }
  }

  function toggleRail() {
    if (railWidthRef.current <= RAIL_LINE) {
      const open = Number(localStorage.getItem("sntp.railOpen"));
      rememberRail(Number.isFinite(open) && open > RAIL_LINE ? open : RAIL_DEFAULT);
    } else {
      try {
        localStorage.setItem("sntp.railOpen", String(railWidthRef.current));
      } catch {
        /* ignore */
      }
      rememberRail(RAIL_MIN);
    }
  }

  function onGripDown(e: React.PointerEvent<HTMLDivElement>) {
    if (e.button !== 0) return;
    e.preventDefault();
    const startX = e.clientX;
    const startW = railWidthRef.current;
    let moved = false;
    const move = (ev: PointerEvent) => {
      if (Math.abs(ev.clientX - startX) > 3) moved = true;
      const next = Math.round(Math.min(RAIL_MAX, Math.max(RAIL_MIN, startW + ev.clientX - startX)));
      railWidthRef.current = next;
      setRailWidth(next);
    };
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      if (!moved) return;
      const raw = railWidthRef.current;
      rememberRail(raw < 72 ? RAIL_MIN : raw);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  }

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    document.documentElement.dataset.themeStyle = themeStyle;
    localStorage.setItem("sntp.theme", theme);
    localStorage.setItem("sntp.themeStyle", themeStyle);
  }, [theme, themeStyle]);

  useEffect(() => {
    if (!themeMenuOpen) return;
    const onDown = (e: MouseEvent) => {
      if (themePickerRef.current && !themePickerRef.current.contains(e.target as Node)) {
        setThemeMenuOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setThemeMenuOpen(false);
    };
    window.addEventListener("mousedown", onDown);
    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("mousedown", onDown);
      window.removeEventListener("keydown", onKey);
    };
  }, [themeMenuOpen]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPalette((v) => !v);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const est = useMemo(
    () => doc?.estimates.find((e) => e.id === estId || e.number === estId) ?? doc?.estimates[0],
    [doc, estId]
  );
  const pos = useMemo(
    () => est?.positions.find((p) => p.id === posId) ?? est?.positions.find((p) => p.kind === "work"),
    [est, posId]
  );

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.repeat || tab !== "smets" || !doc || !est) return;
      const target = e.target as HTMLElement | null;
      if (target?.closest("input, textarea, select")) return;
      if (e.key === "Insert") {
        e.preventDefault();
        const before = pos && (pos.kind === "work" || pos.kind === "section") ? pos.id : undefined;
        editRows(() => api.createPosition(doc.id, est.id, before), (r) => applyDoc(r.document, est.id, r.position.id));
      } else if (e.key === "Delete" && pos && (pos.kind === "work" || pos.kind === "text" || pos.kind === "section")) {
        e.preventDefault();
        const rows = est.positions.filter((p) => p.kind === "work" || p.kind === "section" || p.kind === "text");
        const at = rows.findIndex((p) => p.id === pos.id);
        const next = (at >= 0 ? rows[at + 1] ?? rows[at - 1] : undefined)?.id;
        editRows(() => api.deletePosition(doc.id, est.id, pos.id), (d) => applyDoc(d, est.id, next));
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [tab, doc, est, pos]);

  async function refreshList() {
    try {
      setList(await api.list());
    } catch {
      /* empty */
    }
  }

  useEffect(() => {
    if (!doc) return;
    writeDesk(doc.id, estId, tab);
  }, [doc, estId, tab]);

  useEffect(() => {
    docRef.current = doc;
    const next: DeskPlace = { projectId: doc?.id ?? null, estId: doc ? estId : null, tab, estView };
    if (!historyReady.current || !absorbedBoot.current) {
      historyRef.current = [next];
      histIndex.current = 0;
      placeRef.current = next;
      if (historyReady.current) absorbedBoot.current = true;
      setCanBack(false);
      setCanForward(false);
      return;
    }
    if (restoringPlace.current) {
      placeRef.current = next;
      const want = pendingPlace.current;
      const estimateGone = !!want?.estId && !!doc && !doc.estimates.some((e) => e.id === want.estId || e.number === want.estId);
      if (want && next.projectId === want.projectId && next.tab === want.tab && next.estView === want.estView && (next.estId === want.estId || estimateGone || !want.estId)) {
        historyRef.current[histIndex.current] = next;
        restoringPlace.current = false;
        pendingPlace.current = null;
      }
      setCanBack(histIndex.current > 0);
      setCanForward(histIndex.current < historyRef.current.length - 1);
      return;
    }
    if (samePlace(placeRef.current, next)) return;
    const stack = historyRef.current.slice(0, histIndex.current + 1);
    stack.push(next);
    if (stack.length > 40) stack.shift();
    historyRef.current = stack;
    histIndex.current = stack.length - 1;
    placeRef.current = next;
    setCanBack(histIndex.current > 0);
    setCanForward(false);
  }, [doc, estId, tab, estView]);

  useEffect(() => {
    const onKey = (ev: KeyboardEvent) => {
      if (!ev.altKey || ev.shiftKey || ev.ctrlKey || ev.metaKey) return;
      if (ev.key !== "ArrowLeft" && ev.key !== "ArrowRight") return;
      const target = ev.target as HTMLElement | null;
      if (target?.closest("input, textarea, select")) return;
      ev.preventDefault();
      jumpRef.current(ev.key === "ArrowLeft" ? -1 : 1);
    };
    const onAux = (ev: MouseEvent) => {
      if (ev.button !== 3 && ev.button !== 4) return;
      const target = ev.target as HTMLElement | null;
      if (target?.closest("input, textarea, select")) return;
      ev.preventDefault();
      jumpRef.current(ev.button === 3 ? -1 : 1);
    };
    window.addEventListener("keydown", onKey);
    window.addEventListener("auxclick", onAux);
    const onSvodKey = (ev: KeyboardEvent) => {
      if (tabRef.current !== "svod") return;
      if (ev.altKey) return;
      const key = ev.key;
      const nav = key === "ArrowUp" || key === "ArrowDown" || key === "Home" || key === "End";
      const toggle = (ev.ctrlKey || ev.metaKey) && key === " ";
      const target = ev.target as HTMLElement | null;
      const title = target?.closest(".form-sheet input.title");
      if (key === "Enter" && title instanceof HTMLInputElement && !ev.ctrlKey && !ev.metaKey && !ev.altKey) {
        ev.preventDefault();
        const id = title.dataset.svodId ?? "";
        title.blur();
        const listed = [...document.querySelectorAll<HTMLElement>(".form-sheet [data-svod-id]")].map((el) => el.dataset.svodId ?? "");
        const at = listed.indexOf(id);
        const step = ev.shiftKey ? -1 : 1;
        placeSvodRef.current(at < 0 ? 0 : at + step, "select");
        return;
      }
      if (!nav && !toggle) return;
      if (target?.closest("input, textarea, select") && !title) return;
      if (title instanceof HTMLInputElement && (key === "Home" || key === "End")) {
        const len = title.value.length;
        const from = title.selectionStart ?? 0;
        const to = title.selectionEnd ?? from;
        if (ev.shiftKey) {
          if (key === "Home") title.setSelectionRange(0, to);
          else title.setSelectionRange(from, len);
        } else {
          const pos = key === "Home" ? 0 : len;
          title.setSelectionRange(pos, pos);
        }
        ev.preventDefault();
        return;
      }
      ev.preventDefault();
      if (toggle) {
        toggleSvodRef.current();
        return;
      }
      const mode = ev.shiftKey ? "extend" : ev.ctrlKey || ev.metaKey ? "move" : "select";
      const current = target?.closest("tr")?.querySelector<HTMLElement>("[data-svod-id]")?.dataset.svodId
        ?? document.querySelector<HTMLElement>(".form-sheet tr.is-caret [data-svod-id]")?.dataset.svodId;
      const listed = [...document.querySelectorAll<HTMLElement>(".form-sheet [data-svod-id]")].map((el) => el.dataset.svodId ?? "");
      const at = current ? listed.indexOf(current) : -1;
      if (key === "Home") placeSvodRef.current(0, mode);
      else if (key === "End") placeSvodRef.current(Math.max(0, listed.length - 1), mode);
      else if (key === "ArrowDown") placeSvodRef.current(at < 0 ? 0 : at + 1, mode);
      else placeSvodRef.current(at < 0 ? listed.length - 1 : at - 1, mode);
    };
    window.addEventListener("keydown", onSvodKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("auxclick", onAux);
      window.removeEventListener("keydown", onSvodKey);
    };
  }, []);

  useEffect(() => {
    if (!svodMenu) return;
    const close = (ev: PointerEvent) => {
      const node = ev.target as Node | null;
      if (node && document.querySelector(".svod-menu")?.contains(node)) return;
      setSvodMenu(false);
    };
    const onEsc = (ev: KeyboardEvent) => { if (ev.key === "Escape") setSvodMenu(false); };
    window.addEventListener("pointerdown", close);
    window.addEventListener("keydown", onEsc);
    return () => {
      window.removeEventListener("pointerdown", close);
      window.removeEventListener("keydown", onEsc);
    };
  }, [svodMenu]);

  useEffect(() => {
    refreshList();
    api.health().catch(() => setErr("API недоступен"));
    const desk = readHash() ?? readStoredDesk();
    if (!desk) {
      historyReady.current = true;
      absorbedBoot.current = true;
      return;
    }
    if (desk.tab) setTab(desk.tab);
    const at = session.current;
    api
      .get(desk.projectId)
      .then((d) => {
        if (at === session.current) applyDoc(d, desk.estId);
      })
      .catch(() => clearDesk())
      .finally(() => { historyReady.current = true; });
  }, []);

  useEffect(() => {
    window.sntpHotkey = (cmd) => {
      if (cmd === "edit") setTab("smets");
      if (cmd === "factors" && pos) setTab("smets");
    };
    return () => {
      window.sntpHotkey = undefined;
    };
  }, [pos]);

  useEffect(() => {
    const onHost = (ev: MessageEvent) => {
      const raw = typeof ev.data === "string" ? ev.data : "";
      if (!raw.includes("\"paste\"") && !raw.includes("paste")) return;
      try {
        const data = JSON.parse(raw) as { cmd?: string; text?: string; html?: string };
        if (data.cmd === "paste" && est) {
          const clip = readVolumeClip(data.text ?? "");
          if (clip) putVolumeRef.current(est.id, clip, tabRef.current === "smets");
          else setPaste({ text: data.text, html: data.html });
        }
      } catch {
        /* не сообщение хоста */
      }
    };
    const chrome = (window as unknown as { chrome?: { webview?: { addEventListener: (n: string, fn: (ev: MessageEvent) => void) => void; removeEventListener: (n: string, fn: (ev: MessageEvent) => void) => void } } }).chrome;
    chrome?.webview?.addEventListener("message", onHost);
    const onPaste = (e: ClipboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target?.closest("input, textarea, select")) return;
      if (!est || !doc) return;
      const text = e.clipboardData?.getData("text/plain") ?? "";
      const html = e.clipboardData?.getData("text/html") ?? "";
      const clip = readVolumeClip(text);
      if (clip) {
        e.preventDefault();
        putVolumeRef.current(est.id, clip, tabRef.current === "smets");
        return;
      }
      if (!text.trim() && !html.trim()) return;
      e.preventDefault();
      setPaste({ text, html });
    };
    window.addEventListener("paste", onPaste);
    return () => {
      chrome?.webview?.removeEventListener("message", onHost);
      window.removeEventListener("paste", onPaste);
    };
  }, [est, doc]);

  async function run<T>(fn: () => Promise<T>, after?: (v: T) => void) {
    const at = session.current;
    const seq = ++runSeq.current;
    setBusy(true);
    setErr(null);
    try {
      const v = await fn();
      if (at === session.current && seq === runSeq.current) after?.(v);
      if (seq === runSeq.current) await refreshList();
    } catch (e) {
      if (seq === runSeq.current) setErr(e instanceof Error ? e.message : String(e));
    } finally {
      if (seq === runSeq.current) setBusy(false);
    }
  }

  function saveIfChanged(input: HTMLInputElement, commit: (value: string) => void) {
    const value = input.value;
    if ((input.dataset.saved ?? value) === value) return;
    input.dataset.saved = value;
    commit(value);
  }

  function gridScroll() {
    const wrap = document.querySelector(".grid-wrap");
    return wrap instanceof HTMLElement ? wrap.scrollTop : 0;
  }

  function keepGridScroll(top: number) {
    requestAnimationFrame(() => {
      const wrap = document.querySelector(".grid-wrap");
      if (wrap instanceof HTMLElement) wrap.scrollTop = top;
    });
  }

  async function editRows<T>(fn: () => Promise<T>, after?: (v: T) => void) {
    const at = session.current;
    const top = gridScroll();
    setErr(null);
    try {
      const v = await fn();
      if (at !== session.current) return;
      after?.(v);
      keepGridScroll(top);
    } catch (e) {
      setErr(e instanceof Error ? e.message : String(e));
      keepGridScroll(top);
    }
  }

  function revealFolder(id: string) {
    setErr(null);
    api.revealFolder(id).catch((e) => setErr(e instanceof Error ? e.message : String(e)));
  }

  function revealSources(id: string) {
    setErr(null);
    api.revealSources(id).catch((e) => setErr(e instanceof Error ? e.message : String(e)));
  }

  function openProject(id: string, estId?: string, tab?: Tab) {
    run(() => api.get(id), (d) => {
      applyDoc(d, estId);
      setTab(estId ? "smets" : (tab ?? "smets"));
    });
  }

  function importForeignEstimate(payload: EstimateDrag) {
    const targetId = doc?.id ?? currentProject?.id;
    if (!targetId) {
      setErr("Сначала откройте проект, куда копировать смету");
      return;
    }
    run(() => api.importEstimate(targetId, payload.sourceProjectId, payload.sourceEstimateId), (d) => {
      applyDoc(d);
      setTab("smets");
    });
  }

  function importForeignRate(payload: RateDrag) {
    if (!doc || !estId) {
      setErr("Откройте смету, в которую копировать расценку");
      return;
    }
    run(
      () => api.importPosition(doc.id, estId, payload.sourceProjectId, payload.sourceEstimateId, payload.sourcePositionId),
      (d) => applyDoc(d, estId)
    );
  }

  function detachPreview() {
    const q = new URLSearchParams({ preview: "1" });
    if (previewFocus?.projectId) q.set("project", previewFocus.projectId);
    if (previewFocus?.estimateId) q.set("estimate", previewFocus.estimateId);
    const opened = window.open(`${location.pathname}?${q.toString()}`, "sntp-preview", `width=${previewWidthRef.current},height=860`);
    if (!opened) setErr("Не удалось открыть окно просмотра");
    else setPreviewOpen(false);
  }

  const importEstimateRef = useRef(importForeignEstimate);
  const importRateRef = useRef(importForeignRate);
  importEstimateRef.current = importForeignEstimate;
  importRateRef.current = importForeignRate;
  useEffect(() => listenPreview((cmd) => {
    if (cmd.type === "copy-estimate") importEstimateRef.current(cmd.payload);
    else importRateRef.current(cmd.payload);
  }), []);

  function takeEstimateDrop(ev: { preventDefault: () => void; stopPropagation: () => void; dataTransfer: DataTransfer }) {
    const payload = readMarked<EstimateDrag>(ev.dataTransfer.getData("text/plain"), estimateMark);
    if (!payload) return false;
    ev.preventDefault();
    ev.stopPropagation();
    setTreeTip(null);
    importForeignEstimate(payload);
    return true;
  }

  function goHome() {
    session.current++;
    setDoc(null);
    setEstId(null);
    setPosId(null);
    setTab("smets");
    if (location.hash.startsWith("#/")) history.replaceState(null, "", location.pathname + location.search);
  }

  useEffect(() => {
    const onHash = () => {
      if (readHash()) return;
      session.current++;
      setDoc(null);
      setEstId(null);
      setPosId(null);
      setTab("smets");
    };
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  function svodIds() {
    if (!doc?.summary) return [];
    return visibleSvodIds(svodTree(doc.summary.rows, doc.estimates), svodFold, !!doc.summary.hideStageHeading);
  }

  function pickSvod(id: string, ev: React.MouseEvent) {
    const order = svodIds();
    setSvodCur(id);
    if (ev.shiftKey && svodAnchor.current) {
      const a = order.indexOf(svodAnchor.current);
      const b = order.indexOf(id);
      if (a >= 0 && b >= 0) {
        const [lo, hi] = a < b ? [a, b] : [b, a];
        setSvodSel(order.slice(lo, hi + 1));
        return;
      }
    }
    if (ev.ctrlKey || ev.metaKey) {
      setSvodSel((cur) => (cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]));
      svodAnchor.current = id;
      return;
    }
    setSvodSel([id]);
    svodAnchor.current = id;
  }

  function placeSvod(index: number, mode: "select" | "extend" | "move") {
    const ids = svodIds();
    if (!ids.length) return;
    const at = Math.min(ids.length - 1, Math.max(0, index));
    const next = ids[at];
    if (mode === "extend") {
      const origin = svodAnchor.current && ids.includes(svodAnchor.current) ? svodAnchor.current : next;
      const a = ids.indexOf(origin);
      const [lo, hi] = a < at ? [a, at] : [at, a];
      setSvodSel(ids.slice(lo, hi + 1));
    } else if (mode === "select") {
      setSvodSel([next]);
      svodAnchor.current = next;
    }
    setSvodCur(next);
    requestAnimationFrame(() => {
      const el = document.querySelector<HTMLElement>(`.form-sheet [data-svod-id="${CSS.escape(next)}"]`);
      el?.focus({ preventScroll: true });
      el?.closest("tr")?.scrollIntoView({ block: "nearest" });
    });
  }
  placeSvodRef.current = placeSvod;

  function toggleSvod() {
    const id = svodCur ?? svodAnchor.current;
    if (!id) return;
    setSvodSel((cur) => (cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]));
    setSvodCur(id);
    svodAnchor.current = id;
  }
  toggleSvodRef.current = toggleSvod;

  function takeGroup(r: { document: PirDocument; estimate?: { id: string } | null }) {
    applyDoc(r.document);
    const id = r.estimate?.id;
    if (!id) return;
    setSvodSel([id]);
    svodAnchor.current = id;
    setSvodCur(id);
    setSvodFocus(id);
  }

  function jumpPlace(delta: -1 | 1) {
    const nextIndex = histIndex.current + delta;
    if (nextIndex < 0 || nextIndex >= historyRef.current.length) return;
    histIndex.current = nextIndex;
    const place = historyRef.current[nextIndex];
    pendingPlace.current = place;
    restoringPlace.current = true;
    setCanBack(nextIndex > 0);
    setCanForward(nextIndex < historyRef.current.length - 1);
    if (!place.projectId) {
      session.current++;
      setDoc(null);
      setEstId(null);
      setPosId(null);
      setTab(place.tab);
      setEstView(place.estView);
      return;
    }
    setTab(place.tab);
    setEstView(place.estView);
    const open = docRef.current;
    if (open?.id === place.projectId) {
      const row = open.estimates.find((e) => e.id === place.estId || e.number === place.estId);
      setEstId(place.estId);
      setPosId(row?.positions.find((p) => p.kind === "work")?.id ?? row?.positions[0]?.id ?? null);
      return;
    }
    const at = session.current;
    api.get(place.projectId).then((d) => {
      if (at !== session.current) return;
      restoringPlace.current = true;
      pendingPlace.current = place;
      applyDoc(d, place.estId ?? undefined);
      setTab(place.tab);
      setEstView(place.estView);
    }).catch((e) => {
      restoringPlace.current = false;
      pendingPlace.current = null;
      setErr(e instanceof Error ? e.message : String(e));
    });
  }
  jumpRef.current = jumpPlace;

  function applyDoc(d: PirDocument, selectEst?: string, selectPos?: string) {
    setDoc(d);
    setCurrentProject({ id: d.id, name: d.project.objectName });
    const known = (id?: string | null) => !!id && d.estimates.some((x) => x.id === id || x.number === id);
    const keepEst = known(estId) ? estId : null;
    const next = (known(selectEst) ? selectEst : null) ?? keepEst ?? d.estimates[0]?.id ?? null;
    setEstId(next);
    const e = d.estimates.find((x) => x.id === next || x.number === next);
    const keepPos = posId && e?.positions.some((p) => p.id === posId) ? posId : null;
    setPosId(
      selectPos ?? keepPos ?? e?.positions.find((p) => p.kind === "work")?.id ?? e?.positions[0]?.id ?? null
    );
  }

  putVolumeRef.current = (targetId, clip, openVolumes) => {
    if (!doc) return;
    run(() => api.addVolumeFromBook(doc.id, targetId, clip.bookId, clip.table, clip.item), (d) => {
      applyDoc(d, targetId);
      if (openVolumes) {
        setTab("smets");
        setEstView("volumes");
      }
      setRateLanded(targetId);
      window.setTimeout(() => setRateLanded((id) => (id === targetId ? "" : id)), 1600);
    });
  };

  return (
    <div className="app" style={{ ["--rail" as string]: `${railWidth}px` }}>
      <header className="titleblock">
        <a className="brand" href="#/" title="На главную" onClick={(e) => { e.preventDefault(); goHome(); }}>
          <span className="brand-inner">
            <svg width="28" height="28" viewBox="0 0 28 28" fill="none" aria-hidden="true">
              <rect x="3.5" y="5.5" width="16" height="20" stroke="currentColor" strokeWidth="1.4" />
              <path d="M19.5 5.5h5v6h-5V5.5Z" stroke="currentColor" strokeWidth="1.4" />
              <path d="M7 12.5h9M7 16.5h9M7 20.5h6" stroke="currentColor" strokeWidth="1.2" />
            </svg>
            <span className="brand-text">
              <span className="brand-name">SNTP-ADEPT<sup className="brand-index" title="Программа полностью придумана, создана искусственным интеллектом"><svg className="brand-brain" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 5a3 3 0 1 0-5.997.125 4 4 0 0 0-2.526 5.77 4 4 0 0 0 .556 6.588A4 4 0 1 0 12 18Z" /><path d="M12 5a3 3 0 1 1 5.997.125 4 4 0 0 1 2.526 5.77 4 4 0 0 1-.556 6.588A4 4 0 1 1 12 18Z" /><path d="M15 13a4.5 4.5 0 0 1-3-4 4.5 4.5 0 0 1-3 4" /><path d="M17.599 6.5a3 3 0 0 0 .399-1.375" /><path d="M6.003 5.125A3 3 0 0 0 6.401 6.5" /><path d="M3.477 10.896a4 4 0 0 1 .585-.396" /><path d="M19.938 10.5a4 4 0 0 1 .585.396" /><path d="M6 18a4 4 0 0 1-1.967-.516" /><path d="M19.967 17.484A4 4 0 0 1 18 18" /></svg></sup></span>
            </span>
          </span>
        </a>
        <div className="object-meta">
          <div className="nav-history">
            <button type="button" className="icon-btn" aria-label="Назад" title="Назад, как в проводнике (Alt+←)" disabled={!canBack} onClick={() => jumpPlace(-1)}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M15 5 8 12l7 7" /></svg>
            </button>
            <button type="button" className="icon-btn" aria-label="Вперёд" title="Вперёд (Alt+→)" disabled={!canForward} onClick={() => jumpPlace(1)}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg>
            </button>
          </div>
          <button
            type="button"
            className="btn home-back"
            title="Исходные данные для составления смет: задание, технические требования, программа изысканий, схемы, файлы смежников"
            disabled={!doc && !currentProject}
            onClick={() => {
              const id = doc?.id ?? currentProject?.id;
              if (id) revealSources(id);
            }}
          >
            ИД
          </button>
          {doc ? (
            <>
              <input
                className="title"
                aria-label="Объект"
                value={doc.project.objectName}
                onChange={(e) => setDoc({ ...doc, project: { ...doc.project, objectName: e.target.value } })}
                onFocus={(e) => { e.currentTarget.dataset.saved = e.currentTarget.value; }}
                onBlur={(e) => saveIfChanged(e.currentTarget, (objectName) => run(() => api.patchProject(doc.id, { objectName }), applyDoc))}
              />
            </>
          ) : currentProject ? (
            <>
              <button type="button" className="home-title" title="Вернуться к проекту" onClick={() => openProject(currentProject.id)}>
                {currentProject.name}
              </button>
              <button type="button" className="btn home-back" onClick={() => openProject(currentProject.id)}>
                Вернуться к проекту
              </button>
            </>
          ) : (
            <h1 className="home-empty">Текущий проект не выбран</h1>
          )}
          <span className="preview-cmds">
            <button
              type="button"
              className={`btn home-back ${previewOpen && !previewCollapsed ? "on" : ""}`}
              onClick={() => {
                if (!previewOpen || previewCollapsed) {
                  setPreviewCollapsed(false);
                  setPreviewOpen(true);
                  return;
                }
                setPreviewOpen(false);
              }}
            >
              {previewOpen && !previewCollapsed ? "Скрыть аналоги" : "Аналоги"}
            </button>
            {previewOpen && !previewCollapsed && (
              <button type="button" className="btn home-back" onClick={detachPreview}>Открепить</button>
            )}
          </span>
        </div>
        <div className="title-actions">
          <button className="icon-btn" type="button" aria-label="Палитра команд" title="Ctrl+K" onClick={() => setPalette(true)}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><circle cx="11" cy="11" r="7" /><path d="M20 20 16.5 16.5" /></svg>
          </button>
          <div className="theme-picker-wrap" ref={themePickerRef}>
            <button
              className={`icon-btn ${themeMenuOpen ? "is-on" : ""}`}
              type="button"
              aria-label="Тема"
              title={`Оформление: ${THEME_STYLES.find((s) => s.id === themeStyle)?.title ?? ""} · ${theme === "dark" ? "Тёмная" : "Светлая"}`}
              onClick={() => setThemeMenuOpen((v) => !v)}
            >
              {theme === "dark" ? (
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7">
                  <circle cx="12" cy="12" r="4" />
                  <path d="M12 2v2m0 16v2M4.93 4.93l1.41 1.41m11.32 11.32 1.41 1.41M2 12h2m16 0h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41" />
                </svg>
              ) : (
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7">
                  <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
                </svg>
              )}
            </button>
            {themeMenuOpen && (
              <div className="theme-menu" role="dialog" aria-label="Стили оформления">
                <div className="theme-menu-header">
                  <span>Оформление</span>
                  <span>{theme === "dark" ? "Тёмная" : "Светлая"}</span>
                </div>
                <div className="theme-mode-row">
                  <button
                    type="button"
                    className={`theme-mode-btn ${theme === "light" ? "is-active" : ""}`}
                    onClick={() => setTheme("light")}
                  >
                    ☀️ Светлая
                  </button>
                  <button
                    type="button"
                    className={`theme-mode-btn ${theme === "dark" ? "is-active" : ""}`}
                    onClick={() => setTheme("dark")}
                  >
                    🌙 Тёмная
                  </button>
                </div>
                <div className="theme-styles-list">
                  {THEME_STYLES.map((st) => (
                    <button
                      key={st.id}
                      type="button"
                      className={`theme-style-card ${themeStyle === st.id ? "is-active" : ""}`}
                      onClick={() => {
                        setThemeStyle(st.id);
                        setThemeMenuOpen(false);
                      }}
                    >
                      <span className={`theme-swatch swatch-${st.id}-${theme}`} />
                      <span className="theme-style-info">
                        <span className="theme-style-title">{st.title}</span>
                        <span className="theme-style-desc">{st.desc}</span>
                      </span>
                      {themeStyle === st.id && <span className="theme-check">✓</span>}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
          {doc && (
            <>
              <button
                className="icon-btn"
                type="button"
                aria-label="Открыть папку в проводнике"
                title={doc.project.workFolder ? `Открыть в проводнике: ${doc.project.workFolder}` : "У объекта нет папки"}
                disabled={!doc.project.workFolder}
                onClick={() => revealFolder(doc.id)}
              >
                <FolderIcon />
              </button>
              <button
                className="icon-btn"
                type="button"
                aria-label="Перечитать проект из папки"
                title="Перечитать project.json и сметы из папки объекта и пересчитать"
                disabled={!doc.project.workFolder}
                onClick={() => run(() => api.reloadProject(doc.id), (d) => applyDoc(d))}
              >
                <ReloadIcon />
              </button>
              {tab !== "smets" && (
                <button type="button" className="btn" onClick={() => setTab("smets")} title="Вернуться к смете. Проект остаётся открытым.">
                  К смете
                </button>
              )}
            </>
          )}
        </div>
      </header>

      <nav className={railWidth <= RAIL_LINE ? "rail is-line" : "rail"} aria-label="Разделы">
        <button
          type="button"
          className="rail-fold"
          aria-label={railWidth <= RAIL_LINE ? "Развернуть панель" : "Свернуть в полоску"}
          title={railWidth <= RAIL_LINE ? "Развернуть панель" : "Свернуть в полоску"}
          onClick={toggleRail}
        >
          {railWidth <= RAIL_LINE ? "»" : "«"}
        </button>
        <div
          className="rail-grip"
          role="separator"
          aria-orientation="vertical"
          aria-valuemin={RAIL_MIN}
          aria-valuemax={RAIL_MAX}
          aria-valuenow={railWidth}
          aria-label="Ширина панели"
          title="Потяните край, чтобы задать ширину. Двойной щелчок сворачивает в полоску."
          tabIndex={0}
          onPointerDown={onGripDown}
          onDoubleClick={toggleRail}
          onKeyDown={(e) => {
            if (e.key === "ArrowLeft") rememberRail(railWidthRef.current - 16);
            else if (e.key === "ArrowRight") rememberRail(railWidthRef.current + 16);
            else if (e.key === "Home") rememberRail(RAIL_MIN);
            else if (e.key === "End") rememberRail(RAIL_MAX);
          }}
        />
        <div className="rail-nav">
          {([
            ["svod", "Свод"],
            ["smets", "Смета"],
            ["catalog", "Каталог"],
            ["import", "Импорт"],
            ["export", "Выгрузка"],
            ["issues", doc?.issues.length ? `Уточнения ${doc.issues.length}` : "Уточнения"]
          ] as [Tab, string][]).map(([id, label]) => (
            <button key={id} type="button" className={doc && tab === id ? "on" : ""} title={label} aria-label={label} onClick={() => { if (!doc && id !== "catalog" && id !== "import") return; setTab(id); }}>
              <NavIcon id={id} />
              {id === "issues" && (doc?.issues.length ?? 0) > 0 && <span className="nav-badge">{doc!.issues.length}</span>}
            </button>
          ))}
        </div>
        <div
          className="rail-tree"
          onDragOver={(ev) => {
            if ([...ev.dataTransfer.types].includes("text/plain")) {
              ev.preventDefault();
              ev.dataTransfer.dropEffect = "copy";
            }
          }}
          onDrop={(ev) => { takeEstimateDrop(ev); }}
        >
          <div className="rail-label">
            <span className="rail-label-text">Локальные сметы</span>
            <button type="button" className="btn" disabled={!doc} title="Новая смета" onClick={() => setShowNewLocal(true)}>
              + <span className="rail-label-text">смета</span>
            </button>
          </div>
          {!doc && <p className="muted rail-label-text">Создайте объект в каталоге проектов.</p>}
          {doc?.estimates.map((e) => (
            <button
              key={e.id}
              type="button"
              draggable
              className={[
                "tree-item",
                e.id === est?.id && tab === "smets" ? "is-on" : "",
                rateLanded === e.id ? "is-landed" : "",
                treeTip?.id === e.id && treeTip.mode === "rate" ? "is-rate" : "",
                treeTip?.id === e.id && treeTip.mode !== "rate" ? "is-hot" : "",
                treeTip?.id === e.id && treeTip.mode === "before" ? "drop-before" : "",
                treeTip?.id === e.id && treeTip.mode === "child" ? "drop-child" : ""
              ].filter(Boolean).join(" ")}
              style={{ ["--depth" as string]: String(e.outlineDepth ?? 0) }}
              onMouseEnter={(ev) => showTreeTip(ev.currentTarget, e, "open")}
              onMouseLeave={() => setTreeTip((t) => (t?.id === e.id && t.mode === "open" ? null : t))}
              onDragStart={(ev) => ev.dataTransfer.setData("text/plain", e.id)}
              onDragOver={(ev) => {
                if (isVolumeDrag(ev.dataTransfer.types)) {
                  ev.preventDefault();
                  ev.dataTransfer.dropEffect = "copy";
                  showTreeTip(ev.currentTarget, e, "rate");
                  return;
                }
                ev.preventDefault();
                const rect = ev.currentTarget.getBoundingClientRect();
                showTreeTip(ev.currentTarget, e, ev.clientY > rect.top + rect.height * 0.55 ? "child" : "before");
              }}
              onDragLeave={() => setTreeTip((t) => (t?.id === e.id ? null : t))}
              onDrop={(ev) => {
                if (takeEstimateDrop(ev)) return;
                ev.preventDefault();
                setTreeTip(null);
                const clip = clipFromTransfer(ev.dataTransfer);
                if (clip && doc) {
                  putVolumeRef.current(e.id, clip, tab === "smets");
                  return;
                }
                const from = ev.dataTransfer.getData("text/plain");
                if (!from || from === e.id || !doc) return;
                const rect = (ev.currentTarget as HTMLElement).getBoundingClientRect();
                const asChild = ev.clientY > rect.top + rect.height * 0.55;
                run(() => api.moveOutline(doc.id, from, e.id, asChild), applyDoc);
              }}
              onClick={() => {
                setEstId(e.id);
                setPosId(e.positions.find((p) => p.kind === "work")?.id ?? e.positions[0]?.id ?? null);
                setTab("smets");
              }}
            >
              <span className="num">{e.number ?? ""}</span>
              <span className="form-mark">{estimateFormMark(e.form)}</span>
              <span className="ttl">{e.title}</span>
              <span className="sum">{money(e.engineAmount ?? e.excelAmount)}</span>
            </button>
          ))}
        </div>
      </nav>
      {treeTip && (
        <div className="tree-badge" style={{ top: treeTip.top, left: treeTip.left }} role="tooltip">
          <span className="num">{treeTip.number}{treeTip.form ? ` · ${treeTip.form}` : ""}</span>
          <span className="ttl">{treeTip.title}</span>
          <span className="sum">{treeTip.sum}</span>
          <span className="hint">
            {treeTip.mode === "rate" ? `Расценка попадёт в смету №${treeTip.number}` : treeTip.mode === "child" ? "Вложится в эту смету" : treeTip.mode === "before" ? "Станет соседней перед ней" : "Откроется эта смета"}
          </span>
        </div>
      )}

      <main className="stage">
      {(busy || err) && (
        <div className="stage-note" aria-live="polite">
          <p className={err ? "err" : "muted"}>{err ?? "Считаю…"}</p>
        </div>
      )}

      {!doc && tab !== "catalog" && tab !== "import" && (
        <div className="desk">
          <RecentProjects
            list={list}
            currentId={currentProject?.id}
            onOpen={openProject}
            onPreview={(projectId, estimateId) => {
              setPreviewFocus({ projectId, estimateId });
              setPreviewCollapsed(false);
              setPreviewOpen(true);
            }}
            onCreate={() => setShowNewObject(true)}
            onRefresh={() => {
              setErr(null);
              api.list().then(setList).catch((e) => setErr(e instanceof Error ? e.message : String(e)));
            }}
            onReveal={revealFolder}
            onSources={revealSources}
          />
        </div>
      )}

      {tab === "catalog" && (
        <CatalogPane
          conditions={doc?.project.conditions}
          estimateConditions={est?.conditions}
          estimateSuppressed={est?.suppressedConditionIds}
          onToggleCondition={
            doc
              ? (variable) => {
                  const cur = doc.project.conditions ?? [];
                  const next = cur.some((c) => c.id === variable.id)
                    ? cur.filter((c) => c.id !== variable.id)
                    : [...cur, { id: variable.id, alias: variable.alias, enabled: true, uses: variable.uses }];
                  run(() => api.patchProject(doc.id, { conditions: next }), applyDoc);
                }
              : undefined
          }
          localIds={(pos?.factors ?? []).map((f) => f.catalogKey).filter((id): id is string => !!id)}
          onToggleLocal={
            doc && est && pos
              ? (variable) => {
                  const use = variable.uses[0];
                  const factors = pos.factors ?? [];
                  const has = factors.some((f) => f.catalogKey === variable.id);
                  const next = has
                    ? factors.filter((f) => f.catalogKey !== variable.id)
                    : [...factors, {
                        identifier: variable.id,
                        caption: variable.alias,
                        projectDoc: use?.value,
                        workDoc: use?.value,
                        workProject: use?.value,
                        combineType: "complicating",
                        role: "complicating",
                        enabled: true,
                        source: "catalog",
                        catalogKey: variable.id
                      }];
                  run(() => api.patchPosition(doc.id, pos.id, { factors: next }), (d) => applyDoc(d, est.id, pos.id));
                }
              : undefined
          }
          onToggleEstimate={
            doc && est
              ? (variable, on) => {
                  const projectOn = (doc.project.conditions ?? []).some((c) => c.id === variable.id);
                  if (projectOn) {
                    const suppressed = new Set(est.suppressedConditionIds ?? []);
                    if (on) suppressed.delete(variable.id);
                    else suppressed.add(variable.id);
                    run(() => api.patchEstimate(doc.id, est.id, { suppressedConditionIds: [...suppressed] }), (d) => applyDoc(d, est.id));
                    return;
                  }
                  const cur = est.conditions ?? [];
                  const next = on
                    ? [...cur.filter((c) => c.id !== variable.id), { id: variable.id, alias: variable.alias, enabled: true, uses: variable.uses }]
                    : cur.filter((c) => c.id !== variable.id);
                  run(() => api.patchEstimate(doc.id, est.id, { conditions: next }), (d) => applyDoc(d, est.id));
                }
              : undefined
          }
          onInsertBook={
            doc && est
              ? (bookId, table, item) =>
                  run(() => api.addVolumeFromBook(doc.id, est.id, bookId, table, item), (d) => {
                    applyDoc(d, est.id);
                  })
              : undefined
          }
        />
      )}

      {tab === "import" && (
        <section className="pane import">
          <AnalogPane
            projectId={doc?.id}
            acceptProjectId={doc?.id}
            acceptEstimateId={est?.id}
            onAccepted={(d) => applyDoc(d, est?.id)}
            onError={(message) => setErr(message)}
          />
        </section>
      )}

      {tab === "export" && doc && (
        <section className="pane">
          <p>Голый XML проектной сметы Гранда (без GrandSmetaSign), GUID {"{39200715-…}"}.</p>
          <a href={api.exportXmlUrl(doc.id, est?.id)} download>
            Скачать XML текущей сметы
          </a>
          {" · "}
          <a href={api.exportJsonUrl(doc.id)} download>
            PIR-IR JSON
          </a>
        </section>
      )}

      {doc && tab === "svod" && (
        <section className="pane">
          {!doc?.summary ? (
            <p>
              Свода нет.{" "}
              <button type="button" disabled={!doc} onClick={() => run(() => api.createSummary(doc!.id), applyDoc)}>
                Создать свод 1ПС
              </button>
            </p>
          ) : (
            <article className="form-sheet">
              <header className="form-head">
                <div className="form-kicker">Форма 1ПС</div>
                <h2>{doc.project.objectName}</h2>
                <p className="muted">Щелчок задаёт текущую строку, стрелки переносят её. Shift со стрелками берёт диапазон, Ctrl+пробел добавляет или снимает строку, Ctrl+стрелки идут по списку и не сбрасывают уже выбранное.</p>
              </header>
                <div className="row svod-tools">
                  <button type="button" onClick={() => run(() => api.createForm3p(doc.id).then((r) => r.document), applyDoc)}>
                    Добавить 3П
                  </button>
                  <button type="button" onClick={() => run(() => api.createAutomation(doc.id).then((r) => r.document), applyDoc)}>
                    Добавить автоматизацию
                  </button>
                  <div className="svod-menu">
                    <button
                      type="button"
                      aria-expanded={svodMenu}
                      aria-haspopup="menu"
                      title="Раздел над выделенными, вложить или вынести"
                      onClick={() => setSvodMenu((v) => !v)}
                    >
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="M4 6h16M4 12h10M4 18h13" /></svg>
                      Раздел
                    </button>
                    {svodMenu && (
                      <div className="svod-menu-pop" role="menu">
                        <button
                          type="button"
                          role="menuitem"
                          disabled={svodSel.length === 0}
                          onClick={() => { setSvodMenu(false); run(() => api.createHeading(doc.id, svodSel), takeGroup); }}
                        >
                          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="M4 7h16M4 12h16M8 17h8" /></svg>
                          <span><b>Заголовок</b><small>Поставить раздел над выделенными сметами. Смета разделом не становится.</small></span>
                        </button>
                        <button
                          type="button"
                          role="menuitem"
                          disabled={svodSel.length === 0}
                          onClick={() => { setSvodMenu(false); run(() => api.shiftOutline(doc.id, svodSel, 1), takeGroup); }}
                        >
                          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="M4 6h16M11 12h9M4 18h16M5 9.5 9 12l-4 2.5" /></svg>
                          <span><b>Вложить</b><small>Сверху сразу появляется раздел. Смета остаётся сметой.</small></span>
                        </button>
                        <button
                          type="button"
                          role="menuitem"
                          disabled={svodSel.length === 0}
                          onClick={() => { setSvodMenu(false); run(() => api.shiftOutline(doc.id, svodSel, -1), (r) => applyDoc(r.document)); }}
                        >
                          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="M4 6h16M4 12h9M4 18h16M19 9.5 15 12l4 2.5" /></svg>
                          <span><b>Вынести</b><small>Поднять выделенные сметы на уровень выше.</small></span>
                        </button>
                      </div>
                    )}
                  </div>
                </div>
              <table className="ledger">
                <thead>
                  <tr>
                    <th rowSpan={2}>Перечень выполняемых работ</th>
                    <th rowSpan={2} className="svod-ref">Ссылка<br />на № смет<br />по формам 2П, 3П</th>
                    <th colSpan={3}>Полная стоимость работ, руб.</th>
                  </tr>
                  <tr>
                    <th className="money">изыскательских</th>
                    <th className="money">проектных</th>
                    <th className="money">Всего</th>
                  </tr>
                </thead>
                <tbody>
                  {(() => {
                    const rows = doc.summary.rows;
                    const tree = svodTree(rows, doc.estimates);
                    const all = roll(tree);
                    const stage = doc.project.stageLabel || "Рабочая документация";
                    const objectKey = "svod-object";
                    const stageKey = "svod-stage";
                    const objectOpen = !svodFold[objectKey];
                    const stageOpen = !svodFold[stageKey];
                    const toggle = (key: string) => setSvodFold((cur) => ({ ...cur, [key]: !cur[key] }));
                    const placeOutline = (targetId: string, ev: React.DragEvent) => {
                      ev.preventDefault();
                      if (clipFromTransfer(ev.dataTransfer)) return;
                      const from = ev.dataTransfer.getData("text/plain");
                      if (!from || from === targetId) return;
                      const rect = (ev.currentTarget as HTMLElement).getBoundingClientRect();
                      const asChild = ev.clientY > rect.top + rect.height * 0.55;
                      run(() => api.moveOutline(doc.id, from, targetId, asChild), applyDoc);
                    };
                    const sectionRow = (
                      key: string,
                      title: string,
                      open: boolean,
                      bag: { survey: number; design: number; total: number },
                      onDelete?: () => void,
                      heading?: Estimate
                    ) => (
                      <tr
                        key={key}
                        className={["svod-section", heading && svodSel.includes(heading.id) ? "is-current" : "", heading && heading.id === svodCur ? "is-caret" : ""].filter(Boolean).join(" ")}
                        draggable={!!heading}
                        onClick={(ev) => heading && pickSvod(heading.id, ev)}
                        onDragStart={(ev) => heading && ev.dataTransfer.setData("text/plain", heading.id)}
                        onDragOver={(ev) => heading && ev.preventDefault()}
                        onDrop={(ev) => heading && placeOutline(heading.id, ev)}
                      >
                        <td colSpan={2}>
                          <span className="section-toggle">
                            <button type="button" className="section-caret" onClick={() => toggle(key)} aria-label={open ? "Свернуть раздел" : "Развернуть раздел"}>
                              <CaretIcon open={open} />
                            </button>
                            {heading ? (
                              <>
                              <span className="svod-num">{heading.number}.</span>
                              <input
                                className="title"
                                data-svod-id={heading.id}
                                autoFocus={svodFocus === heading.id}
                                value={heading.title}
                                onFocus={(ev) => {
                                  ev.currentTarget.dataset.saved = ev.currentTarget.value;
                                  setSvodFocus((id) => (id === heading.id ? null : id));
                                }}
                                onClick={(ev) => { ev.stopPropagation(); pickSvod(heading.id, ev); }}
                                onChange={(ev) => {
                                  const next = ev.target.value;
                                  setDoc({
                                    ...doc,
                                    estimates: doc.estimates.map((e) => (e.id === heading.id ? { ...e, title: next } : e))
                                  });
                                }}
                                onBlur={(ev) => saveIfChanged(ev.currentTarget, (title) => run(() => api.patchEstimate(doc.id, heading.id, { title }), applyDoc))}
                              />
                              </>
                            ) : <span>{title}</span>}
                          </span>
                        </td>
                        <td className="money">{money(bag.survey)}</td>
                        <td className="money">{money(bag.design)}</td>
                        <td className="money">
                          <span className="svod-total">
                            <span className="svod-sum">{money(bag.total)}</span>
                            {onDelete && (
                              <span className="svod-acts">
                                <button type="button" className="btn danger" title="Удалить заголовок" onClick={(ev) => { ev.stopPropagation(); onDelete(); }}>
                                  Удалить
                                </button>
                              </span>
                            )}
                          </span>
                        </td>
                      </tr>
                    );
                    const leaves = (nodes: SvodNode[], depth: number): React.ReactNode[] => nodes.flatMap((node) => {
                      if (node.heading) {
                        const open = !svodFold[node.key];
                        return [
                          sectionRow(
                            node.key,
                            node.title,
                            open,
                            roll(node.children),
                            node.est ? () => run(() => api.deleteEstimate(doc.id, node.est!.id), applyDoc) : undefined,
                            node.est
                          ),
                          ...(open ? leaves(node.children, depth + 1) : [])
                        ];
                      }
                      const linked = node.est;
                      return [(
                        <tr
                          key={node.key}
                          className={[linked ? "click" : "", linked && svodSel.includes(linked.id) ? "is-current" : "", linked && linked.id === svodCur ? "is-caret" : ""].filter(Boolean).join(" ")}
                          draggable={!!linked}
                          onClick={(ev) => linked && pickSvod(linked.id, ev)}
                          onDragStart={(ev) => linked && ev.dataTransfer.setData("text/plain", linked.id)}
                          onDragOver={(ev) => ev.preventDefault()}
                          onDrop={(ev) => linked && placeOutline(linked.id, ev)}
                          onDoubleClick={() => {
                            if (!linked) return;
                            setEstId(linked.id);
                            setTab("smets");
                          }}
                        >
                          <td style={{ paddingLeft: `${0.6 + depth * 1.1}rem` }}>
                            {linked ? (
                              <span className="svod-leaf">
                              {depth > 0 && <span className="svod-bullet" aria-hidden="true">•</span>}
                              <input
                                className="title"
                                data-svod-id={linked.id}
                                value={linked.title}
                                onFocus={(ev) => { ev.currentTarget.dataset.saved = ev.currentTarget.value; }}
                                onClick={(ev) => { ev.stopPropagation(); pickSvod(linked.id, ev); }}
                                onDoubleClick={(ev) => ev.stopPropagation()}
                                onChange={(ev) => {
                                  const title = ev.target.value;
                                  setDoc({
                                    ...doc,
                                    estimates: doc.estimates.map((e) => (e.id === linked.id ? { ...e, title } : e))
                                  });
                                }}
                                onBlur={(ev) => saveIfChanged(ev.currentTarget, (title) => run(() => api.patchEstimate(doc.id, linked.id, { title }), applyDoc))}
                              />
                              </span>
                            ) : node.title}
                          </td>
                          <td className="svod-ref">{linked ? `Смета №${node.code}` : node.code}</td>
                          <td className="money">{money(node.survey)}</td>
                          <td className="money">{money(node.design)}</td>
                          <td className="money">
                            <span className="svod-total">
                              <span className="svod-sum">{money(node.total)}</span>
                              {linked && (
                                <span className="svod-acts">
                                  <button
                                    type="button"
                                    className="btn recent-open"
                                    title="Открыть смету. Двойной щелчок по строке делает то же"
                                    onClick={(ev) => {
                                      ev.stopPropagation();
                                      setEstId(linked.id);
                                      setTab("smets");
                                    }}
                                    onDoubleClick={(ev) => ev.stopPropagation()}
                                  >
                                    Открыть
                                  </button>
                                  <button
                                    type="button"
                                    className="btn danger"
                                    title="Удалить смету из проекта"
                                    onClick={(ev) => {
                                      ev.stopPropagation();
                                      run(() => api.deleteEstimate(doc.id, linked.id), applyDoc);
                                    }}
                                    onDoubleClick={(ev) => ev.stopPropagation()}
                                  >
                                    Удалить
                                  </button>
                                </span>
                              )}
                            </span>
                          </td>
                        </tr>
                      ), ...leaves(node.children, depth + 1)];
                    });
                    const vat = doc.summary.totals.vat;
                    const grand = all.total + vat;
                    return (
                      <>
                        {sectionRow(objectKey, doc.project.objectName, objectOpen, all)}
                        {objectOpen && !doc.summary.hideStageHeading && sectionRow(stageKey, stage, stageOpen, all, () => run(() => api.hideStageHeading(doc.id), applyDoc))}
                        {objectOpen && (doc.summary.hideStageHeading || stageOpen) && leaves(tree, 0)}
                        <tr className="svod-foot">
                          <td colSpan={2}>Итого {stage}</td>
                          <td className="money">{money(all.survey)}</td>
                          <td className="money">{money(all.design)}</td>
                          <td className="money">{money(all.total)}</td>
                        </tr>
                        <tr className="svod-foot">
                          <td colSpan={2}>Итого по сводной смете</td>
                          <td className="money">{money(all.survey)}</td>
                          <td className="money">{money(all.design)}</td>
                          <td className="money">{money(all.total)}</td>
                        </tr>
                        <tr className="svod-foot">
                          <td colSpan={2}>Кроме того, НДС {Math.round(doc.project.vatRate * 100)}%</td>
                          <td className="money">{money(0)}</td>
                          <td className="money">{money(vat)}</td>
                          <td className="money">{money(vat)}</td>
                        </tr>
                        <tr className="svod-foot">
                          <td colSpan={2}>Всего по сводной смете</td>
                          <td className="money">{money(all.survey)}</td>
                          <td className="money">{money(all.design + vat)}</td>
                          <td className="money">{money(grand)}</td>
                        </tr>
                      </>
                    );
                  })()}
                </tbody>
              </table>
            </article>
          )}
        </section>
      )}

      {doc && tab === "issues" && (
        <section className="pane">
          {!doc?.issues.length && <p className="muted">Очередь уточнений пуста.</p>}
          <ul className="issues">
            {doc?.issues.map((i) => (
              <li key={i.id} className={i.severity}>
                <b>{i.code}</b> {i.message}
                {i.suggestedAction && <div className="muted">{i.suggestedAction}</div>}
              </li>
            ))}
          </ul>
        </section>
      )}

      {doc && tab === "smets" && (
        <div className="desk">
          {est && doc ? (
            <>
              <div className="desk-bar">
                <div>
                  <h2>
                    <span className="sec-badge">{(sectionFor(est.number, est.title)?.n ?? est.number) + " " + (sectionFor(est.number, est.title)?.short ?? "")}</span>
                    <span>Смета №{est.number} · {est.title}</span>
                  </h2>
                  <div className="meta">
                    {formLabel(est.form)} · группа {bookLabels(sectionFor(est.number, est.title))} · лист {money(est.excelAmount)} · стол {money(est.engineAmount)}
                  </div>
                </div>
                <div className="desk-cmds">
                  <button type="button" className={estView === "grid" ? "btn primary" : "btn"} onClick={() => setEstView("grid")}>Смета</button>
                  <button type="button" className={estView === "volumes" ? "btn primary" : "btn"} onClick={() => setEstView("volumes")}>Объёмы</button>
                  <button
                    type="button"
                    className="btn"
                    title="Перед выбранной строкой. Её позиции остаются."
                    onClick={() =>
                      editRows(
                        () => api.insertSection(doc.id, est.id, pos?.kind === "work" || pos?.kind === "section" ? pos.id : undefined),
                        (r) => applyDoc(r.document, est.id, r.position.id)
                      )
                    }
                  >
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><path d="M4 6h16M4 12h10M4 18h16" /></svg>
                    Раздел
                  </button>
                  <button
                    type="button"
                    className="btn"
                    title="Новая позиция в конце сметы."
                    onClick={() =>
                      editRows(() => api.createPosition(doc.id, est.id), (r) => {
                        applyDoc(r.document, est.id, r.position.id);
                      })
                    }
                  >
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true"><rect x="4" y="5" width="16" height="14" /><path d="M12 8v8M8 12h8" /></svg>
                    Пустая строка
                  </button>
                </div>
              </div>
              <div className="tape">{pos?.engineExpandedFormula || pos?.formula || pos?.excelFormula || "Формула выбранной строки появится здесь."}</div>
              <div className="desk-body">
                <div
                  className={gridDrop ? "grid-wrap is-drop" : "grid-wrap"}
                  onDragOver={(ev) => {
                    if (!isVolumeDrag(ev.dataTransfer.types) && ![...ev.dataTransfer.types].includes("text/plain")) return;
                    ev.preventDefault();
                    ev.dataTransfer.dropEffect = "copy";
                    if (!gridDrop) setGridDrop(true);
                  }}
                  onDragLeave={(ev) => {
                    if (ev.currentTarget.contains(ev.relatedTarget as Node)) return;
                    setGridDrop(false);
                  }}
                  onDrop={(ev) => {
                    const rate = readMarked<RateDrag>(ev.dataTransfer.getData("text/plain"), rateMark);
                    if (rate) {
                      ev.preventDefault();
                      setGridDrop(false);
                      importForeignRate(rate);
                      return;
                    }
                    const clip = clipFromTransfer(ev.dataTransfer);
                    setGridDrop(false);
                    if (!clip || !est) return;
                    ev.preventDefault();
                    putVolumeRef.current(est.id, clip, true);
                  }}
                >
                  {estView === "volumes" ? (
                    <VolumesPane
                      projectId={doc.id}
                      est={est}
                      survey={doc.survey}
                      onDoc={(d) => applyDoc(d, est.id)}
                      onError={(message) => setErr(message)}
                      onOpenBook={(place) => {
                        setBookPlace({ ...place, token: Date.now() });
                        setPreviewCollapsed(false);
                        setPreviewOpen(true);
                      }}
                    />
                  ) : (
                  <EstimateGrid
                    est={est}
                    posId={pos?.id}
                    onSelect={setPosId}
                    onMove={(from, before) => run(() => api.movePosition(doc.id, est.id, from, before), (d) => applyDoc(d, est.id, from))}
                    onRemoveSection={(sectionId) => editRows(() => api.removeSection(doc.id, est.id, sectionId), (d) => applyDoc(d, est.id))}
                    onRenameSection={(sectionId, name) => run(() => api.patchPosition(doc.id, sectionId, { name }), (d) => applyDoc(d, est.id, sectionId))}
                    onDropVolume={(clip) => putVolumeRef.current(est.id, clip, true)}
                    onDropRate={(rate) => importForeignRate(rate)}
                  />
                  )}
                </div>
                <aside className="insp-col">
                  <h3>Инспектор</h3>
                  {pos ? (
                    <Inspector
                      pos={pos}
                      est={est}
                      conditions={[
                        ...(doc.project.conditions ?? []).filter((c) => !(est.suppressedConditionIds ?? []).includes(c.id)),
                        ...(est.conditions ?? [])
                      ]}
                      onSave={(body) => run(() => api.patchPosition(doc.id, pos.id, body), (d) => applyDoc(d, est.id, pos.id))}
                      onSaveEst={(body) => run(() => api.patchEstimate(doc.id, est.id, body), (d) => applyDoc(d, est.id, pos.id))}
                      onFind={() => {
                        const text = [pos.name, pos.basis?.raw, pos.code, pos.catalogCode, pos.formula].filter(Boolean).join("\n");
                        run(() => api.searchLine(text), (found) => {
                          const hit = found.hits[0];
                          if (!hit || found.status === "ambiguous") {
                            setErr(found.status === "catalog-miss" ? "Расценка не найдена" : "Несколько кандидатов — выберите в строке ввода");
                            return;
                          }
                          run(() => api.applyRate(doc.id, pos.id, hit), (d) => applyDoc(d, est.id, pos.id));
                        });
                      }}
                    />
                  ) : (
                    <p className="muted">Выберите строку.</p>
                  )}
                </aside>
              </div>
              <Composer
                estimate={est}
                onInsert={(hit) =>
                  run(() => api.createPosition(doc.id, est.id), (r) => {
                    applyDoc(r.document, est.id, r.position.id);
                    run(() => api.applyRate(doc.id, r.position.id, hit), (d) => applyDoc(d, est.id, r.position.id));
                  })
                }
              />
            </>
          ) : (
            <div className="empty">
              <p>Нет локальной сметы.</p>
              <button type="button" className="primary" onClick={() => setShowNewLocal(true)}>
                Новая локальная смета
              </button>
            </div>
          )}
        </div>
      )}

      </main>

      <footer className="statusbar">
        <span className="status-side">{doc ? `${doc.project.designer || "—"}  ·  ${doc.project.customer || "—"}` : "СНТП · ПИР"}</span>
        {doc && (
          <div className="chips">
            <span className="chip">Стадия <b>{doc.project.stage === "projectDoc" ? "П" : doc.project.stage === "workProject" ? "РП" : "РД"}</b></span>
            <span className="chip">Ктек <b>{doc.project.currentIndex ?? "—"}</b></span>
            <span className="chip">НДС <b>{Math.round(doc.project.vatRate * 100)} %</b></span>
            {doc.issues.length > 0 && (
              <button type="button" className="chip chip-warn" onClick={() => setTab("issues")}>
                {doc.issues.length} уточн.
              </button>
            )}
          </div>
        )}
        <ZoomControl zoom={zoom} onZoom={setZoom} />
        <span className="status-side status-money">
          {doc?.summary ? (
            <>
              без НДС <b>{money(doc.summary.totals.design)}</b> · с НДС <b>{money(doc.summary.totals.grand)}</b>
            </>
          ) : (
            doc ? "свод ещё не собран" : ""
          )}
        </span>
      </footer>

      {previewOpen && (
        <PreviewDesk
          focus={previewFocus}
          bookPlace={bookPlace}
          hostProjectId={doc?.id}
          hostEstimateId={estId}
          onAccepted={(d) => applyDoc(d, estId ?? undefined)}
          collapsed={previewCollapsed}
          onCollapsedChange={setPreviewCollapsed}
          onClose={() => setPreviewOpen(false)}
          onDetach={detachPreview}
          onWidth={(width) => { previewWidthRef.current = width; }}
        />
      )}

      {showNewObject && (
        <NewObjectDialog
          onClose={() => setShowNewObject(false)}
          onCreate={(body) =>
            run(() => api.createObject(body), (d) => {
              applyDoc(d);
              setShowNewObject(false);
              setTab("smets");
            })
          }
        />
      )}
      {showNewLocal && doc && (
        <NewLocalDialog
          onClose={() => setShowNewLocal(false)}
          onCreate={(body) =>
            run(() => api.createLocal(doc.id, body), (r) => {
              applyDoc(r.document, r.estimate.id);
              setShowNewLocal(false);
              setTab("smets");
            })
          }
        />
      )}
      {paste && doc && est && (
        <div className="modal" onClick={() => setPaste(null)}>
          <form
            className="dialog"
            onClick={(e) => e.stopPropagation()}
            onSubmit={(e) => {
              e.preventDefault();
              const text = paste.text ?? "";
              const html = paste.html;
              const insert = (force: boolean) => run(
                () => api.pasteApply(doc.id, est.id, text, html, force),
                (d) => {
                  applyDoc(d, est.id);
                  setPaste(null);
                  setPasteTune(null);
                  setPasteForce(false);
                }
              );
              if (pasteForce) {
                insert(true);
                return;
              }
              api.recognizePaste(doc.id, text, html).then((seen) => {
                if (seen.needsTune) {
                  setPasteTune(seen.scores.filter((s) => s.looksLikeBook && s.confidence < 0.6).map((s) => s.note).join("\n") || "Нужно правило сопоставления");
                  setPasteForce(true);
                  return;
                }
                insert(false);
              }).catch((err) => setErr(err instanceof Error ? err.message : String(err)));
            }}
          >
            <h2>Вставка из буфера</h2>
            <p className="muted">Рубли из Excel не становятся A/B, пока в строке нет обоснования.</p>
            <pre className="paste-table">{(paste.text ?? "").slice(0, 1200)}</pre>
            {pasteTune && (
              <div>
                <p className="err">{pasteTune}</p>
                <label>Как в файле <input value={mapFrom} onChange={(e) => setMapFrom(e.target.value)} /></label>
                <label>В системе <input value={mapTo} onChange={(e) => setMapTo(e.target.value)} /></label>
                <label>Роль <input value={mapRole} onChange={(e) => setMapRole(e.target.value)} /></label>
                <button
                  type="button"
                  onClick={() => {
                    api.analogMaps(doc.id).then((cur) => api.saveAnalogMaps(doc.id, [...(cur.rules ?? []), { from: mapFrom, to: mapTo, role: mapRole }])).then(() => {
                      setPasteForce(false);
                      setPasteTune(null);
                    }).catch((err) => setErr(err instanceof Error ? err.message : String(err)));
                  }}
                >
                  Запомнить правило
                </button>
              </div>
            )}
            <button type="button" onClick={() => { setPaste(null); setPasteTune(null); setPasteForce(false); }}>Отмена</button>
            <button type="submit" className="primary">{pasteForce ? "Вставить как есть" : "Вставить"}</button>
          </form>
        </div>
      )}
      {palette && (
        <div className="overlay" onClick={() => setPalette(false)}>
          <div className="palette" role="dialog" aria-label="Палитра команд" onClick={(e) => e.stopPropagation()}>
            <input
              autoFocus
              placeholder="Смета, расценка, команда…"
              value={paletteQ}
              onChange={(e) => setPaletteQ(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Escape") setPalette(false); }}
            />
            <div className="palette-list">
              {([
                ["Новый объект", () => setShowNewObject(true)],
                ["Новая смета по разделу 1ПС", () => doc && setShowNewLocal(true)],
                ["Пересчитать", () => doc && run(() => api.recalc(doc.id), applyDoc)],
                ["Собрать свод 1ПС", () => doc && run(() => api.createSummary(doc.id), applyDoc)],
                ["Сверка со сборниками", () => doc && run(() => api.bookCheck(doc.id), applyDoc)],
                ["Объёмы работ", () => { setTab("smets"); setEstView("volumes"); }],
                ["Копия json сметы", () => doc && est && doc.project.workFolder && run(() => api.copyEstimate(doc.id, est.id).then((r) => r.document), applyDoc)],
                ["Перечитать проект из папки", () => doc?.project.workFolder && run(() => api.reloadProject(doc.id), (d) => applyDoc(d))],
                ["Открыть папку в проводнике", () => doc?.project.workFolder && revealFolder(doc.id)],
                ["Применить состав работ", () => doc && run(() => api.applyScope(doc.id, est?.id), applyDoc)],
                ["Свод", () => setTab("svod")],
                ["Каталог", () => setTab("catalog")],
                ["Импорт", () => setTab("import")],
                ["Выгрузка", () => setTab("export")],
                ["Тема: Светлая", () => setTheme("light")],
                ["Тема: Тёмная", () => setTheme("dark")],
                ["Стиль: Инженерный стол (Крафт)", () => setThemeStyle("kraft")],
                ["Стиль: Чертёжный кабинет", () => setThemeStyle("shadcn")],
                ["Стиль: Проектный офис", () => setThemeStyle("sharepoint")]
              ] as [string, () => unknown][])
                .filter(([label]) => label.toLowerCase().includes(paletteQ.trim().toLowerCase()))
                .map(([label, fn]) => (
                  <button
                    key={label}
                    type="button"
                    onClick={() => {
                      fn();
                      setPalette(false);
                      setPaletteQ("");
                    }}
                  >
                    {label}
                  </button>
                ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function formLabel(f: string) {
  const x = f.toLowerCase();
  if (x.includes("707")) return "2П 707";
  if (x.includes("asutp")) return "АСУТП";
  if (x.includes("3p") || x.includes("labor")) return "3П";
  if (x.includes("sbc")) return "2П СБЦ";
  return f;
}

function estimateFormMark(f: string) {
  const x = f.toLowerCase();
  if (x.includes("3p") || x.includes("labor")) return "3П";
  if (x.includes("2p") || x.includes("707") || x.includes("sbc")) return "2П";
  if (x.includes("asutp")) return "АСУ ТП";
  return "";
}

function shareOf(amount: number | null | undefined, total: number | null | undefined) {
  if (amount == null || total == null || total === 0) return "—";
  return `${(amount / total * 100).toLocaleString("ru-RU", { minimumFractionDigits: 1, maximumFractionDigits: 1 })} %`;
}

function isLaborForm(f?: string) {
  const x = (f ?? "").toLowerCase();
  return x.includes("3p") || x.includes("labor");
}

function isAsutpForm(f?: string) {
  return (f ?? "").toLowerCase().includes("asutp");
}

function svodBase(doc: PirDocument) {
  const design = doc.summary?.totals.design;
  if (design && design > 0) return design;
  return doc.estimates.reduce((s, e) => s + (e.engineAmount ?? e.excelAmount ?? 0), 0);
}

function shareOfSvod(e: Estimate, doc: PirDocument) {
  const base = svodBase(doc);
  const amt = e.engineAmount ?? e.excelAmount ?? 0;
  if (!base) return "—";
  return (
    (amt / base * 100).toLocaleString("ru-RU", {
      minimumFractionDigits: 1,
      maximumFractionDigits: 1
    }) + "%"
  );
}

function EstimateGrid({
  est,
  posId,
  onSelect,
  onMove,
  onRemoveSection,
  onRenameSection,
  onDropVolume,
  onDropRate
}: {
  est: Estimate;
  posId?: string;
  onSelect: (id: string) => void;
  onMove: (fromId: string, beforeId: string | null) => void;
  onRemoveSection: (sectionId: string) => void;
  onRenameSection: (sectionId: string, name: string) => void;
  onDropVolume: (clip: VolumeClip) => void;
  onDropRate: (rate: RateDrag) => void;
}) {
  const labor = isLaborForm(est.form);
  const asutp = isAsutpForm(est.form);
  const [folded, setFolded] = useState<Record<string, boolean>>({});
  const sectionSums = new Map(est.positions.filter((p) => p.kind === "sectionTotal").map((p) => [p.section ?? "", p.engineAmount]));
  const lastBodyId = [...est.positions].reverse().find((p) => p.kind === "work" || p.kind === "section")?.id;
  const hidden = new Set<string>();
  let hideSection: string | null = null;
  for (const p of est.positions) {
    if (p.kind === "section") {
      hideSection = folded[p.id] ? p.id : null;
      continue;
    }
    if (hideSection && (p.kind === "work" || p.kind === "text" || p.kind === "sectionTotal"))
      hidden.add(p.id);
  }

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.altKey || e.ctrlKey || e.metaKey) return;
      if (e.key !== "ArrowUp" && e.key !== "ArrowDown" && e.key !== "PageUp" && e.key !== "PageDown") return;
      const target = e.target as HTMLElement | null;
      if (target?.closest("input, textarea, select")) return;
      if (document.querySelector(".palette, .modal")) return;
      const rows = est.positions.filter((p) => !hidden.has(p.id) && (p.kind === "work" || p.kind === "section" || p.kind === "text"));
      if (rows.length === 0) return;
      const current = Math.max(0, rows.findIndex((p) => p.id === posId));
      const wrap = document.querySelector(".grid-wrap");
      const page = wrap ? Math.max(1, Math.floor(wrap.clientHeight / 36) - 1) : 8;
      const step = e.key === "PageUp" || e.key === "PageDown" ? page : 1;
      const dir = e.key === "ArrowUp" || e.key === "PageUp" ? -1 : 1;
      const next = rows[Math.min(rows.length - 1, Math.max(0, current + dir * step))];
      if (!next || next.id === posId) return;
      e.preventDefault();
      onSelect(next.id);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [est, posId, folded, onSelect]);

  useEffect(() => {
    const wrap = document.querySelector(".grid-wrap");
    if (!(wrap instanceof HTMLElement)) return;
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
      const rows = est.positions.filter((p) => !hidden.has(p.id) && (p.kind === "work" || p.kind === "section" || p.kind === "text"));
      if (rows.length === 0) return;
      const current = Math.max(0, rows.findIndex((p) => p.id === posId));
      const next = rows[Math.min(rows.length - 1, Math.max(0, current + steps))];
      if (!next || next.id === posId) return;
      onSelect(next.id);
    };
    wrap.addEventListener("wheel", onWheel, { passive: false });
    return () => wrap.removeEventListener("wheel", onWheel);
  }, [est, posId, folded, onSelect]);

  useEffect(() => {
    const row = document.querySelector(`.grid tr[data-pos="${posId}"]`);
    const wrap = document.querySelector(".grid-wrap");
    if (!(row instanceof HTMLElement) || !(wrap instanceof HTMLElement)) return;
    const rowBox = row.getBoundingClientRect();
    const wrapBox = wrap.getBoundingClientRect();
    if (rowBox.top < wrapBox.top) wrap.scrollTop -= wrapBox.top - rowBox.top;
    else if (rowBox.bottom > wrapBox.bottom) wrap.scrollTop += rowBox.bottom - wrapBox.bottom;
  }, [posId]);

  return (
    <table className="grid">
      <thead>
        <tr>
          <th>№</th>
          {labor ? (
            <>
              <th>Должность</th>
              <th>Чел.</th>
              <th>Чел-дн</th>
              <th>Ставка</th>
            </>
          ) : asutp ? (
            <>
              <th>Часть</th>
              <th>Баллы</th>
              <th>Множитель</th>
              <th>Коэф.</th>
            </>
          ) : (
            <>
              <th>Наименование</th>
              <th>Обоснование</th>
              <th>Формула</th>
              <th>Коэф.</th>
            </>
          )}
          <th>Excel</th>
          <th>Движок</th>
          <th>Δ</th>
          <th>Доля</th>
        </tr>
      </thead>
      <tbody>
        {est.positions.map((p) => {
          if (hidden.has(p.id)) return null;
          const share = positionShare(est.positions, p);
          const title = p.kind === "section"
            ? <SectionTitle name={p.name} onCommit={(name) => onRenameSection(p.id, name)} />
            : p.name;
          const foldedSection = p.kind === "section" && folded[p.id];
          const rowKind = p.kind === "section" ? "sec" : p.kind === "sectionTotal" ? "subtotal" : p.kind === "total" ? "grand" : p.kind !== "work" ? "dim" : "";
          return (
          <tr
            key={p.id}
            data-pos={p.id}
            className={`${p.id === posId ? "sel" : ""} ${rowKind}`.trim()}
            onClick={() => onSelect(p.id)}
            onDragOver={(ev) => {
              if (isVolumeDrag(ev.dataTransfer.types)) {
                ev.preventDefault();
                ev.dataTransfer.dropEffect = "copy";
                return;
              }
              ev.preventDefault();
            }}
            onDrop={(ev) => {
              const rate = readMarked<RateDrag>(ev.dataTransfer.getData("text/plain"), rateMark);
              if (rate) {
                ev.preventDefault();
                ev.stopPropagation();
                onDropRate(rate);
                return;
              }
              const clip = clipFromTransfer(ev.dataTransfer);
              if (clip) {
                ev.preventDefault();
                ev.stopPropagation();
                onDropVolume(clip);
                return;
              }
              ev.preventDefault();
              const from = ev.dataTransfer.getData("text/plain");
              if (!from || from === p.id) return;
              const movable = p.kind === "work" || p.kind === "section";
              const rect = ev.currentTarget.getBoundingClientRect();
              const pastLast = p.id === lastBodyId && ev.clientY > rect.top + rect.height / 2;
              onMove(from, movable && !pastLast ? p.id : null);
            }}
          >
            <td
              className={p.kind === "work" || p.kind === "section" ? "row-handle" : undefined}
              draggable={p.kind === "work" || p.kind === "section"}
              title={p.kind === "work" || p.kind === "section" ? "Перетащить строку. ↑↓ и Page Up/Down — выбор, Insert — новая, Delete — удалить" : undefined}
              onDragStart={(ev) => {
                ev.stopPropagation();
                ev.dataTransfer.setData("text/plain", p.id);
              }}
            >
              {p.kind === "section" && (
                <button
                  type="button"
                  className="sec-fold"
                  aria-expanded={!foldedSection}
                  aria-label={foldedSection ? "Развернуть раздел" : "Свернуть раздел"}
                  title={foldedSection ? "Развернуть раздел" : "Свернуть раздел"}
                  onClick={(ev) => {
                    ev.stopPropagation();
                    setFolded((cur) => ({ ...cur, [p.id]: !cur[p.id] }));
                  }}
                >
                  {foldedSection ? "▸" : "▾"}
                </button>
              )}
              {p.number}
            </td>
            {labor ? (
              <>
                <td>{title}</td>
                <td className="num">{p.qty}</td>
                <td className="num">{p.quantity ?? "—"}</td>
                <td className="num">{money(p.priceA)}</td>
              </>
            ) : asutp ? (
              <>
                <td>{title}</td>
                <td className="num">{p.quantity ?? "—"}</td>
                <td className="num">{p.priceA ?? "—"}</td>
                <td className="small">{factorChips(p.factors)}</td>
              </>
            ) : (
              <>
                <td>{title}</td>
                <td className="small">{p.code ?? p.basis?.raw?.slice(0, 80)}</td>
                <td className="small">{p.engineExpandedFormula ?? p.formula}</td>
                <td className="small">{factorChips(p.factors)}</td>
              </>
            )}
            <td className="num">{money(p.excelAmount)}</td>
            <td className="num">{money(foldedSection ? sectionSums.get(p.id) : p.engineAmount)}</td>
            <td className={Math.abs(p.delta ?? 0) > 1 ? "delta" : "num"}>{money(p.delta)}</td>
            <td className="share">
              {share ? (
                <>
                  <div title={p.kind === "section" ? "Доля раздела в смете" : undefined}>{share.ofEstimate}</div>
                  {share.ofSection && <div className="muted">раздел {share.ofSection}</div>}
                  <div className="bar" aria-hidden="true">
                    <i style={{ width: share.width }} />
                  </div>
                </>
              ) : (
                p.kind === "section" ? null : "—"
              )}
              {p.kind === "section" && (
                <button
                  type="button"
                  className="btn sec-remove"
                  aria-label="Удалить заголовок раздела"
                  title="Удалить заголовок, строки останутся"
                  onMouseDown={(ev) => ev.stopPropagation()}
                  onClick={(ev) => {
                    ev.stopPropagation();
                    onRemoveSection(p.id);
                  }}
                >
                  Удалить
                </button>
              )}
            </td>
          </tr>
          );
        })}
      </tbody>
    </table>
  );
}

function countWord(n: number, one: string, few: string, many: string) {
  const m10 = n % 10;
  const m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return `${n} ${one}`;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return `${n} ${few}`;
  return `${n} ${many}`;
}

function recentWhen(iso?: string) {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleString("ru-RU", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

function EstimateBlock({
  project,
  foreign,
  onOpen,
  onPreview
}: {
  project: ProjectListItem;
  foreign: boolean;
  onOpen: (id: string, estId?: string) => void;
  onPreview: (estimateId: string) => void;
}) {
  const estimates = project.estimates ?? [];
  if (!estimates.length) return null;
  return (
    <tr className="est-block">
      <td colSpan={7}>
        <div className="est-scroll">
          <div className="est-head">
            <span>№</span>
            <span>Вид</span>
            <span>Наименование</span>
            <span>Автор</span>
            <span>Стоимость</span>
            <span>Доля</span>
            <span />
          </div>
          <ul className="est-tree">
            {estimates.map((e) => {
              const id = e.id || e.number;
              return (
              <li key={id}>
                <button
                  type="button"
                  className="est-tree-item"
                  draggable
                  title={foreign ? "Открыть в аналогах, текущий проект не меняется" : "Открыть смету в текущем проекте"}
                  onClick={() => (foreign ? onPreview(id) : onOpen(project.id, id))}
                  onDragStart={(ev) => {
                    ev.stopPropagation();
                    putDrag(ev.dataTransfer, estimateDragText({
                      sourceProjectId: project.id,
                      sourceEstimateId: id,
                      title: e.title,
                      projectName: project.name
                    }));
                  }}
                >
                  <span className="num" style={{ paddingLeft: `${e.depth * 0.7}rem` }}>{e.number}</span>
                  <span className="form-mark">{estimateFormMark(e.form ?? "")}</span>
                  <span className="ttl">{e.title}</span>
                  <span className="author">{e.author || "—"}</span>
                  <span className="sum">{money(e.amount ?? undefined)}</span>
                  <span className="pct">{shareOf(e.amount, project.total)}</span>
                </button>
                <button
                  type="button"
                  className="est-preview"
                  title="Открыть расценки в окне просмотра"
                  onClick={(ev) => {
                    ev.stopPropagation();
                    onPreview(id);
                  }}
                >
                  Аналоги
                </button>
              </li>
              );
            })}
          </ul>
        </div>
      </td>
    </tr>
  );
}

function CaretIcon({ open }: { open: boolean }) {
  return (
    <svg className={open ? "caret is-open" : "caret"} width="20" height="20" viewBox="0 0 20 20" aria-hidden="true">
      <path d="M7 4.5 13.5 10 7 15.5V4.5Z" fill="currentColor" />
    </svg>
  );
}

function RecentProjects({
  list,
  currentId,
  onOpen,
  onPreview,
  onCreate,
  onRefresh,
  onReveal,
  onSources
}: {
  list: ProjectListItem[];
  currentId?: string;
  onOpen: (id: string, estId?: string, tab?: Tab) => void;
  onPreview: (projectId: string, estimateId: string) => void;
  onCreate: () => void;
  onRefresh: () => void;
  onReveal: (id: string) => void;
  onSources: (id: string) => void;
}) {
  const currentRef = useRef<HTMLTableRowElement>(null);
  const [openIds, setOpenIds] = useState<Record<string, boolean>>({});
  useEffect(() => {
    currentRef.current?.scrollIntoView({ block: "nearest" });
  }, [currentId]);
  return (
    <section className="recent">
      <div className="recent-head">
        <div>
          <h2>Проекты</h2>
          <p className="muted">Название показывает состав. Проект открывается кнопкой «Открыть».</p>
        </div>
        <div className="recent-cmds">
          <button type="button" className="btn" onClick={onRefresh} title="Перечитать каталог проектов с диска">
            <ReloadIcon />
            Обновить
          </button>
          <button type="button" className="btn primary" onClick={onCreate}>Новый объект</button>
        </div>
      </div>
      {list.length === 0 ? (
        <p>В каталоге пока нет папок.</p>
      ) : (
        <div className="recent-wrap">
          <table className="ledger recent-table">
            <thead>
              <tr>
                <th>Объект</th>
                <th>Стадия</th>
                <th className="num">Смет</th>
                <th className="num">Общая стоимость</th>
                <th>Изменён</th>
                <th>Создал</th>
                <th aria-label="Действия" />
              </tr>
            </thead>
            <tbody>
              {list.map((p) => {
                const current = p.id === currentId;
                const open = !!openIds[p.id];
                return (
                <Fragment key={p.id}>
                <tr
                  ref={current ? currentRef : undefined}
                  className={current ? "is-current" : undefined}
                  aria-current={current ? "true" : undefined}
                >
                  <td className="name">
                    <button
                      type="button"
                      className="current-toggle"
                      aria-expanded={open}
                      title={open ? "Свернуть состав" : "Показать состав, не открывая проект"}
                      onClick={(ev) => {
                        ev.stopPropagation();
                        setOpenIds((cur) => ({ ...cur, [p.id]: !cur[p.id] }));
                      }}
                    >
                      <CaretIcon open={open} />
                      <span>{p.name}</span>
                      {current && <span className="current-mark">текущий</span>}
                    </button>
                  </td>
                  <td>{p.stage || "П"}</td>
                  <td className="num">{p.estimateCount ?? 0}</td>
                  <td className="num" title="Сумма смет, без НДС">{money(p.total ?? undefined)}</td>
                  <td className="when">{recentWhen(p.updatedAt)}</td>
                  <td>{p.designer || "—"}</td>
                  <td className="acts">
                    <button
                      type="button"
                      className="btn recent-open"
                      title="Открыть папку проекта в проводнике"
                      onClick={() => onReveal(p.id)}
                    >
                      Открыть каталог
                    </button>
                    <button
                      type="button"
                      className="btn recent-open"
                      title="Исходные данные для составления смет: задание, технические требования, программа изысканий, схемы, файлы смежников"
                      onClick={() => onSources(p.id)}
                    >
                      ИД
                    </button>
                    <button
                      type="button"
                      className="btn recent-open"
                      title="Открыть свод"
                      onClick={() => onOpen(p.id, undefined, "svod")}
                    >
                      Открыть
                    </button>
                  </td>
                </tr>
                {open && (p.estimates?.length ? (
                  <EstimateBlock
                    project={p}
                    foreign={p.id !== currentId}
                    onOpen={onOpen}
                    onPreview={(estimateId) => onPreview(p.id, estimateId)}
                  />
                ) : (
                  <tr className="est-block"><td colSpan={7}><p className="muted">Смет нет.</p></td></tr>
                ))}
                </Fragment>
                );
              })}
            </tbody>
          </table>
          <p className="muted">
            {countWord(list.length, "папка", "папки", "папок")} ·{" "}
            {countWord(list.reduce((s, p) => s + (p.estimateCount ?? 0), 0), "смета", "сметы", "смет")}
          </p>
        </div>
      )}
    </section>
  );
}

function SectionTitle({ name, onCommit }: { name: string; onCommit: (name: string) => void }) {
  const [value, setValue] = useState(name);
  useEffect(() => setValue(name), [name]);
  return (
    <input
      className="title"
      aria-label="Название раздела"
      value={value}
      onMouseDown={(ev) => ev.stopPropagation()}
      onClick={(ev) => ev.stopPropagation()}
      onChange={(ev) => setValue(ev.target.value)}
      onBlur={() => {
        const next = value.trim() || "Раздел";
        setValue(next);
        if (next !== name) onCommit(next);
      }}
      onKeyDown={(ev) => {
        if (ev.key === "Enter") ev.currentTarget.blur();
      }}
    />
  );
}

function positionShare(positions: Position[], pos: Position) {
  let current = "";
  let ownSection = "";
  const sectionSum = new Map<string, number>();
  let total = 0;
  for (const p of positions) {
    if (p.kind === "section") current = p.id;
    if (p.id === pos.id) ownSection = current;
    if (p.kind !== "work") continue;
    const value = p.engineAmount ?? 0;
    total += value;
    sectionSum.set(current, (sectionSum.get(current) ?? 0) + value);
  }
  if (total <= 0) return null;
  const pct = (n: number) =>
    (n * 100).toLocaleString("ru-RU", { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + " %";
  const bar = (ratio: number) => `${Math.max(0, Math.min(100, ratio * 100)).toFixed(1)}%`;
  if (pos.kind === "section") {
    const ratio = (sectionSum.get(pos.id) ?? 0) / total;
    return { ofEstimate: pct(ratio), ofSection: null, width: bar(ratio) };
  }
  if (pos.kind !== "work") return null;
  const amount = pos.engineAmount ?? 0;
  const ofEstimate = amount / total;
  const sectionTotal = sectionSum.get(ownSection) ?? 0;
  const ofSection = ownSection && sectionTotal > 0 && Math.abs(sectionTotal - total) > 0.01 ? amount / sectionTotal : null;
  return {
    ofEstimate: pct(ofEstimate),
    ofSection: ofSection == null ? null : pct(ofSection),
    width: bar(ofEstimate)
  };
}

function factorChips(factors: { identifier: string; workDoc?: number; projectDoc?: number; source?: string }[]) {
  if (!factors?.length) return "—";
  return factors
    .map((f) => `${f.identifier}=${f.workDoc ?? f.projectDoc ?? "—"}`)
    .join(" · ");
}

function NewObjectDialog({
  onClose,
  onCreate
}: {
  onClose: () => void;
  onCreate: (body: Record<string, unknown>) => void;
}) {
  const [objectName, setObjectName] = useState("Новый объект");
  const [stage, setStage] = useState("workDoc");
  const [designer, setDesigner] = useState("");
  const [customer, setCustomer] = useState("");
  const [vatRate, setVatRate] = useState(0.22);
  const [currentIndex, setCurrentIndex] = useState("");
  return (
    <div className="modal" onClick={onClose}>
      <form
        className="dialog"
        onClick={(e) => e.stopPropagation()}
        onSubmit={(e) => {
          e.preventDefault();
          onCreate({
            objectName,
            stage,
            designer,
            customer,
            vatRate,
            currentIndex: currentIndex ? Number(currentIndex) : null,
            roundMode: "none",
            kponPolicy: "autoKpon"
          });
        }}
      >
        <h2>Новый объект</h2>
        <label>
          Наименование
          <input value={objectName} onChange={(e) => setObjectName(e.target.value)} required />
        </label>
        <label>
          Стадия
          <select value={stage} onChange={(e) => setStage(e.target.value)}>
            <option value="projectDoc">П — проектная документация</option>
            <option value="workDoc">РД — рабочая документация</option>
            <option value="workProject">РП — рабочий проект</option>
          </select>
        </label>
        <label>
          Проектировщик
          <input value={designer} onChange={(e) => setDesigner(e.target.value)} />
        </label>
        <label>
          Заказчик
          <input value={customer} onChange={(e) => setCustomer(e.target.value)} />
        </label>
        <label>
          НДС
          <input type="number" step="0.01" value={vatRate} onChange={(e) => setVatRate(Number(e.target.value))} />
        </label>
        <label>
          Ктек (необяз.)
          <input value={currentIndex} onChange={(e) => setCurrentIndex(e.target.value)} />
        </label>
        <div className="row">
          <button type="submit" className="primary">
            Создать объект
          </button>
          <button type="button" onClick={onClose}>
            Отмена
          </button>
        </div>
      </form>
    </div>
  );
}

function NewLocalDialog({
  onClose,
  onCreate
}: {
  onClose: () => void;
  onCreate: (body: Record<string, unknown>) => void;
}) {
  const [sectionId, setSectionId] = useState("1.1");
  const section = SECTIONS.find((s) => s.id === sectionId) ?? SECTIONS[0];
  const [title, setTitle] = useState(section.name);
  const [offers, setOffers] = useState<OfferGroup[]>([]);
  const [showHidden, setShowHidden] = useState(false);
  useEffect(() => {
    api.offers().then(setOffers).catch(() => setOffers([]));
  }, []);
  return (
    <div className="modal" onClick={onClose}>
      <form
        className="dialog wide"
        onClick={(e) => e.stopPropagation()}
        onSubmit={(e) => {
          e.preventDefault();
          onCreate({
            title,
            number: section.id,
            form: section.form,
            chapterCaption: section.name
          });
        }}
      >
        <div className="form-kicker">Новая локальная смета</div>
        <h2>К какому разделу 1ПС она относится</h2>
        <p className="muted">Раздел задаёт группу справочников. Строка ввода ищет расценки сначала только в этих сборниках.</p>
        <div className="sec-list" role="listbox">
          {SECTIONS.map((sec) => (
            <button
              key={sec.id}
              type="button"
              className={sec.id === section.id ? "is-on" : ""}
              onClick={() => {
                setSectionId(sec.id);
                setTitle(sec.name);
              }}
            >
              <span className="n">{sec.n}</span>
              <span>{sec.name}</span>
              <small>{bookLabels(sec)}</small>
            </button>
          ))}
        </div>
        <label>
          Название
          <input value={title} onChange={(e) => setTitle(e.target.value)} required />
        </label>
        {offers.some((g) => g.offers.some((o) => showHidden || !o.hidden)) && (
          <div className="offer-list">
            <div className="form-kicker">Не учтено в цене сборника</div>
            <p className="muted">Это отдельные локальные сметы, а не коэффициенты. Название подставится в поле выше. «Не предлагать» скрывает пункт для всех объектов.</p>
            <label className="offer-hide-toggle">
              <input type="checkbox" checked={showHidden} onChange={(e) => setShowHidden(e.target.checked)} />
              Показать скрытые
            </label>
            {offers.map((group) => {
              const rows = group.offers.filter((o) => showHidden || !o.hidden);
              if (rows.length === 0) return null;
              return (
                <div key={group.bookId}>
                  <b>{group.bookTitle}</b>
                  <ul>
                    {rows.map((offer) => (
                      <li key={offer.id} className={offer.hidden ? "is-hidden" : ""}>
                        <button type="button" onClick={() => setTitle(offer.title)}>{offer.title}</button>
                        {offer.text && offer.text !== offer.title && <span className="muted">{offer.text}</span>}
                        <button
                          type="button"
                          className="btn"
                          onClick={() => api.hideOffer(offer.id, !offer.hidden).then(() => api.offers().then(setOffers))}
                        >
                          {offer.hidden ? "Вернуть" : "Не предлагать"}
                        </button>
                      </li>
                    ))}
                  </ul>
                </div>
              );
            })}
          </div>
        )}
        <div className="row">
          <button type="submit" className="primary">Создать смету</button>
          <button type="button" onClick={onClose}>Отмена</button>
        </div>
      </form>
    </div>
  );
}

function Inspector({
  pos,
  est,
  conditions,
  onSave,
  onSaveEst,
  onFind
}: {
  pos: Position;
  est: Estimate;
  conditions?: { id: string; alias: string }[];
  onSave: (body: Record<string, unknown>) => void | Promise<void>;
  onSaveEst: (body: Record<string, unknown>) => void | Promise<void>;
  onFind?: () => void;
}) {
  const labor = isLaborForm(est.form);
  const asutp = isAsutpForm(est.form);
  const [name, setName] = useState(pos.name);
  const [code, setCode] = useState(pos.code ?? pos.catalogCode ?? "");
  const [a, setA] = useState(String(pos.priceA ?? ""));
  const [b, setB] = useState(String(pos.priceB ?? ""));
  const [x, setX] = useState(String(pos.quantity ?? ""));
  const [xmin, setXmin] = useState(String(pos.xmin ?? ""));
  const [xmax, setXmax] = useState(String(pos.xmax ?? ""));
  const [heads, setHeads] = useState(String(pos.qty ?? ""));
  const [catalogs, setCatalogs] = useState<LaborCatalog[]>([]);
  const [groupId, setGroupId] = useState(est.labor?.groupCatalogId ?? "sntp-groups");
  const [wageId, setWageId] = useState(est.labor?.wageCatalogId ?? "sntp-20-26");
  const [hoursId, setHoursId] = useState(est.labor?.hoursCatalogId ?? "sibur-hours");
  const [workId, setWorkId] = useState(est.labor?.workCatalogId ?? "sibur-works");
  const [share, setShare] = useState(String(est.labor?.salaryShare ?? 0.45));
  const [profitPct, setProfitPct] = useState(String(est.labor?.profitPercent ?? 8));
  const [regK, setRegK] = useState(String(est.labor?.regionalK ?? 1));
  const [corrK, setCorrK] = useState(String(est.asutp?.correctionK ?? 0.7));
  const [q, setQ] = useState("");
  const [hits, setHits] = useState<unknown[]>([]);
  useEffect(() => {
    if (!labor) return;
    api.laborCatalogs().then(setCatalogs).catch(() => setCatalogs([]));
  }, [labor]);
  useEffect(() => {
    setName(pos.name);
    setCode(pos.code ?? pos.catalogCode ?? "");
    setA(String(pos.priceA ?? ""));
    setB(String(pos.priceB ?? ""));
    setX(String(pos.quantity ?? ""));
    setXmin(String(pos.xmin ?? ""));
    setXmax(String(pos.xmax ?? ""));
    setHeads(String(pos.qty ?? ""));
    setGroupId(est.labor?.groupCatalogId ?? "sntp-groups");
    setWageId(est.labor?.wageCatalogId ?? "sntp-20-26");
    setHoursId(est.labor?.hoursCatalogId ?? "sibur-hours");
    setWorkId(est.labor?.workCatalogId ?? "sibur-works");
    setShare(String(est.labor?.salaryShare ?? 0.45));
    setProfitPct(String(est.labor?.profitPercent ?? 8));
    setRegK(String(est.labor?.regionalK ?? 1));
    setCorrK(String(est.asutp?.correctionK ?? 0.7));
  }, [pos.id, est.id, est.labor, est.asutp]);

  const groupBooks = catalogs.filter((c) => c.kind === "group");
  const wageBooks = catalogs.filter((c) => !c.kind || c.kind === "wage");
  const hourBooks = catalogs.filter((c) => c.kind === "hours");
  const groupBook = groupBooks.find((c) => c.id === groupId) ?? groupBooks[0];
  const wageBook = wageBooks.find((c) => c.id === wageId) ?? wageBooks[0];
  const workBooks = catalogs.filter((c) => c.kind === "work");
  const hoursBook = hourBooks.find((c) => c.id === hoursId) ?? hourBooks[0];
  const workBook = workBooks.find((c) => c.id === workId) ?? workBooks[0];

  return (
    <div className="insp">
      <NotesHelp raw={pos.basis?.raw} />
      <label>
        {labor ? "Должность" : asutp ? "Часть" : "Наименование"}
        <input value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      {!labor && !asutp && (
        <label>
          Код / обоснование
          <input value={code} onChange={(e) => setCode(e.target.value)} />
        </label>
      )}
      {pos.catalogCode && <p className="muted">Каталог: {pos.catalogCode}</p>}
      {labor ? (
        <>
          <p className="labor-books-title">Справочники для смет 3П</p>
          {groupBook && (
            <label>
              Типовая группа · {groupBook.title}
              <select
                value=""
                onChange={(e) => {
                  const post = groupBook.posts.find((p) => p.id === e.target.value);
                  if (!post) return;
                  setGroupId(groupBook.id);
                  setName(post.title);
                }}
              >
                <option value="">Группа должностей…</option>
                {groupBook.posts.map((p) => (
                  <option key={p.id} value={p.id}>{p.title}</option>
                ))}
              </select>
            </label>
          )}
          {wageBook && (
            <label>
              Ставка · {wageBook.title} · {wageBook.customer}
              <select
                value=""
                onChange={(e) => {
                  const post = wageBook.posts.find((p) => p.id === e.target.value);
                  if (!post) return;
                  setWageId(wageBook.id);
                  setA(String(post.daily));
                }}
              >
                <option value="">Должность, дневная ставка…</option>
                {[...new Set(wageBook.posts.map((p) => p.group))].map((group) => (
                  <optgroup key={group} label={group}>
                    {wageBook.posts.filter((p) => p.group === group).map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.grade ? `${p.title}, ${p.grade}` : p.title} · {p.daily.toLocaleString("ru-RU")}
                      </option>
                    ))}
                  </optgroup>
                ))}
              </select>
            </label>
          )}
          {workBook && (
            <label>
              Типовая работа · {workBook.title} · {workBook.customer}
              <select
                value=""
                onChange={(e) => {
                  const post = workBook.posts.find((p) => p.id === e.target.value);
                  if (!post) return;
                  setWorkId(workBook.id);
                  setName(post.title);
                }}
              >
                <option value="">Работа из перечня…</option>
                {[...new Set(workBook.posts.map((p) => p.group))].map((group) => (
                  <optgroup key={group} label={group}>
                    {workBook.posts.filter((p) => p.group === group).map((p) => (
                      <option key={p.id} value={p.id}>{p.id} {p.title}</option>
                    ))}
                  </optgroup>
                ))}
              </select>
            </label>
          )}
          {hoursBook && (
            <label>
              Трудозатраты · {hoursBook.title} · {hoursBook.customer}
              <select
                value=""
                onChange={(e) => {
                  const post = hoursBook.posts.find((p) => p.id === e.target.value);
                  if (!post) return;
                  setHoursId(hoursBook.id);
                  setX(String(Math.round((post.daily / 8) * 1000) / 1000));
                }}
              >
                <option value="">Работа, чел·ч на лист…</option>
                {[...new Set(hoursBook.posts.map((p) => p.group))].map((group) => (
                  <optgroup key={group} label={group}>
                    {hoursBook.posts.filter((p) => p.group === group).map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.grade ? `${p.title}, ${p.grade}` : p.title}{p.format ? ` · ${p.format}` : ""} · {p.daily} чел·ч
                      </option>
                    ))}
                  </optgroup>
                ))}
              </select>
            </label>
          )}
          <p className="muted">Группа задаёт должность. Типовая работа тоже пишет наименование строки. Ставка — из справочника СНТП. Чел-дни — из норматива СИБУР: часы на лист ÷ 8.</p>
          <div className="row3">
            <label>
              Чел.
              <input value={heads} onChange={(e) => setHeads(e.target.value)} />
            </label>
            <label>
              Чел-дн
              <input value={x} onChange={(e) => setX(e.target.value)} />
            </label>
            <label>
              Ставка
              <input value={a} onChange={(e) => setA(e.target.value)} />
            </label>
          </div>
          <p className="muted">
            Зарплата {money(est.labor?.salaryTotal)} · себест. {money(est.labor?.primeCost)} · прибыль{" "}
            {money(est.labor?.profit)}
          </p>
          <div className="row3">
            <label>
              Доля ЗП
              <input value={share} onChange={(e) => setShare(e.target.value)} />
            </label>
            <label>
              Прибыль %
              <input value={profitPct} onChange={(e) => setProfitPct(e.target.value)} />
            </label>
            <label>
              Районный К
              <input value={regK} onChange={(e) => setRegK(e.target.value)} />
            </label>
          </div>
        </>
      ) : asutp ? (
        <>
          <div className="row3">
            <label>
              Баллы
              <input value={x} onChange={(e) => setX(e.target.value)} />
            </label>
            <label>
              Множитель
              <input value={a} onChange={(e) => setA(e.target.value)} />
            </label>
            <label>
              Ккорр.
              <input value={corrK} onChange={(e) => setCorrK(e.target.value)} />
            </label>
          </div>
          <p className="muted">
            Части: {(est.asutp?.enabledParts ?? []).join(", ") || "—"}
            {est.asutp?.catalog ? ` · ${est.asutp.catalog}` : ""}
          </p>
        </>
      ) : (
        <>
          <div className="row3">
            <label>
              A
              <input value={a} onChange={(e) => setA(e.target.value)} />
            </label>
            <label>
              B
              <input value={b} onChange={(e) => setB(e.target.value)} />
            </label>
            <label>
              X
              <input value={x} onChange={(e) => setX(e.target.value)} />
            </label>
          </div>
          <div className="row3">
            <label>
              Xmin
              <input value={xmin} onChange={(e) => setXmin(e.target.value)} />
            </label>
            <label>
              Xmax
              <input value={xmax} onChange={(e) => setXmax(e.target.value)} />
            </label>
            <label>
              Кол.
              <input defaultValue={pos.qty} readOnly />
            </label>
          </div>
        </>
      )}
      <button
        type="button"
        className="primary"
        onClick={() => {
          void (async () => {
            if (labor) {
              await onSave({
                name,
                qty: heads === "" ? 1 : Number(heads.replace(",", ".")),
                quantity: x === "" ? null : Number(x.replace(",", ".")),
                priceA: a === "" ? null : Number(a.replace(",", "."))
              });
              const laborBody: LaborModel = {
                groupCatalogId: groupId || undefined,
                wageCatalogId: wageId || undefined,
                hoursCatalogId: hoursId || undefined,
                workCatalogId: workId || undefined,
                salaryShare: Number(share.replace(",", ".")) || 0.45,
                profitPercent: Number(profitPct.replace(",", ".")) || 8,
                regionalK: Number(regK.replace(",", ".")) || 1
              };
              await onSaveEst({ labor: laborBody });
              return;
            }
            if (asutp) {
              await onSave({
                name,
                quantity: x === "" ? null : Number(x.replace(",", ".")),
                priceA: a === "" ? null : Number(a.replace(",", "."))
              });
              await onSaveEst({
                asutp: { ...est.asutp, correctionK: Number(corrK.replace(",", ".")) || 0.7 }
              });
              return;
            }
            await onSave({
              name,
              code,
              priceA: a === "" ? null : Number(a.replace(",", ".")),
              priceB: b === "" ? null : Number(b.replace(",", ".")),
              quantity: x === "" ? null : Number(x.replace(",", ".")),
              xmin: xmin === "" ? null : Number(xmin.replace(",", ".")),
              xmax: xmax === "" ? null : Number(xmax.replace(",", "."))
            });
          })();
        }}
      >
        Применить и пересчитать
      </button>
      <div className="formula">
        <div>{pos.excelFormula}</div>
        <div>{pos.formula}</div>
        <div>{pos.engineExpandedFormula}</div>
      </div>
      {(conditions ?? []).length > 0 && (
        <>
          <h4>Усложняющие факторы строки</h4>
          <ul className="factor-row-list">
            {(conditions ?? []).map((c) => {
              const off = pos.suppressedConditionIds?.includes(c.id) ?? false;
              return (
                <li key={c.id}>
                  <label>
                    <input
                      type="checkbox"
                      checked={!off}
                      onChange={() => {
                        const ids = new Set(pos.suppressedConditionIds ?? []);
                        if (off) ids.delete(c.id);
                        else ids.add(c.id);
                        void onSave({ suppressedConditionIds: [...ids] });
                      }}
                    />
                    {c.alias} <code>{c.id}</code>
                  </label>
                </li>
              );
            })}
          </ul>
        </>
      )}
      <h4>Коэффициенты (П / РД / РП)</h4>
      <ul>
        {pos.factors.map((f) => (
          <li key={f.identifier}>
            {f.identifier}: {f.projectDoc ?? "—"} / {f.workDoc ?? "—"} / {f.workProject ?? "—"}
            <span className="muted">
              {" "}
              · {f.role ?? "other"} · {f.source ?? "—"}
              {f.catalogKey ? ` · ${f.catalogKey}` : ""}
            </span>
            {f.letterRef && <div className="muted">{f.letterRef}</div>}
          </li>
        ))}
      </ul>
      <label>
        Поиск в справочнике
        <input
          value={q}
          placeholder="849 / Ктек / инд.3кв"
          onChange={(e) => setQ(e.target.value)}
          onBlur={() => {
            if (!q.trim()) return;
            Promise.all([api.catalogRates(q), api.catalogKoeffs(q), api.catalogIndexes(q)])
              .then((all) => setHits(all.flat().slice(0, 12)))
              .catch(() => setHits([]));
          }}
        />
      </label>
      <button type="button" onClick={onFind}>
        Найти расценку в строке
      </button>
      {hits.length > 0 && (
        <ul className="catalog-hits">
          {hits.map((h, i) => {
            const row = h as { name?: string; code?: string; value?: number; typeName?: string };
            return (
              <li key={i}>
                {row.code ?? row.typeName ?? ""} {row.name} {row.value ?? ""}
              </li>
            );
          })}
        </ul>
      )}
      {pos.items?.length > 0 && (
        <>
          <h4>Интервалы</h4>
          <ul>
            {pos.items.map((it, i) => (
              <li key={i}>
                {it.caption ?? it.code} [{it.minQuantity ?? "…"}; {it.maxQuantity ?? "…"}] A={it.priceA} B={it.priceB}
              </li>
            ))}
          </ul>
        </>
      )}
      {pos.relativeFactors?.length > 0 && (
        <>
          <h4>РПД</h4>
          <ul>
            {pos.relativeFactors.map((r) => (
              <li key={r.caption}>
                {r.caption}: {r.projectDoc ?? r.workDoc}% → {money(r.relativePrice)}
              </li>
            ))}
          </ul>
        </>
      )}
      <p className="muted">Источник: {pos.origin ?? "—"} · {pos.sheetName}</p>
    </div>
  );
}
