#include "../../config.h"
#include "provider.h"
#include "ui.h"
#include "../../globals.h"
#include "draw.h"
#include "notes.h"
#include "tasks.h"
#include "task_visibility.h"
#include "settings.h"
#include "calendar_week.h"
#include "home_almanac.h"
#include "home_weather.h"
#include "home_holidays.h"
#include "home_delivery.h"
#include "home_bridge_status.h"
#include "battery.h"

#include <ctime>

namespace {
constexpr int kInset = 14;
constexpr int kRight = W - kInset;
constexpr int kContentWidth = W - 2 * kInset;
constexpr int kHeaderY = 10;
constexpr int kHomeHeaderCenterY = kHeaderY + 7;
constexpr int kHomeSideSlotOffset = 62;
constexpr int kBatteryBodyWidth = 23;
constexpr int kBatteryHeight = 12;
constexpr int kBatteryContactWidth = 2;
constexpr int kBatteryWidth = kBatteryBodyWidth + kBatteryContactWidth;
constexpr int kHomeBatteryX = kRight - kBatteryWidth;
constexpr int kHomeStatusGap = 7;
constexpr int kHomeStatusWidth = 11;
constexpr int kHomeStatusHeight = 14;
constexpr int kHomeStatusX = kHomeBatteryX - kHomeStatusGap - kHomeStatusWidth;
static_assert(kHomeStatusX >= kInset && kHomeBatteryX + kBatteryWidth <= W);
constexpr int kHeaderRuleY = 31;
constexpr int kHomeWeatherHeight = 17;
constexpr int kHomeDeliveryRuleY = 141;
constexpr int kHomeDeliveryColumnGap = 4;
constexpr int kHomeDeliveryLabelY = 146;
constexpr int kHomeDeliverySecondLineY = 164;
constexpr int kHomeDeliveryCountY = 182;
constexpr int kContentTop = 43;
constexpr int kFooterRuleY = 174;
constexpr int kFooterTextY = 181;
constexpr int kAnswerLinesPerPage = 6;

struct UiTranslation { const char* en; const char* fr; const char* es; const char* it; const char* pl; const char* cs; const char* ru; const char* uk; const char* nl; const char* pt; const char* sk; };
const UiTranslation kUiTranslations[] = {
  {"TODAY","AUJOURD'HUI","HOY","OGGI","DZISIAJ","DNES","SEGODNYA","SOHODNI","VANDAAG","HOJE","DNES"},
  {"TASKS","TACHES","TAREAS","ATTIVITA","ZADANIA","UKOLY","ZADACHI","ZAVDANNYA","TAKEN","TAREFAS","ULOHY"},
  {"EVENTS","RENDEZ-VOUS","EVENTOS","EVENTI","TERMINY","TERMINY","SOBYTIYA","PODIYI","AFSPRAKEN","EVENTOS","TERMINY"},
  {"NEXT","SUIVANT","SIGUIENTE","AVANTI","DALEJ","DALSI","DALEE","DALI","VOLGENDE","SEGUINTE","DALEJ"},
  {"BACK","RETOUR","ATRAS","INDIETRO","WSTECZ","ZPET","NAZAD","NAZAD","TERUG","VOLTAR","SPAT"},
  {"NOTE","NOTE","NOTA","NOTA","NOTATKA","POZNAMKA","ZAPISKA","NOTATKA","NOTITIE","NOTA","POZNAMKA"},
  {"ASK","QUESTION","PREGUNTA","DOMANDA","PYTANIE","DOTAZ","VOPROS","PYTANNYA","VRAAG","PERGUNTA","OTAZKA"},
  {"LISTENING","ECOUTE","ESCUCHANDO","ASCOLTO","SLUCHAM","POSLOUCHAM","SLUSHAYU","SLUKHAYU","LUISTEREN","OUVINDO","POCUVAM"},
  {"TRANSCRIBING","TRANSCRIPTION","TRANSCRIBIENDO","TRASCRIZIONE","TRANSKRYPCJA","PREPISUJI","RASSHIFROVKA","TRANSKRYBUYU","TRANSCRIBEREN","TRANSCREVENDO","PREPISUJEM"},
  {"RECORDING","ENREGISTRE","GRABANDO","REGISTRA","NAGRYWANIE","NAHRAVANI","ZAPIS","ZAPYS","OPNAME","GRAVANDO","NAHRAVAM"},
  {"SAVED","GARDE","GUARDADO","SALVATO","ZAPISANO","ULOZENO","SOHRANENO","ZBEREZHENO","OPGESLAGEN","GUARDADO","ULOZENE"},
  {"ANSWER","REPONSE","RESPUESTA","RISPOSTA","ODPOWIEDZ","ODPOVED","OTVET","VIDPOVID","ANTWOORD","RESPOSTA","ODPOVED"},
  {"FROM MEMORY","DE LA MEMOIRE","DE MEMORIA","DA MEMORIA","Z PAMIECI","Z PAMETI","IZ PAMYATI","Z PAMYATI","UIT GEHEUGEN","DA MEMORIA","ZO SPOMIENOK"},
  {"END","FIN","FIN","FINE","KONIEC","KONEC","KONEC","KINETS","EINDE","FIM","KONIEC"},
  {"READY","PRET","LISTO","PRONTO","GOTOWE","PRIPRAVENO","GOTOVO","HOTOVO","KLAAR","PRONTO","PRIPRAVENE"},
  {"CONNECTING","CONNEXION","CONECTANDO","CONNESSIONE","LACZENIE","PRIPOJOVANI","PODKLYUCHENIE","PIDKLYUCHENNYA","VERBINDEN","CONECTANDO","PRIPAJAM"},
  {"ORGANIZING","CLASSEMENT","ORGANIZANDO","ORGANIZZO","PORZADKUJE","TRIDIM","SORTIRUYU","SORTUYU","ORDENEN","ORGANIZANDO","TRIEDIM"},
  {"REMEMBERING","MEMORISE","RECORDANDO","RICORDO","ZAPAMIETUJE","PAMATUJI","ZAPOMINAYU","ZAPAMYATOVUYU","ONTHOUDEN","MEMORIZANDO","UKLADAM"},
  {"THINKING","REFLEXION","PENSANDO","PENSO","MYSLE","PREMYSLIM","DUMAYU","DUMAYU","DENKEN","PENSANDO","PREMYSLAM"},
  {"SEARCHING","RECHERCHE","BUSCANDO","CERCO","SZUKAM","HLEDAM","ISHCHU","SHUKAYU","ZOEKEN","PROCURANDO","HLADAM"},
  {"ALMOST THERE","PRESQUE FINI","CASI LISTO","QUASI FATTO","PRAWIE GOTOWE","TEMER HOTOVO","POCHTI GOTOVO","MAYZHE HOTOVO","BIJNA KLAAR","QUASE PRONTO","TAKMER HOTOVO"},
  {"TRY AGAIN","REESSAYER","REINTENTAR","RIPROVA","SPROBUJ","ZNOVU","POVTORIT","SPROBUVATY","OPNIEUW","TENTAR NOVAMENTE","SKUS ZNOVA"}
  ,{"ONE MOMENT","UN MOMENT","UN MOMENTO","UN MOMENTO","CHWILA","MOMENT","MOMENT","KHVYLYNU","EVEN WACHTEN","UM MOMENTO","MOMENT"}
  ,{"SAVED FOR LATER","GARDE POUR PLUS TARD","GUARDADO PARA DESPUES","SALVATO PER DOPO","ZAPISANE NA POZNIEJ","ULOZENO NA POZDEJI","SOHRANENO NA POTOM","ZBEREZHENO NA POTIM","BEWAARD VOOR LATER","GUARDADO PARA DEPOIS","ULOZENE NA NESKOR"}
  ,{"RECALLING","RAPPEL","RECORDANDO","RICHIAMO","PRZYPOMINAM","VYBAVUJI","VSPOMINAYU","ZGHADUYU","OPHALEN","RELEMBRANDO","SPOMINAM"}
  ,{"NO ANSWER","PAS DE REPONSE","SIN RESPUESTA","NESSUNA RISPOSTA","BRAK ODPOWIEDZI","BEZ ODPOVEDI","NET OTVETA","NEMA VIDPOVIDI","GEEN ANTWOORD","SEM RESPOSTA","BEZ ODPOVEDE"}
  ,{"NO","AUCUN","NINGUNO","NESSUNO","BRAK","ZADNE","NET","NEMA","GEEN","SEM","ZIADNE"}
  ,{"ALL","TOUT","TODO","TUTTO","WSZYSTKO","VSE","VSE","USE","ALLES","TUDO","VSETKO"}
  ,{"DONE","FAIT","HECHO","FATTO","GOTOWE","HOTOVO","GOTOVO","HOTOVO","KLAAR","FEITO","HOTOVO"}
  ,{"AGAIN: BACK","ENCORE: RETOUR","OTRA VEZ: ATRAS","ANCORA: INDIETRO","PONOWNIE: WSTECZ","ZNOVU: ZPET","SNOVA: NAZAD","ZNOVU: NAZAD","NOGMAALS: TERUG","NOVAMENTE: VOLTAR","ZNOVU: SPAT"}
  ,{"NOT READY YET","PAS ENCORE PRET","AUN NO LISTO","NON ANCORA PRONTO","JESZCZE NIE GOTOWE","JESTE NEPRIPRAVENO","ESHCHO NE GOTOVO","SHCHE NE HOTOVO","NOG NIET KLAAR","AINDA NAO PRONTO","CAKA NA NASTAVENIE"}
  ,{"SET UP","CONFIGURER","CONFIGURAR","CONFIGURA","KONFIGURUJ","NASTAVIT","NASTROIT","NALASHTUVATY","INSTELLEN","CONFIGURAR","NASTAVIT"}
  ,{"IN PALA FLASHER","DANS PALA FLASHER","EN PALA FLASHER","IN PALA FLASHER","W PALA FLASHER","V PALA FLASHER","V PALA FLASHER","U PALA FLASHER","IN PALA FLASHER","NO PALA FLASHER","V PALA FLASHER"}
  ,{"SOMETHING WENT WRONG","UNE ERREUR","ALGO SALIO MAL","QUALCOSA NON VA","COS POSZLO NIE TAK","NECO SE NEPOVEDLO","CHTO-TO NE TAK","SHCHOS NE TAK","ER GING IETS MIS","ALGO CORREU MAL","NIECO SA NEPODARILO"}
  ,{"MEMORIES","SOUVENIRS","RECUERDOS","RICORDI","WSPOMNIENIA","VZPOMINKY","VOSPOMINANIYA","SPHADY","HERINNERINGEN","MEMORIAS","SPOMIENKY"}
};

const char* translatedUi(const char* english) {
  const String language = palaUiLanguage();
  if (language == "sk") {
    static const char* const extra[][2] = {
      {"DATE NOT SET", "CHYBA DATUM"},
      {"JOIN WI-FI", "PRIPOJ WI-FI"},
      {"PASSWORD", "HESLO"},
      {"OPEN IN BROWSER", "OTVOR V PREHLIADACI"},
      {"SAME WI-FI", "ROVNAKA WI-FI"},
      {"WORKING", "PRACUJEM"},
      {"SETUP", "NASTAVENIE"},
      {"SETTINGS", "NASTAVENIA"},
      {"SPEAKER", "REPRODUKTOR"},
      {"UPPER +", "HORE +"},
      {"LOWER -", "DOLE -"},
      {"PRESS TO WAKE", "STLAC NA PREBUDENIE"},
      {"RESTING", "SPANOK"},
      {"SYNC", "SYNCHRONIZACIA"},
      {"LOCAL", "LOKALNE"},
      {"WITH MEMORY", "ZO SPOMIENOK"},
      {"WIFI ERROR", "CHYBA WI-FI"},
      {"NO SPEECH", "BEZ RECI"},
      {"PROCESSING FAILED", "CHYBA SPRACOVANIA"},
      {"STORAGE FULL", "PLNA PAMAT"},
      {"RECORD AGAIN", "NAHRAJ ZNOVA"},
      {"ASK AGAIN", "OPYTAJ SA ZNOVA"},
      {"KUBO DISABLED", "KUBO VYPNUTE"},
      {"NO WIFI", "BEZ WI-FI"},
      {"VOICE FAILED", "CHYBA HLASU"},
      {"BRIEF FAILED", "CHYBA SUHRNU"},
      {"SD ERROR", "CHYBA SD"},
      {"COMPANION", "SPRIEVODCA"},
      {"Wi-Fi network", "Siet Wi-Fi"},
      {"Not connected", "Bez pripojenia"},
      {"Key", "Heslo"},
      {"Volume", "Hlasitost"},
      {"Hold both to exit", "Podrz obe na navrat"},
    };
    for (const auto& pair : extra) if (!strcmp(english, pair[0])) return pair[1];
  }
  for (const auto& row : kUiTranslations) {
    if (strcmp(row.en, english)) continue;
    if (language == "sk") return row.sk;
    if (language == "fr") return row.fr; if (language == "es") return row.es;
    if (language == "it") return row.it; if (language == "pl") return row.pl;
    if (language == "cs") return row.cs; if (language == "ru") return row.ru;
    if (language == "uk") return row.uk; if (language == "nl") return row.nl;
    if (language == "pt") return row.pt; return english;
  }
  return english;
}

const char* uiText(const char* german, const char* english) {
  const String language = palaUiLanguage();
  if (language == "de") return german;
  return translatedUi(english);
}

void labelLeft(int y, const char* text, int scale, uint8_t color, int maxWidth = kContentWidth) {
  if (!text || !strlen(text)) return;
  while (scale > 1 && textW(text, scale) > maxWidth) scale--;
  drawStrFit(kInset, y, maxWidth, text, scale, color);
}

void labelRight(int y, const char* text, int scale, uint8_t color, int maxWidth = kContentWidth) {
  if (!text || !strlen(text)) return;
  while (scale > 1 && textW(text, scale) > maxWidth) scale--;
  const int width = min(textW(text, scale), maxWidth);
  drawStrFit(kRight - width, y, width, text, scale, color);
}

void drawHeader(const char* right, uint8_t color) {
  labelLeft(kHeaderY, palaNoteName().c_str(), 1, color, 76);
  labelRight(kHeaderY, right, 1, color, 88);
  hline(kInset, kHeaderRuleY, kContentWidth, color);
}

void drawFooter(const char* left, const char* right, uint8_t color) {
  hline(kInset, kFooterRuleY, kContentWidth, color);
  // Footer copy is interaction guidance, not user content. Never run it
  // through drawStrFit(), because an instruction such as ZURUECK must not turn
  // into an ambiguous ZUR... . All footer labels are deliberately authored to
  // fit at scale 1 and are drawn verbatim.
  if (left && strlen(left)) drawStr(kInset, kFooterTextY, left, 1, color);
  if (right && strlen(right))
    drawStr(kRight - textW(right, 1), kFooterTextY, right, 1, color);
}

void drawFooterCentered(const char* text, uint8_t color) {
  hline(kInset, kFooterRuleY, kContentWidth, color);
  if (text && strlen(text)) drawStrC(W / 2, kFooterTextY, text, 1, color);
}

void drawBattery(int percent, int y) {
  const int bars = batteryBarCount(percent);
  if (bars < 0) return;
  constexpr int x = kHomeBatteryX;
  strokeRect(x, y, kBatteryBodyWidth, kBatteryHeight, 1, BLACK);
  fillRect(x + kBatteryBodyWidth, y + 3, kBatteryContactWidth, 6, BLACK);
  for (int i = 0; i < bars; i++) fillRect(x + 3 + i * 6, y + 3, 5, 6, BLACK);
}

void drawCompanionHeader(int batteryPercent) {
  constexpr const char* mode = "WEB";
  const int nameWidth = W / 2 - textW(mode, 1) / 2 - kInset - 6;
  labelLeft(kHeaderY, palaNoteName().c_str(), 1, BLACK, nameWidth);
  drawStrC(W / 2, kHeaderY, mode, 1, BLACK);
  drawBattery(batteryPercent, 9);
  hline(kInset, kHeaderRuleY, kContentWidth, BLACK);
}

void drawHero(const char* text, int y, uint8_t color, int maxBottom = 122) {
  if (!text || !strlen(text)) return;
  for (int scale = 3; scale >= 1; scale--) {
    if (textW(text, scale) <= kContentWidth && y + uiFontHeight(scale) <= maxBottom) {
      labelLeft(y, text, scale, color);
      return;
    }
  }
  labelLeft(y, text, 1, color);
}

void drawTwoLineHero(const char* first, const char* second, int y, uint8_t color) {
  int scale = 3;
  while (scale > 1 && (textW(first, scale) > kContentWidth || textW(second, scale) > kContentWidth)) scale--;
  const int lineHeight = uiFontHeight(scale) + 4;
  labelLeft(y, first, scale, color);
  labelLeft(y + lineHeight, second, scale, color);
}

void drawStatusHero(const char* text, int y, uint8_t color) {
  if (!text || !*text) return;
  // Do not use labelLeft here: it silently falls back to regular-weight text.
  if (textW(text, 2) <= kContentWidth) {
    drawStr(kInset, y, text, 2, color);
    return;
  }
  // Never fall back to the thin body font for a long status. Split it into
  // two bold lines instead (for example SPAETER / WEITER).
  String value = text;
  int split = value.lastIndexOf(' ', value.length() / 2 + 1);
  if (split <= 0) split = value.indexOf(' ', value.length() / 2);
  if (split > 0) {
    const String first = value.substring(0, split);
    const String second = value.substring(split + 1);
    if (textW(first.c_str(), 2) <= kContentWidth && textW(second.c_str(), 2) <= kContentWidth) {
      drawStr(kInset, y - 7, first.c_str(), 2, color);
      drawStr(kInset, y + 19, second.c_str(), 2, color);
      return;
    }
  }
  // Long translations (including single words) wrap in a compact bold font.
  // Device status translations are ASCII/transliterated, unlike user content.
  String remaining = text;
  int lineY = y;
  while (remaining.length()) {
    unsigned int count = remaining.length();
    while (count > 1 && textW(remaining.substring(0, count).c_str(), -1) > kContentWidth) --count;
    if (count < remaining.length()) {
      const int space = remaining.lastIndexOf(' ', count);
      if (space > 0) count = space;
    }
    drawStr(kInset, lineY, remaining.substring(0, count).c_str(), -1, color);
    remaining = remaining.substring(count);
    remaining.trim();
    lineY += 18;
  }
}

void drawProgressSegments(int current, int total, int y, uint8_t color) {
  total = constrain(total, 1, 5);
  current = constrain(current, 0, total);
  constexpr int gap = 6;
  const int width = (kContentWidth - (total - 1) * gap) / total;
  for (int i = 0; i < total; i++) {
    const int x = kInset + i * (width + gap);
    if (i < current) fillRect(x, y, width, 6, color);
    else strokeRect(x, y, width, 6, 1, color);
  }
}

void drawTimer(uint32_t elapsedMs, uint8_t color) {
  const uint32_t seconds = elapsedMs / 1000;
  char timer[12];
  snprintf(timer, sizeof(timer), "%02lu:%02lu",
           static_cast<unsigned long>(seconds / 60),
           static_cast<unsigned long>(seconds % 60));
  drawBigStrC(100, 113, 39, timer, color);
}

int memoryStage(const char* phase) {
  if (!phase) return 0;
  if (!strcmp(phase, "CONNECTING")) return 1;
  if (!strcmp(phase, "DECODING")) return 2;
  if (!strcmp(phase, "UNDERSTANDING") || !strcmp(phase, "ORGANIZING") || !strcmp(phase, "REMEMBERING")) return 3;
  if (!strcmp(phase, "QUEUED")) return 0;
  return 0;
}

int askStage(const char* action) {
  if (!action) return 0;
  if (!strcmp(action, "CONNECTING")) return 1;
  if (!strcmp(action, "THINKING") || !strcmp(action, "DECODING") ||
      !strcmp(action, "UNDERSTANDING") || !strcmp(action, "PREPARING") ||
      !strcmp(action, "TODAY") || !strcmp(action, "SEARCHING")) return 2;
  if (!strcmp(action, "BUILDING")) return 3;
  return 0;
}

const char* memoryPhaseCopy(const char* phase) {
  if (!phase) return uiText("MOMENT...", "ONE MOMENT");
  if (!strcmp(phase, "CONNECTING")) return uiText("VERBINDE", "CONNECTING");
  if (!strcmp(phase, "SYNCING")) return uiText("SYNCHRONISIERE", "SYNCING");
  if (!strcmp(phase, "DECODING")) return uiText("TRANSKRIBIERE", "TRANSCRIBING");
  if (!strcmp(phase, "UNDERSTANDING") || !strcmp(phase, "ORGANIZING")) return uiText("ORDNE EIN", "ORGANIZING");
  if (!strcmp(phase, "REMEMBERING")) return uiText("MERKE MIR", "REMEMBERING");
  if (!strcmp(phase, "QUEUED")) return uiText("SPAETER WEITER", "SAVED FOR LATER");
  if (!strcmp(phase, "DONE")) return uiText("FERTIG", "DONE");
  return uiText("MOMENT...", "ONE MOMENT");
}

const char* askPhaseCopy(const char* action) {
  if (!action) return uiText("DENKE NACH", "THINKING");
  if (!strcmp(action, "CONNECTING")) return uiText("VERBINDE", "CONNECTING");
  if (!strcmp(action, "DECODING") || !strcmp(action, "UNDERSTANDING")) return uiText("HOERE REIN", "LISTENING");
  if (!strcmp(action, "PREPARING")) return uiText("HOLE ES", "RECALLING");
  if (!strcmp(action, "SEARCHING")) return uiText("SUCHE...", "SEARCHING");
  if (!strcmp(action, "BUILDING")) return uiText("FAST DA", "ALMOST THERE");
  if (!strcmp(action, "TODAY")) return uiText("HEUTE", "TODAY");
  return uiText("DENKE NACH", "THINKING");
}

std::vector<String> answerLines(const String& raw) {
  const String text = normalizeForDisplay(raw);
  std::vector<String> lines;
  String line;
  int pos = 0;
  while (pos < static_cast<int>(text.length())) {
    while (pos < static_cast<int>(text.length()) && text[pos] == ' ') pos++;
    if (pos >= static_cast<int>(text.length())) break;
    int end = text.indexOf(' ', pos);
    if (end < 0) end = text.length();
    String word = text.substring(pos, end);
    pos = end + 1;
    while (textW(word.c_str(), 1) > kContentWidth) {
      int cut = word.length();
      while (cut > 1 && textW((word.substring(0, cut) + "-").c_str(), 1) > kContentWidth) cut--;
      if (line.length()) { lines.push_back(line); line = ""; }
      lines.push_back(word.substring(0, cut) + "-");
      word = word.substring(cut);
    }
    const String candidate = line.length() ? line + " " + word : word;
    if (textW(candidate.c_str(), 1) <= kContentWidth) line = candidate;
    else { if (line.length()) lines.push_back(line); line = word; }
  }
  if (line.length()) lines.push_back(line);
  if (lines.empty()) lines.push_back(uiText("KEINE ANTWORT", "NO ANSWER"));
  return lines;
}

String greetingLabel() {
  const String local = utcToLocalDeviceLabel(currentUtcIso());
  if (local.length() < 13 || local == "time not set") return "TODAY";
  const int hour = local.substring(11, 13).toInt();
  if (hour < 12) return "GUTEN MORGEN";
  if (hour < 18) return "GUTEN TAG";
  return "GUTEN ABEND";
}

String nextTodayItem(const std::vector<BrainTaskRecord>& tasks, const String& todayKey) {
  String candidate;
  String candidateDue;
  for (const BrainTaskRecord& task : tasks) {
    if (task.done || task.type == "event") continue;
    if (!task.dueUtc.length()) {
      if (!candidate.length()) candidate = task.text;
      continue;
    }
    if (!todayKey.length()) continue;
    const String localDue = utcToLocalDeviceLabel(task.dueUtc);
    if (localDue.length() < 10 || localDue == "time not set") continue;
    if (localDue.substring(0, 10) > todayKey) continue;
    if (!candidate.length() || localDue < candidateDue) {
      candidate = task.text;
      candidateDue = localDue;
    }
  }
  return normalizeForDisplay(candidate);
}

std::vector<BrainTaskRecord> todayAgendaItems(bool events) {
  const String nowUtc = currentUtcIso();
  const String localNow = utcToLocalDeviceLabel(nowUtc);
  const String todayKey = localNow.length() >= 10 && localNow != "time not set"
                            ? localNow.substring(0, 10) : "";
  std::vector<BrainTaskRecord> items;
  for (const BrainTaskRecord& item : loadBrainTasks()) {
    if (item.done) continue;
    if (!item.dueUtc.length()) {
      if (!events && undatedTaskVisible(item.type == "event", item.done)) items.push_back(item);
      continue;
    }
    if (!todayKey.length()) continue;
    const String localDue = utcToLocalDeviceLabel(item.dueUtc);
    if (localDue.length() < 10 || localDue == "time not set") continue;
    const String dueDay = localDue.substring(0, 10);
    if (events ? (item.type == "event" && dueDay == todayKey && eventStillRelevant(
                   dueDay.c_str(), todayKey.c_str(), item.dueUtc.c_str(), nowUtc.c_str(),
                   item.datePrecision == "day"))
               : (item.type != "event" && dueDay <= todayKey)) items.push_back(item);
  }
  std::sort(items.begin(), items.end(), [](const BrainTaskRecord& a, const BrainTaskRecord& b) {
    return a.dueUtc < b.dueUtc;
  });
  return items;
}

constexpr int kAgendaLinesPerPage = 5;

int agendaPages(const std::vector<BrainTaskRecord>& items) {
  int pages = 0;
  for (const BrainTaskRecord& item : items) {
    const int lines = max(1, static_cast<int>(answerLines(item.text).size()));
    pages += (lines + kAgendaLinesPerPage - 1) / kAgendaLinesPerPage;
  }
  return max(1, pages);
}

void drawSunGlyph(int x, int y, bool rays) {
  strokeCircle(x, y, 4, 1, BLACK);
  if (!rays) return;
  hline(x - 8, y, 2, BLACK);
  hline(x + 7, y, 2, BLACK);
  vline(x, y - 8, 2, BLACK);
  vline(x, y + 7, 2, BLACK);
  for (int dx : {-1, 1}) {
    for (int dy : {-1, 1}) line(x + 5 * dx, y + 5 * dy, x + 6 * dx, y + 6 * dy, BLACK);
  }
}

void drawCloudGlyph(int x, int y) {
  static constexpr int outline[][2] = {
    {-8, 4}, {-9, 2}, {-8, -1}, {-5, -1}, {-3, -5}, {0, -6},
    {3, -5}, {5, -2}, {7, -2}, {10, 1}, {10, 3}, {7, 6}, {-6, 6}, {-8, 4},
  };
  for (size_t i = 1; i < sizeof(outline) / sizeof(outline[0]); ++i) {
    line(x + outline[i - 1][0], y + outline[i - 1][1],
         x + outline[i][0], y + outline[i][1], BLACK);
  }
}

void drawWeatherGlyph(int x, int y, WeatherGlyph glyph) {
  switch (glyph) {
    case WeatherGlyph::Sun: drawSunGlyph(x, y, true); return;
    case WeatherGlyph::PartlyCloudy:
      drawSunGlyph(x - 5, y - 4, false);
      hline(x - 13, y - 4, 2, BLACK);
      line(x - 12, y - 8, x - 11, y - 7, BLACK);
      fillRect(x - 8, y - 4, 12, 6, WHITE);
      drawCloudGlyph(x + 2, y + 2);
      return;
    case WeatherGlyph::Cloud: drawCloudGlyph(x, y); return;
    case WeatherGlyph::Fog:
      hline(x - 8, y - 5, 17, BLACK);
      hline(x - 6, y, 14, BLACK);
      hline(x - 9, y + 5, 17, BLACK);
      return;
    case WeatherGlyph::Rain:
    case WeatherGlyph::Snow:
    case WeatherGlyph::Thunder:
      drawCloudGlyph(x, y - 2);
      if (glyph == WeatherGlyph::Rain) {
        for (int dx : {-5, 0, 5}) line(x + dx, y + 6, x + dx - 1, y + 8, BLACK);
      } else if (glyph == WeatherGlyph::Snow) {
        for (int dx : {-5, 0, 5}) {
          px(x + dx, y + 6, BLACK);
          px(x + dx + 1, y + 8, BLACK);
        }
      } else {
        line(x + 1, y + 5, x - 2, y + 6, BLACK);
        line(x - 2, y + 6, x + 2, y + 6, BLACK);
        line(x + 2, y + 6, x, y + 8, BLACK);
      }
      return;
    case WeatherGlyph::Unknown: return;
  }
}

void drawMoonGlyph(int x, int y, MoonPhase phase) {
  constexpr int radius = 8;
  if (phase == MoonPhase::Unknown) return;
  if (phase == MoonPhase::Full) {
    fillCircle(x, y, radius, BLACK);
    return;
  }
  strokeCircle(x, y, radius, 1, BLACK);
  if (phase == MoonPhase::New) return;
  const int index = static_cast<int>(phase);
  const int illumination = index < 4 ? index : 8 - index;
  const int edge = illumination == 1 ? 7 : illumination == 2 ? 0 : -7;
  for (int dy = -radius + 1; dy < radius; ++dy) {
    int span = radius - 1;
    while (span * span + dy * dy >= radius * radius) --span;
    for (int dx = -span; dx <= span; ++dx) {
      if ((index < 4 ? dx : -dx) * 10 >= edge * span) px(x + dx, y + dy, BLACK);
    }
  }
}

void drawHomeWeather(int top, const String& todayKey, int64_t utc) {
  const int centerY = top + kHomeWeatherHeight / 2;
  HomeForecast forecast{};
  if (homeForecastFor(todayKey, forecast)) {
    drawWeatherGlyph(kInset + 19, centerY, weatherGlyph(forecast.code));
    char temperature[17];
    snprintf(temperature, sizeof(temperature), "%d / %d", forecast.minimum, forecast.maximum);
    int minX, minY, maxX, maxY, advance;
    textBoundsFont(temperature, 1, &minX, &minY, &maxX, &maxY, &advance);
    const int width = max(maxX - minX, advance);
    const int x = (W - width - 5) / 2;
    const int textY = centerY - (maxY - minY - 1) / 2;
    drawStr(x, textY, temperature, 1, BLACK);
    strokeCircle(x + width + 2, textY + 2, 2, 1, BLACK);
  }
  drawMoonGlyph(kRight - 19, centerY, moonPhaseAt(utc));
}

void drawHomeWeek(const String& todayKey, int64_t utc) {
  constexpr int top = kHeaderRuleY + 1;
  constexpr int weekdayScale = 3;
  constexpr int labelScale = 1;
  constexpr int sideGap = 5;
  constexpr int highlightPaddingTop = 3;
  constexpr int highlightHeight = 18;
  constexpr int dividerGap = 7;
  constexpr int dateHighlightTop = kHomeDeliveryRuleY - dividerGap - highlightHeight;
  constexpr int dateY = dateHighlightTop + highlightPaddingTop;
  constexpr int weatherY = top + dividerGap;
  constexpr int weatherBottom = weatherY + kHomeWeatherHeight;
  const int year = todayKey.substring(0, 4).toInt();
  const int month = todayKey.substring(5, 7).toInt();
  const int day = todayKey.substring(8, 10).toInt();
  const auto week = calendarWeek(year, month, day);
  if (!week.number) {
    drawStrC(W / 2, top + (kHomeDeliveryRuleY - top - uiFontHeight(1)) / 2,
        uiText("DATUM FEHLT", "DATE NOT SET"), 1, BLACK);
    return;
  }

  static const char* const months[] = {
    "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
    "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"
  };
  static const char* const weekdays[] = {"MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"};
  char weekNumber[8];
  snprintf(weekNumber, sizeof(weekNumber), "CW%02d", week.number);
  const char* monthLabel = months[month - 1];
  const char* weekdayLabel = weekdays[week.today];
  int minX, minY, maxX, maxY, advance;
  textBoundsFont(weekdayLabel, weekdayScale, &minX, &minY, &maxX, &maxY, &advance);
  const int weekdayWidth = max(maxX - minX, advance);
  const int weekdayHeight = maxY - minY;
  const int weekdayY = weatherBottom + (dateHighlightTop - weatherBottom - weekdayHeight) / 2;
  const int sideHeadingY = weekdayY + (weekdayHeight - uiFontHeight(labelScale)) / 2;
  const int weekdayRight = W / 2 - weekdayWidth / 2 + weekdayWidth;
  if (weekdayRight + sideGap > kRight - textW(weekNumber, labelScale)) {
    snprintf(weekNumber, sizeof(weekNumber), "W%02d", week.number);
  }
  drawHomeWeather(weatherY, todayKey, utc);
  labelLeft(sideHeadingY, monthLabel, labelScale, BLACK);
  drawStrC(W / 2, weekdayY, weekdayLabel, weekdayScale, BLACK);
  labelRight(sideHeadingY, weekNumber, labelScale, BLACK);

  constexpr int weekendStart = 5;
  constexpr int weekendGap = 4;
  constexpr int cell = (kContentWidth - weekendGap) / 7;
  constexpr int weekX = (W - 7 * cell - weekendGap) / 2;
  for (int i = 0; i < 7; i++) {
    const int x = weekX + i * cell + (i >= weekendStart ? weekendGap : 0);
    const bool today = i == week.today;
    if (today) fillRect(x + 1, dateHighlightTop, cell - 2, highlightHeight, BLACK);
    const CalendarDate date = calendarDateInWeek(year, month, day, i - week.today);
    const bool holiday = homeHolidayFor(date.year, date.month, date.day);
    const String value = String(date.day);
    drawStrC(x + cell / 2, dateY, value.c_str(), holiday ? -1 : 1, today ? WHITE : BLACK);
  }
  vline(weekX + weekendStart * cell + weekendGap / 2, dateY - 2, highlightHeight - 2, BLACK);
}

void drawHomeBridgeStatus(HomeBridgeLink link) {
  constexpr uint16_t wifi[kHomeStatusHeight] = {
      0b00000000000, 0b00000000000, 0b00011111000, 0b01100000110,
      0b10000000001, 0b00000000000, 0b00011111000, 0b00100000100,
      0b00000000000, 0b00001110000, 0b00000000000, 0b00000100000,
      0b00000000000, 0b00000000000,
  };
  constexpr uint16_t usb[kHomeStatusHeight] = {
      0b00000100000, 0b00001110000, 0b00011111000, 0b00000100000,
      0b00000100111, 0b00000100111, 0b00000100010, 0b01100100010,
      0b01100100100, 0b00100101000, 0b00010110000, 0b00001100000,
      0b00000100000, 0b00001110000,
  };
  constexpr uint16_t bluetooth[kHomeStatusHeight] = {
      0b00000100000, 0b00000110000, 0b00000101000, 0b00100100100,
      0b00010101000, 0b00001110000, 0b00000100000, 0b00001110000,
      0b00010101000, 0b00100100100, 0b00000101000, 0b00000110000,
      0b00000100000, 0b00000000000,
  };

  const uint16_t* glyph;
  switch (link) {
    case HomeBridgeLink::Wifi: glyph = wifi; break;
    case HomeBridgeLink::Usb: glyph = usb; break;
    case HomeBridgeLink::Bluetooth: glyph = bluetooth; break;
    case HomeBridgeLink::None: return;
  }
  for (int row = 0; row < kHomeStatusHeight; ++row) {
    for (int column = 0; column < kHomeStatusWidth; ++column) {
      if (glyph[row] & (1u << (kHomeStatusWidth - 1 - column))) {
        px(kHomeStatusX + column, kHomeHeaderCenterY - kHomeStatusHeight / 2 + row, BLACK);
      }
    }
  }
}

constexpr int kHomeQuotaDigitWidth = 6;
constexpr int kHomeQuotaSymbolWidth = 7;
constexpr int kHomeQuotaHeight = 9;
constexpr int kHomeQuotaAdvance = kHomeQuotaDigitWidth + 1;

int homeQuotaWidth(int remaining) {
  if (remaining < 0 || remaining > 100) return 0;
  const int digits = remaining == 100 ? 3 : remaining >= 10 ? 2 : 1;
  return digits * kHomeQuotaAdvance + kHomeQuotaSymbolWidth;
}

void drawHomeQuotaPercent(int center, int y, int remaining) {
  // Black bitmap strokes keep the small numeric label readable on e-paper.
  constexpr uint8_t digits[10][kHomeQuotaHeight] = {
    {0x78, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0xcc, 0x78}, // 0
    {0x30, 0x70, 0xb0, 0x30, 0x30, 0x30, 0x30, 0x30, 0xfc}, // 1
    {0x78, 0xcc, 0x0c, 0x18, 0x30, 0x60, 0xc0, 0xc0, 0xfc}, // 2
    {0x78, 0xcc, 0x0c, 0x18, 0x38, 0x0c, 0x0c, 0xcc, 0x78}, // 3
    {0x18, 0x38, 0x58, 0x98, 0x98, 0xfc, 0x18, 0x18, 0x18}, // 4
    {0xfc, 0xc0, 0xc0, 0xf8, 0x0c, 0x0c, 0x0c, 0xcc, 0x78}, // 5
    {0x38, 0x60, 0xc0, 0xf8, 0xcc, 0xcc, 0xcc, 0xcc, 0x78}, // 6
    {0xfc, 0x0c, 0x18, 0x30, 0x30, 0x60, 0x60, 0x60, 0x60}, // 7
    {0x78, 0xcc, 0xcc, 0xcc, 0x78, 0xcc, 0xcc, 0xcc, 0x78}, // 8
    {0x78, 0xcc, 0xcc, 0xcc, 0x7c, 0x0c, 0x0c, 0x18, 0x70}, // 9
  };
  constexpr uint8_t percent[] = {0x62, 0x94, 0x94, 0x68, 0x10, 0x2c, 0x52, 0x52, 0x8c};
  static_assert(sizeof(percent) == kHomeQuotaHeight);

  const int width = homeQuotaWidth(remaining);
  if (!width) return;
  char value[4];
  snprintf(value, sizeof(value), "%d", remaining);
  int x = center - width / 2;
  for (const char* digit = value; *digit; ++digit) {
    drawBitmap1BPP(x, y, digits[*digit - '0'], kHomeQuotaDigitWidth, kHomeQuotaHeight, BLACK);
    x += kHomeQuotaAdvance;
  }
  drawBitmap1BPP(x, y, percent, kHomeQuotaSymbolWidth, kHomeQuotaHeight, BLACK);
}

int drawPendingRecordings(int count) {
  constexpr int right = kHomeStatusX - 6;
  if (count <= 0) return right;
  constexpr int barHeights[] = {3, 7, 11, 6, 3};
  constexpr int barCount = sizeof(barHeights) / sizeof(barHeights[0]);
  constexpr int barWidth = 2;
  constexpr int barGap = 1;
  constexpr int waveWidth = barCount * (barWidth + barGap) - barGap;
  constexpr int labelGap = 6;
  constexpr int groupWidthLimit = 2 * (right - W / 2);

  String label(count);
  if (waveWidth + labelGap + textW(label.c_str(), 1) > groupWidthLimit) {
    label = "100k+";
  }
  const int groupWidth = waveWidth + labelGap + textW(label.c_str(), 1);
  const int x = right - groupWidth;
  for (int i = 0; i < barCount; ++i) {
    const int height = barHeights[i];
    fillRect(x + i * (barWidth + barGap), kHomeHeaderCenterY - height / 2, barWidth, height, BLACK);
  }
  drawStr(x + waveWidth + labelGap, kHeaderY, label.c_str(), 1, BLACK);
  return x;
}

void drawHomeDeliveryValue(int center, const char* first, const char* second, const char* value) {
  drawStrC(center, kHomeDeliveryLabelY, first, 1, BLACK);
  drawStrC(center, kHomeDeliverySecondLineY, second, 1, BLACK);
  constexpr int width = (kContentWidth - kHomeDeliveryColumnGap) / 2;
  drawStrC(center, kHomeDeliveryCountY, value, textW(value, 2) <= width ? 2 : 1, BLACK);
}

void drawHomeDeliveryCount(int center, const char* first, const char* second, int count) {
  char value[8];
  if (count < 0) strcpy(value, "-");
  else if (count > 999999) strcpy(value, "999999+");
  else snprintf(value, sizeof(value), "%d", count);
  drawHomeDeliveryValue(center, first, second, value);
}

void drawDeliveryAccent(int x, int glyphTop, bool caron) {
  const int y = glyphTop - 4;
  if (caron) {
    line(x - 2, y, x, y + 2, BLACK);
    line(x, y + 2, x + 2, y, BLACK);
  } else {
    line(x - 1, y + 2, x + 2, y, BLACK);
  }
}
}  // namespace

