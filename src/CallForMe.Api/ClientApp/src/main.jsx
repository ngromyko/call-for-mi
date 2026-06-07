import React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App.jsx";
import { I18nProvider } from "./i18n/I18nContext.jsx";

const root = createRoot(document.getElementById("root"));
root.render(
  <I18nProvider>
    <App />
  </I18nProvider>
);
