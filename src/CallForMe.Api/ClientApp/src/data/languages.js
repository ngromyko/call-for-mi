export const callLanguages = [
  { code: "ru-RU", name: "Русский" },
  { code: "uk-UA", name: "Українська" },
  { code: "en-US", name: "English" },
  { code: "pl-PL", name: "Polski" },
  { code: "de-DE", name: "Deutsch" },
  { code: "es-ES", name: "Español" },
  { code: "fr-FR", name: "Français" },
  { code: "it-IT", name: "Italiano" },
  { code: "cs-CZ", name: "Čeština" }
];

export const remoteLanguages = [
  { code: "auto", name: { ru: "Автоопределение", en: "Auto detect" } },
  ...callLanguages
];

export function callLanguageName(code, fallback = "Auto") {
  if (code === "auto") return fallback;
  return callLanguages.find(language => language.code === code)?.name || code || fallback;
}