void showIdle(int pendingCount, int batteryPercent) {
  clearWhite();
  const String name = palaNoteName();
  const auto counts = homeDeliveryCounts();
  const auto codex = counts.codexAt(time(nullptr), palaPrefersWeeklyCodexLimit());
  constexpr int usageGap = 4;
  constexpr int segmentWidth = 3;
  constexpr int segmentHeight = 4;
  constexpr int segmentStep = segmentWidth + 1;
  constexpr int gaugeGap = 2;
  const int percentWidth = homeQuotaWidth(codex.remaining);
  const int gaugeWidth = codex.segments ? codex.segments * segmentStep - 1 : 0;
  const int usageWidth = max(percentWidth, gaugeWidth);
  const int usageSlot = usageWidth ? usageWidth + usageGap : 0;
  const int nameWidth = textW(name.c_str(), 1);
  const int nameWithUsage = max(kInset, W / 2 - kHomeSideSlotOffset - (nameWidth + usageSlot) / 2);
  const int queueGap = pendingCount > 0 ? 6 : 4;
  const int requestedSpace = usageSlot ? nameWithUsage + nameWidth + usageSlot + queueGap : 0;
  const int queueLeft = drawPendingRecordings(pendingCount);
  const bool showUsage = usageSlot && queueLeft >= requestedSpace;
  const int nameLeft = showUsage ? nameWithUsage : max(kInset, W / 2 - kHomeSideSlotOffset - nameWidth / 2);
  const int availableName = showUsage ? nameWidth : queueLeft - 4 - nameLeft;
  drawStrFit(nameLeft, kHeaderY, availableName, name.c_str(), 1, BLACK);
  if (showUsage) {
    const int center = nameLeft + nameWidth + usageGap + usageWidth / 2;
    const int usageHeight = kHomeQuotaHeight + (codex.segments ? gaugeGap + segmentHeight : 0);
    const int percentY = kHomeHeaderCenterY - usageHeight / 2;
    drawHomeQuotaPercent(center, percentY, codex.remaining);
    const int gaugeY = percentY + kHomeQuotaHeight + gaugeGap;
    for (int i = 0; i < codex.segments; ++i) {
      const int x = center - gaugeWidth / 2 + i * segmentStep;
      if (i < codex.filled) fillRect(x, gaugeY, segmentWidth, segmentHeight, BLACK);
      else hline(x, gaugeY + segmentHeight - 1, segmentWidth, BLACK);
    }
  }
  const HomeBridgeLink bridgeLink = homeBridgeStatus.latest();
  drawHomeBridgeStatus(bridgeLink);
  drawBattery(batteryPercent, kHomeHeaderCenterY - kBatteryHeight / 2);
  hline(kInset, kHeaderRuleY, kContentWidth, BLACK);
  const String nowUtc = currentUtcIso();
  const String localNow = utcToLocalDeviceLabel(nowUtc);
  const String todayKey = localNow.length() >= 10 && localNow != "time not set" ? localNow.substring(0, 10) : "";
  drawHomeWeek(todayKey, time(nullptr));
  hline(kInset, kHomeDeliveryRuleY, kContentWidth, BLACK);
  vline(W / 2, kHomeDeliveryLabelY, 52, BLACK);
  constexpr int halfColumn = (kContentWidth - kHomeDeliveryColumnGap) / 4;
  constexpr int left = kInset + halfColumn, right = kRight - halfColumn;
  const bool sk = palaUiLanguage() == "sk";
  drawHomeDeliveryCount(left, "General", sk ? "otvorene" : "open", counts.generalOpen);
  if (counts.waiting > 0) {
    drawHomeDeliveryCount(right, sk ? "Caka na" : "Awaiting", "Google", counts.waiting);
    if (sk) {
      drawDeliveryAccent(right - 28, kHomeDeliveryLabelY, true);
      drawDeliveryAccent(right - 16, kHomeDeliveryLabelY + 3, false);
    }
  } else {
    drawHomeDeliveryValue(right, sk ? "Posledne" : "Last", sk ? "dorucenie" : "delivery",
                          counts.lastDelivery[0] ? counts.lastDelivery : "-");
    if (sk) {
      drawDeliveryAccent(right + (textW("Posledne", 1) - textW("e", 1)) / 2,
                         kHomeDeliveryLabelY + 3, false);
      drawDeliveryAccent(right - textW("dorucenie", 1) / 2 + textW("doru", 1) + textW("c", 1) / 2,
                         kHomeDeliverySecondLineY + 3, true);
    }
  }
  if (sk) {
    drawDeliveryAccent(left + (textW("otvorene", 1) - textW("e", 1)) / 2,
                       kHomeDeliverySecondLineY + 3, false);
  }
  refresh();
  homeBridgeStatus.drawn(bridgeLink);
}

