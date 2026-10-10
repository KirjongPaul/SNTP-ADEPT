(() => {
  const DATA = window.PIR_DATA;
  const P = DATA.project;

  const DIALECT = {
    P2_707_ADEPT: { label: "2П · 707/пр", hint: "Выгрузка «Адепт: Проект». Ставки A/B и Xзад читаются с листа." },
    P2_SBC_MANUAL: { label: "2П · СБЦ", hint: "Рукописный бланк. Цепочка множителей, ставки часто уже свёрнуты." },
    ASUTP_2016: { label: "Карточка АСУ ТП", hint: "Баллы Ф1–Ф10 и части ТЗ…ПО по СБЦ АСУТП 2016." },
    LABOR_3P: { label: "3П · трудозатраты", hint: "Исполнители, чел.-дни, средняя оплата. Форма МДС." },
    SVOD_1PS: { label: "1ПС · свод", hint: "Сводная смета на ПИР." },
  };


  const BOOKS = {
    "NZ-849": { id: "NZ-849", short: "НЗ 849", title: "Объекты газовой промышленности" },
    "NZ-912": { id: "NZ-912", short: "НЗ 912", title: "Инженерная защита территорий" },
    "NZ-51": { id: "NZ-51", short: "НЗ 51", title: "Автомобильные дороги" },
    "NZ-847": { id: "NZ-847", short: "НЗ 847", title: "Нормативные затраты на подготовку ПД" },
    "NZ-140": { id: "NZ-140", short: "НЗ 140", title: "СБЦ на проектные работы в строительстве" },
    "SBC-GAZ": { id: "SBC-GAZ", short: "ГАЗООБ", title: "Газооборудование и газоснабжение" },
    "SBC-NPZ": { id: "SBC-NPZ", short: "НПЗ", title: "Объекты нефтепереработки и нефтехимии" },
    "SBC-SVYAZ": { id: "SBC-SVYAZ", short: "СВЯЗЬ", title: "Объекты связи" },
    "SBC-FIRE": { id: "SBC-FIRE", short: "ПРОТИВОПОЖ", title: "Системы противопожарной и охранной защиты" },
    "SBC-MTN": { id: "SBC-MTN", short: "МТН", title: "Объекты магистрального транспорта нефти" },
    "SBC-VIK": { id: "SBC-VIK", short: "ВИК", title: "Сети водоснабжения и канализации" },
    "SBC-ASUTP": { id: "SBC-ASUTP", short: "АСУТП", title: "СБЦ АСУТП 2016" },
    "LABOR-3P": { id: "LABOR-3P", short: "3П", title: "Трудозатраты, форма 3П" },
    METHOD: { id: "METHOD", short: "Методика", title: "Методика 29.12.2009" },
  };

  const SECTIONS = [
    { id: "1.1", n: "1.1", short: "ГП", name: "Генеральный план", dialect: "P2_707_ADEPT", books: ["NZ-849", "NZ-912"] },
    { id: "1.2", n: "1.2", short: "АД", name: "Автомобильные дороги", dialect: "P2_707_ADEPT", books: ["NZ-51"] },
    { id: "1.3", n: "1.3", short: "ТХ", name: "Технология производства", dialect: "P2_707_ADEPT", books: ["NZ-849"] },
    { id: "1.4", n: "1.4", short: "ОВ", name: "Отопление, вентиляция и кондиционирование", dialect: "P2_707_ADEPT", books: ["SBC-GAZ", "SBC-NPZ"] },
    { id: "1.5", n: "1.5", short: "АСУТП", name: "АСУ технологическим процессом", dialect: "ASUTP_2016", books: ["SBC-ASUTP"] },
    { id: "1.6", n: "1.6", short: "АСУЭ", name: "АСУ энергоснабжением", dialect: "ASUTP_2016", books: ["SBC-ASUTP"] },
    { id: "1.7", n: "1.7", short: "АЭО", name: "Автоматизация электрического освещения", dialect: "ASUTP_2016", books: ["SBC-ASUTP"] },
    { id: "1.8", n: "1.8", short: "СПБА", name: "Противопожарная безопасность и автоматика", dialect: "ASUTP_2016", books: ["SBC-ASUTP", "SBC-FIRE"] },
    { id: "1.9", n: "1.9", short: "СС", name: "Сети связи и сигнализации", dialect: "P2_SBC_MANUAL", books: ["SBC-SVYAZ", "SBC-FIRE"] },
    { id: "1.10", n: "1.10", short: "ЭС", name: "Электроснабжение", dialect: "P2_SBC_MANUAL", books: ["SBC-MTN", "SBC-NPZ", "SBC-GAZ", "NZ-847", "METHOD"] },
    { id: "1.11", n: "1.11", short: "ЭХЗ", name: "Электрохимическая защита", dialect: "P2_SBC_MANUAL", books: ["SBC-GAZ", "NZ-140"] },
    { id: "1.12", n: "1.12", short: "ВК", name: "Сети водоснабжения и канализации", dialect: "P2_SBC_MANUAL", books: ["SBC-VIK", "SBC-NPZ", "SBC-FIRE"] },
    { id: "1.13", n: "1.13", short: "ЛЧ", name: "Линейная часть", dialect: "P2_707_ADEPT", books: ["NZ-849"] },
    { id: "1.14", n: "1.14", short: "Бур", name: "Проект бурения скважин", dialect: "LABOR_3P", books: ["LABOR-3P"] },
  ];

  function bookOfCat(c) {
    const num = String(c.num || "");
    if (c.kind === "NZ") {
      if (num.startsWith("849")) return "NZ-849";
      if (num.startsWith("912")) return "NZ-912";
      if (num.startsWith("51")) return "NZ-51";
      if (num.startsWith("847")) return "NZ-847";
      if (num.startsWith("140")) return "NZ-140";
    }
    if (c.kind === "SBC") {
      const map = { ГАЗООБ: "SBC-GAZ", НПЗ: "SBC-NPZ", СВЯЗЬ: "SBC-SVYAZ", ПРОТИВОПОЖ: "SBC-FIRE", МТН: "SBC-MTN", ВИК: "SBC-VIK" };
      return map[c.alias] || "SBC-OTHER";
    }
    if (c.kind === "METHOD") return "METHOD";
    return "OTHER";
  }

  DATA.catalog.forEach((c) => {
    c.book = bookOfCat(c);
  });

  function inferSectionId(smeta) {
    const blob = (smeta.header?.smeta_number || "") + " " + (smeta.id || "");
    const m = blob.match(/(\d+\.\d+)/);
    if (!m) return null;
    return SECTIONS.some((x) => x.id === m[1]) ? m[1] : null;
  }

  DATA.smetas.forEach((s) => {
    if (!s.section) s.section = inferSectionId(s);
  });

  const NAV = [
    { id: "svod", href: "#/svod", title: "Свод", key: "1" },
    { id: "smeta", href: "#/smeta", title: "Смета", key: "2" },
    { id: "catalog", href: "#/catalog", title: "Каталог", key: "3" },
    { id: "import", href: "#/import", title: "Импорт", key: "4" },
    { id: "export", href: "#/export", title: "Выгрузка", key: "5" },
  ];

  const state = {
    view: "svod",
    smetaId: DATA.smetas[0].id,
    sel: null,
    q: "",
    inspector: true,
    palette: false,
    create: false,
    secPick: false,
    catSection: null,
    compose: { q: "", hi: 0, wider: false, focus: false },
    theme: matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light",
  };
  const createForm = { sectionId: "1.1", name: "", num: "" };


  const $ = (id) => document.getElementById(id);
  const money = (n) =>
    n == null || Number.isNaN(n)
      ? "—"
      : new Intl.NumberFormat("ru-RU", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n);
  const money0 = (n) =>
    n == null ? "—" : new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 0 }).format(n);
  const num = (n) => (n == null ? "" : String(n).replace(".", ","));

  function round2(n) {
    return Math.round((Number(n) + (n >= 0 ? 1e-12 : -1e-12)) * 100) / 100;
  }

  function smetaById(id) {
    return DATA.smetas.find((s) => s.id === id) || DATA.smetas[0];
  }

  function allRows(s) {
    const out = [];
    (s.chapters || []).forEach((ch, ci) => {
      ch.rows.forEach((r, ri) => out.push({ ...r, _ci: ci, _ri: ri, _key: ci + ":" + ri }));
    });
    return out;
  }

  function getRow(s, key) {
    if (!key) return null;
    const [ci, ri] = key.split(":").map(Number);
    return s.chapters?.[ci]?.rows?.[ri] || null;
  }

  function kProd(row) {
    return (row.cf || []).reduce((a, c) => a * (c.v || 1), 1);
  }

  function posCost(row) {
    if (row.k !== "Work") return row.calc ?? row.sheet ?? 0;
    const Q = +(row.rate?.q ?? 1) || 1;
    if (row.rate && row.rate.a != null) {
      const A = +row.rate.a || 0;
      const B = +row.rate.b || 0;
      const X = +row.rate.x || 0;
      const interp = /0\.4|0,4/.test(row.fs || "") && row.rate.xmin != null;
      const xeff = interp ? 0.4 * row.rate.xmin + 0.6 * X : X;
      return round2((A + B * xeff) * Q * kProd(row));
    }
    const chain = parseChain(row.fs);
    if (chain) {
      const k = (row.cf || []).reduce((a, c) => a * (c.v || 1), 1);
      return round2(chain.base * (chain.qty || Q) * k);
    }
    return row.calc ?? row.sheet ?? 0;
  }

  function parseChain(fs) {
    if (!fs) return null;
    const parts = String(fs)
      .replace(/,/g, ".")
      .split(/[x×*]/i)
      .map((p) => parseFloat(p.replace(/[^0-9.]+/g, "")))
      .filter((n) => Number.isFinite(n));
    if (parts.length < 2) return null;
    return { base: parts[0], qty: parts[1] };
  }

  function recalc(s) {
    const rows = allRows(s);
    let works = 0;
    rows.forEach((r) => {
      if (r.k === "Work") {
        const c = posCost(r);
        r.calc = c;
        if (r.sheet != null) r.delta = round2(c - r.sheet);
        works = round2(works + c);
      }
    });
    const acc = rows.filter((r) => r.k === "Accrual");
    let run = works;
    acc.forEach((a) => {
      const m = /[×x*]\s*([0-9]+[.,][0-9]+)/i.exec(a.fs || a.name || "");
      const k = m ? parseFloat(m[1].replace(",", ".")) : 1;
      run = round2(run * k);
      a.calc = run;
    });
    const tot = rows.find((r) => r.k === "Total" && /Всего/i.test(r.name || ""));
    if (tot) tot.calc = run;
    s.total_calc = acc.length ? run : works;
    s.n_works = rows.filter((r) => r.k === "Work").length;
    return s.total_calc;
  }

  DATA.smetas.forEach(recalc);

  function toast(msg) {
    const el = $("toast");
    el.textContent = msg;
    el.hidden = false;
    clearTimeout(toast._t);
    toast._t = setTimeout(() => {
      el.hidden = true;
    }, 2400);
  }

  function cleanTitle(s) {
    return String(s || "").replace(/^[«"„“]+|[»"”]+$/g, "").trim();
  }

  function parseHash() {
    const h = (location.hash || "#/svod").replace(/^#\/?/, "");
    const [view, arg] = h.split("/");
    if (NAV.some((n) => n.id === view)) state.view = view;
    else state.view = "svod";
    if (arg) {
      const hit = DATA.smetas.find((s) => s.id === decodeURIComponent(arg) || s.header.smeta_number?.includes(arg));
      if (hit) state.smetaId = hit.id;
    }
  }

  function go(view, smetaId) {
    if (smetaId) state.smetaId = smetaId;
    state.view = view;
    const tail = view === "smeta" ? "/" + encodeURIComponent(state.smetaId) : "";
    history.replaceState(null, "", "#/" + view + tail);
    render();
  }

  function setTheme(t) {
    state.theme = t;
    document.documentElement.setAttribute("data-theme", t);
    const btn = document.querySelector("[data-theme-toggle]");
    if (btn) btn.setAttribute("aria-label", t === "dark" ? "Светлая тема" : "Тёмная тема");
  }

  function renderChrome() {
    $("object-title").textContent = cleanTitle(P.object);
    $("title-chips").innerHTML = [
      chip("Стадия", P.stage === "Р" ? "РД" : P.stage),
      chip("Ктек", num(P.ktek)),
      chip("Кст", num(P.kst)),
      chip("НДС", "22 %"),
      DATA.known_error ? `<span class="chip chip-warn" data-testid="chip-book-error">ошибка книги · ${money(DATA.known_error.delta)}</span>` : "",
    ].join("");

    $("rail-nav").innerHTML = NAV.map(
      (n) =>
        `<a href="${n.href}" data-testid="nav-${n.id}" ${state.view === n.id ? 'aria-current="page"' : ""}>${n.title}<kbd>${n.key}</kbd></a>`
    ).join("");

    $("rail-tree").innerHTML =
      `<div class="rail-label">Локальные сметы<button class="btn" type="button" data-testid="button-new-smeta" id="btn-new-smeta">+ смета</button></div>` +
      DATA.smetas
        .map((s) => {
          const n = (s.header.smeta_number || "").replace("Смета ", "") || s.id;
          return `<button class="tree-item ${s.id === state.smetaId && state.view === "smeta" ? "is-on" : ""}" type="button" data-testid="tree-${cssId(s.id)}" data-open-smeta="${escapeAttr(s.id)}" title="${escapeAttr(s.header.object_name || s.id)}">
            <span class="num">${escapeHtml(n.replace("№",""))}</span>
            <span class="ttl">${escapeHtml(s.header.object_name || s.id)}</span>
            <span class="sum">${money0(s.total_sheet)}</span>
          </button>`;
        })
        .join("");

    const tot = DATA.svod.grand;
    $("status-left").textContent = P.designer + "  ·  " + P.customer;
    $("status-right").innerHTML = `без НДС <b>${money(DATA.svod.total)}</b>  ·  с НДС <b>${money(tot)}</b>`;
  }

  function chip(k, v) {
    return `<span class="chip">${k} <b>${escapeHtml(v)}</b></span>`;
  }

  function render() {
    parseHash();
    renderChrome();
    const toastEl = $("toast");
    if (toastEl && !toast._hold) toastEl.hidden = true;
    const stage = $("stage");
    const map = { svod: viewSvod, smeta: viewSmeta, catalog: viewCatalog, import: viewImport, export: viewExport };
    stage.innerHTML = (map[state.view] || viewSvod)();
    bindStage();
    if (state.compose.focus) {
      const inp = $("compose-q");
      if (inp) {
        inp.value = state.compose.q;
        inp.focus();
      }
      state.compose.focus = false;
    }
  }

  function viewSvod() {
    const max = Math.max(...DATA.svod.rows.filter((r) => r.total > 0 && r.k !== "Total").map((r) => r.total || 0), 1);
    const body = DATA.svod.rows
      .map((r) => {
        if (!r.name && !r.n) return "";
        if (/Корректировка рабочей/.test(r.name || "") || r.n === "1") {
          return `<tr class="sec"><td colspan="5">${escapeHtml(r.name || "Рабочая документация")}</td></tr>`;
        }
        const isTot = /Итого|Всего|НДС/i.test(r.name || "") || r.k === "Total";
        const cls = isTot ? (/НДС/.test(r.name) ? "vat" : "total") : "";
        const open = r.sheet ? ` data-open="${escapeAttr(r.sheet)}" data-testid="svod-${cssId(r.n || r.name)}"` : "";
        const share = r.total && !isTot ? `<div class="bar" aria-hidden="true"><i style="width:${((r.total / max) * 100).toFixed(1)}%"></i></div>` : "";
        const err = r.sheet === DATA.known_error.sheet ? ` <span class="chip chip-warn">формула без «=»</span>` : "";
        return `<tr class="${cls}"${open}>
          <td class="num">${escapeHtml(r.n || "")}</td>
          <td>${escapeHtml(r.name || "")}${err}</td>
          <td class="money">${r.link ? escapeHtml(r.link) : ""}</td>
          <td class="money">${r.design ? money(r.design) : ""}</td>
          <td class="money">${r.total ? money(r.total) : ""}${share}</td>
        </tr>`;
      })
      .join("");
    return `<div class="view"><div class="svod">
      <article class="form-sheet">
        <header class="form-head">
          <div>
            <div class="form-kicker">Форма 1ПС</div>
            <h2>Сводная смета на проектные работы и инженерные изыскания</h2>
          </div>
          <div class="form-side">Всего с НДС 22 %<strong data-testid="text-svod-grand">${money(DATA.svod.grand)}</strong>
            <button class="btn" type="button" data-testid="button-new-from-svod" id="btn-new-from-svod">Новая смета</button>
          </div>
        </header>
        <dl class="form-meta">
          <div><dt>Объект</dt><dd>${escapeHtml(P.object)}</dd></div>
          <div><dt>Генпроектировщик</dt><dd>${escapeHtml(P.designer)}</dd></div>
          <div><dt>Заказчик</dt><dd>${escapeHtml(P.customer)}</dd></div>
        </dl>
        <table class="ledger">
          <thead><tr><th>№</th><th>Перечень работ</th><th>Ссылка</th><th>Проектные</th><th>Всего, руб.</th></tr></thead>
          <tbody>${body}</tbody>
        </table>
        <p class="alert">${escapeHtml(P.note)}</p>
      </article>
    </div></div>`;
  }

  function viewSmeta() {
    const s = smetaById(state.smetaId);
    recalc(s);
    const rows = allRows(s);
    const sel = state.sel && getRow(s, state.sel) ? state.sel : rows.find((r) => r.k === "Work")?._key;
    state.sel = sel;
    const row = getRow(s, sel);
    const d = DIALECT[s.dialect] || { label: s.dialect };

    const isCard = s.dialect === "ASUTP_2016" || s.dialect === "LABOR_3P";
    if (!rows.length && isCard) return cardView(s, d);

    const trs = rows
      .map((r) => {
        const on = r._key === sel ? " is-on" : "";
        const kind = r.k === "Section" ? "sec" : r.k === "Total" ? "tot" : r.k === "Accrual" ? "acc" : "";
        const code = r.ref?.code || "";
        const st = statusDot(r);
        const pills = (r.cf || [])
          .filter((c) => c.v != null)
          .map((c) => `<span class="pill">${escapeHtml(c.id)} ${num(c.v)}</span>`)
          .join("");
        const delta =
          r.k === "Work" && r.delta != null && Math.abs(r.delta) > 0.05
            ? `<div class="delta-bad">${r.delta > 0 ? "+" : ""}${money(r.delta)}</div>`
            : "";
        const warn = (r.warn || []).some((w) => /ОШИБКА/.test(w))
          ? `<span class="chip chip-warn">нет «=»</span>`
          : "";
        return `<tr class="${kind}${on}" data-key="${r._key}" data-testid="row-${r._key}" tabindex="0">
          <td class="c-num">${escapeHtml(r.n || "")}</td>
          <td class="name">${st}${escapeHtml(r.k === "Section" ? r.name : r.name)} ${warn}<div class="cf">${pills}</div></td>
          <td class="c-code">${escapeHtml(code)}</td>
          <td class="c-money">${money(r.calc ?? r.sheet)}${delta}</td>
        </tr>`;
      })
      .join("");

    return `<div class="view"><div class="desk">
      <div class="desk-bar">
        <div>
          <h2 data-testid="text-smeta-title"><span class="sec-badge" data-testid="text-smeta-section">${escapeHtml(sectionOf(s)?.n || "—")} ${escapeHtml(sectionOf(s)?.short || "")}</span><span>${escapeHtml(s.header.smeta_number || "")} · ${escapeHtml(s.header.object_name || "")}</span></h2>
          <div class="meta">${escapeHtml(d.label)} · группа ${escapeHtml(bookLabels(sectionOf(s)))} · ${s.n_works || 0} поз. · лист ${money(s.total_sheet)} · стол ${money(s.total_calc)}</div>
        </div>
        <div style="display:flex;gap:.5rem;align-items:center">
          <label class="search"><input id="smeta-q" data-testid="input-smeta-filter" placeholder="Фильтр по строке" value="${escapeAttr(state.q)}" /></label>
          <button class="btn" type="button" data-testid="button-toggle-inspector" id="btn-ins">${state.inspector ? "Скрыть инспектор" : "Инспектор"}</button>
          <button class="btn btn-primary" type="button" data-testid="button-add-from-catalog" id="btn-add-cat">Вставить расценку</button>
        </div>
      </div>
      ${tapeHtml(row)}
      <div class="desk-body ${state.inspector ? "" : "no-ins"}">
        <div class="grid-wrap">
          <table class="est">
            <thead><tr><th>№</th><th>Характеристика</th><th>Обоснование</th><th>Стоимость</th></tr></thead>
            <tbody id="est-body">${trs || `<tr class="empty-row"><td colspan="4">Нет позиций. Начните ввод в строке ниже — поиск идёт по сборникам раздела.</td></tr>`}</tbody>
          </table>
        </div>
        ${state.inspector ? inspectorHtml(s, row) : ""}
      </div>
      ${composerHtml(s)}
    </div></div>`;
  }

  function cardView(s, d) {
    const isLabor = s.dialect === "LABOR_3P";
    const pack = (DATA.cards && DATA.cards.asutp || []).find((c) => c.id === s.id);
    const extra = isLabor
      ? `<p>Форма 3П. Итог ${money(s.total_sheet)} уходит в свод строкой 1.14. Исполнители и чел.-дни живут на исходном листе; в PIR-IR это карточка, не бланк 2П.</p>`
      : `<p>Карточка СБЦ АСУТП 2016. Стоимость входит в свод. Баллы Ф1–Ф10 не раскладываются в строки 2П — поэтому здесь нет сетки Адепта.</p>${
          (pack && pack.lines || []).map((t) => `<p>${escapeHtml(t)}</p>`).join("")
        }`;
    return `<div class="view"><div class="pane">
      <div class="pane-head"><div><h2>${escapeHtml(s.header.smeta_number || s.id)}</h2>
      <p class="meta" style="color:var(--color-text-muted);margin-top:.35rem;max-width:62ch">${escapeHtml(s.header.object_name || "")} · ${escapeHtml(d.label)}</p></div>
      <strong style="font-family:var(--font-mono);font-size:var(--text-lg)">${money(s.total_sheet)}</strong></div>
      <div class="empty">${extra}</div>
    </div></div>`;
  }

  function statusDot(r) {
    if (r.k !== "Work") return "";
    const st = r.ref?.st;
    const cls = st === "matched" || st === "catalog-miss" ? "st-ok" : st === "partial" ? "st-part" : st ? "st-err" : "st-part";
    return `<i class="st ${cls}" title="${st || "нет ссылки"}"></i>`;
  }

  function tapeHtml(row) {
    if (!row || row.k !== "Work") {
      return `<div class="tape"><div class="tape-empty">Выберите позицию — формула станет уравнением, а не строкой в ячейке.</div></div>`;
    }
    const A = row.rate?.a ?? "";
    const B = row.rate?.b ?? "";
    const X = row.rate?.x ?? "";
    const Q = row.rate?.q ?? 1;
    const ktek = (row.cf || []).find((c) => /ктек/i.test(c.id || ""))?.v ?? P.ktek;
    const kst = (row.cf || []).find((c) => /кст/i.test(c.id || ""))?.v ?? P.kst;
    const extras = (row.cf || [])
      .map((c, i) => {
        if (/ктек|кст/i.test(c.id || "")) return "";
        return `<span class="op">×</span><label class="tok"><span>${escapeHtml(c.id || "К")}</span><input data-tok="cf-${i}" data-testid="input-k-${i}" value="${num(c.v)}" inputmode="decimal" /></label>`;
      })
      .join("");
    const can = row.rate && row.rate.a != null;
    const abx = can
      ? `<span class="op">(</span>
        <label class="tok"><span>A</span><input data-tok="a" data-testid="input-a" value="${num(A)}" inputmode="decimal" /></label>
        <span class="op">+</span>
        <label class="tok"><span>B</span><input data-tok="b" data-testid="input-b" value="${num(B)}" inputmode="decimal" /></label>
        <span class="op">×</span>
        <label class="tok"><span>Xзад</span><input data-tok="x" data-testid="input-x" value="${num(X)}" inputmode="decimal" /></label>
        <span class="op">)</span>
        <span class="op">×</span>
        <label class="tok"><span>Кол</span><input data-tok="q" data-testid="input-q" value="${num(Q)}" inputmode="decimal" /></label>
        <span class="op">×</span>
        <label class="tok"><span>Ктек</span><input data-tok="ktek" data-testid="input-ktek" value="${num(ktek)}" inputmode="decimal" /></label>
        <span class="op">×</span>
        <label class="tok"><span>Кст</span><input data-tok="kst" data-testid="input-kst" value="${num(kst)}" inputmode="decimal" /></label>`
      : `<label class="tok"><span>база</span><input data-tok="base" data-testid="input-base" value="${num(parseChain(row.fs)?.base || row.sheet || "")}" inputmode="decimal" /></label>
        <span class="op">×</span>
        <label class="tok"><span>Кол</span><input data-tok="q" data-testid="input-q" value="${num(parseChain(row.fs)?.qty || Q)}" inputmode="decimal" /></label>`;
    return `<div class="tape" data-testid="tape">
      <div class="tape-eq">
        ${abx}
        ${extras}
      </div>
      <div class="tape-res" data-testid="text-tape-result">= ${money(posCost(row))}<small>${row.sheet != null ? "лист " + money(row.sheet) : "нет контроля листа"}</small></div>
    </div>`;
  }

  function inspectorHtml(s, row) {
    if (!row) return `<aside class="inspector"><div class="empty"><h3>Нет строки</h3><p>Отметьте позицию в бланке.</p></div></aside>`;
    if (row.k !== "Work") {
      return `<aside class="inspector" data-testid="inspector">
        <h3>${row.k === "Accrual" ? "Начисление" : row.k === "Total" ? "Итог" : "Раздел"}</h3>
        <p>${escapeHtml(row.name)}</p>
        <dl class="kv"><dt>Сумма</dt><dd>${money(row.calc ?? row.sheet)}</dd>
        <dt>Формула</dt><dd>${escapeHtml(row.fs || "—")}</dd></dl>
      </aside>`;
    }
    const ref = row.ref || {};
    const rate = row.rate || {};
    const cfs = (row.cf || [])
      .map(
        (c, i) => `<div class="cf-row">
          <input value="${escapeAttr(c.id)}" data-cf-id="${i}" aria-label="Имя коэффициента" />
          <input value="${num(c.v)}" data-cf-v="${i}" inputmode="decimal" aria-label="Значение" />
          <span>${escapeHtml(c.title || c.role || "")}</span>
        </div>`
      )
      .join("");
    const warns = (row.warn || [])
      .filter((w) => !/нет значений переменных|наш движок не посчитал/i.test(w))
      .map((w) => `<p class="alert" style="margin:0 0 .5rem">${escapeHtml(w)}</p>`)
      .join("");
    return `<aside class="inspector" data-testid="inspector">
      <h3>Позиция ${escapeHtml(row.n || "")}</h3>
      <p>${escapeHtml(row.name)}</p>
      ${warns}
      <h3>Ссылка</h3>
      <dl class="kv">
        <dt>Код</dt><dd>${escapeHtml(ref.code || "—")}</dd>
        <dt>Документ</dt><dd>${escapeHtml([ref.kind, ref.num].filter(Boolean).join(" ") || "—")}</dd>
        <dt>Таблица</dt><dd>${escapeHtml(ref.table || "—")} · п. ${escapeHtml(ref.par || "—")}</dd>
        <dt>Статус</dt><dd>${escapeHtml(ref.st || "—")}</dd>
      </dl>
      <h3>Ставки</h3>
      <dl class="kv">
        <dt>A</dt><dd>${money(rate.a)}</dd>
        <dt>B</dt><dd>${money(rate.b)}</dd>
        <dt>Xзад</dt><dd>${rate.x ?? "—"} ${escapeHtml(rate.xu || "")}</dd>
        <dt>Интервал</dt><dd>${rate.xmin ?? "—"} … ${rate.xmax ?? "—"}</dd>
      </dl>
      <h3>Коэффициенты</h3>
      <div class="cf-edit">${cfs || "<p>Нет отдельных коэффициентов.</p>"}</div>
      <h3>Формула листа</h3>
      <p style="font-family:var(--font-mono);font-size:var(--text-xs)">${escapeHtml(row.fs || "—")}</p>
    </aside>`;
  }

  function viewCatalog() {
    const q = state.q.trim().toLowerCase();
    const rows = DATA.catalog.filter((c) => {
      if (state.catSection) {
        const sec = SECTIONS.find((x) => x.id === state.catSection);
        if (sec && !sec.books.includes(c.book)) return false;
      }
      if (!q) return true;
      return [c.code, c.title, c.name, c.num, c.alias].join(" ").toLowerCase().includes(q);
    });
    const list = rows
      .map(
        (c) => `<button class="cat-row" type="button" data-code="${escapeAttr(c.code)}" data-testid="cat-${cssId(c.code)}">
          <code>${escapeHtml(c.code)}</code>
          <span class="n">${escapeHtml(c.name || c.title || "")}</span>
          <span class="m">${c.a != null ? "A " + money0(c.a) : ""}</span>
          <span class="m">${c.b != null ? "B " + money0(c.b) : ""}</span>
          <span class="m">${c.uses || ""}×</span>
          <span class="btn">В смету</span>
        </button>`
      )
      .join("");
    return `<div class="view"><div class="pane">
      <div class="pane-head">
        <div>
          <h2>Каталог обоснований</h2>
          <p style="color:var(--color-text-muted);font-size:var(--text-xs);margin-top:.35rem">${DATA.catalog.length} ссылок из книги Степновской СПХГ. Ставки A/B есть у выгрузки 707/пр.</p>
        </div>
        <label class="search"><input id="cat-q" data-testid="input-catalog" placeholder="НЗ, СБЦ, таблица" value="${escapeAttr(state.q)}" /></label>
      </div>
      <div class="cat-secs" data-testid="catalog-sections">${catalogSectionChips()}</div>
      <div class="cat-list">${list || '<div class="empty"><h3>Ничего не найдено</h3><p>Смените запрос или снимите фильтр.</p></div>'}</div>
    </div></div>`;
  }

  function viewImport() {
    const groups = {};
    DATA.smetas.forEach((s) => {
      groups[s.dialect] = groups[s.dialect] || { n: 0, sum: 0, sheets: [] };
      groups[s.dialect].n += 1;
      groups[s.dialect].sum += s.total_sheet || 0;
      groups[s.dialect].sheets.push(s.header.object_name || s.id);
    });
    const cards = Object.entries(groups)
      .map(([k, g]) => {
        const d = DIALECT[k] || { label: k, hint: "" };
        return `<article class="dialect">
          <h3>${escapeHtml(d.label)}</h3>
          <p>${escapeHtml(d.hint)} Листы: ${escapeHtml(g.sheets.join(", "))}.</p>
          <div class="cnt">${g.n}<small style="display:block;font-size:var(--text-xs);color:var(--color-text-muted)">${money0(g.sum)}</small></div>
        </article>`;
      })
      .join("");
    return `<div class="view"><div class="pane">
      <div class="pane-head">
        <div>
          <h2>Импорт книги</h2>
          <p style="color:var(--color-text-muted);font-size:var(--text-xs);margin-top:.35rem">Распознаватель читает шапку, а не фиксированные колонки. Пять диалектов одной книги — это как раз то, чего нет у Адепта и Гранда.</p>
        </div>
        <button class="btn btn-primary" type="button" data-testid="button-reimport" id="btn-reimport">Повторить разбор V3.2</button>
      </div>
      <div class="dialects">${cards}</div>
      <h3 style="margin:1.5rem 0 .75rem;font-size:var(--text-sm)">Карта колонок 2П · 707/пр</h3>
      <table class="map-table">
        <thead><tr><th>Поле канона</th><th>Как ищем</th><th>Зачем HTML</th></tr></thead>
        <tbody>
          <tr><td>number</td><td>подпись «№ п/п»</td><td>якорь строки, не координата Excel</td></tr>
          <tr><td>name</td><td>«Характеристика предприятия»</td><td>полный текст, без обрезки под ширину окна Адепта</td></tr>
          <tr><td>justification</td><td>«Номер частей, глав, таблиц»</td><td>разбор в код NZ-849-3.8-10</td></tr>
          <tr><td>calc</td><td>«Расчет стоимости»</td><td>символьная и числовая формула рядом</td></tr>
          <tr><td>cost</td><td>«Стоимость, руб»</td><td>контроль листа против движка</td></tr>
        </tbody>
      </table>
    </div></div>`;
  }

  function viewExport() {
    const s = smetaById(state.smetaId);
    const works = allRows(s).filter((r) => r.k === "Work");
    const preview = works
      .slice(0, 8)
      .map(
        (r) => `<tr>
          <td>${escapeHtml(r.n)}</td>
          <td>${escapeHtml(r.name)}</td>
          <td>${escapeHtml(r.ref?.code || r.just || "")}</td>
          <td>${escapeHtml(r.fs || "")}</td>
          <td class="money" style="text-align:right;font-family:var(--font-mono)">${money(r.calc)}</td>
        </tr>`
      )
      .join("");
    return `<div class="view"><div class="pane">
      <div class="pane-head">
        <div>
          <h2>Выгрузка для «Адепт: Проект»</h2>
          <p style="color:var(--color-text-muted);font-size:var(--text-xs);margin-top:.35rem">Машинного импорта смет ПИР в модуле ПРОЕКТ нет. Канал — бланк 2П в Excel и буфер. GUID чужих XML не подменяем.</p>
        </div>
        <div style="display:flex;gap:.5rem">
          <button class="btn" type="button" data-testid="button-copy-tsv" id="btn-copy">Копировать TSV</button>
          <button class="btn btn-primary" type="button" data-testid="button-export-json" id="btn-json">Скачать PIR-IR</button>
        </div>
      </div>
      <table class="map-table">
        <thead><tr><th>№</th><th>Характеристика</th><th>Обоснование</th><th>Расчёт</th><th>Стоимость</th></tr></thead>
        <tbody>${preview}</tbody>
      </table>
    </div></div>`;
  }


  function sectionOf(s) {
    return SECTIONS.find((x) => x.id === s.section) || null;
  }

  function bookLabels(sec) {
    if (!sec) return "—";
    return sec.books.map((id) => BOOKS[id]?.short || id).join(" · ");
  }

  function catalogSectionChips() {
    const all = `<button type="button" data-cat-sec="" class="${state.catSection ? "" : "is-on"}" data-testid="cat-sec-all">Все</button>`;
    return (
      all +
      SECTIONS.map((sec) => {
        const n = DATA.catalog.filter((c) => sec.books.includes(c.book)).length;
        if (!n) return "";
        return `<button type="button" data-cat-sec="${sec.id}" class="${state.catSection === sec.id ? "is-on" : ""}" data-testid="cat-sec-${cssId(sec.id)}">${escapeHtml(sec.n)} ${escapeHtml(sec.short)}</button>`;
      }).join("")
    );
  }

  function poolFor(s) {
    if (state.compose.wider) return DATA.catalog;
    const sec = sectionOf(s);
    if (!sec) return DATA.catalog;
    const hits = DATA.catalog.filter((c) => sec.books.includes(c.book));
    return hits.length ? hits : DATA.catalog;
  }

  function searchRates(s, q) {
    const pool = poolFor(s);
    const qq = q.trim().toLowerCase().replace(/\s+/g, " ");
    if (!qq) {
      return [...pool].sort((a, b) => (b.uses || 0) - (a.uses || 0)).slice(0, 8);
    }
    const compact = qq.replace(/\s/g, "");
    const scored = [];
    pool.forEach((c) => {
      const code = (c.code || "").toLowerCase();
      const name = (c.name || "").toLowerCase();
      const title = (c.title || "").toLowerCase();
      const alias = (c.alias || "").toLowerCase();
      const blob = [c.table, c.par, c.num].join(" ").toLowerCase();
      let sc = 0;
      if (code === compact || code === qq) sc = 100;
      else if (code.startsWith(compact) || code.includes(compact)) sc = 80;
      else if (name.startsWith(qq)) sc = 70;
      else if (name.includes(qq)) sc = 50;
      else if (alias.includes(qq) || title.includes(qq)) sc = 35;
      else if (blob.includes(qq) || blob.includes(compact)) sc = 20;
      if (sc) scored.push({ c, sc: sc + Math.min(c.uses || 0, 8) });
    });
    scored.sort((a, b) => b.sc - a.sc);
    return scored.slice(0, 8).map((x) => x.c);
  }

  function composerHtml(s) {
    const sec = sectionOf(s);
    const labels = bookLabels(sec);
    const ph = state.compose.wider ? "Искать во всех сборниках книги" : "Искать в " + labels;
    return `<div class="composer" data-testid="composer">
      <button class="composer-sec" type="button" id="btn-sec" data-testid="button-smeta-section" aria-expanded="${state.secPick ? "true" : "false"}" title="Сменить раздел сметы">
        <span class="n">${escapeHtml(sec?.n || "—")}</span>
        <span>${escapeHtml(sec?.short || "раздел")}</span>
      </button>
      <div class="composer-field">
        <input id="compose-q" data-testid="input-compose" autocomplete="off" placeholder="${escapeAttr(ph)}" value="${escapeAttr(state.compose.q)}" />
        <div class="compose-hits" id="compose-hits" hidden role="listbox"></div>
      </div>
      <button class="btn ${state.compose.wider ? "is-on" : ""}" type="button" id="btn-wider" data-testid="button-wider">${state.compose.wider ? "Группа раздела" : "Все сборники"}</button>
      ${state.secPick ? secPopHtml(s) : ""}
    </div>`;
  }

  function secPopHtml(s) {
    return `<div class="sec-pop" id="sec-pop" role="listbox" data-testid="section-pop">
      <div class="sec-pop-head">Раздел задаёт группу справочников</div>
      ${SECTIONS.map((sec) => {
        const on = sec.id === s.section ? "is-on" : "";
        return `<button type="button" data-assign-sec="${sec.id}" class="${on}">
          <span class="n">${escapeHtml(sec.n)}</span>
          <span>${escapeHtml(sec.name)}</span>
          <small>${escapeHtml(bookLabels(sec))}</small>
        </button>`;
      }).join("")}
    </div>`;
  }

  function drawHits() {
    const box = $("compose-hits");
    const inp = $("compose-q");
    if (!box || !inp) return;
    const s = smetaById(state.smetaId);
    const q = inp.value;
    state.compose.q = q;
    const hits = searchRates(s, q);
    state.compose._hits = hits;
    if (state.compose.hi >= hits.length) state.compose.hi = Math.max(0, hits.length - 1);
    if (!hits.length) {
      box.hidden = false;
      box.innerHTML = `<div class="compose-empty">${state.compose.wider ? "Нет совпадений в каталоге книги." : "В группе раздела нет совпадений. Откройте «Все сборники» или смените раздел."}</div>`;
      return;
    }
    box.hidden = false;
    box.innerHTML = hits
      .map((c, i) => {
        const bk = BOOKS[c.book]?.short || c.alias || c.kind || "";
        return `<button type="button" class="compose-hit ${i === state.compose.hi ? "is-on" : ""}" role="option" data-hit="${i}" data-testid="hit-${cssId(c.code)}">
          <code>${escapeHtml(c.code)}</code>
          <span class="nm">${escapeHtml(c.name || c.title || "")}</span>
          <span class="bk">${escapeHtml(bk)}</span>
        </button>`;
      })
      .join("");
    box.querySelectorAll("[data-hit]").forEach((btn) => {
      btn.addEventListener("mousedown", (e) => e.preventDefault());
      btn.addEventListener("click", () => {
        const hit = hits[+btn.getAttribute("data-hit")];
        if (hit) insertRate(hit.code);
      });
    });
  }

  function bindComposer() {
    const inp = $("compose-q");
    if (!inp) return;
    inp.addEventListener("input", () => {
      state.compose.hi = 0;
      drawHits();
    });
    inp.addEventListener("focus", () => drawHits());
    inp.addEventListener("keydown", (e) => {
      const hits = state.compose._hits || [];
      if (e.key === "ArrowDown") {
        e.preventDefault();
        state.compose.hi = Math.min(hits.length - 1, state.compose.hi + 1);
        drawHits();
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        state.compose.hi = Math.max(0, state.compose.hi - 1);
        drawHits();
      } else if (e.key === "Enter") {
        e.preventDefault();
        const hit = hits[state.compose.hi];
        if (hit) insertRate(hit.code);
      } else if (e.key === "Escape") {
        $("compose-hits") && ($("compose-hits").hidden = true);
        state.secPick = false;
        const pop = $("sec-pop");
        if (pop) pop.remove();
      }
    });
    inp.addEventListener("blur", () => {
      setTimeout(() => {
        const box = $("compose-hits");
        if (box) box.hidden = true;
      }, 180);
    });
    $("btn-wider")?.addEventListener("click", () => {
      state.compose.wider = !state.compose.wider;
      state.compose.focus = true;
      render();
    });
    $("btn-sec")?.addEventListener("click", () => {
      state.secPick = !state.secPick;
      render();
      state.compose.focus = false;
    });
    $("stage").querySelectorAll("[data-assign-sec]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const s = smetaById(state.smetaId);
        s.section = btn.getAttribute("data-assign-sec");
        state.secPick = false;
        state.compose.wider = false;
        render();
        toast("Раздел " + s.section + " · " + bookLabels(sectionOf(s)));
      });
    });
  }

  function nextNumber(sectionId) {
    const nums = DATA.smetas
      .filter((s) => s.section === sectionId)
      .map((s) => {
        const m = ((s.header.smeta_number || "") + " " + s.id).match(/(\d+\.\d+(?:\.\d+)?)/);
        return m ? m[1] : "";
      })
      .filter(Boolean);
    if (!nums.length) return sectionId;
    const suffixes = nums.map((n) => {
      const parts = n.split(".");
      return parts.length >= 3 ? +parts[2] : 1;
    });
    return sectionId + "." + (Math.max(...suffixes, 0) + 1);
  }

  function drawCreate() {
    const box = $("create-secs");
    if (!box) return;
    box.innerHTML = SECTIONS.map((sec) => {
      const on = sec.id === createForm.sectionId ? "is-on" : "";
      const n = DATA.catalog.filter((c) => sec.books.includes(c.book)).length;
      return `<button type="button" class="sec-row ${on}" role="option" data-create-sec="${sec.id}" data-testid="create-section-${cssId(sec.id)}">
        <span class="n">${escapeHtml(sec.n)}</span>
        <span class="ttl">${escapeHtml(sec.name)}</span>
        <span class="books">${escapeHtml(bookLabels(sec))}${n ? " · " + n + " расц." : ""}</span>
      </button>`;
    }).join("");
    box.querySelectorAll("[data-create-sec]").forEach((btn) => {
      btn.addEventListener("click", () => {
        createForm.sectionId = btn.getAttribute("data-create-sec");
        const sec = SECTIONS.find((x) => x.id === createForm.sectionId);
        createForm.num = nextNumber(createForm.sectionId);
        if (!createForm.name || SECTIONS.some((x) => x.name === createForm.name)) createForm.name = sec?.name || "";
        $("create-num").value = createForm.num;
        $("create-name").value = createForm.name;
        drawCreate();
      });
    });
  }

  function openCreate() {
    state.create = true;
    const sec = SECTIONS.find((x) => x.id === createForm.sectionId) || SECTIONS[0];
    createForm.sectionId = sec.id;
    createForm.num = nextNumber(sec.id);
    createForm.name = sec.name;
    $("create").hidden = false;
    $("create-num").value = createForm.num;
    $("create-name").value = createForm.name;
    drawCreate();
    $("create-name").focus();
    $("create-name").select();
  }

  function closeCreate() {
    state.create = false;
    $("create").hidden = true;
  }

  function commitCreate() {
    const sec = SECTIONS.find((x) => x.id === createForm.sectionId);
    if (!sec) return;
    const num = ($("create-num").value || nextNumber(sec.id)).trim();
    const name = ($("create-name").value || sec.name).trim();
    let sid = num + " " + sec.short;
    let i = 2;
    while (DATA.smetas.some((x) => x.id === sid)) {
      sid = num + " " + sec.short + " " + i;
      i += 1;
    }
    const isCard = sec.dialect === "ASUTP_2016" || sec.dialect === "LABOR_3P";
    const smeta = {
      id: sid,
      dialect: sec.dialect,
      section: sec.id,
      header: { smeta_number: "Смета №" + num, object_name: name, stage: P.stage, code: P.code },
      total_sheet: 0,
      total_calc: 0,
      n_works: 0,
      warnings: [],
      chapters: isCard ? [] : [{ cap: sec.name, rows: [] }],
    };
    DATA.smetas.push(smeta);
    closeCreate();
    state.compose.focus = !isCard;
    go("smeta", smeta.id);
    toast("Создана " + smeta.header.smeta_number + " · раздел " + sec.n + " " + sec.short);
  }

  function bindStage() {
    $("stage").querySelectorAll("[data-open]").forEach((el) => {
      el.addEventListener("click", () => go("smeta", el.getAttribute("data-open")));
    });
    document.querySelectorAll("[data-open-smeta]").forEach((el) => {
      el.onclick = () => go("smeta", el.getAttribute("data-open-smeta"));
    });

    const body = $("est-body");
    if (body) {
      body.querySelectorAll("tr[data-key]").forEach((tr) => {
        tr.addEventListener("click", () => {
          state.sel = tr.getAttribute("data-key");
          render();
        });
      });
    }

    $("stage").querySelectorAll("[data-tok]").forEach((inp) => {
      inp.addEventListener("change", () => {
        const tok = inp.getAttribute("data-tok");
        const val = inp.value;
        applyTok(tok, val);
      });
    });

    const fq = $("smeta-q");
    if (fq) {
      fq.addEventListener("input", () => {
        const q = fq.value.trim().toLowerCase();
        $("est-body")?.querySelectorAll("tr[data-key]").forEach((tr) => {
          tr.style.display = !q || tr.textContent.toLowerCase().includes(q) ? "" : "none";
        });
      });
    }
    const cq = $("cat-q");
    if (cq) {
      cq.addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
          state.q = cq.value;
          render();
        }
      });
    }

    $("btn-ins")?.addEventListener("click", () => {
      state.inspector = !state.inspector;
      render();
    });
    $("btn-add-cat")?.addEventListener("click", () => {
      const inp = $("compose-q");
      if (inp) {
        inp.focus();
        drawHits();
        return;
      }
      state.q = "";
      go("catalog");
    });
    $("btn-new-smeta")?.addEventListener("click", openCreate);
    $("btn-new-from-svod")?.addEventListener("click", openCreate);
    bindComposer();
    $("stage").querySelectorAll("[data-cat-sec]").forEach((btn) => {
      btn.addEventListener("click", () => {
        const id = btn.getAttribute("data-cat-sec");
        state.catSection = id || null;
        render();
      });
    });
    $("stage").querySelectorAll("[data-code]").forEach((btn) => {
      btn.addEventListener("click", () => insertRate(btn.getAttribute("data-code")));
    });
    $("btn-reimport")?.addEventListener("click", () => {
      toast("Книга V3.2 уже разобрана: 15 листов, 5 диалектов, 0 неразобранных обоснований.");
    });
    $("btn-copy")?.addEventListener("click", copyTsv);
    $("btn-json")?.addEventListener("click", downloadJson);

    $("stage").querySelectorAll("[data-cf-v]").forEach((inp) => {
      inp.addEventListener("change", () => {
        const s = smetaById(state.smetaId);
        const row = getRow(s, state.sel);
        const i = +inp.getAttribute("data-cf-v");
        if (row?.cf?.[i]) row.cf[i].v = parseRu(inp.value);
        recalc(s);
        render();
      });
    });
  }

  function applyTok(tok, raw) {
    const s = smetaById(state.smetaId);
    const row = getRow(s, state.sel);
    if (!row) return;
    row.rate = row.rate || {};
    const v = parseRu(raw);
    if (tok === "a") row.rate.a = v;
    if (tok === "b") row.rate.b = v;
    if (tok === "x") row.rate.x = v;
    if (tok === "q") row.rate.q = v;
    if (tok === "ktek") upsertCf(row, "Ктек", v, "индекс");
    if (tok === "kst") upsertCf(row, "Кст", v, "стадийность");
    if (tok === "base") {
      const qty = parseChain(row.fs)?.qty || row.rate?.q || 1;
      row.fs = v + "x" + qty;
    }
    if (tok.startsWith("cf-")) {
      const i = +tok.slice(3);
      if (row.cf?.[i]) row.cf[i].v = v;
    }
    recalc(s);
    const msg = "Пересчитано: " + money(posCost(row));
    setTimeout(() => {
      render();
      toast(msg);
    }, 0);
  }

  function upsertCf(row, id, v, role) {
    row.cf = row.cf || [];
    const hit = row.cf.find((c) => c.id === id);
    if (hit) hit.v = v;
    else row.cf.push({ id, v, role, title: id, basis: "" });
  }

  function parseRu(s) {
    const n = parseFloat(String(s).replace(/\s/g, "").replace(",", "."));
    return Number.isFinite(n) ? n : 0;
  }

  function insertRate(code) {
    const cat = DATA.catalog.find((c) => c.code === code);
    if (!cat) return;
    const s = smetaById(state.smetaId);
    if (!s.chapters.length) s.chapters.push({ cap: "Добавлено", rows: [] });
    const ch = s.chapters[0];
    const n = String(ch.rows.filter((r) => r.k === "Work").length + 1);
    const row = {
      i: 900 + ch.rows.length,
      n,
      k: "Work",
      name: cat.name || cat.title,
      just: cat.code,
      fs: "(A + B * Xзад) * Количество * Ктек * Кст",
      sheet: null,
      calc: null,
      ref: { code: cat.code, kind: cat.kind, num: cat.num, title: cat.title, table: cat.table, par: cat.par, alias: cat.alias, st: "matched" },
      rate: { a: cat.a, b: cat.b, x: cat.xmin || 1, xmin: cat.xmin, xmax: cat.xmax, q: 1, xu: cat.xu, orig: "catalog" },
      cf: [
        { id: "Ктек", v: P.ktek, role: "индекс", title: "Индекс", basis: P.ktek_basis },
        { id: "Кст", v: P.kst, role: "стадийность", title: "Стадия РД", basis: P.kst_basis },
      ],
      warn: [],
    };
    const firstWork = ch.rows.findIndex((r) => r.k === "Total" || r.k === "Accrual");
    if (firstWork >= 0) ch.rows.splice(firstWork, 0, row);
    else ch.rows.push(row);
    state.sel = "0:" + ch.rows.indexOf(row);
    recalc(s);
    state.compose.q = "";
    state.compose.hi = 0;
    state.compose.focus = true;
    go("smeta", s.id);
    toast("Вставлена " + cat.code);
  }

  function copyTsv() {
    const s = smetaById(state.smetaId);
    const lines = [["№", "Наименование", "Обоснование", "Формула", "Стоимость"].join("\t")];
    allRows(s)
      .filter((r) => r.k === "Work")
      .forEach((r) => lines.push([r.n, r.name, r.ref?.code || "", r.fs || "", String(r.calc ?? "")].join("\t")));
    const text = lines.join("\n");
    if (navigator.clipboard?.writeText) navigator.clipboard.writeText(text).then(() => toast("TSV в буфере — для вставки в Excel"));
    else toast("Буфер недоступен в этой среде");
  }

  function downloadJson() {
    const s = smetaById(state.smetaId);
    const blob = new Blob([JSON.stringify({ project: P, smeta: s }, null, 2)], { type: "application/json" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = "pir-ir-" + (s.header.smeta_number || "smeta").replace(/\s+/g, "_") + ".json";
    a.click();
    toast("PIR-IR сохранён");
  }

  function paletteItems(q) {
    const qq = q.trim().toLowerCase();
    const items = [
      { t: "Новая смета", s: "создать", run: () => openCreate() },
      ...NAV.map((n) => ({ t: n.title, s: "раздел", run: () => go(n.id) })),
      ...DATA.smetas.map((s) => ({
        t: (s.header.smeta_number || "") + " " + (s.header.object_name || ""),
        s: money(s.total_sheet),
        run: () => go("smeta", s.id),
      })),
      ...DATA.catalog.slice(0, 40).map((c) => ({
        t: c.code + " · " + (c.name || c.title || ""),
        s: "каталог",
        run: () => insertRate(c.code),
      })),
    ];
    return qq ? items.filter((i) => (i.t + i.s).toLowerCase().includes(qq)) : items.slice(0, 18);
  }

  function openPalette() {
    state.palette = true;
    $("palette").hidden = false;
    $("palette-q").value = "";
    drawPalette();
    $("palette-q").focus();
  }
  function closePalette() {
    state.palette = false;
    $("palette").hidden = true;
  }
  function drawPalette() {
    const items = paletteItems($("palette-q").value);
    $("palette-list").innerHTML = items
      .map(
        (it, i) =>
          `<button type="button" role="option" data-pi="${i}" class="${i === 0 ? "is-on" : ""}">${escapeHtml(it.t)}<small>${escapeHtml(it.s)}</small></button>`
      )
      .join("");
    $("palette-list").querySelectorAll("button").forEach((b) => {
      b.addEventListener("click", () => {
        items[+b.getAttribute("data-pi")].run();
        closePalette();
      });
    });
    drawPalette._items = items;
  }

  function escapeHtml(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }
  function escapeAttr(s) {
    return escapeHtml(s).replace(/'/g, "&#39;");
  }
  function cssId(s) {
    return String(s).replace(/[^a-zA-Z0-9а-яА-Я_-]+/g, "-");
  }

  document.querySelector("[data-theme-toggle]").addEventListener("click", () => {
    setTheme(state.theme === "dark" ? "light" : "dark");
  });
  $("btn-palette").addEventListener("click", openPalette);
  $("create").addEventListener("click", (e) => {
    if (e.target.id === "create") closeCreate();
  });
  $("btn-create-go").addEventListener("click", commitCreate);
  $("create-name").addEventListener("keydown", (e) => {
    if (e.key === "Enter") commitCreate();
  });
  $("create-num").addEventListener("keydown", (e) => {
    if (e.key === "Enter") commitCreate();
  });
  $("palette").addEventListener("click", (e) => {
    if (e.target.id === "palette") closePalette();
  });
  $("palette-q").addEventListener("input", drawPalette);
  $("palette-q").addEventListener("keydown", (e) => {
    if (e.key === "Escape") closePalette();
    if (e.key === "Enter") {
      const it = drawPalette._items?.[0];
      if (it) {
        it.run();
        closePalette();
      }
    }
  });

  window.addEventListener("hashchange", render);
  window.addEventListener("keydown", (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
      e.preventDefault();
      state.palette ? closePalette() : openPalette();
      return;
    }
    if (e.key === "Escape" && state.palette) closePalette();
    if (e.key === "Escape" && state.create) {
      closeCreate();
      return;
    }
    if (e.target.matches("input, textarea")) return;
    if (e.key.toLowerCase() === "n" && !e.ctrlKey) {
      e.preventDefault();
      openCreate();
      return;
    }
    const n = NAV.find((x) => x.key === e.key);
    if (n && !e.ctrlKey) {
      e.preventDefault();
      go(n.id);
    }
    if (state.view === "smeta" && (e.key === "ArrowDown" || e.key === "ArrowUp")) {
      const s = smetaById(state.smetaId);
      const rows = allRows(s);
      const i = rows.findIndex((r) => r._key === state.sel);
      const j = e.key === "ArrowDown" ? Math.min(rows.length - 1, i + 1) : Math.max(0, i - 1);
      if (rows[j]) {
        state.sel = rows[j]._key;
        render();
        e.preventDefault();
      }
    }
  });

  setTheme(state.theme);
  parseHash();
  render();
})();
