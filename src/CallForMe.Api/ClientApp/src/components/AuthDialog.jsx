import { useEffect, useState } from "react";
import { Dialog, Icon } from "./Dialog.jsx";
import { useI18n } from "../i18n/I18nContext.jsx";

export function AuthDialog({ open, mode, onModeChange, onClose, onSubmit, submitting }) {
  const { t } = useI18n();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const isRegister = mode === "register";

  useEffect(() => {
    if (open) {
      setUsername("");
      setPassword("");
      setError("");
    }
  }, [open, mode]);

  async function submit(event) {
    event.preventDefault();
    setError("");
    if (!username.trim() || !password) {
      setError(t("auth.missing"));
      return;
    }

    try {
      await onSubmit({ username: username.trim(), password });
      onClose();
    } catch (apiError) {
      setError(apiError.message || t("auth.failed"));
    }
  }

  return (
    <Dialog open={open} className="modal small-modal" onCancel={onClose}>
      <form className="modal-card" noValidate onSubmit={submit}>
        <div className="modal-header">
          <div>
            <span className="eyebrow">{t("auth.eyebrow")}</span>
            <h2>{isRegister ? t("auth.registerTitle") : t("auth.loginTitle")}</h2>
            <p>{t("auth.help")}</p>
          </div>
          <button type="button" className="icon-button" onClick={onClose} aria-label={t("dialogs.close")}>
            <Icon>close</Icon>
          </button>
        </div>
        <div className="form-error-summary" hidden={!error}>{error}</div>
        <label>
          <span>{t("auth.username")}</span>
          <input type="text" autoComplete="username" placeholder={t("auth.username")} value={username} onChange={event => setUsername(event.target.value)} />
        </label>
        <label>
          <span>{t("auth.password")}</span>
          <input
            type="password"
            autoComplete={isRegister ? "new-password" : "current-password"}
            placeholder={t("auth.passwordPlaceholder")}
            value={password}
            onChange={event => setPassword(event.target.value)}
          />
        </label>
        <div className="auth-switch">
          <button type="button" className={!isRegister ? "active" : ""} onClick={() => onModeChange("login")}>{t("auth.login")}</button>
          <button type="button" className={isRegister ? "active" : ""} onClick={() => onModeChange("register")}>{t("auth.register")}</button>
        </div>
        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onClose}>{t("dialogs.cancel")}</button>
          <button type="submit" className="primary-button" disabled={submitting}>
            <Icon>{isRegister ? "person_add" : "login"}</Icon>
            <span>{isRegister ? t("auth.registerSubmit") : t("auth.loginSubmit")}</span>
          </button>
        </div>
      </form>
    </Dialog>
  );
}
