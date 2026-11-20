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

#include <ctime>

namespace {
constexpr int kInset = 14;
constexpr int kRight = W - kInset;
constexpr int kContentWidth = W - 2 * kInset;
constexpr int kHeaderY = 10;
constexpr int kHomeSideSlotOffset = 62;
constexpr int kBatteryBodyWidth = 23;
constexpr int kBatteryContactWidth = 2;
constexpr int kBatteryWidth = kBatteryBodyWidth + kBatteryContactWidth;
constexpr int kHomeBatteryX = kRight - kBatteryWidth;
constexpr int kSleepLockGap = 7;
constexpr int kSleepLockWidth = 11;
constexpr int kSleepLockX = kHomeBatteryX - kSleepLockGap - kSleepLockWidth;
static_assert(kSleepLockX >= kInset && kHomeBatteryX + kBatteryWidth <= W);
constexpr int kHeaderRuleY = 31;
constexpr int kHomeAgendaRuleY = 156;
constexpr int kHomeAgendaColumnGap = 4;
constexpr int kHomeAgendaLabelY = kHomeAgendaRuleY + 7;
constexpr int kHomeAgendaCountY = kHomeAgendaRuleY + 22;
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
      {"NO WIFI", "BEZ WI-FI"},
      {"VOICE FAILED", "CHYBA HLASU"},
      {"BRIEF FAILED", "CHYBA SUHRNU"},
      {"SD ERROR", "CHYBA SD"},
      {"COMPANION", "SPRIEVODCA"},
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

void drawBattery(int percent) {
  if (percent < 0) return;
  percent = constrain(percent, 0, 100);
  constexpr int x = kHomeBatteryX, y = 9, height = 12;
  strokeRect(x, y, kBatteryBodyWidth, height, 1, BLACK);
  fillRect(x + kBatteryBodyWidth, y + 3, kBatteryContactWidth, 6, BLACK);
  const int bars = percent > 66 ? 3 : percent > 33 ? 2 : percent > 10 ? 1 : 0;
  for (int i = 0; i < bars; i++) fillRect(x + 3 + i * 6, y + 3, 5, 6, BLACK);
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
  strokeCircle(x, y, 3, 1, BLACK);
  if (!rays) return;
  hline(x - 7, y, 2, BLACK);
  hline(x + 6, y, 2, BLACK);
  vline(x, y - 7, 2, BLACK);
  vline(x, y + 6, 2, BLACK);
  for (int dx : {-1, 1}) {
    for (int dy : {-1, 1}) line(x + 4 * dx, y + 4 * dy, x + 5 * dx, y + 5 * dy, BLACK);
  }
}

void drawCloudGlyph(int x, int y) {
  static constexpr int outline[][2] = {
    {-7, 3}, {-8, 1}, {-7, -1}, {-4, -1}, {-3, -4}, {0, -5},
    {3, -4}, {4, -2}, {6, -2}, {8, 0}, {8, 2}, {6, 4}, {-6, 4}, {-7, 3},
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
      drawSunGlyph(x - 4, y - 4, false);
      hline(x - 11, y - 4, 2, BLACK);
      vline(x - 4, y - 9, 2, BLACK);
      fillRect(x - 6, y - 4, 10, 5, WHITE);
      drawCloudGlyph(x + 2, y + 2);
      return;
    case WeatherGlyph::Cloud: drawCloudGlyph(x, y); return;
    case WeatherGlyph::Fog:
      hline(x - 7, y - 4, 14, BLACK);
      hline(x - 5, y, 12, BLACK);
      hline(x - 8, y + 4, 15, BLACK);
      return;
    case WeatherGlyph::Rain:
    case WeatherGlyph::Snow:
    case WeatherGlyph::Thunder:
      drawCloudGlyph(x, y - 3);
      if (glyph == WeatherGlyph::Rain) {
        for (int dx : {-4, 0, 4}) line(x + dx, y + 4, x + dx - 1, y + 6, BLACK);
      } else if (glyph == WeatherGlyph::Snow) {
        for (int dx : {-4, 0, 4}) {
          px(x + dx, y + 4, BLACK);
          px(x + dx + 1, y + 7, BLACK);
        }
      } else {
        line(x + 1, y + 2, x - 2, y + 5, BLACK);
        line(x - 2, y + 5, x + 2, y + 5, BLACK);
        line(x + 2, y + 5, x, y + 7, BLACK);
      }
      return;
    case WeatherGlyph::Unknown: return;
  }
}

void drawMoonGlyph(int x, int y, MoonPhase phase) {
  constexpr int radius = 6;
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

void drawHomeWeather(int y, const String& todayKey, int64_t utc) {
  HomeForecast forecast{};
  if (homeForecastFor(todayKey, forecast)) {
    drawWeatherGlyph(kInset + 19, y + 8, weatherGlyph(forecast.code));
    char temperature[17];
    snprintf(temperature, sizeof(temperature), "%d / %d", forecast.minimum, forecast.maximum);
    const int width = textW(temperature, 1);
    const int x = (W - width - 5) / 2;
    drawStr(x, y + 2, temperature, 1, BLACK);
    strokeCircle(x + width + 2, y + 4, 2, 1, BLACK);
  }
  drawMoonGlyph(kRight - 19, y + 8, moonPhaseAt(utc));
}

void drawHomeWeek(const String& todayKey, int64_t utc) {
  constexpr int top = kHeaderRuleY + 1;
  constexpr int bottom = kHomeAgendaRuleY;
  constexpr int largeWeekdayScale = 3;
  constexpr int compactWeekdayScale = 2;
  constexpr int labelScale = 1;
  constexpr int sideGap = 5;
  constexpr int rowGap = 13;
  constexpr int highlightPaddingTop = 3;
  constexpr int highlightHeight = 18;
  constexpr int weatherGap = 12;
  constexpr int weatherHeight = 16;
  const int weekdayHeight = uiFontHeight(largeWeekdayScale);
  const int calendarHeight = weekdayHeight + rowGap + highlightHeight + weatherGap + weatherHeight;
  const int headingY = top + (bottom - top - calendarHeight) / 2;
  const int sideHeadingY = headingY + (weekdayHeight - uiFontHeight(labelScale)) / 2;
  const int dateY = headingY + weekdayHeight + rowGap + highlightPaddingTop;
  const int month = todayKey.substring(5, 7).toInt();
  const auto week = calendarWeek(todayKey.substring(0, 4).toInt(),
      month, todayKey.substring(8, 10).toInt());
  if (!week.number) {
    drawStrC(W / 2, top + (bottom - top - uiFontHeight(1)) / 2,
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
  const int desiredWidth = textW(weekdayLabel, largeWeekdayScale);
  const auto fitsLargeWeekday = [&] {
    const int centerHalfWidth = min(W / 2 - kInset - textW(monthLabel, labelScale),
                                   kRight - textW(weekNumber, labelScale) - W / 2);
    return desiredWidth <= 2 * (centerHalfWidth - sideGap);
  };
  if (!fitsLargeWeekday()) snprintf(weekNumber, sizeof(weekNumber), "W%02d", week.number);
  const int weekdayScale = fitsLargeWeekday() ? largeWeekdayScale : compactWeekdayScale;
  const int weekdayY = headingY + (weekdayHeight - uiFontHeight(weekdayScale)) / 2;
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
    if (today) fillRect(x + 1, dateY - highlightPaddingTop, cell - 2, highlightHeight, BLACK);
    const String date = String(week.days[i]);
    drawStrC(x + cell / 2, dateY, date.c_str(), 1, today ? WHITE : BLACK);
  }
  vline(weekX + weekendStart * cell + weekendGap / 2, dateY - 2, highlightHeight - 2, BLACK);
  drawHomeWeather(dateY - highlightPaddingTop + highlightHeight + weatherGap, todayKey, utc);
}

int drawPendingRecordings(int count) {
  if (count <= 0) return W / 2;
  constexpr int wave[] = {0, -2, -4, -5, -5, -4, -2, 0, 2, 4, 5, 5, 4, 2, 0};
  constexpr int waveWidth = sizeof(wave) / sizeof(wave[0]);
  constexpr int waveY = kHeaderY + 6;
  constexpr int gap = 6;
  const String label = String(count);
  const int x = (W - waveWidth - gap - textW(label.c_str(), 1)) / 2;
  for (int i = 1; i < waveWidth; i++) {
    line(x + i - 1, waveY + wave[i - 1], x + i, waveY + wave[i], BLACK);
    line(x + i - 1, waveY + wave[i - 1] + 1, x + i, waveY + wave[i] + 1, BLACK);
  }
  drawStr(x + waveWidth + gap, kHeaderY, label.c_str(), 1, BLACK);
  return x;
}

const char* compactHomeAgendaLabel(const char* label) {
  static constexpr struct { const char* full; const char* compact; } labels[] = {
    {"AUFGABEN", "AUFG."},
    {"ZAVDANNYA", "ZAVD."},
    {"RENDEZ-VOUS", "RDV"},
    {"SOBYTIYA", "SOBYT."},
    {"AFSPRAKEN", "AFSPR."},
  };
  for (const auto& entry : labels) {
    if (strcmp(label, entry.full) == 0) return entry.compact;
  }
  return label;
}

void drawHomeAgendaCount(int left, const char* label, int count) {
  constexpr int width = (kContentWidth - kHomeAgendaColumnGap) / 2;
  const int center = left + width / 2;
  label = compactHomeAgendaLabel(label);
  if (textW(label, 1) <= width) drawStrC(center, kHomeAgendaLabelY, label, 1, BLACK);
  else drawStrFit(left, kHomeAgendaLabelY, width, label, 1, BLACK);

  char value[8];
  snprintf(value, sizeof(value), "%d", min(count, 999));
  drawStrC(center, kHomeAgendaCountY, value, 2, BLACK);
}
}  // namespace

void showIdle(int pendingCount, int batteryPercent) {
  clearWhite();
  const int nameRight = drawPendingRecordings(pendingCount) - 4;
  const String name = palaNoteName();
  const int nameWidth = textW(name.c_str(), 1);
  const int nameLeft = max(kInset, W / 2 - kHomeSideSlotOffset - nameWidth / 2);
  drawStrFit(nameLeft, kHeaderY, nameRight - nameLeft, name.c_str(), 1, BLACK);
  drawBattery(batteryPercent);
  hline(kInset, kHeaderRuleY, kContentWidth, BLACK);
  const String nowUtc = currentUtcIso();
  const String localNow = utcToLocalDeviceLabel(nowUtc);
  const String todayKey = localNow.length() >= 10 && localNow != "time not set" ? localNow.substring(0, 10) : "";
  const std::vector<BrainTaskRecord> tasks = loadBrainTasks();
  int taskCount = 0, eventCount = 0;
  for (const BrainTaskRecord& task : tasks) {
    if (task.done) continue;
    if (!task.dueUtc.length()) {
      if (undatedTaskVisible(task.type == "event", task.done)) taskCount++;
      continue;
    }
    if (!todayKey.length()) continue;
    const String localDue = utcToLocalDeviceLabel(task.dueUtc);
    if (localDue.length() < 10 || localDue == "time not set") continue;
    const String dueDay = localDue.substring(0, 10);
    if (task.type == "event") {
      if (dueDay == todayKey && eventStillRelevant(dueDay.c_str(), todayKey.c_str(),
          task.dueUtc.c_str(), nowUtc.c_str(), task.datePrecision == "day")) eventCount++;
    }
    else if (dueDay <= todayKey) taskCount++;
  }
  drawHomeWeek(todayKey, time(nullptr));
  hline(kInset, kHomeAgendaRuleY, kContentWidth, BLACK);
  const int eventsLeft = W / 2 + kHomeAgendaColumnGap / 2;
  vline(W / 2, kHomeAgendaLabelY, 31, BLACK);
  drawHomeAgendaCount(kInset, uiText("AUFGABEN", "TASKS"), taskCount);
  drawHomeAgendaCount(eventsLeft, uiText("TERMINE", "EVENTS"), eventCount);
  refresh();
}

int todayAgendaPageCount(bool events) {
  return agendaPages(todayAgendaItems(events));
}

void showTodayAgenda(bool events, int page) {
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
  if (items.empty() || pages == 1) drawFooterCentered(uiText("NOCHMAL: ZURUECK", "AGAIN: BACK"), BLACK);
  else drawFooter(uiText("WEITER", "NEXT"), uiText("ZURUECK", "BACK"), BLACK);
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
  drawStatusHero(memoryPhaseCopy(phase), 78, BLACK);
  const int stage = memoryStage(phase);
  drawProgressSegments(stage, 3, 151, BLACK);
  refresh();
}

void showBacklogProcessing(int completed, int total, const char* phase) {
  clearWhite();
  completed = constrain(completed, 0, max(total, 1));
  total = max(total, 1);
  drawHeader(translatedUi("SYNC"), BLACK);
  char progress[20];
  snprintf(progress, sizeof(progress), "%d / %d", completed, total);
  labelLeft(kContentTop, progress, 1, BLACK);
  drawStatusHero(memoryPhaseCopy(phase), 78, BLACK);
  strokeRect(kInset, 149, kContentWidth, 12, 1, BLACK);
  const int innerWidth = kContentWidth - 4;
  const int filled = total > 0 ? (innerWidth * completed) / total : 0;
  if (filled > 0) fillRect(kInset + 2, 151, filled, 8, BLACK);
  refresh();
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

void showTransferConnecting() {
  clearWhite();
  drawHeader(translatedUi("LOCAL"), BLACK);
  labelLeft(kContentTop, translatedUi("COMPANION"), 1, BLACK);
  drawStatusHero(uiText("VERBINDE", "CONNECTING"), 67, BLACK);
  drawProgressSegments(1, 3, 143, BLACK);
  drawFooter("1 / 3", translatedUi("WORKING"), BLACK);
  refresh();
}

void showTransferMode(const char* ip, const char* apPassword) {
  clearWhite();
  drawHeader(translatedUi("COMPANION"), BLACK);
  if (apPassword && strlen(apPassword) > 0) {
    labelLeft(kContentTop, translatedUi("JOIN WI-FI"), 1, BLACK);
    drawHero("PALA NOTE", 66, BLACK, 100);
    labelLeft(112, translatedUi("PASSWORD"), 1, BLACK);
    labelLeft(132, apPassword, 2, BLACK);
    drawFooter(translatedUi("SETUP"), ip, BLACK);
  } else {
    labelLeft(kContentTop, translatedUi("OPEN IN BROWSER"), 1, BLACK);
    fillRect(kInset, 70, kContentWidth, 52, BLACK);
    drawStrInBox(kInset, 70, kContentWidth, 52, ip, 2, WHITE);
    labelLeft(140, translatedUi("SAME WI-FI"), 1, BLACK);
    drawFooter(translatedUi("COMPANION"), translatedUi("READY"), BLACK);
  }
  refresh();
}

void showVolumeSetting(int percent) {
  clearWhite();
  drawHeader(translatedUi("SETTINGS"), BLACK);
  const int level = constrain(percent, 20, 100);
  char value[18];
  snprintf(value, sizeof(value), palaUiLanguage() == "sk" ? "HLASITOST %d" : "VOLUME %d", (level + 19) / 20);
  labelLeft(kContentTop, translatedUi("SPEAKER"), 1, BLACK);
  drawHero(value, 67, BLACK, 108);
  const int bars = constrain((level + 19) / 20, 1, 5);
  for (int i = 0; i < 5; i++) {
    const int height = 8 + i * 7;
    if (i < bars) fillRect(18 + i * 34, 157 - height, 20, height, BLACK);
    else strokeRect(18 + i * 34, 157 - height, 20, height, 1, BLACK);
  }
  drawFooter(translatedUi("UPPER +"), translatedUi("LOWER -"), BLACK);
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
  constexpr int x = kSleepLockX;
  constexpr int y = kHeaderY;
  strokeRect(x, y + 5, kSleepLockWidth, 9, 1, BLACK);
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

// 1 accepts, 0 declines, -1 leaves the proposal pending on the Mac.
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
      return up ? 1 : 0;
    }
    delay(10);
  }
  return -1;
}
