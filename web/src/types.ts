export type DesignStage = "projectDoc" | "workDoc" | "workProject";
export type EstimateForm =
  | "form2P707"
  | "form2PSbc"
  | "asutpCard"
  | "form1Ps"
  | "form3P"
  | "laborCost"
  | "unknown"
  | string;

export interface LaborModel {
  groupCatalogId?: string;
  wageCatalogId?: string;
  hoursCatalogId?: string;
  workCatalogId?: string;
  salaryShare: number;
  profitPercent: number;
  regionalK: number;
  salaryTotal?: number;
  primeCost?: number;
  profit?: number;
}

export interface AsutpModel {
  needTz?: boolean;
  enabledParts?: string[];
  gazpromK?: number;
  correctionK?: number;
  catalog?: string;
}

export interface ProjectCondition {
  id: string;
  alias: string;
  enabled?: boolean;
  uses: { bookId: string; bookTitle: string; bookMark: string; table: string; item: string; value: number; priceTables: string[] }[];
}

export interface Factor {
  identifier: string;
  caption: string;
  code?: string;
  projectDoc?: number;
  workDoc?: number;
  workProject?: number;
  combineType?: string;
  role?: string;
  enabled?: boolean;
  source?: string;
  catalogKey?: string;
  letterRef?: string;
}

export interface IntervalItem {
  caption?: string;
  code?: string;
  minQuantity?: number;
  maxQuantity?: number;
  priceA?: number;
  priceB?: number;
}

export interface RelativeFactor {
  caption: string;
  projectDoc?: number;
  workDoc?: number;
  relativePrice?: number;
  enabled?: boolean;
}

export interface Position {
  id: string;
  number: string;
  name: string;
  kind: string;
  code?: string;
  priceA?: number;
  priceB?: number;
  quantity?: number;
  quantityUnit?: string;
  qty: number;
  xmin?: number;
  xmax?: number;
  items: IntervalItem[];
  formula?: string;
  excelFormula?: string;
  expandedFormula?: string;
  engineExpandedFormula?: string;
  catalogCode?: string;
  factors: Factor[];
  relativeFactors: RelativeFactor[];
  excelAmount?: number;
  engineAmount?: number;
  delta?: number;
  sheetName?: string;
  estimateNumber?: string;
  origin?: string;
  section?: string;
  suppressedConditionIds?: string[];
  basis?: { kind?: string; raw?: string; doc?: string; table?: string; item?: string };
}

export interface Estimate {
  id: string;
  sheet: string;
  form: EstimateForm;
  title: string;
  number?: string;
  author?: string;
  outlineDepth?: number;
  heading?: boolean;
  chapterCaption?: string;
  positions: Position[];
  factors: Factor[];
  conditions?: ProjectCondition[];
  suppressedConditionIds?: string[];
  excelAmount?: number;
  engineAmount?: number;
  optimizationK?: number;
  districtK?: number;
  volumeK?: number;
  labor?: LaborModel;
  asutp?: AsutpModel;
  volumes?: WorkVolume[];
}

export interface WorkVolume {
  id: string;
  name: string;
  unit?: string;
  quantity?: number;
  positionIds: string[];
  bookId?: string;
  table?: string;
  item?: string;
  status: string;
  candidates: VolumeCandidate[];
  chosenItems: string[];
  protocol: string[];
  pick?: VolumePick | null;
}

export interface VolumePick {
  item: string;
  xmin?: number;
  xmax?: number;
  unit?: string;
  a?: number;
  b?: number;
  kind: string;
}

export interface VolumeCandidate {
  bookId: string;
  table: string;
  item: string;
  name: string;
  unit?: string;
  xmin?: number;
  xmax?: number;
  a?: number;
  b?: number;
  reason: string;
}

export interface Clarification {
  id: string;
  severity: string;
  code: string;
  message: string;
  estimateId?: string;
  positionId?: string;
  field?: string;
  suggestedAction?: string;
}

export interface SurveyRateUse {
  bookId: string;
  orderRef: string;
  nd: string;
  table: string;
  item?: string;
  note: string;
}

export interface SurveyParameter {
  id: string;
  name: string;
  unit?: string;
  origin: string;
  quantity?: number;
  quantityTo?: number;
  text?: string;
  manual: boolean;
  normNd?: string;
  normClause?: string;
  normTable?: string;
  quote?: string;
  why?: string;
  uses: SurveyRateUse[];
  protocol: string[];
}

export interface SurveySheet {
  kind: string;
  title: string;
  parameters: SurveyParameter[];
}

export interface PirDocument {
  schema: string;
  id: string;
  survey?: SurveySheet;
  source: { file?: string; kind?: string };
  project: {
    objectName: string;
    stage: DesignStage;
    stageLabel: string;
    phase?: string;
    designer: string;
    customer: string;
    vatRate: number;
    currentIndex?: number;
    currentIndexCaption?: string;
    currentIndexLetter?: string;
    roundMode: string;
    kponPolicy: string;
    workFolder?: string;
    conditions?: ProjectCondition[];
  };
  summary?: {
    form: string;
    rows: { code: string; title: string; ref?: string; survey: number; design: number; total: number; heading?: boolean }[];
    totals: { design: number; vat: number; grand: number; survey: number };
    hideStageHeading?: boolean;
  };
  estimates: Estimate[];
  issues: Clarification[];
}

export interface ProjectEstimateHead {
  id: string;
  number: string;
  title: string;
  depth: number;
  amount?: number | null;
  form?: string;
  author?: string;
}

export interface ProjectListItem {
  id: string;
  name: string;
  updatedAt: string;
  stage?: string;
  estimateCount?: number;
  folder?: string;
  designer?: string;
  authors?: string[];
  total?: number | null;
  estimates?: ProjectEstimateHead[];
}

export interface ImportProgress {
  percent: number;
  phase: string;
  message: string;
  sheet?: string | null;
  form?: string | null;
  sheetIndex?: number | null;
  sheetCount?: number | null;
}
