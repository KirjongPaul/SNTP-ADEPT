import React from "react";
import ReactDOM from "react-dom/client";
import { App } from "./App";
import { PreviewDesk } from "./PreviewDesk";
import "./styles.css";

const params = new URLSearchParams(location.search);
const preview = params.get("preview") === "1";
document.documentElement.dataset.window = preview ? "preview" : "work";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    {preview ? (
      <PreviewDesk
        detached
        focus={params.get("project") ? { projectId: params.get("project")!, estimateId: params.get("estimate") || undefined } : null}
      />
    ) : (
      <App />
    )}
  </React.StrictMode>
);
