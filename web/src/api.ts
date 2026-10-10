import type {
  AsutpModel,
  Factor,
  ImportProgress,
  IntervalItem,
  LaborModel,
  PirDocument,
  ProjectCondition,
  Position,
  ProjectListItem,
  RelativeFactor
} from "./types";

async function json<T>(r: Response): Promise<T> {
  if (!r.ok) {
    const t = await r.text();
    let message = t;
    try {
      message = (JSON.parse(t) as { error?: string }).error || t;
    } catch {
      /* ответ не JSON */
    }
    throw new Error(message || r.statusText);
  }
  return r.json() as Promise<T>;
}

export const api = {
  health: () => fetch("/api/health").then(json<{ ok: boolean }>),
  list: () => fetch("/api/projects").then(json<ProjectListItem[]>),
  get: (id: string) => fetch(`/api/projects/${id}`).then(json<PirDocument>),
  createObject: (body: Record<string, unknown>) =>
    fetch("/api/projects", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<PirDocument>),
  patchProject: (id: string, body: Record<string, unknown>) =>
    fetch(`/api/projects/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<PirDocument>),
  createLocal: (id: string, body: Record<string, unknown>) =>
    fetch(`/api/projects/${id}/estimates`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<{ document: PirDocument; estimate: { id: string } }>),
  createForm3p: (id: string) =>
    fetch(`/api/projects/${id}/estimates/form3p`, { method: "POST" }).then(json<{ document: PirDocument; estimate: { id: string } }>),
  createAutomation: (id: string) =>
    fetch(`/api/projects/${id}/estimates/automation`, { method: "POST" }).then(json<{ document: PirDocument; estimate: { id: string } }>),
  createHeading: (id: string, memberIds: string[]) =>
    fetch(`/api/projects/${id}/summary/headings`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ memberIds })
    }).then(json<{ document: PirDocument; estimate: { id: string } }>),
  shiftOutline: (id: string, estimateIds: string[], delta: -1 | 1) =>
    fetch(`/api/projects/${id}/outline/level`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ estimateIds, delta })
    }).then(json<{ document: PirDocument; estimate?: { id: string } | null }>),
  hideStageHeading: (id: string) =>
    fetch(`/api/projects/${id}/summary/hide-stage`, { method: "POST" }).then(json<PirDocument>),
  moveOutline: (id: string, estimateId: string, targetId: string | null, asChild: boolean) =>
    fetch(`/api/projects/${id}/outline`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ estimateId, targetId, asChild })
    }).then(json<PirDocument>),
  movePosition: (id: string, estId: string, positionId: string, beforeId: string | null) =>
    fetch(`/api/projects/${id}/estimates/${estId}/positions/move`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ positionId, beforeId })
    }).then(json<PirDocument>),
  removeSection: (id: string, estId: string, sectionId: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/sections/${sectionId}`, { method: "DELETE" }).then(json<PirDocument>),
  insertSection: (id: string, estId: string, beforeId?: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/sections`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ beforeId: beforeId ?? null })
    }).then(json<{ document: PirDocument; position: Position }>),
  createPosition: (id: string, estId: string, beforeId?: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/positions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ beforeId: beforeId ?? null })
    }).then(json<{ document: PirDocument; position: Position }>),
  deletePosition: (id: string, estId: string, posId: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/positions/${posId}`, { method: "DELETE" }).then(json<PirDocument>),
  createSummary: (id: string) =>
    fetch(`/api/projects/${id}/summary`, { method: "POST" }).then(json<PirDocument>),
  recalc: (id: string) =>
    fetch(`/api/projects/${id}/recalculate`, { method: "POST" }).then(json<PirDocument>),
  patchEstimate: (
    id: string,
    estId: string,
    body: { title?: string; labor?: LaborModel; asutp?: AsutpModel; conditions?: ProjectCondition[]; suppressedConditionIds?: string[] }
  ) =>
    fetch(`/api/projects/${id}/estimates/${estId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<PirDocument>),
  patchPosition: (
    id: string,
    posId: string,
    body: {
      quantity?: number;
      priceA?: number;
      priceB?: number;
      name?: string;
      code?: string;
      qty?: number;
      xmin?: number;
      xmax?: number;
      factors?: Factor[];
      items?: IntervalItem[];
      relativeFactors?: RelativeFactor[];
      suppressedConditionIds?: string[];
    }
  ) =>
    fetch(`/api/projects/${id}/positions/${posId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<PirDocument>),
  importExcel: (file: File, onProgress?: (p: ImportProgress) => void) => {
    const fd = new FormData();
    fd.append("file", file);
    return fetch("/api/import/excel", {
      method: "POST",
      body: fd,
      headers: { Accept: "text/event-stream" }
    }).then((r) => readImportExcel(r, onProgress));
  },
  removeProject: (id: string) =>
    fetch(`/api/projects/${id}`, { method: "DELETE" }).then(json<{ ok: boolean; id: string }>),
  reloadProject: (id: string) =>
    fetch(`/api/projects/${id}/reload`, { method: "POST" }).then(json<PirDocument>),
  revealFolder: (id: string) =>
    fetch(`/api/projects/${id}/reveal`, { method: "POST" }).then(json<{ ok: boolean; folder: string }>),
  importGrand: (file: File) => {
    const fd = new FormData();
    fd.append("file", file);
    return fetch("/api/import/grand-xml", { method: "POST", body: fd }).then(json<PirDocument>);
  },
  sources: (id: string) => fetch(`/api/projects/${id}/sources`).then(json<{ items: SourceFile[] }>),
  addSources: (id: string, files: File[]) => {
    const fd = new FormData();
    for (const file of files) fd.append("file", file, file.name);
    return fetch(`/api/projects/${id}/sources`, { method: "POST", body: fd }).then(json<{ items: SourceFile[] }>);
  },
  revealSources: (id: string) =>
    fetch(`/api/projects/${id}/sources/reveal`, { method: "POST" }).then(json<{ ok: boolean; folder: string }>),
  openSource: (id: string, name: string) =>
    fetch(`/api/projects/${id}/sources/open`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name })
    }).then(json<{ ok: boolean }>),
  removeSource: (id: string, name: string) =>
    fetch(`/api/projects/${id}/sources/remove`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name })
    }).then(json<{ items: SourceFile[] }>),
  analogs: (id: string) => fetch(`/api/projects/${id}/analogs`).then(json<{ items: AnalogCard[] }>),
  analog: (id: string, analogId: string) =>
    fetch(`/api/projects/${id}/analogs/${analogId}`).then(json<AnalogDetail>),
  pickAnalogs: (id: string) =>
    fetch(`/api/projects/${id}/analogs/pick`, { method: "POST" }).then(json<{ items: AnalogCard[]; folder: string; picked: number }>),
  importAnalog: (id: string, file: File) => {
    const fd = new FormData();
    fd.append("file", file);
    return fetch(`/api/projects/${id}/analogs`, { method: "POST", body: fd }).then(json<{ card: AnalogCard; catalog: { items: AnalogCard[] } }>);
  },
  reimportAnalog: (id: string, analogId: string) =>
    fetch(`/api/projects/${id}/analogs/${analogId}/reimport`, { method: "POST" }).then(json<{ items: AnalogCard[]; document?: PirDocument }>),
  deleteAnalog: (id: string, analogId: string) =>
    fetch(`/api/projects/${id}/analogs/${analogId}`, { method: "DELETE" }).then(json<{ items: AnalogCard[] }>),
  renameAnalog: (id: string, analogId: string, systemName: string) =>
    fetch(`/api/projects/${id}/analogs/${analogId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ systemName })
    }).then(json<{ items: AnalogCard[] }>),
  acceptAnalogEstimates: (id: string, analogId: string, estimateIds: string[]) =>
    fetch(`/api/projects/${id}/analogs/${analogId}/accept-estimates`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ estimateIds })
    }).then(json<PirDocument>),
  acceptAnalogPositions: (id: string, analogId: string, targetEstimateId: string, positionIds: string[]) =>
    fetch(`/api/projects/${id}/analogs/${analogId}/accept-positions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ targetEstimateId, positionIds })
    }).then(json<PirDocument>),
  revealAnalogs: (id: string) =>
    fetch(`/api/projects/${id}/analogs/reveal`, { method: "POST" }).then(json<{ ok: boolean; folder: string }>),
  openAnalog: (id: string, analogId: string) =>
    fetch(`/api/projects/${id}/analogs/${analogId}/open`, { method: "POST" }).then(json<{ ok: boolean }>),
  revealAnalogFile: (id: string, analogId: string) =>
    fetch(`/api/projects/${id}/analogs/${analogId}/reveal`, { method: "POST" }).then(json<{ ok: boolean; path: string }>),
  analogMaps: (id: string) => fetch(`/api/projects/${id}/analog-mappings`).then(json<{ rules: AnalogMapRule[] }>),
  saveAnalogMaps: (id: string, rules: AnalogMapRule[]) =>
    fetch(`/api/projects/${id}/analog-mappings`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ rules })
    }).then(json<{ rules: AnalogMapRule[] }>),
  recognizePaste: (id: string, text: string, html?: string) =>
    fetch(`/api/projects/${id}/analog-paste`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text, html })
    }).then(json<{ needsTune: boolean; scores: { note: string; confidence: number; looksLikeBook: boolean }[] }>),
  exportXmlUrl: (id: string, estimateId?: string) =>
    `/api/projects/${id}/export/grand-xml${estimateId ? `?estimateId=${encodeURIComponent(estimateId)}` : ""}`,
  exportJsonUrl: (id: string) => `/api/projects/${id}/json`,
  catalogStatus: () => fetch("/api/catalog/status").then(json<{ koeffs: number; indexes: number; rates: number; rpds: number }>),
  catalogKoeffs: (q: string) => fetch(`/api/catalog/koeffs?q=${encodeURIComponent(q)}`).then(json<unknown[]>),
  catalogIndexes: (q: string) => fetch(`/api/catalog/indexes?q=${encodeURIComponent(q)}`).then(json<unknown[]>),
  catalogRates: (q: string) => fetch(`/api/catalog/rates?q=${encodeURIComponent(q)}`).then(json<unknown[]>),
  catalogSync: () => fetch("/api/catalog/sync-apr", { method: "POST" }).then(json<unknown>),
  catalogGroups: (kind: string) => fetch(`/api/catalog/groups?kind=${encodeURIComponent(kind)}`).then(json<{ kind: string; nodes: { name: string; count: number }[] }>),
  catalogGazprom: () => fetch("/api/catalog/gazprom").then(json<{ identifier: string; caption: string; value?: number; role?: string; source?: string }[]>),
  catalogSyncFgis: () => fetch("/api/catalog/sync-fgis", { method: "POST" }).then(json<unknown>),
  searchLine: (text: string) =>
    fetch("/api/catalog/search-line", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text })
    }).then(json<{ status: string; hits: { status: string; code: string; name: string; priceA?: number; priceB?: number; hasPrices: boolean; source: string; kind?: string; doc?: string; table?: string; item?: string }[]; vendor: { identifier: string; caption: string; value?: number }[] }>),
  applyRate: (id: string, posId: string, hit: unknown) =>
    fetch(`/api/projects/${id}/positions/${posId}/apply-rate`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(hit)
    }).then(json<PirDocument>),
  pastePreview: (id: string, estId: string, text: string, html?: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/paste`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text, html })
    }).then(json<{ format: string; columnMap: string[]; rows: { kind: string; cells: string[] }[] }>),
  pasteApply: (id: string, estId: string, text: string, html?: string, force?: boolean) =>
    fetch(`/api/projects/${id}/estimates/${estId}/paste/apply`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text, html, force: !!force })
    }).then(json<PirDocument>),
  openFolder: (path: string) =>
    fetch("/api/projects/open-folder", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path })
    }).then(json<PirDocument>),
  copyEstimate: (id: string, estId: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/copy`, { method: "POST" }).then(json<{ document: PirDocument }>),
  importEstimate: (id: string, sourceProjectId: string, sourceEstimateId: string) =>
    fetch(`/api/projects/${id}/import-estimate`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ sourceProjectId, sourceEstimateId })
    }).then(json<PirDocument>),
  importPosition: (id: string, estId: string, sourceProjectId: string, sourceEstimateId: string, sourcePositionId: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/import-position`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ sourceProjectId, sourceEstimateId, sourcePositionId })
    }).then(json<PirDocument>),
  deleteEstimate: (id: string, estId: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}`, { method: "DELETE" }).then(json<PirDocument>),
  drafts: (id: string) =>
    fetch(`/api/projects/${id}/drafts`).then(json<{ scope?: { status: string; items: unknown[] } | null; vor: { title: string; status: string; rows: unknown[] }[]; assignments: string[] }>),
  applyScope: (id: string, estimateId?: string) =>
    fetch(`/api/projects/${id}/scope/apply${estimateId ? `?estimateId=${encodeURIComponent(estimateId)}` : ""}`, { method: "POST" }).then(json<PirDocument>),
  books: () => fetch("/api/books").then(json<BookSummary[]>),
  offers: () => fetch("/api/books/offers").then(json<OfferGroup[]>),
  hideOffer: (id: string, hidden: boolean) =>
    fetch("/api/books/offers/hide", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ id, hidden })
    }).then(json<{ ok: boolean }>),
  nameOffer: (id: string, title: string) =>
    fetch("/api/books/offers/name", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ id, title })
    }).then(json<{ ok: boolean }>),
  book: (id: string) => fetch(`/api/books/${id}`).then(json<{ book: ReferenceBook; report?: string | null; variables?: FactorVariable[]; codexHref?: string | null }>),
  bookNotes: (id: string, anchor?: string) =>
    fetch(`/api/books/${id}/notes${anchor ? `?anchor=${encodeURIComponent(anchor)}` : ""}`).then(json<BookNote[]>),
  userNotes: (id: string, anchor?: string) =>
    fetch(`/api/books/${id}/user-notes${anchor ? `?anchor=${encodeURIComponent(anchor)}` : ""}`).then(json<UserBookNote[]>),
  addUserNote: (id: string, anchor: string, text: string, source: string) =>
    fetch(`/api/books/${id}/user-notes`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ anchor, text, source })
    }).then(json<UserBookNote>),
  editUserNote: (id: string, noteId: string, text: string) =>
    fetch(`/api/books/${id}/user-notes/${noteId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text })
    }).then(json<UserBookNote>),
  deleteUserNote: (id: string, noteId: string) =>
    fetch(`/api/books/${id}/user-notes/${noteId}`, { method: "DELETE" }).then(json<{ ok: boolean }>),
  laborCatalogs: () => fetch("/api/catalog/labor").then(json<LaborCatalog[]>),
  assignLaborRole: (catalogId: string, postId: string, roleId: string) =>
    fetch(`/api/catalog/labor/${encodeURIComponent(catalogId)}/posts/${encodeURIComponent(postId)}/role`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ roleId })
    }).then(json<LaborCatalog[]>),
  assignLaborLink: (groupCatalogId: string, groupPostId: string, wageCatalogId: string, wagePostId: string) =>
    fetch(`/api/catalog/labor/${encodeURIComponent(groupCatalogId)}/posts/${encodeURIComponent(groupPostId)}/link`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ catalogId: wageCatalogId, postId: wagePostId })
    }).then(json<LaborCatalog[]>),
  me: () => fetch("/api/me").then(json<{ userName: string | null; account: string; resolved: boolean }>),
  mySettings: () => fetch("/api/me/settings").then(json<{ previewWidth: number | null }>),
  saveMySettings: (body: { previewWidth: number }) =>
    fetch("/api/me/settings", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<{ previewWidth: number }>),
  bookCheck: (id: string) => fetch(`/api/projects/${id}/book-check`, { method: "POST" }).then(json<PirDocument>),
  deriveVolumes: (id: string, estId: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/volumes/derive`, { method: "POST" }).then(json<PirDocument>),
  patchVolume: (id: string, estId: string, volId: string, body: { quantity?: number; chosenItems?: string[] }) =>
    fetch(`/api/projects/${id}/estimates/${estId}/volumes/${volId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<PirDocument>),
  applyVolumes: (id: string, estId: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/volumes/apply`, { method: "POST" }).then(json<PirDocument>),
  addVolumeFromBook: (id: string, estId: string, bookId: string, table: string, item: string) =>
    fetch(`/api/projects/${id}/estimates/${estId}/volumes/from-book`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ bookId, table, item })
    }).then(json<PirDocument>),
  buildSurvey: (id: string, body: Record<string, unknown> = {}) =>
    fetch(`/api/projects/${id}/survey/geology`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<PirDocument>),
  patchSurvey: (id: string, paramId: string, body: { quantity?: number; text?: string }) =>
    fetch(`/api/projects/${id}/survey/geology/${encodeURIComponent(paramId)}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    }).then(json<PirDocument>)
};

export interface LaborPost {
  id: string;
  group: string;
  title: string;
  grade: string;
  format?: string;
  daily: number;
  monthly: number;
  roleId?: string;
  links?: Record<string, string>;
}

export interface LaborCatalog {
  id: string;
  kind?: "wage" | "hours" | string;
  title: string;
  customer: string;
  order: string;
  validFrom: string;
  rateCatalogId?: string;
  posts: LaborPost[];
}

export interface BookSummary {
  id: string;
  kind: string;
  title: string;
  orderRef: string;
  status: string;
  folder: string;
  tables: number;
  items: number;
  notes: number;
}

export interface UserBookNote {
  id: string;
  bookId: string;
  anchor: string;
  text: string;
  source: string;
  userName: string;
  createdAt: string;
  updatedAt: string;
  history: { at: string; userName: string; text: string }[];
}

export interface BookNote {
  id: string;
  letterRef: string;
  letterDate?: string;
  anchor: string;
  quote: string;
  page?: number;
  file?: string;
  url?: string;
}

export interface OfferGroup {
  bookId: string;
  bookTitle: string;
  offers: { id: string; bookId: string; bookTitle: string; clauseId: string; mark: string; text: string; shortTitle: string; title: string; hidden: boolean; changedBy: string; changedAt: string; history: { at: string; userName: string; title: string }[] }[];
}

export interface FactorUse {
  bookId: string;
  bookTitle: string;
  bookMark: string;
  table: string;
  item: string;
  value: number;
  priceTables: string[];
}

export interface FactorVariable {
  id: string;
  alias: string;
  name: string;
  uses: FactorUse[];
}

export interface ReferenceBook {
  id: string;
  kind: string;
  title: string;
  orderRef: string;
  orderDate?: string;
  orderYear?: number;
  priceLevel?: string;
  frsnName?: string;
  frsnMatch?: boolean;
  status: string;
  codex?: { nd: string };
  aliases: string[];
  generalPart: { id: string; text: string; src?: { page?: number } }[];
  tables: { code: string; kind: string; name: string; paramUnit?: string; items: { n: string; name: string; unit?: string; xmin?: number; xmax?: number; a?: number; b?: number; check: string; src?: { page?: number } }[] }[];
  rules: { id: string; kind: string; clauseId?: string; text?: string; src?: { page?: number } }[];
  factors: { id: string; name: string; value?: number; condition?: string }[];
  notes: BookNote[];
}

async function readImportExcel(
  r: Response,
  onProgress?: (p: ImportProgress) => void
): Promise<PirDocument> {
  if (!r.ok) {
    const t = await r.text();
    throw new Error(parseErrorBody(t) || r.statusText);
  }
  const ct = r.headers.get("content-type") ?? "";
  if (!ct.includes("text/event-stream")) {
    return r.json() as Promise<PirDocument>;
  }
  if (!r.body) throw new Error("Нет потока прогресса импорта");

  const reader = r.body.getReader();
  const decoder = new TextDecoder();
  let buf = "";
  let document: PirDocument | undefined;
  let error: string | undefined;

  const consume = (chunk: string) => {
    buf += chunk;
    const parts = buf.split(/\r?\n\r?\n/);
    buf = parts.pop() ?? "";
    for (const raw of parts) {
      if (!raw.trim()) continue;
      let event = "message";
      const dataLines: string[] = [];
      for (const line of raw.split(/\r?\n/)) {
        if (line.startsWith("event:")) event = line.slice(6).trim();
        else if (line.startsWith("data:")) dataLines.push(line.slice(5).replace(/^\s/, ""));
      }
      if (dataLines.length === 0) continue;
      const data = dataLines.join("\n");
      if (event === "progress") {
        try {
          onProgress?.(JSON.parse(data) as ImportProgress);
        } catch {
          /* ignore malformed progress */
        }
      } else if (event === "done") {
        document = JSON.parse(data) as PirDocument;
      } else if (event === "error") {
        error = parseErrorBody(data) || data;
      }
    }
  };

  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      consume(decoder.decode());
      break;
    }
    consume(decoder.decode(value, { stream: true }));
  }

  if (error) throw new Error(error);
  if (!document) throw new Error("Импорт Excel не вернул документ");
  return document;
}

function parseErrorBody(t: string): string {
  try {
    const j = JSON.parse(t) as { error?: string };
    if (j && typeof j.error === "string") return j.error;
  } catch {
    /* plain text */
  }
  return t;
}

export interface SourceFile {
  name: string;
  size: number;
  modified: string;
  kind: string;
}

export interface AnalogCard {
  id: string;
  systemName: string;
  sourceName: string;
  format: string;
  size: number;
  modified: string;
  importedAt: string;
  nameEdited?: boolean;
  status: string;
  stale: boolean;
  sheets?: string[];
  facts?: { label: string; value: string }[];
  parts?: string[];
}

export interface AnalogMapRule {
  from: string;
  to: string;
  role: string;
}

export interface AnalogDetail {
  card: AnalogCard;
  read?: {
    estimates: { id: string; number?: string; title: string; engineAmount?: number | null; excelAmount?: number | null; positions: { id: string; number?: string; name: string; kind: string; quantity?: number | null; engineAmount?: number | null; excelAmount?: number | null }[] }[];
    scores: { positionId: string; confidence: number; note: string }[];
  } | null;
}
