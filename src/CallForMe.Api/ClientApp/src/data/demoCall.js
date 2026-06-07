const demoStartedAt = Date.now() - 9 * 60 * 1000;

export const demoCall = {
  id: "demo-call",
  displayName: "Пример звонка",
  phoneNumber: "+48123456789",
  prompt: "Уточнить свободное время для записи и попросить подтверждение по SMS.",
  language: "pl-PL",
  userLanguage: "ru-RU",
  autoPilot: false,
  status: "Completed",
  createdAt: new Date(demoStartedAt).toISOString(),
  updatedAt: new Date(demoStartedAt + 2 * 60 * 1000).toISOString(),
  durationSeconds: 74,
  transcript: [
    {
      id: "demo-1",
      speaker: "Assistant",
      text: "Здравствуйте. Я звоню от имени клиента, чтобы узнать доступное время для записи.",
      translation: "Dzien dobry. Dzwonie w imieniu klienta, aby zapytac o wolny termin.",
      timestamp: new Date(demoStartedAt).toISOString()
    },
    {
      id: "demo-2",
      speaker: "Remote",
      text: "Mamy wolne miejsce jutro o 10:30 albo w piatek po poludniu.",
      translation: "Есть свободное место завтра в 10:30 или в пятницу после обеда.",
      timestamp: new Date(demoStartedAt + 60 * 1000).toISOString()
    },
    {
      id: "demo-3",
      speaker: "Assistant",
      text: "Подтвердите, пожалуйста, пятницу после обеда и отправьте SMS с адресом.",
      translation: "Prosze potwierdzic piatek po poludniu i wyslac SMS z adresem.",
      timestamp: new Date(demoStartedAt + 2 * 60 * 1000).toISOString()
    }
  ],
  suggestions: [],
  summary: {
    title: "Запись согласована",
    outcome: "Служба предложила два времени. Выбран вариант в пятницу после обеда.",
    nextSteps: ["Дождаться SMS с адресом", "Взять документ, если он нужен для визита"]
  },
  isDemo: true
};
