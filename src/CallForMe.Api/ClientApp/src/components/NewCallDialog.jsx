import { useEffect, useState } from "react";
import { Dialog, Icon } from "./Dialog.jsx";
import { callLanguages, remoteLanguages } from "../data/languages.js";
import { useI18n } from "../i18n/I18nContext.jsx";

const initialForm = {
  phoneNumber: "+",
  displayName: "",
  prompt: "",
  userLanguage: "ru-RU",
  language: "auto",
  autoPilot: false
};

export function NewCallDialog({ open, onClose, onSubmit, submitting }) {
  const { locale, t } = useI18n();
  const [form, setForm] = useState(initialForm);
  const [errors, setErrors] = useState({});
  const [summary, setSummary] = useState("");

  useEffect(() => {
    if (open) {
      setErrors({});
      setSummary("");
    }
  }, [open]);

  function update(name, value) {
    setForm(current => ({ ...current, [name]: value }));
  }

  async function submit(event) {
    event.preventDefault();
    const payload = {
      ...form,
      phoneNumber: form.phoneNumber.replace(/\s/g, ""),
      displayName: form.displayName.trim(),
      prompt: form.prompt.trim()
    };

    const localErrors = {};
    if (!/^\+[1-9]\d{7,14}$/.test(payload.phoneNumber)) {
      localErrors.phoneNumber = [t("newCall.invalidPhone")];
    }
    if (payload.prompt.length < 10) {
      localErrors.prompt = [t("newCall.invalidPrompt")];
    }
    if (Object.keys(localErrors).length) {
      setErrors(localErrors);
      setSummary(Object.values(localErrors)[0][0] || t("newCall.invalidFields"));
      return;
    }

    try {
      await onSubmit(payload);
      setForm(initialForm);
      onClose();
    } catch (error) {
      const nextErrors = normalizeErrors(error.errors || {});
      setErrors(nextErrors);
      setSummary(firstError(nextErrors) || error.message || t("newCall.checkFields"));
    }
  }

  return (
    <Dialog open={open} onCancel={onClose}>
      <form className="modal-card" noValidate onSubmit={submit}>
        <div className="modal-header">
          <div>
            <span className="eyebrow">{t("newCall.eyebrow")}</span>
            <h2>{t("newCall.title")}</h2>
            <p>{t("newCall.help")}</p>
          </div>
          <button type="button" className="icon-button" onClick={onClose} aria-label={t("dialogs.close")}>
            <Icon>close</Icon>
          </button>
        </div>

        <div className="form-error-summary" hidden={!summary}>{summary}</div>

        <Field label={t("newCall.phone")} error={errors.phoneNumber}>
          <input
            name="phoneNumber"
            type="tel"
            required
            autoComplete="tel"
            placeholder={t("newCall.phonePlaceholder")}
            value={form.phoneNumber}
            onChange={event => update("phoneNumber", event.target.value)}
            className={errors.phoneNumber ? "field-invalid" : ""}
            aria-invalid={!!errors.phoneNumber}
          />
        </Field>

        <Field label={t("newCall.displayName")} error={errors.displayName}>
          <input
            name="displayName"
            type="text"
            autoComplete="off"
            placeholder={t("newCall.displayNamePlaceholder")}
            value={form.displayName}
            onChange={event => update("displayName", event.target.value)}
            className={errors.displayName ? "field-invalid" : ""}
            aria-invalid={!!errors.displayName}
          />
        </Field>

        <Field label={t("newCall.prompt")} error={errors.prompt}>
          <textarea
            name="prompt"
            required
            rows="4"
            placeholder={t("newCall.promptPlaceholder")}
            value={form.prompt}
            onChange={event => update("prompt", event.target.value)}
            className={errors.prompt ? "field-invalid" : ""}
            aria-invalid={!!errors.prompt}
          />
        </Field>

        <div className="language-row">
          <label>
            <span>{t("newCall.userLanguage")}</span>
            <select value={form.userLanguage} onChange={event => update("userLanguage", event.target.value)}>
              {callLanguages.map(language => <option key={language.code} value={language.code}>{language.name}</option>)}
            </select>
          </label>
          <Icon>arrow_forward</Icon>
          <label>
            <span>{t("newCall.remoteLanguage")}</span>
            <select value={form.language} onChange={event => update("language", event.target.value)}>
              {remoteLanguages.map(language => (
                <option key={language.code} value={language.code}>
                  {typeof language.name === "string" ? language.name : language.name[locale] || language.name.ru}
                </option>
              ))}
            </select>
          </label>
        </div>

        <label className="modal-toggle">
          <input
            type="checkbox"
            checked={form.autoPilot}
            onChange={event => update("autoPilot", event.target.checked)}
          />
          <span className="toggle-visual"><span /></span>
          <span><strong>{t("newCall.autoPilot")}</strong><small>{t("newCall.autoPilotHelp")}</small></span>
        </label>

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>{t("dialogs.cancel")}</button>
          <button type="submit" className="primary-button" disabled={submitting}>
            <Icon>call</Icon>
            <span>{submitting ? t("newCall.submitting") : t("newCall.submit")}</span>
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function Field({ label, error, children }) {
  return (
    <label>
      <span>{label}</span>
      {children}
      <small className="field-error">{Array.isArray(error) ? error.join(" ") : error || ""}</small>
    </label>
  );
}

function normalizeErrors(errors) {
  return Object.fromEntries(Object.entries(errors).map(([key, value]) => [
    key.charAt(0).toLowerCase() + key.slice(1),
    value
  ]));
}

function firstError(errors) {
  return Object.values(errors).flat().filter(Boolean)[0];
}