int todayAgendaPageCount(bool events) {
  return agendaPages(todayAgendaItems(events));
}

void showTodayAgenda(bool events, int page, AgendaNavigation navigation) {
  clearWhite();
  const std::vector<BrainTaskRecord> items = todayAgendaItems(events);
  const int pages = agendaPages(items);
  page = constrain(page, 0, pages - 1);
  char pageLabel[12];
  snprintf(pageLabel, sizeof(pageLabel), "%d/%d", page + 1, pages);
  drawHeader(items.empty() ? nullptr : pageLabel, BLACK);
  labelLeft(kContentTop, events ? uiText("TERMINE", "EVENTS") : uiText("AUFGABEN", "TASKS"), 1, BLACK, 112);

  if (items.empty()) {
    drawTwoLineHero(events ? uiText("KEINE", "NO") : uiText("NICHTS", "ALL"),
                    events ? uiText("TERMINE", "EVENTS") : uiText("OFFEN", "DONE"),
                    76, BLACK);
  } else {
    int remaining = page;
    int selected = 0;
    int firstLine = 0;
    std::vector<String> lines;
    for (int i = 0; i < static_cast<int>(items.size()); i++) {
      lines = answerLines(items[i].text);
      const int itemPages = max(1, (static_cast<int>(lines.size()) + kAgendaLinesPerPage - 1) /
                                       kAgendaLinesPerPage);
      if (remaining < itemPages) {
        selected = i;
        firstLine = remaining * kAgendaLinesPerPage;
        break;
      }
      remaining -= itemPages;
    }
    const String localDue = utcToLocalDeviceLabel(items[selected].dueUtc);
    String meta = events && items[selected].datePrecision != "day" && localDue.length() >= 16 ? localDue.substring(11, 16)
                                                    : String(selected + 1);
    labelRight(kContentTop, meta.c_str(), 1, BLACK, 58);
    for (int i = 0; i < kAgendaLinesPerPage && firstLine + i < static_cast<int>(lines.size()); i++)
      drawStr(kInset, 68 + i * 19, lines[firstLine + i].c_str(), 1, BLACK);
  }
  if (navigation != AgendaNavigation::EventsShortcut) {
    if (page + 1 < pages || navigation == AgendaNavigation::MoreDisplayPages)
      drawFooter("NEXT", "HOME", BLACK);
    else drawFooterCentered("HOME", BLACK);
  } else if (items.empty() || pages == 1) {
    drawFooterCentered(uiText("NOCHMAL: ZURUECK", "AGAIN: BACK"), BLACK);
  } else {
    drawFooter(uiText("WEITER", "NEXT"), uiText("ZURUECK", "BACK"), BLACK);
  }
  refresh();
}

