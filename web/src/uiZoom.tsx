import { useEffect, useState } from "react";

const key = "sntp.zoom";
const min = 80;
const max = 200;
const step = 10;

function clamp(percent: number) {
  return Math.min(max, Math.max(min, percent));
}

export function readZoom() {
  try {
    const n = Number(localStorage.getItem(key));
    if (Number.isFinite(n)) return clamp(Math.round(n / step) * step);
  } catch {
    /* настройки окна недоступны */
  }
  return 100;
}

function applyZoom(percent: number) {
  document.documentElement.style.setProperty("--ui-zoom", String(percent / 100));
}

export function useUiZoom(enabled = true) {
  const [zoom, setZoom] = useState(() => {
    const value = readZoom();
    if (enabled) applyZoom(value);
    return value;
  });

  useEffect(() => {
    if (!enabled) return;
    applyZoom(zoom);
    try {
      localStorage.setItem(key, String(zoom));
    } catch {
      /* остаётся на это окно */
    }
  }, [zoom, enabled]);

  useEffect(() => {
    if (!enabled) return;
    const onStorage = (e: StorageEvent) => {
      if (e.key === key) setZoom(readZoom());
    };
    let acc = 0;
    const onWheel = (e: WheelEvent) => {
      if (!e.ctrlKey) return;
      e.preventDefault();
      e.stopPropagation();
      const px = e.deltaMode === 1 ? e.deltaY * 16 : e.deltaMode === 2 ? e.deltaY * window.innerHeight : e.deltaY;
      acc += px;
      let steps = 0;
      while (acc >= 80) { steps += 1; acc -= 80; }
      while (acc <= -80) { steps -= 1; acc += 80; }
      if (!steps) return;
      setZoom((cur) => clamp(cur - steps * step));
    };
    window.addEventListener("storage", onStorage);
    window.addEventListener("wheel", onWheel, { passive: false, capture: true });
    return () => {
      window.removeEventListener("storage", onStorage);
      window.removeEventListener("wheel", onWheel, { capture: true });
    };
  }, [enabled]);

  return [zoom, setZoom] as const;
}

export function ZoomControl({ zoom, onZoom }: { zoom: number; onZoom: (next: number) => void }) {
  return (
    <span className="status-zoom">
      <button type="button" aria-label="Уменьшить масштаб" title="Мельче" disabled={zoom <= min} onClick={() => onZoom(clamp(zoom - step))}>−</button>
      <button type="button" className="status-zoom-read" title="Щелчок возвращает 100 %" onClick={() => onZoom(100)}>
        Масштаб <b>{zoom} %</b>
      </button>
      <button type="button" aria-label="Увеличить масштаб" title="Крупнее" disabled={zoom >= max} onClick={() => onZoom(clamp(zoom + step))}>+</button>
    </span>
  );
}
