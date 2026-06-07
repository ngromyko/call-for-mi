import { Dialog, Icon } from "./Dialog.jsx";
import { useI18n } from "../i18n/I18nContext.jsx";

export function HelpDialog({ open, onClose }) {
  const { t } = useI18n();
  const steps = [
    [t("help.step1Title"), t("help.step1Text")],
    [t("help.step2Title"), t("help.step2Text")],
    [t("help.step3Title"), t("help.step3Text")]
  ];

  return (
    <Dialog open={open} className="modal small-modal" onCancel={onClose}>
      <form method="dialog" className="modal-card" onSubmit={event => {
        event.preventDefault();
        onClose();
      }}>
        <div className="modal-header">
          <div>
            <span className="eyebrow">{t("help.eyebrow")}</span>
            <h2>{t("help.title")}</h2>
          </div>
          <button type="button" className="icon-button" onClick={onClose} aria-label={t("dialogs.close")}>
            <Icon>close</Icon>
          </button>
        </div>
        <ol className="help-list">
          {steps.map(([title, text], index) => (
            <li key={title}>
              <span>{index + 1}</span>
              <p><strong>{title}</strong>{text}</p>
            </li>
          ))}
        </ol>
        <button type="submit" className="primary-button wide">{t("help.done")}</button>
      </form>
    </Dialog>
  );
}