void showCalendarMonth(int monthOffset, bool moreDisplayPages) {
  const String local = utcToLocalDeviceLabel(currentUtcIso());
  const CalendarMonth month = calendarMonth(local.substring(0, 4).toInt(),
      local.substring(5, 7).toInt(), local.substring(8, 10).toInt(), monthOffset);

  clearWhite();
  if (month.rows == 0) {
    drawStrC(W / 2, 82, uiText("DATUM FEHLT", "DATE NOT SET"), 2, BLACK);
  } else {
    constexpr int margin = 8;
    constexpr int columnWidth = (W - 2 * margin) / 8;
    constexpr int firstCenter = margin + columnWidth / 2;
    static const char* const names[] = {"JAN", "FEB", "MAR", "APR", "MAY", "JUN",
                                        "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"};
    static const char* const headings[] = {"CW", "MO", "TU", "WE", "TH", "FR", "SA", "SU"};

    char title[16];
    snprintf(title, sizeof(title), "%s %d", names[month.month - 1], month.year);
    drawStrC(W / 2, 12, title, 2, BLACK);
    hline(margin, 35, W - 2 * margin, BLACK);
    for (int column = 0; column < 8; ++column) {
      const int center = firstCenter + column * columnWidth;
      drawCharLegacy(center - 9, 44, headings[column][0], 1, BLACK);
      drawCharLegacy(center, 44, headings[column][1], 1, BLACK);
    }
    vline(margin + columnWidth, 41, 19 * month.rows + 12, BLACK);
    for (int row = 0; row < month.rows; ++row) {
      const int y = 61 + 19 * row;
      const auto& week = month.weeks[row];
      char value[4];
      snprintf(value, sizeof(value), "%02d", week.number);
      drawStrC(firstCenter, y, value, -1, BLACK);
      for (int column = 0; column < 7; ++column) {
        const int day = week.days[column];
        if (!day) continue;
        const int center = firstCenter + (column + 1) * columnWidth;
        const bool today = day == month.today;
        const bool holiday = homeHolidayFor(month.year, month.month, day);
        if (today) fillRect(center - 10, y - 2, 21, 17, BLACK);
        snprintf(value, sizeof(value), "%d", day);
        drawStrC(center, y, value, today || holiday ? -1 : 1, today ? WHITE : BLACK);
      }
    }
  }
  if (moreDisplayPages) drawFooter("NEXT", "HOME", BLACK);
  else drawFooterCentered("HOME", BLACK);
  refresh();
}

