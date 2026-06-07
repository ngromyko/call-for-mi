import { AppView } from "./components/AppView.jsx";
import { useCallForMeApp } from "./hooks/useCallForMeApp.js";

export function App() {
  const app = useCallForMeApp();
  return <AppView app={app} />;
}
