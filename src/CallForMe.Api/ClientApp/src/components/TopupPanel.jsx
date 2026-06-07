import { useMemo, useState } from "react";
import { Icon } from "./Dialog.jsx";
import { formatBalance, formatTon, formatUsdt, isTonUsdtNetwork, tonTransferLink, usdtTransferLink } from "../utils/format.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function TopupPanel({ config, onRefreshTon, onCopyAddress, onCopyComment }) {
  const { t } = useI18n();
  const [currency, setCurrency] = useState("TON");
  const [amount, setAmount] = useState("0.1");
  const [showDetails, setShowDetails] = useState(false);

  const payment = useMemo(() => {
    const ton = config.tonPayments || {};
    const usdt = config.usdtPayments || {};
    const current = currency === "USDT" ? usdt : ton;
    const min = Number(currency === "USDT" ? current.minUsdtAmount || 1 : current.minTonAmount || 0.1);
    const normalizedAmount = Math.max(Number(amount || min), min);
    const credits = normalizedAmount * Number(currency === "USDT" ? current.creditsPerUsdt || 0 : current.creditsPerTon || 0);
    return { current, min, amount: normalizedAmount, credits };
  }, [amount, config, currency]);

  const enabled = !!payment.current.enabled;
  const amountValue = Number(amount || 0) < payment.min ? String(payment.min) : amount;
  const qrAmount = encodeURIComponent(String(payment.amount));
  const isUsdt = currency === "USDT";
  const walletAddress = payment.current.walletAddress || "";
  const comment = payment.current.comment || "";
  const paymentLink = isUsdt
    ? usdtTransferLink(walletAddress, payment.amount, comment, payment.current.network, payment.current.jettonMasterAddress)
    : tonTransferLink(walletAddress, payment.amount, comment);
  const canOpenWallet = !isUsdt || isTonUsdtNetwork(payment.current.network);
  const text = isUsdt
    ? t("topup.usdtText", {
      amount: formatUsdt(payment.amount),
      network: payment.current.network || "TON",
      credits: formatBalance(payment.credits),
      address: payment.current.walletAddress || ""
    })
    : t("topup.tonText", {
      amount: formatTon(payment.amount),
      credits: formatBalance(payment.credits)
    });

  return (
    <section className="ton-topup-panel payment-card" aria-label={t("topup.payment")}>
      <form onSubmit={event => {
        event.preventDefault();
        setShowDetails(true);
      }}>
        <label>
          <span>{t("topup.amountCurrency", { currency })}</span>
          <div className="amount-input-shell">
            <input
              type="number"
              min={payment.min}
              step="0.01"
              value={amountValue}
              inputMode="decimal"
              onChange={event => setAmount(event.target.value)}
            />
            <span>{currency} {t("topup.approxUsd", { amount: formatBalance(payment.credits) })}</span>
          </div>
        </label>
        <label>
          <span>{t("topup.currency")}</span>
          <select value={currency} onChange={event => setCurrency(event.target.value)} autoComplete="off">
            <option value="TON">TON</option>
            <option value="USDT">USDT</option>
          </select>
        </label>
        <button type="submit" className="primary-button">
          <Icon>payments</Icon>
          {t("topup.pay")}
        </button>
      </form>

      <div className="ton-payment-box" hidden={!showDetails || !enabled}>
        <strong>{isUsdt ? t("topup.usdtDetails") : t("topup.tonDetails")}</strong>
        <span>{text}</span>
        <div className="payment-value-card">
          <span>{t("topup.walletAddress")}</span>
          <code>{walletAddress}</code>
          <button type="button" className="secondary-button" onClick={() => onCopyAddress(walletAddress)}>
            <Icon>content_copy</Icon>
            {t("topup.copyAddress")}
          </button>
        </div>
        <div className="payment-value-card">
          <span>{t("topup.comment")}</span>
          <button type="button" className="copy-value-button" onClick={() => onCopyComment(comment)}>
            <code>{comment}</code>
            <Icon>content_copy</Icon>
          </button>
        </div>
        <div className="ton-qr-box">
          <img
            src={isUsdt ? `/api/usdt/qr?amount=${qrAmount}` : `/api/ton/qr?amount=${qrAmount}`}
            alt={isUsdt ? t("topup.qrUsdt") : t("topup.qrTon")}
            loading="lazy"
          />
        </div>
        {canOpenWallet ? (
          <a
            className="primary-button"
            href={paymentLink}
            target="_blank"
            rel="noreferrer"
          >
            <Icon>open_in_new</Icon>
            <span>{isUsdt ? t("topup.openUsdtWallet") : t("topup.openWallet")}</span>
          </a>
        ) : null}
        <button type="button" className="secondary-button" onClick={onRefreshTon}>
          <Icon>done</Icon>
          {t("topup.refreshNow")}
        </button>
      </div>
    </section>
  );
}
