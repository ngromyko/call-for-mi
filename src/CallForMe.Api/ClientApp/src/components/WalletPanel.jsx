import { useState } from "react";
import { Icon } from "./Dialog.jsx";
import { PaymentProcessingBanner } from "./PaymentProcessingBanner.jsx";
import { TopupPanel } from "./TopupPanel.jsx";
import { formatBalance, formatShortDate, formatTon, formatUsdt } from "../utils/format.js";
import { useI18n } from "../i18n/I18nContext.jsx";

function paymentCurrency(item) {
  return String(item?.currency || "TON").toUpperCase();
}

function paymentAmountText(item) {
  return paymentCurrency(item) === "USDT"
    ? `${formatUsdt(item.tonAmount)} USDT`
    : `${formatTon(item.tonAmount)} TON`;
}

export function WalletPanel({
  authenticated,
  balance,
  config,
  payments,
  processingPayments,
  checkingPayments,
  onRedeem,
  onOpenAuth,
  onRefreshTon,
  onCopyAddress,
  onCopyComment
}) {
  const { t } = useI18n();
  const [promoCode, setPromoCode] = useState("");
  const lastTransaction = (payments || [])
    .filter(item => String(item.status || "").toLowerCase() !== "processing")
    .at(0);

  return (
    <section className="wallet-card" aria-label={t("navigation.wallet")}>
      <section className="wallet-balance-card" aria-label={t("topup.availableCredits")}>
        <span>{t("topup.availableCredits")}</span>
        <strong>{formatBalance(balance?.balance)}</strong>
      </section>

      {!authenticated ? (
        <button type="button" className="account-button" onClick={() => onOpenAuth("login")}>
          <Icon>login</Icon>
          {t("sidebar.login")}
        </button>
      ) : (
        <>
          <PaymentProcessingBanner
            payments={processingPayments}
            checking={checkingPayments}
            onRefresh={onRefreshTon}
            onCopyComment={onCopyComment}
          />

          <TopupPanel
            config={config}
            onRefreshTon={onRefreshTon}
            onCopyAddress={onCopyAddress}
            onCopyComment={onCopyComment}
          />

          <details className="promo-collapse">
            <summary>{t("topup.promoPrompt")}</summary>
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
          </details>

          {lastTransaction ? (
            <section className="last-transaction-card" aria-label={t("topup.lastTransaction")}>
              <span>{t("topup.lastTransaction")}</span>
              <article className="ton-payment-item confirmed">
                <div>
                  <strong className="last-transaction-line">
                    <span>{paymentAmountText(lastTransaction)}</span>
                    <Icon>arrow_forward</Icon>
                    <span>{formatBalance(lastTransaction.creditsAmount)} {t("topup.credits")}</span>
                  </strong>
                  <span className="last-transaction-meta">
                    {t("topup.creditedStatus")} · {formatShortDate(lastTransaction.receivedAt || lastTransaction.createdAt)}
                  </span>
                </div>
              </article>
            </section>
          ) : null}
        </>
      )}
    </section>
  );
}