void showRecording() {
  fillRect(0, 0, W, H, BLACK);
  drawHeader(uiText("NOTIZ", "NOTE"), WHITE);
  drawStatusHero(uiText("HOERE ZU", "LISTENING"), 67, WHITE);
  fillCircle(kRight - 7, 146, 7, WHITE);
  labelLeft(139, uiText("AUFNAHME", "RECORDING"), 1, WHITE);
  refresh();
}

void showRecordingProgress(uint32_t elapsedMs, int moments) {
  fillRect(0, 0, W, H, BLACK);
  drawHeader(uiText("NOTIZ", "NOTE"), WHITE);
  labelLeft(kContentTop, uiText("AUFNAHME", "RECORDING"), 1, WHITE);
  drawTimer(elapsedMs, WHITE);
  refreshFast();
}

void showSaved(int noteNumber) {
  clearWhite();
  char number[12];
  snprintf(number, sizeof(number), "#%03d", noteNumber % 1000);
  drawHeader(number, BLACK);
  strokeCircle(36, 67, 14, 2, BLACK);
  thickLine(29, 67, 34, 73, 2, BLACK);
  thickLine(34, 73, 44, 60, 2, BLACK);
  drawStatusHero(uiText("GEMERKT", "SAVED"), 96, BLACK);
  refresh();
}

