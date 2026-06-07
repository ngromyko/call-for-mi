# Call For Me

ASP.NET Core backend for AI-assisted outbound phone calls through Twilio ConversationRelay.

The project also includes a responsive personal-use web interface for calling services in another
language. Open `http://localhost:5226` after starting the app.

## Features

- Starts outbound calls with Twilio Programmable Voice.
- Receives live ConversationRelay transcripts over WebSocket.
- Generates an automatic response and three operator suggestions.
- Lets an operator send a custom response or toggle autopilot.
- Shows the other person's speech together with a translation into the user's language.
- Keeps quick replies as separate display text and translated spoken text.
- Publishes live updates through SignalR.
- Persists call history, balances, and promo codes to SQLite.
- Uses a React frontend split into components, with Russian and English UI localization.
- Includes an admin promo-code cabinet and a promo-code balance flow.
- Includes a small MCP stdio server so Codex/ChatGPT-style clients can call the service tools.
- Starts only real Twilio calls; demo and simulated calls are disabled.

## Run locally

```powershell
dotnet run --project src/CallForMe.Api
```

The API listens on `http://localhost:5226` by default.

The personal calling interface is available at the root URL. API discovery is available at `/api`
and health information at `/health`.

The app stores its data in `src/CallForMe.Api/data/callforme.db` by default. On first startup after
upgrading from the older JSON version, existing calls from `data/calls.json` are imported into
SQLite if the `calls` table is empty.

## Frontend

The frontend source lives in `src/CallForMe.Api/ClientApp` and builds into `src/CallForMe.Api/wwwroot`.

```powershell
cd src/CallForMe.Api/ClientApp
npm install
npm run build
```

The built site loads a hashed `/assets/index-*.js` bundle; the old monolithic `wwwroot/app.js` is no longer used.
UI localization is in `ClientApp/src/i18n/locales.js`. To add another site language, add it to
`availableLocales` and add a matching dictionary in `messages`.

If Twilio or OpenAI are not configured, the UI shows a setup screen with the missing keys. You can
paste OpenAI and Twilio credentials directly in that screen; the app saves them to
`src/CallForMe.Api/appsettings.Local.json`, which is ignored by git. The same screen also keeps the
PowerShell `dotnet user-secrets` commands for manual setup.

Create a real call after configuring Twilio and OpenAI:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5226/api/calls `
  -ContentType application/json `
  -Body '{"phoneNumber":"+48123456789","prompt":"Узнай свободное время для записи к врачу","userLanguage":"ru-RU","language":"pl-PL","autoPilot":false}'
```

## Real Twilio configuration

Use environment variables or .NET user secrets. Do not commit credentials.

The app also recognizes common environment variable names:

- `OPENAI_API_KEY`
- `TWILIO_ACCOUNT_SID`
- `TWILIO_AUTH_TOKEN`
- `TWILIO_FROM_NUMBER` or `TWILIO_PHONE_NUMBER`
- `TWILIO_PUBLIC_BASE_URL`
- `TELEGRAM_CLIENT_ID`

```powershell
dotnet user-secrets init --project src/CallForMe.Api
dotnet user-secrets set "Twilio:Enabled" "true" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:AccountSid" "AC..." --project src/CallForMe.Api
dotnet user-secrets set "Twilio:AuthToken" "..." --project src/CallForMe.Api
dotnet user-secrets set "Twilio:FromNumber" "+1..." --project src/CallForMe.Api
dotnet user-secrets set "Twilio:PublicBaseUrl" "https://your-public-domain" --project src/CallForMe.Api
dotnet user-secrets set "AI:Enabled" "true" --project src/CallForMe.Api
dotnet user-secrets set "AI:ApiKey" "..." --project src/CallForMe.Api
```

Telegram login uses the current Telegram Login library. Create a bot through BotFather, configure
the site domain, get the Telegram Login `Client ID`, then configure:

```powershell
dotnet user-secrets set "TelegramAuth:Enabled" "true" --project src/CallForMe.Api
dotnet user-secrets set "TelegramAuth:ClientId" "1234567890" --project src/CallForMe.Api
```

`Twilio:PublicBaseUrl` must be an HTTPS address reachable by Twilio. The service derives the
ConversationRelay `wss://` endpoint from that address.

## Live client

Connect a SignalR client to `/hubs/calls`, call `SubscribeCall(callId)`, and listen for:

- `CallUpdated`
- `TranscriptAdded`

## Promo codes and balance

Admin users can open settings in the UI and create promo codes with a balance amount and optional
activation limit. Users enter a promo code from the sidebar; the balance is tied to a browser client
id stored in local storage.

TON and USDT top-ups use an admin-owned wallet address. Create or open a wallet in Tonkeeper, use
Receive -> TON, and paste only the public address into the admin settings. Do not paste a seed phrase
or private key into the app. For USDT on TON, use the same TON wallet address and set the network to
`TON`; the generated QR code uses Tonkeeper transfer links and the official USDt-on-TON jetton master
address.

Relevant endpoints:

- `GET /api/balance/{clientId}`
- `POST /api/promocodes/redeem`
- `GET /api/admin/promocodes`
- `POST /api/admin/promocodes`
- `PATCH /api/admin/promocodes/{id}`

## MCP server

Run the MCP stdio server with:

```powershell
$env:CALLFORME_API_URL = "http://localhost:5226"
$env:CALLFORME_ADMIN_PASSWORD = "your-admin-password"
dotnet run --project src/CallForMe.Mcp
```

The MCP server exposes tools for listing/creating calls, sending messages, ending calls, reading
balance, redeeming promo codes, and admin promo-code management. It talks to the HTTP API instead of
reading the database directly.

## Production notes

- SQLite is fine for one API instance. Use PostgreSQL before running multiple API instances.
- Put the service behind HTTPS and keep Twilio signature validation enabled.
- WebSocket handshakes and HTTP callbacks are validated with `X-Twilio-Signature`.
- Add authentication and authorization before exposing operator endpoints.
- Confirm recording and AI-disclosure requirements for every calling jurisdiction.

## GitHub Actions deploy

The `.github/workflows/deploy.yml` workflow builds the React frontend, publishes the ASP.NET API as
a self-contained Linux x64 app, uploads it over SSH, preserves the server `data` directory and
`appsettings.Local.json`, then restarts the API.

It runs on pushes to `main` and can also be started manually from the GitHub Actions tab.

Repository secrets required:

- `DEPLOY_HOST` - server IP or hostname.
- `DEPLOY_USER` - SSH user.
- `DEPLOY_SSH_KEY` - private SSH key allowed on the server.

Optional repository secrets:

- `DEPLOY_PORT` - SSH port, defaults to `22`.
- `DEPLOY_PATH` - app directory, defaults to `/home/skynet/call-for-mi`.
