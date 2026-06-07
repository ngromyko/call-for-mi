import { createContext, useContext, useMemo, useState } from "react";
import { availableLocales, messages } from "./locales.js";

const STORAGE_KEY = "callforme_locale";
const fallbackLocale = "ru";

const I18nContext = createContext(null);

function getInitialLocale() {
  const saved = localStorage.getItem(STORAGE_KEY);
  if (saved && messages[saved]) return saved;

  const browser = navigator.language?.slice(0, 2).toLowerCase();
  return messages[browser] ? browser : fallbackLocale;
}

function resolveMessage(locale, key) {
  const parts = key.split(".");
  let current = messages[locale];
  for (const part of parts) {
    current = current?.[part];
  }

  if (typeof current === "string") return current;
  if (locale !== fallbackLocale) return resolveMessage(fallbackLocale, key);
  return key;
}

function interpolate(template, values = {}) {
  return template.replace(/\{\{(\w+)\}\}/g, (_, key) => values[key] ?? "");
}

export function I18nProvider({ children }) {
  const [locale, setLocaleState] = useState(getInitialLocale);

  const value = useMemo(() => {
    const setLocale = nextLocale => {
      const safeLocale = messages[nextLocale] ? nextLocale : fallbackLocale;
      localStorage.setItem(STORAGE_KEY, safeLocale);
      document.documentElement.lang = safeLocale;
      setLocaleState(safeLocale);
    };

    return {
      locale,
      locales: availableLocales,
      setLocale,
      t: (key, values) => interpolate(resolveMessage(locale, key), values)
    };
  }, [locale]);

  document.documentElement.lang = locale;
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  const context = useContext(I18nContext);
  if (!context) {
    throw new Error("useI18n must be used inside I18nProvider");
  }

  return context;
}
