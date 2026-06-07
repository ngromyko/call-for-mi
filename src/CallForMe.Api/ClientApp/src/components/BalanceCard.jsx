import { useState } from "react";
import { Icon } from "./Dialog.jsx";
import { TopupPanel } from "./TopupPanel.jsx";
import { formatBalance } from "../utils/format.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function BalanceCard({
  authenticated,
  balance,
  config,
  payments,
  onRedeem,
  onOpenAuth,
  onLoadDepositInfo,
  onRefreshTon,
  onCopyAddress,
  onCopyComment
}) {
  const { t } = useI18n();
  const [promoCode, setPromoCode] = useState("");
  const [topupOpen, setTopupOpen] = useState(false);

  return (
    <section className="balance-card" aria-label={t("sidebar.credits")}>
      <div>
        <span>{t("sidebar.credits")}</span>
        <strong>{formatBalance(balance?.balance)}</strong>
      </div>

      {authenticated ? (
        <form
          className="promo-redeem-form"
          onSubmit={event => {
            event.preventDefault();
            onRedeem(promoCode, () => setPromoCode(""));
          }}
        >
          <input
            type="text"
            autoComplete="off"
            placeholder={t("sidebar.promoPlaceholder")}
            aria-label={t("sidebar.promoPlaceholder")}
            value={promoCode}
            onChange={event => setPromoCode(event.target.value.toUpperCase())}
          />
          <button type="submit" aria-label={t("sidebar.applyPromo")}>
            <Icon>add_card</Icon>
          </button>
        </form>
      ) : null}

      {authenticated ? (
        <button
          type="button"
          className="ton-toggle-button"
          onClick={async () => {
            if (!topupOpen) await onLoadDepositInfo();
            setTopupOpen(!topupOpen);
          }}
        >
          <Icon>account_balance_wallet</Icon>
          {t("sidebar.topup")}
        </button>
      ) : null}

      {authenticated && topupOpen ? (
        <TopupPanel
          config={config}
          payments={payments}
          onRefreshTon={onRefreshTon}
          onCopyAddress={onCopyAddress}
          onCopyComment={onCopyComment}
        />
      ) : null}
    </section>
  );
}
