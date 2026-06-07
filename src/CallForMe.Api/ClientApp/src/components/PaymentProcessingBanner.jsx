import { Icon } from "./Dialog.jsx";
import { formatBalance, formatTon, formatUsdt } from "../utils/format.js";
import { useI18n } from "../i18n/I18nContext.jsx";

function amountText(item) {
  return item.currency === "USDT" ? formatUsdt(item.tonAmount) : formatTon(item.tonAmount);
}

export function PaymentProcessingBanner({ payments, checking, onRefresh, onCopyComment }) {
  const { t } = useI18n();
  if (!payments?.length) return null;

  return (
    <div className="account-processing-list">
      {payments.map(item => (
        <article key={item.id} className="account-processing-banner">
          <Icon className="spinning-icon">sync</Icon>
          <div>
            <strong>{t("topup.processingTitle")}</strong>
            <span>{t("topup.processingText", {
              amount: amountText(item),
              currency: item.currency,
              credits: formatBalance(item.creditsAmount)
            })}</span>
            <button type="button" className="inline-copy-button" onClick={() => onCopyComment(item.comment)}>
              {item.comment}
            </button>
          </div>
          <button type="button" className="icon-button" onClick={onRefresh} disabled={checking} aria-label={t("topup.checkPayment")}>
            <Icon>refresh</Icon>
          </button>
        </article>
      ))}
    </div>
  );
}
