import { useI18n } from "../i18n/I18nContext.jsx";

export function LanguageSwitcher() {
  const { locale, locales, setLocale, t } = useI18n();

  return (
    <section className="language-switcher" aria-label={t("languages.uiLanguage")}>
      <label>
        <span>{t("languages.uiLanguage")}</span>
        <select value={locale} onChange={event => setLocale(event.target.value)} autoComplete="off">
          {locales.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}
        </select>
      </label>
    </section>
  );
}
