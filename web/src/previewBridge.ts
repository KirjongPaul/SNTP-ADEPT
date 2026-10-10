export const estimateMark = "sntp-adept-estimate/v1";
export const rateMark = "sntp-adept-rate/v1";
export const previewChannelName = "sntp-preview";

export type EstimateDrag = {
  sourceProjectId: string;
  sourceEstimateId: string;
  title?: string;
  projectName?: string;
};

export type RateDrag = {
  sourceProjectId: string;
  sourceEstimateId: string;
  sourcePositionId: string;
  name?: string;
};

export type PreviewCommand =
  | { type: "copy-estimate"; payload: EstimateDrag }
  | { type: "copy-rate"; payload: RateDrag };

export function estimateDragText(payload: EstimateDrag) {
  return `${estimateMark}\n${JSON.stringify(payload)}`;
}

export function rateDragText(payload: RateDrag) {
  return `${rateMark}\n${JSON.stringify(payload)}`;
}

export function readMarked<T>(text: string, mark: string): T | null {
  const raw = text.trim();
  if (!raw.startsWith(mark)) return null;
  const json = raw.split("\n")[1];
  if (!json) return null;
  try {
    return JSON.parse(json) as T;
  } catch {
    return null;
  }
}

export function putDrag(data: DataTransfer, text: string) {
  data.setData("text/plain", text);
  data.effectAllowed = "copy";
}

export function sendPreview(command: PreviewCommand) {
  const channel = new BroadcastChannel(previewChannelName);
  channel.postMessage(command);
  channel.close();
}

export function listenPreview(onCommand: (command: PreviewCommand) => void) {
  const channel = new BroadcastChannel(previewChannelName);
  channel.onmessage = (ev: MessageEvent<PreviewCommand>) => {
    if (ev.data?.type === "copy-estimate" || ev.data?.type === "copy-rate") onCommand(ev.data);
  };
  return () => channel.close();
}