void showMemoryProcessing(int noteNumber, const char* phase) {
  clearWhite();
  char number[12];
  snprintf(number, sizeof(number), "#%03d", noteNumber % 1000);
  drawHeader(number, BLACK);
  labelLeft(kContentTop, uiText("NOTIZ", "NOTE"), 1, BLACK);
  if (phase && !strcmp(phase, "CONNECTING")) labelRight(kContentTop, "Wi-Fi", 1, BLACK);
  drawStatusHero(memoryPhaseCopy(phase), 78, BLACK);
  const int stage = memoryStage(phase);
  drawProgressSegments(stage, 3, 151, BLACK);
  refresh();
}

void showBacklogProcessing(int completed, int total, const char* phase,
                           BacklogLink link, void (*serviceConnection)()) {
  clearWhite();
  completed = constrain(completed, 0, max(total, 1));
  total = max(total, 1);
  drawHeader(translatedUi("SYNC"), BLACK);
  char progress[20];
  snprintf(progress, sizeof(progress), "%d / %d", completed, total);
  labelLeft(kContentTop, progress, 1, BLACK);
  if (const char* transport = backlogTransportLabel(link)) labelRight(kContentTop, transport, 1, BLACK);
  drawStatusHero(memoryPhaseCopy(phase), 78, BLACK);
  strokeRect(kInset, 149, kContentWidth, 12, 1, BLACK);
  const int innerWidth = kContentWidth - 4;
  const int filled = total > 0 ? (innerWidth * completed) / total : 0;
  if (filled > 0) fillRect(kInset + 2, 151, filled, 8, BLACK);
  refresh(serviceConnection);
}

