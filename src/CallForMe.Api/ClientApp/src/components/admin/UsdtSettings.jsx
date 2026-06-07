import { useEffect, useState } from "react";
import { Icon } from "../Dialog.jsx";
import { useI18n } from "../../i18n/I18nContext.jsx";

export function UsdtSettings({ config, onSave }) {
  const { t } = useI18n();
  const usdt = config.usdtPayments || {};
  const [walletAddress, setWalletAddress] = useState(usdt.walletAddress || "");
  const [network, setNetwork] = useState(usdt.walletAddress ? usdt.network || "TON" : "TON");
  const [creditsPerUsdt, setCreditsPerUsdt] = useState(usdt.creditsPerUsdt || 100);
  const [minUsdtAmount, setMinUsdtAmount] = useState(usdt.minUsdtAmount || 1);

  useEffect(() => {
    setWalletAddress(usdt.walletAddress || "");
    setNetwork(usdt.walletAddress ? usdt.network || "TON" : "TON");
    setCreditsPerUsdt(usdt.creditsPerUsdt || 100);
    setMinUsdtAmount(usdt.minUsdtAmount || 1);
  }, [usdt.walletAddress, usdt.network, usdt.creditsPerUsdt, usdt.minUsdtAmount]);

  return (
    <div className="settings-section">
      <h3>USDT</h3>
      <div className="settings-form usdt-settings-form">
        <label>
          <span>{t("admin.usdtWallet")}</span>
          <input type="text" placeholder="UQ... / EQ..." autoComplete="off" value={walletAddress} onChange={event => setWalletAddress(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.network")}</span>
          <input type="text" placeholder="TON" autoComplete="off" value={network} onChange={event => setNetwork(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.creditsPerUsdt")}</span>
          <input type="number" min="1" step="1" value={creditsPerUsdt} onChange={event => setCreditsPerUsdt(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.minUsdt")}</span>
          <input type="number" min="0.01" step="0.01" value={minUsdtAmount} onChange={event => setMinUsdtAmount(event.target.value)} />
        </label>
        <button type="button" className="primary-button" onClick={() => onSave({ walletAddress, network, creditsPerUsdt: Number(creditsPerUsdt), minUsdtAmount: Number(minUsdtAmount) })}>
          <Icon>payments</Icon>
          {t("admin.saveUsdt")}
        </button>
      </div>
      <p>{t("admin.usdtWalletHelp")}</p>
    </div>
  );
}
