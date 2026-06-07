import { useState } from "react";
import { Icon } from "../Dialog.jsx";
import { useI18n } from "../../i18n/I18nContext.jsx";
import { formatBalance } from "../../utils/format.js";

export function PromoSettings({ promoCodes, onCreate, onToggle }) {
  const { t } = useI18n();
  const [code, setCode] = useState("");
  const [amount, setAmount] = useState(100);
  const [limit, setLimit] = useState("");

  return (
    <div className="settings-section">
      <h3>{t("admin.promoCodes")}</h3>
      <div className="settings-form promo-admin-form">
        <label>
          <span>{t("admin.code")}</span>
          <input type="text" placeholder="WELCOME100" autoComplete="off" value={code} onChange={event => setCode(event.target.value.toUpperCase())} />
        </label>
        <label>
          <span>{t("admin.amountCredits")}</span>
          <input type="number" min="1" step="1" value={amount} onChange={event => setAmount(event.target.value)} />
        </label>
        <label>
          <span>{t("admin.redemptionLimit")}</span>
          <input type="number" min="1" step="1" placeholder={t("admin.noLimit")} value={limit} onChange={event => setLimit(event.target.value)} />
        </label>
        <button
          type="button"
          className="primary-button"
          onClick={() => onCreate({
            code,
            amount: Number(amount),
            maxRedemptions: limit ? Number(limit) : null
          }, () => {
            setCode("");
            setLimit("");
          })}
        >
          <Icon>add</Icon>
          {t("admin.createPromo")}
        </button>
      </div>
      <div className="promo-admin-list">
        {(promoCodes || []).length ? promoCodes.map(item => {
          const limitText = item.maxRedemptions ? `${item.redemptionCount}/${item.maxRedemptions}` : `${item.redemptionCount}`;
          return (
            <article key={item.id} className={`promo-admin-item ${item.active ? "" : "disabled"}`}>
              <div>
                <strong>{item.code}</strong>
                <span>{t("admin.promoActivations", { amount: formatBalance(item.amount), limit: limitText })}</span>
              </div>
              <button type="button" className="secondary-button" onClick={() => onToggle(item.id, !item.active)}>
                <Icon>{item.active ? "block" : "check_circle"}</Icon>
                {item.active ? t("admin.promoActive") : t("admin.promoDisabled")}
              </button>
            </article>
          );
        }) : <div className="empty-history">{t("admin.emptyPromo")}</div>}
      </div>
    </div>
  );
}
