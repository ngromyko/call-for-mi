import { useEffect, useState } from "react";
import { Icon } from "../Dialog.jsx";
import { useI18n } from "../../i18n/I18nContext.jsx";
import { formatBalance, formatTon, formatUsdt } from "../../utils/format.js";

function paymentAmountText(payment) {
  const currency = String(payment.currency || "TON").toUpperCase();
  return currency === "USDT"
    ? `${formatUsdt(payment.tonAmount)} USDT`
    : `${formatTon(payment.tonAmount)} TON`;
}

export function TonSettings({ config, payments, onSave }) {
  const { t } = useI18n();
  const ton = config.tonPayments || {};
  const [walletAddress, setWalletAddress] = useState(ton.walletAddress || "");
  const [creditsPerTon, setCreditsPerTon] = useState(ton.creditsPerTon || 1000);
  const [minTonAmount, setMinTonAmount] = useState(ton.minTonAmount || 0.1);

  useEffect(() => {
    setWalletAddress(ton.walletAddress || "");
    setCreditsPerTon(ton.creditsPerTon || 1000);
    setMinTonAmount(ton.minTonAmount || 0.1);
  }, [ton.walletAddress, ton.creditsPerTon, ton.minTonAmount]);

  return (
    <div className="settings-section">
      <h3>TON</h3>
      <div className="settings-form ton-settings-form">
        <label>
          <span>{t("admin.tonWallet")}</span>
          <input type="text" placeholder="UQ..." autoComplete="off" value={walletAddress} onChange={event => setWalletAddress(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.creditsPerTon")}</span>
          <input type="number" min="1" step="1" value={creditsPerTon} onChange={event => setCreditsPerTon(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.minTon")}</span>
          <input type="number" min="0.01" step="0.01" value={minTonAmount} onChange={event => setMinTonAmount(event.target.value)} />
        </label>
        <button type="button" className="primary-button" onClick={() => onSave({ walletAddress, creditsPerTon: Number(creditsPerTon), minTonAmount: Number(minTonAmount) })}>
          <Icon>account_balance_wallet</Icon>
          {t("admin.saveTon")}
        </button>
      </div>
      <p className="settings-help-text">{t("admin.tonWalletHelp")}</p>
      <div className="ton-admin-list">
        {(payments || []).length ? payments.slice(0, 10).map(payment => (
          <article key={payment.id} className="ton-admin-item confirmed">
            <div>
              <strong>{paymentAmountText(payment)} {"->"} {formatBalance(payment.creditsAmount)}</strong>
              <span>{t("admin.credited")} · {payment.comment}</span>
              <small>{payment.clientId}</small>
            </div>
          </article>
        )) : <div className="empty-history">{t("admin.emptyTonPayments")}</div>}
      </div>
    </div>
  );
}
