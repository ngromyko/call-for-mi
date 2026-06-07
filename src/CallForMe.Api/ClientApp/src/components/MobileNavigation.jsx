import { Icon } from "./Dialog.jsx";
import { useI18n } from "../i18n/I18nContext.jsx";

export function MobileNavigation({ view, isAdmin, ready, onChangeView, onNewCall }) {
  const { t } = useI18n();
  const items = [
    ["history", "history", t("navigation.history")],
    ["wallet", "account_balance_wallet", t("navigation.wallet")],
    ["account", "account_circle", t("navigation.account")]
  ];

  return (
    <>
      <nav className="mobile-nav" aria-label={t("navigation.main")}>
        {items.map(([id, icon, label]) => (
          <button
            key={id}
            type="button"
            className={`mobile-nav-button ${view === id ? "active" : ""}`}
            onClick={() => onChangeView(id)}
          >
            <Icon>{icon}</Icon>
            <span>{label}</span>
          </button>
        ))}
      </nav>
      <button
        type="button"
        className="mobile-nav-action"
        aria-label={t("sidebar.newCall")}
        onClick={onNewCall}
        disabled={!ready}
      >
        <Icon>add_call</Icon>
      </button>
    </>
  );
}
