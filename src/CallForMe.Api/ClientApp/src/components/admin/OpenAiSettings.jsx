import { useEffect, useState } from "react";
import { Icon } from "../Dialog.jsx";
import { useI18n } from "../../i18n/I18nContext.jsx";

export function OpenAiSettings({ config, onSave }) {
  const { t } = useI18n();
  const [apiKey, setApiKey] = useState("");
  const [model, setModel] = useState(config.aiModel || "gpt-5.4-mini");

  useEffect(() => {
    setModel(config.aiModel || "gpt-5.4-mini");
  }, [config.aiModel]);

  return (
    <div className="settings-section">
      <h3>OpenAI</h3>
      <div className="settings-form">
        <label>
          <span>{t("admin.apiKey")}</span>
          <input type="password" placeholder="sk-..." autoComplete="off" value={apiKey} onChange={event => setApiKey(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.model")}</span>
          <input type="text" autoComplete="off" value={model} onChange={event => setModel(event.target.value)} />
        </label>
        <button type="button" className="primary-button" onClick={() => onSave({ apiKey, model }, () => setApiKey(""))}>
          <Icon>key</Icon>
          {t("admin.saveOpenAi")}
        </button>
      </div>
      <p>{t("admin.openAiHelp")}</p>
    </div>
  );
}
