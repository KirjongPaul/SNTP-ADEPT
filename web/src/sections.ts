export type PirSection = {
  id: string;
  n: string;
  short: string;
  name: string;
  form: string;
  books: string[];
};

export const BOOKS: Record<string, { short: string; title: string }> = {
  "NZ-849": { short: "НЗ 849", title: "Объекты газовой промышленности" },
  "NZ-912": { short: "НЗ 912", title: "Инженерная защита территорий" },
  "NZ-51": { short: "НЗ 51", title: "Автомобильные дороги" },
  "NZ-847": { short: "НЗ 847", title: "Подготовка проектной документации" },
  "NZ-140": { short: "НЗ 140", title: "СБЦ на проектные работы" },
  "SBC-GAZ": { short: "ГАЗООБ", title: "Газооборудование и газоснабжение" },
  "SBC-NPZ": { short: "НПЗ", title: "Нефтепереработка и нефтехимия" },
  "SBC-SVYAZ": { short: "СВЯЗЬ", title: "Объекты связи" },
  "SBC-FIRE": { short: "ПРОТИВОПОЖ", title: "Противопожарная защита" },
  "SBC-MTN": { short: "МТН", title: "Магистральный транспорт нефти" },
  "SBC-VIK": { short: "ВИК", title: "Водоснабжение и канализация" },
  "SBC-ASUTP": { short: "АСУТП", title: "СБЦ АСУТП 2016" },
  "LABOR-3P": { short: "3П", title: "Трудозатраты" }
};

/** Раздел свода 1ПС задаёт группу справочников, в которой ищет строка ввода. */
export const SECTIONS: PirSection[] = [
  { id: "1.1", n: "1.1", short: "ГП", name: "Генеральный план", form: "form2P707", books: ["NZ-849", "NZ-912"] },
  { id: "1.2", n: "1.2", short: "АД", name: "Автомобильные дороги", form: "form2P707", books: ["NZ-51"] },
  { id: "1.3", n: "1.3", short: "ТХ", name: "Технология производства", form: "form2P707", books: ["NZ-849"] },
  { id: "1.4", n: "1.4", short: "ОВ", name: "Отопление и вентиляция", form: "form2P707", books: ["SBC-GAZ", "SBC-NPZ"] },
  { id: "1.5", n: "1.5", short: "АСУТП", name: "АСУ технологическим процессом", form: "asutpCard", books: ["SBC-ASUTP"] },
  { id: "1.6", n: "1.6", short: "АСУЭ", name: "АСУ энергоснабжением", form: "asutpCard", books: ["SBC-ASUTP"] },
  { id: "1.7", n: "1.7", short: "АЭО", name: "Автоматизация освещения", form: "asutpCard", books: ["SBC-ASUTP"] },
  { id: "1.8", n: "1.8", short: "СПБА", name: "Противопожарная автоматика", form: "asutpCard", books: ["SBC-ASUTP", "SBC-FIRE"] },
  { id: "1.9", n: "1.9", short: "СС", name: "Сети связи", form: "form2PSbc", books: ["SBC-SVYAZ", "SBC-FIRE"] },
  { id: "1.10", n: "1.10", short: "ЭС", name: "Электроснабжение", form: "form2PSbc", books: ["SBC-MTN", "SBC-NPZ", "SBC-GAZ", "NZ-847"] },
  { id: "1.11", n: "1.11", short: "ЭХЗ", name: "Электрохимическая защита", form: "form2PSbc", books: ["SBC-GAZ", "NZ-140"] },
  { id: "1.12", n: "1.12", short: "ВК", name: "Водоснабжение и канализация", form: "form2PSbc", books: ["SBC-VIK", "SBC-NPZ"] },
  { id: "1.13", n: "1.13", short: "ЛЧ", name: "Линейная часть", form: "form2P707", books: ["NZ-849"] },
  { id: "1.14", n: "1.14", short: "Бур", name: "Бурение скважин", form: "form3P", books: ["LABOR-3P"] }
];

export function sectionById(id?: string | null) {
  if (!id) return undefined;
  const m = id.match(/\d+\.\d+/);
  const key = m?.[0] ?? id;
  return SECTIONS.find((s) => s.id === key);
}

/** Номер сметы 3.1 — это раздел свода 1.1 той же дисциплины (стадия · раздел). */
export function sectionFor(number?: string | null, title?: string | null) {
  const direct = sectionById(number);
  if (direct && number?.startsWith(direct.id)) return direct;
  const parts = (number ?? "").split(".").filter(Boolean);
  if (parts.length >= 2) {
    const shifted = SECTIONS.find((s) => s.id === `1.${parts[1]}`);
    if (shifted) return shifted;
  }
  const text = (title ?? "").toLowerCase();
  return SECTIONS.find((s) => text.includes(s.name.toLowerCase()) || text.includes(s.short.toLowerCase()));
}

export function bookLabels(section?: PirSection) {
  if (!section) return "все сборники";
  return section.books.map((id) => BOOKS[id]?.short ?? id).join(" · ");
}

export function bookOfHit(hit: { code?: string; doc?: string; kind?: string; name?: string }) {
  const blob = `${hit.code ?? ""} ${hit.doc ?? ""} ${hit.kind ?? ""} ${hit.name ?? ""}`;
  if (/849/.test(blob)) return "NZ-849";
  if (/912/.test(blob)) return "NZ-912";
  if (/(^|\D)51(\D|$)/.test(blob) && /NZ|НЗ|пр/i.test(blob)) return "NZ-51";
  if (/847/.test(blob)) return "NZ-847";
  if (/140/.test(blob) && /NZ|НЗ|СБЦ/i.test(blob)) return "NZ-140";
  if (/АСУТП|ASUTP|81-2001-22/i.test(blob)) return "SBC-ASUTP";
  if (/ГАЗООБ|газооборуд/i.test(blob)) return "SBC-GAZ";
  if (/НПЗ|нефтехим/i.test(blob)) return "SBC-NPZ";
  if (/связ/i.test(blob)) return "SBC-SVYAZ";
  if (/противопож|пожар/i.test(blob)) return "SBC-FIRE";
  if (/магистральн/i.test(blob)) return "SBC-MTN";
  if (/водоснаб|канализ|ВИК/i.test(blob)) return "SBC-VIK";
  if (/3П|трудозатрат|LABOR/i.test(blob)) return "LABOR-3P";
  if (/^NZ/i.test(hit.code ?? "") || hit.kind === "NZ") return "NZ-849";
  if (/^SBC/i.test(hit.code ?? "") || hit.kind === "SBC") return "SBC-GAZ";
  return "OTHER";
}

export function inSection(hit: { code?: string; doc?: string; kind?: string; name?: string }, section?: PirSection) {
  if (!section) return true;
  return section.books.includes(bookOfHit(hit));
}