void showAskListening() {
  fillRect(0, 0, W, H, BLACK);
  drawHeader(uiText("FRAGE", "ASK"), WHITE);
  drawStatusHero(uiText("HOERE ZU", "LISTENING"), 67, WHITE);
  fillCircle(kRight - 7, 146, 7, WHITE);
  labelLeft(139, uiText("AUFNAHME", "RECORDING"), 1, WHITE);
  refresh();
}

void showAskThinking() { showAskProgress("THINKING", translatedUi("WITH MEMORY")); }

void showAskProgress(const char* action, const char* object) {
  clearWhite();
  drawHeader(uiText("FRAGE", "ASK"), BLACK);
  if (action && !strcmp(action, "CONNECTING")) labelRight(kContentTop, "Wi-Fi", 1, BLACK);
  drawStatusHero(askPhaseCopy(action), 78, BLACK);
  const int stage = askStage(action);
  drawProgressSegments(stage, 3, 151, BLACK);
  refresh();
}

void showAskSpeaking(const String& answer, int sourceCount) {
  showAskSpeakingPage(answer, sourceCount, 0);
}

int askAnswerPageCount(const String& answer) {
  const int count = answerLines(answer).size();
  return max(1, (count + kAnswerLinesPerPage - 1) / kAnswerLinesPerPage);
}

void showAskSpeakingPage(const String& answer, int sourceCount, int page) {
  const std::vector<String> lines = answerLines(answer);
  const int pages = max(1, (static_cast<int>(lines.size()) + kAnswerLinesPerPage - 1) / kAnswerLinesPerPage);
  page = constrain(page, 0, pages - 1);
  clearWhite();
  char pageLabel[12];
  snprintf(pageLabel, sizeof(pageLabel), "%d/%d", page + 1, pages);
  drawHeader(pageLabel, BLACK);
  labelLeft(kContentTop, sourceCount > 0 ? uiText("AUS ERINNERUNGEN", "FROM MEMORY") : uiText("ANTWORT", "ANSWER"), 1, BLACK);
  const int first = page * kAnswerLinesPerPage;
  for (int i = 0; i < kAnswerLinesPerPage && first + i < static_cast<int>(lines.size()); i++)
    labelLeft(65 + i * 18, lines[first + i].c_str(), 1, BLACK);
  drawFooter(page < pages - 1 ? uiText("WEITER", "NEXT") : uiText("ENDE", "END"),
             uiText("ZURUECK", "BACK"), BLACK);
  refresh();
}

void showNotePairingWait(bool bluetooth) {
  clearWhite();
  drawHeader("PAIRING", BLACK);
  labelLeft(kContentTop, bluetooth ? "Bluetooth" : "USB", 1, BLACK);
  drawStatusHero(bluetooth ? "READY TO PAIR" : "APPROVED", 75, BLACK);
  labelLeft(130, bluetooth ? "Open settings on PC" : "Finish on computer", 1, BLACK);
  drawFooter("LOWER: CANCEL", nullptr, BLACK);
  refresh();
}

void showNoteUsbPairingPrompt(const String& peer) {
  clearWhite();
  drawHeader("USB PAIRING", BLACK);
  labelLeft(kContentTop, "Allow this Bridge?", 1, BLACK);
  drawStrFit(kInset, 82, kContentWidth, normalizeForDisplay(peer).c_str(), 2, BLACK);
  labelLeft(132, "UPPER: YES", 1, BLACK);
  drawFooter("LOWER: CANCEL", nullptr, BLACK);
  refresh();
}

void showNoteBluetoothPairingNumber(uint32_t code) {
  clearWhite();
  drawHeader("BT PAIRING", BLACK);
  labelLeft(kContentTop, "Compare code on PC", 1, BLACK);
  char digits[7];
  snprintf(digits, sizeof(digits), "%06lu", static_cast<unsigned long>(code % 1000000U));
  drawStrC(W / 2, 82, digits, 3, BLACK);
  labelLeft(132, "UPPER: YES", 1, BLACK);
  drawFooter("LOWER: CANCEL", nullptr, BLACK);
  refresh();
}

void showTransferConnecting(int batteryPercent) {
  clearWhite();
  drawCompanionHeader(batteryPercent);
  labelLeft(kContentTop, translatedUi("COMPANION"), 1, BLACK);
  labelRight(kContentTop, "Wi-Fi", 1, BLACK);
  drawStatusHero(uiText("VERBINDE", "CONNECTING"), 67, BLACK);
  drawProgressSegments(1, 3, 143, BLACK);
  drawFooterCentered(translatedUi("WORKING"), BLACK);
  refresh();
}

