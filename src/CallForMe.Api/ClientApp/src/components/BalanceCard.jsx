import { Icon } from "./Dialog.jsx";
import { callPrice } from "../utils/callMetrics.js";
import { formatBalance } from "../utils/format.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function BalanceCard({
  authenticated,
  balance,
  config,
  onOpenTopup
}) {
  const { t } = useI18n();
  const pricePerMinute = callPrice(config);

  return (
    <section className="balance-card balance-summary-card" aria-label={t("sidebar.credits")}>
      <div>
        <span>{t("sidebar.credits")}</span>
        <strong>{formatBalance(balance?.balance)}</strong>
        <small className="minute-price">{t("call.pricePerMinute", { price: formatBalance(pricePerMinute) })}</small>
      </div>

      {authenticated ? (
        <button type="button" className="ton-toggle-button" onClick={onOpenTopup}>
          <Icon>account_balance_wallet</Icon>
          {t("sidebar.topup")}
        </button>
      ) : null}
    </section>
  );
}
