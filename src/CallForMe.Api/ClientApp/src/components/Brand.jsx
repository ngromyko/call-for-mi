import { useI18n } from "../i18n/I18nContext.jsx";

export function Brand({ className = "" }) {
  const { t } = useI18n();

  return (
    <div className={`brand ${className}`.trim()}>
      <span className="brand-mark material-symbols-rounded">graphic_eq</span>
      <div>
        <strong>Call for me</strong>
        <span>{t("app.assistant")}</span>
      </div>
    </div>
  );
}
