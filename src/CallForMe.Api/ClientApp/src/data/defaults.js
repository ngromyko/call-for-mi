import { demoCall } from "./demoCall.js";

export const defaultConfig = {
  twilioEnabled: false,
  aiEnabled: false,
  readyForRealCalls: false,
  callPricePerMinute: 0.5,
  tonPayments: {},
  usdtPayments: {}
};

export const defaultAuth = { authenticated: false, user: null, balanceClientId: null };
export const defaultBalance = { clientId: "", balance: 0 };
export const defaultActiveCall = demoCall;
