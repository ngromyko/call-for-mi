import { useI18n } from "../../i18n/I18nContext.jsx";

export function MissingBox({ config }) {
  const { t } = useI18n();
  const missing = [...(config.twilioMissing || []), ...(config.aiMissing || [])];
  const twilioInvalid = config.twilioCredentialsValid === false;

  return (
    <div className="missing-box">
      {missing.length ? (
        <>
          <strong>{t("setup.missing")}</strong>{" "}
          {missing.map(item => <code key={item}>{item}</code>)}
        </>
      ) : twilioInvalid ? (
        <><strong>Twilio:</strong> {t("setup.twilioInvalid")}</>
      ) : (
        <strong>{t("setup.complete")}</strong>
      )}
    </div>
  );
}
