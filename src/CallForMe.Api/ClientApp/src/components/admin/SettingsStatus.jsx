import { Icon } from "../Dialog.jsx";
import { useI18n } from "../../i18n/I18nContext.jsx";

export function SettingsStatus({ config, onCheckTwilio }) {
  const { t } = useI18n();
  const twilioCredentialsInvalid = config.twilioCredentialsValid === false;
  const twilioReady = config.twilioEnabled && !twilioCredentialsInvalid;

  return (
    <div className="settings-status">
      <div className={`settings-card ${twilioReady ? "ready" : "missing"}`}>
        <Icon>phone_in_talk</Icon>
        <div>
          <strong>Twilio</strong>
          <small>
            {twilioCredentialsInvalid
              ? t("admin.twilioKeysFailed")
              : (config.twilioEnabled ? t("admin.twilioFieldsReady") : t("admin.twilioMissing"))}
          </small>
          <button type="button" className="inline-check-button" onClick={onCheckTwilio}>{t("admin.checkTwilio")}</button>
        </div>
      </div>
      <div className={`settings-card ${config.aiEnabled ? "ready" : "missing"}`}>
        <Icon>auto_awesome</Icon>
        <div>
          <strong>OpenAI</strong>
          <small>{config.aiEnabled ? t("admin.aiReady", { model: config.aiModel || t("admin.aiDefault") }) : t("admin.aiMissing")}</small>
        </div>
      </div>
    </div>
  );
}