namespace {
constexpr int kCompanionNetworkY = 61;
constexpr int kCompanionNetworkLineHeight = 17;
constexpr int kCompanionAddressY = 101;
constexpr int kCompanionAddressHeight = 28;
constexpr int kCompanionPasswordY = 135;

void drawCompanionNetworkName(const String& network) {
  String name = normalizeForDisplay(network);
  if (!name.length()) name = "SSID";
  int edge = name.length();
  while (edge > 0 && textW(name.substring(0, edge).c_str(), 1) > kContentWidth) --edge;
  labelLeft(kCompanionNetworkY, name.substring(0, edge).c_str(), 1, BLACK);
  if (edge < static_cast<int>(name.length())) {
    labelLeft(kCompanionNetworkY + kCompanionNetworkLineHeight, name.substring(edge).c_str(), 1, BLACK);
  }
}

void drawCompanionVolumeOverlay(int percent) {
  constexpr int panelTop = 46;
  constexpr int panelHeight = 114;
  constexpr int barWidth = 20, barGap = 10, barCount = 5;
  constexpr int chartWidth = barCount * barWidth + (barCount - 1) * barGap;
  constexpr int chartLeft = (W - chartWidth) / 2;
  constexpr int chartBottom = 142;
  static_assert(panelTop + panelHeight < kFooterRuleY);
  strokeRoundRect(kInset, panelTop, kContentWidth, panelHeight, 10, 2, BLACK);
  drawStrC(W / 2, 60, uiText("Lautstaerke", translatedUi("Volume")), 2, BLACK);

  const int level = (constrain(percent, 20, 100) + 19) / 20;
  for (int i = 0; i < barCount; i++) {
    const int x = chartLeft + i * (barWidth + barGap);
    const int height = 10 + i * 8;
    if (i < level) fillRect(x, chartBottom - height, barWidth, height, BLACK);
    else strokeRect(x, chartBottom - height, barWidth, height, 1, BLACK);
  }
  drawFooter(translatedUi("UPPER +"), translatedUi("LOWER -"), BLACK);
}
}  // namespace

void showTransferMode(CompanionLink link, const String& network, const String& ip,
                      const String& apPassword, int batteryPercent, int volumeOverlay) {
  clearWhite();
  drawCompanionHeader(batteryPercent);
  if (volumeOverlay >= 0) {
    drawCompanionVolumeOverlay(volumeOverlay);
    refresh();
    return;
  }
  if (link == CompanionLink::Offline) {
    labelLeft(kContentTop, "Wi-Fi", 1, BLACK);
    drawStatusHero(uiText("Nicht verbunden", "Not connected"), 66, BLACK);
  } else {
    const bool hotspot = link == CompanionLink::Hotspot;
    labelLeft(kContentTop, hotspot ? "Hotspot" : uiText("WLAN-Netz", "Wi-Fi network"), 1, BLACK);
    drawCompanionNetworkName(network);

    const String address = String("http://") + ip;
    const bool fitsUrl = textW(address.c_str(), 1) <= kContentWidth - 12;
    const int addressScale = !fitsUrl && textW(ip.c_str(), 2) <= kContentWidth - 12 ? 2 : 1;
    fillRect(kInset, kCompanionAddressY, kContentWidth, kCompanionAddressHeight, BLACK);
    drawStrInBox(kInset, kCompanionAddressY, kContentWidth, kCompanionAddressHeight,
                 fitsUrl ? address.c_str() : ip.c_str(), addressScale, WHITE);
    if (hotspot) {
      labelLeft(kCompanionPasswordY, uiText("WPA2", "Key"), 1, BLACK);
      labelRight(kCompanionPasswordY, apPassword.c_str(), 1, BLACK);
    }
  }
  drawFooterCentered(uiText("Beide halten", "Hold both to exit"), BLACK);
  refresh();
}

void showSetupRequired() {
  clearWhite();
  drawHeader(translatedUi("SETUP"), BLACK);
  labelLeft(kContentTop, uiText("NOCH NICHT BEREIT", "NOT READY YET"), 1, BLACK);
  drawHero(uiText("EINRICHTEN", "SET UP"), 70, BLACK, 120);
  labelLeft(143, PALA_MAC_BRIDGE ? (palaUiLanguage() == "sk" ? "PODRZ OBE TLACIDLA" : "HOLD BOTH BUTTONS") : uiText("IM PALA FLASHER", "IN PALA FLASHER"), 1, BLACK);
  drawFooter("USB", uiText("BEREIT", "READY"), BLACK);
  refresh();
}

void showError(const char* message) {
  Serial.printf("[UI] error: %s\n", message ? message : "unspecified");
  clearWhite();
  drawHeader(uiText("FEHLER", "ERROR"), BLACK);
  drawStatusHero(uiText("NOCHMAL?", "TRY AGAIN"), 72, BLACK);
  labelLeft(143, uiText("HAT NICHT GEKLAPPT", "SOMETHING WENT WRONG"), 1, BLACK);
  if (message && *message) labelLeft(158, translatedUi(message), 1, BLACK);
  refresh();
}

void showSleepLocked() {
  // Preserve the useful home screen and add only a compact persistent sleep
  // indicator beside the battery. E-paper keeps this visible without power.
  constexpr int x = kHomeStatusX;
  constexpr int y = kHeaderY;
  fillRect(x, y, kHomeStatusWidth, kHomeStatusHeight, WHITE);
  strokeRect(x, y + 5, kHomeStatusWidth, 9, 1, BLACK);
  // Lock shackle: two vertical shoulders joined by a short top line.
  vline(x + 2, y + 2, 4, BLACK);
  vline(x + 8, y + 2, 4, BLACK);
  hline(x + 2, y + 1, 7, BLACK);
  fillRect(x + 5, y + 9, 2, 3, BLACK);
  refresh();
}

void showUltraSleepScreen() {
  clearWhite();
  drawHeader(translatedUi("RESTING"), BLACK);
  const String name = palaNoteName();
  int nameScale = 3;
  while (nameScale > 1 && textW(name.c_str(), nameScale) > kContentWidth) nameScale--;
  drawStrC(W / 2, 76, name.c_str(), nameScale, BLACK);
  drawStrC(W / 2, 123, translatedUi("PRESS TO WAKE"), 1, BLACK);
  refreshClean();
}

static int calendarButtonChoice() {
  bool armed = false;
  const uint32_t since = millis();
  while (millis() - since < 30000UL) {
    const bool up = digitalRead(BTN_REC) == LOW, down = digitalRead(BTN_PWR) == LOW;
    if (!up && !down) armed = true;
    if (armed && (up != down)) {
      delay(35);
      const int pin = up ? BTN_REC : BTN_PWR;
      if (digitalRead(pin) != LOW) continue;
      while (digitalRead(pin) == LOW && millis() - since < 30000UL) delay(10);
      return up ? 0 : 1;
    }
    delay(10);
  }
  return -1;
}

int chooseCalendarEventDate(const String& title, const String& first, const String& second) {
  const bool sk = palaUiLanguage() == "sk";
  clearWhite();
  drawHeader(sk ? "VYBER DATUM" : "CHOOSE DATE", BLACK);
  labelLeft(38, normalizeForDisplay(title).c_str(), 1, BLACK);
  labelLeft(75, (String("1  ") + first).c_str(), 1, BLACK);
  labelLeft(108, (String("2  ") + second).c_str(), 1, BLACK);
  drawFooter(sk ? "HORE 1" : "UP 1", sk ? "DOLE 2" : "DOWN 2", BLACK);
  refresh();
  return calendarButtonChoice();
}

// 1 accepts, 0 declines, -1 leaves the proposal pending at the Bridge.
int confirmCalendarEvent(const String& title, const String& start, const String& end, int minutes) {
  const bool sk = palaUiLanguage() == "sk";
  clearWhite();
  drawHeader(sk ? "KALENDAR" : "CALENDAR", BLACK);
  labelLeft(38, normalizeForDisplay(title).c_str(), 1, BLACK);
  labelLeft(66, start.c_str(), 1, BLACK);
  labelLeft(91, end.c_str(), 1, BLACK);
  if (minutes >= 0) labelLeft(117, (String(sk ? "Predstih " : "Alert ") + minutes + " min").c_str(), 1, BLACK);
  labelLeft(143, sk ? "Ulozit termin?" : "Save event?", 1, BLACK);
  drawFooter(sk ? "HORE ANO" : "UP YES", sk ? "DOLE NIE" : "DOWN NO", BLACK);
  refresh();
  const int button = calendarButtonChoice();
  return button < 0 ? -1 : 1 - button;
}
