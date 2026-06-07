import { useEffect, useState } from "react";
import { Icon } from "../Dialog.jsx";
import { useI18n } from "../../i18n/I18nContext.jsx";

export function TwilioSettings({ config, onSave, onCheck }) {
  const { t } = useI18n();
  const [accountSid, setAccountSid] = useState("");
  const [authToken, setAuthToken] = useState("");
  const [fromNumber, setFromNumber] = useState(config.fromNumber || "");
  const [publicBaseUrl, setPublicBaseUrl] = useState(config.publicBaseUrl || "");

  useEffect(() => {
    setFromNumber(config.fromNumber || "");
    setPublicBaseUrl(config.publicBaseUrl || "");
  }, [config.fromNumber, config.publicBaseUrl]);

  return (
    <div className="settings-section">
      <h3>Twilio</h3>
      <div className="settings-form twilio-form">
        <label>
          <span>{t("admin.accountSid")}</span>
          <input type="text" placeholder={t("admin.fullSidPlaceholder")} autoComplete="off" value={accountSid} onChange={event => setAccountSid(event.target.value)} />
          <small className="saved-secret-hint">{config.accountSid ? t("admin.savedSid", { sid: config.accountSid }) : ""}</small>
        </label>
        <label>
          <span>{t("admin.authToken")}</span>
          <input type="password" placeholder="auth token" autoComplete="off" value={authToken} onChange={event => setAuthToken(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.twilioNumber")}</span>
          <input type="tel" placeholder="+1234567890" autoComplete="tel" value={fromNumber} onChange={event => setFromNumber(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.publicUrl")}</span>
          <input type="url" placeholder="https://your-tunnel.example" autoComplete="off" value={publicBaseUrl} onChange={event => setPublicBaseUrl(event.target.value)} />
        </label>
        <button
          type="button"
          className="primary-button"
          onClick={() => onSave({ accountSid, authToken, fromNumber, publicBaseUrl }, () => {
            setAccountSid("");
            setAuthToken("");
          })}
        >
          <Icon>phone_enabled</Icon>
          {t("admin.saveTwilio")}
        </button>
        <button type="button" className="secondary-button" onClick={onCheck}>
          <Icon>verified</Icon>
          {t("admin.checkTwilio")}
        </button>
      </div>
      <p><code>PublicBaseUrl</code> {t("admin.twilioHelp")}</p>
    </div>
  );
}
