import { AppView } from "./components/AppView.jsx";
import { useCallForMeApp } from "./hooks/useCallForMeApp.js";
import { useSeoMetadata } from "./hooks/useSeoMetadata.js";

export function App() {
  useSeoMetadata();

  const app = useCallForMeApp();
  return <AppView app={app} />;
}
