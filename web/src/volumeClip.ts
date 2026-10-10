export const volumeClipMark = "sntp-adept-volume/v1";

export type VolumeClip = {
  bookId: string;
  table: string;
  item: string;
  name: string;
  unit?: string;
  xmin?: number;
  xmax?: number;
  a?: number;
  b?: number;
};

export function volumeClipText(clip: VolumeClip) {
  const band = [clip.unit, clip.xmin != null || clip.xmax != null ? `${clip.xmin ?? "—"}–${clip.xmax ?? "—"}` : "", clip.a != null ? `а=${clip.a}` : "", clip.b != null ? `в=${clip.b}` : ""]
    .filter(Boolean)
    .join(" ");
  return `${volumeClipMark}\n${JSON.stringify(clip)}\n${clip.name}\nтабл. ${clip.table} п. ${clip.item} ${band}`;
}

export function isVolumeDrag(types: ArrayLike<string>) {
  for (let i = 0; i < types.length; i++) if (types[i] === "application/x-sntp-volume") return true;
  return false;
}

export function clipFromTransfer(data: DataTransfer): VolumeClip | null {
  return readVolumeClip(data.getData("application/x-sntp-volume") || data.getData("text/plain"));
}

export function readVolumeClip(text: string): VolumeClip | null {
  const raw = text.trim();
  if (!raw.startsWith(volumeClipMark)) return null;
  const json = raw.split("\n")[1];
  if (!json) return null;
  try {
    const clip = JSON.parse(json) as VolumeClip;
    if (!clip.bookId || !clip.table || !clip.item) return null;
    return clip;
  } catch {
    return null;
  }
}
