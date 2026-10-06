import React from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./styles.css";
import "./series.css";

// The pages remember and restore their own scroll position (see pageMemory.ts). The browser's automatic restoration runs at the
// wrong moment for a page that is rebuilt on every route change, and would override ours.
history.scrollRestoration = "manual";

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
