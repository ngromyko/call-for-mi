import { useCallback, useEffect, useMemo, useState } from "react";
import { BalanceCard } from "./BalanceCard.jsx";
import { Brand } from "./Brand.jsx";
import { Icon } from "./Dialog.jsx";
import { HistoryPanel } from "./HistoryPanel.jsx";
import { LanguageSwitcher } from "./LanguageSwitcher.jsx";
import { WalletPanel } from "./WalletPanel.jsx";
import { useI18n } from "../i18n/I18nContext.jsx";

export function Sidebar({
  auth,
  balance,
  calls,
  realCallsCount,
  activeCall,
  config,
  tonPayments,
  isAdmin,
  view,
  onNewCall,
  onOpenAuth,
  onLogout,
  onRefreshCalls,
  onSelectCall,
  onHideCall,
  onOpenSettings,
  onOpenHelp,
  onOpenTopup,
  onRedeemPromo,
  onRefreshTon,
  onCopyAddress,
  onCopyComment
}) {
  const { t } = useI18n();
  const [checkingPayments, setCheckingPayments] = useState(false);
  const authenticated = !!auth?.authenticated;
  const accountVisible = view === "account";
  const walletVisible = view === "wallet";
  const profileVisible = accountVisible || walletVisible;
  const ready = !!config.readyForRealCalls;
  const setupReason = config.setupReason || t("setup.checkBeforeCall");
  const processingPayments = useMemo(
    () => (tonPayments || []).filter(item => String(item.status || "").toLowerCase() === "processing"),
    [tonPayments]);
  const refreshPayments = useCallback(async () => {
    setCheckingPayments(true);
    try {
      await onRefreshTon?.();
    } finally {
      setCheckingPayments(false);
    }
  }, [onRefreshTon]);

  useEffect(() => {
    if (!authenticated || !processingPayments.length) return undefined;
    const timer = window.setInterval(() => {
      refreshPayments();
    }, 20000);
    return () => window.clearInterval(timer);
  }, [authenticated, processingPayments.length, refreshPayments]);

  return (
    <aside className="sidebar">
      <Brand />

      {!profileVisible ? (
        <button
          className="new-call-button"
          type="button"
          onClick={onNewCall}
          disabled={!ready}
          title={ready ? "" : setupReason}
        >
          <Icon>call</Icon>
          {t("sidebar.newCall")}
        </button>
      ) : null}

      {accountVisible ? (
        <>
          <section className="account-card" onClick={() => !authenticated && onOpenAuth("login")}>
            <div>
              <span>{t("sidebar.account")}</span>
              <strong>{authenticated ? (auth.user.displayName || auth.user.username) : t("sidebar.guest")}</strong>
            </div>
            <div className="account-actions">
              {!authenticated ? (
                <button
                  type="button"
                  className="account-button"
                  onClick={event => {
                    event.stopPropagation();
                    onOpenAuth("login");
                  }}
                >
                  <Icon>login</Icon>
                  {t("sidebar.login")}
                </button>
              ) : (
                <button
                  type="button"
                  className="account-button"
                  onClick={event => {
                    event.stopPropagation();
                    onLogout();
                  }}
                >
                  <Icon>logout</Icon>
                  {t("sidebar.logout")}
                </button>
              )}
              {isAdmin ? (
                <button
                  type="button"
                  className="account-button admin-account-button"
                  onClick={event => {
                    event.stopPropagation();
                    onOpenSettings();
                  }}
                >
                  <Icon>tune</Icon>
                  {t("navigation.admin")}
                </button>
              ) : null}
            </div>
          </section>

          <LanguageSwitcher />

          {authenticated ? (
            <BalanceCard
              authenticated={authenticated}
              balance={balance}
              config={config}
              onOpenTopup={onOpenTopup}
            />
          ) : null}
        </>
      ) : null}

      {walletVisible ? (
        <WalletPanel
          authenticated={authenticated}
          balance={balance}
          config={config}
          payments={tonPayments}
          processingPayments={processingPayments}
          checkingPayments={checkingPayments}
          onRedeem={onRedeemPromo}
          onOpenAuth={onOpenAuth}
          onRefreshTon={refreshPayments}
          onCopyAddress={onCopyAddress}
          onCopyComment={onCopyComment}
        />
      ) : null}

      {isAdmin && !ready ? (
        <div className="setup-warning">
          <Icon>settings_alert</Icon>
          <div>
            <strong>{t("sidebar.setupTitle")}</strong>
            <p>{setupReason || t("sidebar.setupText")}</p>
            <button className="setup-link" type="button" onClick={onOpenSettings}>{t("sidebar.openSettings")}</button>
          </div>
        </div>
      ) : null}

      {!profileVisible ? (
        <HistoryPanel
          calls={calls}
          activeCall={activeCall}
          realCallsCount={realCallsCount}
          onRefresh={onRefreshCalls}
          onSelect={onSelectCall}
          onHide={onHideCall}
        />
      ) : null}

      <div className="sidebar-bottom">
        <button className="help-button" type="button" onClick={onOpenHelp}>
          <Icon>help</Icon>
          {t("sidebar.help")}
        </button>
      </div>
    </aside>
  );
}
