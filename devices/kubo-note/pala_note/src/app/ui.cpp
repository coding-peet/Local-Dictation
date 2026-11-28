#include "../../config.h"
#include "display_pipeline.h"
#include "provider.h"
#include "ui.h"
#include "network.h"
#include "calendar_button.h"
#include "record.h"
#include "stage2_bench.h"
#include "restart_maintenance.h"
#include "../../globals.h"
#include "draw.h"
#include "notes.h"
#include "monthly_recordings.h"
#include "tasks.h"
#include "agenda_index.h"
#include "task_visibility.h"
#include "settings.h"
#include "calendar_week.h"
#include "home_almanac.h"
#include "home_weather.h"
#include "home_holidays.h"
#include "home_delivery.h"
#include "home_bridge_status.h"
#include "battery.h"
#include "date_validation.h"
#include "local_calendar.h"
#include "rtc.h"

#include <atomic>
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
constexpr int kHomeDeliveryColumnWidth = W / 2 - 6;
constexpr int kHomeDeliveryLabelY = 151;
constexpr int kHomeDeliveryValueCenterY = 182;
constexpr int kContentTop = 43;
constexpr int kFooterRuleY = 174;
constexpr int kFooterTextY = 181;
constexpr int kAnswerLinesPerPage = 6;

struct UiTranslation { const char* en; const char* fr; const char* es; const char* it; const char* pl; const char* cs; const char* ru; const char* uk; const char* nl; const char* pt; const char* sk; };
// The panel reads static localized captions without opening settings during REC.
std::atomic<const char*> savingCaption{"SAVING"};
std::atomic<const char*> lockedCaption{"LOCKED"}, stopCaption{"Either button stops"};
std::atomic<const char*> priorSaveCaption{"Saving last note"};
const UiTranslation kUiTranslations[] = {
  {"SAVING","SAUVEGARDE","GUARDANDO","SALVATAGGIO","ZAPISYWANIE","UKLADANI","SOHRANENIE","ZBEREZHENNYA","OPSLAAN","SALVANDO","UKLADAM"},
  {"Saving last note","Sauve note prec.","Guarda anterior","Salvo precedente","Zapis poprzedniej","Ukladam minulou","Pishu proshluyu","Pyshu poperedniu","Vorige opslaan","Gravando anterior","Ukladam predoslu"},
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
      {"PROCESSING OFF", "SPRACOVANIE VYPNUTE"},
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

// Front view: the upper side button is level with y~143; the lower one
// sits just below the panel. Keep each answer next to its own right edge cue.
void drawSideButtonCue(int rowY) {
  hline(kRight + 3, rowY + 6, W - kRight - 5, BLACK);
}

void drawSideButtonChoices(const char* upper, const char* lower) {
  auto choice = [](int y, const char* text) {
    if (!text) return;
    labelRight(y, text, 1, BLACK);
    drawSideButtonCue(y);
  };
  choice(137, upper);
  choice(kFooterTextY, lower);
}

void drawConfirmationChoices(bool lowerCancels) {
  const bool sk = palaUiLanguage() == "sk";
  drawSideButtonChoices(sk ? "ANO" : "YES",
                        lowerCancels ? (sk ? "ZRUSIT" : "CANCEL") : (sk ? "NIE" : "NO"));
}

void drawFooterCentered(const char* text, uint8_t color) {
  hline(kInset, kFooterRuleY, kContentWidth, color);
  if (text && strlen(text)) drawStrC(W / 2, kFooterTextY, text, 1, color);
}

void drawBattery(int percent) {
  const int bars = batteryBarCount(percent);
  if (bars < 0) return;
  constexpr int x = kHomeBatteryX;
  constexpr int y = kHomeHeaderCenterY - kBatteryHeight / 2;
  strokeRect(x, y, kBatteryBodyWidth, kBatteryHeight, 1, BLACK);
  fillRect(x + kBatteryBodyWidth, y + 3, kBatteryContactWidth, 6, BLACK);
  if (bars == 0) {
    constexpr int markWidth = 3;
    constexpr int markX = x + (kBatteryBodyWidth - markWidth) / 2;
    fillRect(markX, y + 2, markWidth, 5, BLACK);
    fillRect(markX, y + 8, markWidth, 2, BLACK);
    return;
  }
  for (int i = 0; i < bars; i++) fillRect(x + 3 + i * 6, y + 3, 5, 6, BLACK);
}

void drawModeHeaderWithBattery(const char* mode, int batteryPercent) {
  const int nameWidth = W / 2 - textW(mode, 1) / 2 - kInset - 6;
  labelLeft(kHeaderY, palaNoteName().c_str(), 1, BLACK, nameWidth);
  drawStrC(W / 2, kHeaderY, mode, 1, BLACK);
  drawBattery(batteryPercent);
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

template <class Emit>
void visitAnswerLines(const String& raw, Emit emit) {
  const String text = normalizeForDisplay(raw);
  bool any = false;
  const auto output = [&](const String& line) { any = true; return emit(line); };
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
      if (line.length()) { if (!output(line)) return; line = ""; }
      if (!output(word.substring(0, cut) + "-")) return;
      word = word.substring(cut);
    }
    const String candidate = line.length() ? line + " " + word : word;
    if (textW(candidate.c_str(), 1) <= kContentWidth) line = candidate;
    else { if (line.length()) if (!output(line)) return; line = word; }
  }
  if (line.length()) if (!output(line)) return;
  if (!any) output(uiText("KEINE ANTWORT", "NO ANSWER"));
}

std::vector<String> answerLines(const String& raw) {
  std::vector<String> lines;
  visitAnswerLines(raw, [&](const String& line) { lines.push_back(line); return true; });
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

constexpr int kAgendaLinesPerPage = 5;

struct AgendaScreenPage {
  std::array<String, kAgendaLinesPerPage> lines;
  String label;
  bool filled = false;
};

struct AgendaScreen {
  AgendaIndexCursor cursor;
  AgendaScreenPage current;
  AgendaScreenPage next;
  String nowUtc;
  String today;
  int page = -1;
  int item = 0;
  int linesRead = 0;
  bool events = false;
  bool end = false;
};
AgendaScreen agendaScreen;

struct AgendaTaskRead {
  AgendaScreen& screen;
  BrainTaskRecord task;
  bool found = false;
};

bool readAgendaTask(const BrainTaskRecord& item, void* context) {
  auto& read = *static_cast<AgendaTaskRead*>(context);
  auto& screen = read.screen;
  if (item.done) return true;
  const bool event = item.type == "event";
  if (event != screen.events) return true;
  if (item.dueUtc.isEmpty()) {
    if (screen.events) return true;
  } else {
    if (screen.today.isEmpty()) { screen.end = true; return false; }
    const String localDue = utcToLocalDeviceLabel(item.dueUtc);
    if (localDue.length() < 10 || localDue == "time not set") return true;
    const String day = localDue.substring(0, 10);
    if (day > screen.today) { screen.end = true; return false; }
    if (screen.events && (day != screen.today ||
        !eventStillRelevant(day.c_str(), screen.today.c_str(), item.dueUtc.c_str(),
                            screen.nowUtc.c_str(), item.datePrecision == "day"))) return true;
  }
  read.task = item;
  read.found = true;
  return false;
}

AgendaScreenPage readAgendaScreenPage() {
  auto& screen = agendaScreen;
  AgendaScreenPage page;
  if (screen.end) return page;
  AgendaTaskRead read{screen};
  if (screen.linesRead) {
    // A long task spans pages. Keep its cursor rather than the remaining text.
    AgendaIndexCursor sameTask = screen.cursor;
    --sameTask.next;
    if (!visitAgendaIndex(sameTask, readAgendaTask, &read)) return page;
  } else {
    if (!visitAgendaIndex(screen.cursor, readAgendaTask, &read)) return page;
    if (read.found) ++screen.item;
  }
  if (!read.found) {
    screen.end = true;
    return page;
  }
  const auto& task = read.task;
  const String localDue = utcToLocalDeviceLabel(task.dueUtc);
  page.label = screen.events && task.datePrecision != "day" && localDue.length() >= 16
      ? localDue.substring(11, 16) : String(screen.item);
  int seen = 0;
  int kept = 0;
  bool more = false;
  visitAnswerLines(task.text, [&](const String& line) {
    if (seen++ < screen.linesRead) return true;
    if (kept == kAgendaLinesPerPage) { more = true; return false; }
    page.lines[kept++] = line;
    return true;
  });
  page.filled = true;
  screen.linesRead = more ? screen.linesRead + kept : 0;
  return page;
}

bool beginAgendaScreen(bool events) {
  agendaScreen = AgendaScreen{};
  auto& screen = agendaScreen;
  screen.events = events;
  screen.nowUtc = currentUtcIso();
  const String localNow = utcToLocalDeviceLabel(screen.nowUtc);
  if (localNow.length() >= 10 && localNow != "time not set") screen.today = localNow.substring(0, 10);
  char previousDay[11]{};
  if (events && !screen.today.isEmpty()) {
    const int year = localNow.substring(0, 4).toInt();
    const int month = localNow.substring(5, 7).toInt();
    const int day = localNow.substring(8, 10).toInt();
    if (calendarWeek(year, month, day).number) {
      const auto previous = calendarDateInWeek(year, month, day, -1);
      snprintf(previousDay, sizeof(previousDay), "%04d-%02d-%02d", previous.year, previous.month, previous.day);
    }
  }
  if (!openAgendaIndex(screen.cursor, false, *previousDay ? previousDay : nullptr)) return false;
  screen.cursor.mask = 3;
  screen.cursor.match = events ? 1 : 0;
  screen.current = readAgendaScreenPage();
  screen.next = readAgendaScreenPage();
  screen.page = 0;
  return true;
}

void drawSunGlyph(int x, int y, bool rays) {
  strokeCircle(x, y, 5, 1, BLACK);
  if (!rays) return;
  hline(x - 9, y, 2, BLACK);
  hline(x + 8, y, 2, BLACK);
  vline(x, y - 9, 2, BLACK);
  vline(x, y + 8, 2, BLACK);
  for (int dx : {-1, 1}) {
    for (int dy : {-1, 1}) line(x + 6 * dx, y + 6 * dy, x + 7 * dx, y + 7 * dy, BLACK);
  }
}

void drawCloudGlyph(int x, int y, int height = 14) {
  static constexpr int outline[][2] = {
    {-10, 5}, {-11, 2}, {-10, -1}, {-6, -1}, {-4, -6}, {0, -7},
    {4, -6}, {6, -2}, {8, -2}, {12, 1}, {12, 4}, {8, 7}, {-7, 7}, {-10, 5},
  };
  const auto ordinate = [y, height](int dy) { return y + (dy + 7) * height / 14 - height / 2; };
  for (size_t i = 1; i < sizeof(outline) / sizeof(outline[0]); ++i) {
    line(x + outline[i - 1][0], ordinate(outline[i - 1][1]),
         x + outline[i][0], ordinate(outline[i][1]), BLACK);
  }
}

void drawWeatherGlyph(int x, int y, WeatherGlyph glyph) {
  switch (glyph) {
    case WeatherGlyph::Sun: drawSunGlyph(x, y, true); return;
    case WeatherGlyph::PartlyCloudy:
      drawSunGlyph(x - 6, y - 4, false);
      hline(x - 15, y - 4, 2, BLACK);
      line(x - 14, y - 9, x - 13, y - 8, BLACK);
      fillRect(x - 10, y - 4, 15, 7, WHITE);
      drawCloudGlyph(x + 2, y + 3);
      return;
    case WeatherGlyph::Cloud: drawCloudGlyph(x, y); return;
    case WeatherGlyph::Fog:
      hline(x - 10, y - 6, 20, BLACK);
      hline(x - 7, y, 17, BLACK);
      hline(x - 11, y + 6, 20, BLACK);
      return;
    case WeatherGlyph::Rain:
      drawCloudGlyph(x, y - 2);
      for (int dx : {-6, 0, 6}) line(x + dx, y + 7, x + dx - 2, y + 10, BLACK);
      return;
    case WeatherGlyph::Snow:
      drawCloudGlyph(x, y - 3, 12);
      for (int dx : {-6, 6}) {
        line(x + dx - 2, y + 6, x + dx + 2, y + 10, BLACK);
        line(x + dx + 2, y + 6, x + dx - 2, y + 10, BLACK);
        hline(x + dx - 2, y + 8, 5, BLACK);
      }
      return;
    case WeatherGlyph::Thunder: {
      drawCloudGlyph(x, y - 2);
      // Keep a white pixel between the bolt and each side of the cloud base.
      hline(x - 3, y + 5, 7, WHITE);
      static constexpr int bolt[][2] = {
        {2, 2}, {1, 2}, {0, 2}, {-1, 2}, {-2, 3}, {-4, 7},
        {-2, 5}, {0, 3}, {0, 2}, {-1, 2}, {-2, 2}, {-3, 1},
      };
      for (size_t row = 0; row < sizeof(bolt) / sizeof(bolt[0]); ++row)
        hline(x + bolt[row][0], y - 1 + row, bolt[row][1], BLACK);
      return;
    }
    case WeatherGlyph::Unknown: return;
  }
}

void drawMoonGlyph(int x, int y, MoonPhase phase) {
  constexpr int radius = 8;
  if (phase == MoonPhase::Unknown) return;
  if (phase == MoonPhase::New) {
    fillCircle(x, y, radius, BLACK);
    return;
  }
  strokeCircle(x, y, radius, 1, BLACK);
  if (phase == MoonPhase::Full) return;
  const int index = static_cast<int>(phase);
  const int illumination = index < 4 ? index : 8 - index;
  const int edge = illumination == 1 ? 7 : illumination == 2 ? 0 : -7;
  for (int dy = -radius + 1; dy < radius; ++dy) {
    int span = radius - 1;
    while (span * span + dy * dy >= radius * radius) --span;
    for (int dx = -span; dx <= span; ++dx) {
      // The illuminated side stays white. Waxing lights the right side.
      if ((index < 4 ? dx : -dx) * 10 < edge * span) px(x + dx, y + dy, BLACK);
    }
  }
}

void drawHomeWeather(int top, const String& todayKey, int64_t utc) {
  const int centerY = top + kHomeWeatherHeight / 2;
  HomeForecast forecast{};
  if (homeForecastFor(todayKey, forecast)) {
    // Keep the top margin and use the spare pixels above the month label.
    drawWeatherGlyph(kInset + 19, centerY + 1, weatherGlyph(forecast.code));
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

enum class HomeDeliveryIcon { None, Stopwatch, Check };

void drawHomeGoogleLabel(int center, HomeDeliveryIcon icon, bool slovak) {
  const bool checked = icon == HomeDeliveryIcon::Check;
  const char* label = checked ? (slovak ? "Dorucene" : "Delivered") : "Google";
  const int iconWidth = checked ? 11 : 14;
  const int iconSpace = icon == HomeDeliveryIcon::None ? 0 : iconWidth + 4;
  const int x = center - (iconSpace + textW(label, 1)) / 2;
  const int y = kHomeDeliveryLabelY;
  if (icon == HomeDeliveryIcon::Stopwatch) {
    const int cx = x + 7, cy = y + 8;
    strokeCircle(cx, cy, 6, 1, BLACK);
    fillRect(cx - 2, y - 2, 5, 2, BLACK);
    vline(cx, y, 2, BLACK);
    line(cx + 4, y + 3, cx + 6, y + 1, BLACK);
    line(cx, cy, cx + 3, cy - 3, BLACK);
    px(cx, cy - 1, BLACK);
  } else if (checked) {
    for (int dy = 0; dy < 2; ++dy) {
      line(x, y + 7 + dy, x + 3, y + 10 + dy, BLACK);
      line(x + 3, y + 10 + dy, x + 10, y + 3 + dy, BLACK);
    }
  }
  const int labelX = x + iconSpace;
  drawStr(labelX, y, label, 1, BLACK);
  if (checked && slovak) {
    // The native font is ASCII. Place the caron and acute above their lowercase glyphs.
    const int caron = labelX + textW("Doru", 1) + textW("c", 1) / 2;
    const int acute = labelX + textW("Dorucen", 1) + textW("e", 1) / 2;
    line(caron - 2, y - 1, caron, y + 1, BLACK);
    line(caron, y + 1, caron + 2, y - 1, BLACK);
    line(acute - 1, y + 1, acute + 2, y - 1, BLACK);
  }
}

void drawHomeDeliveryValue(int center, const char* value) {
  int scale = 3;
  while (scale > 1 && textW(value, scale) > kHomeDeliveryColumnWidth) --scale;
  int x0, y0, x1, y1;
  textBoundsFont(value, scale, &x0, &y0, &x1, &y1, nullptr);
  drawStrC(center, kHomeDeliveryValueCenterY - (y1 - y0) / 2, value, scale, BLACK);
}

void drawHomeDeliveryCount(int center, int count) {
  char value[8];
  if (count < 0) strcpy(value, "-");
  else if (count > 999999) strcpy(value, "999999+");
  else snprintf(value, sizeof(value), "%d", count);
  drawHomeDeliveryValue(center, value);
}
}  // namespace

void showIdle(int pendingCount, int batteryPercent) {
  note_display::startHome();
  clearWhite();
  if (note_display::abandonSupersededUi()) return;
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
      if (codex.segmentFilled(i)) fillRect(x, gaugeY, segmentWidth, segmentHeight, BLACK);
      else hline(x, gaugeY + segmentHeight - 1, segmentWidth, BLACK);
    }
  }
  if (note_display::abandonSupersededUi()) return;
  const HomeBridgeLink bridgeLink = homeBridgeStatus.latest();
  drawHomeBridgeStatus(bridgeLink);
  drawBattery(batteryPercent);
  hline(kInset, kHeaderRuleY, kContentWidth, BLACK);
  const String nowUtc = currentUtcIso();
  const String localNow = utcToLocalDeviceLabel(nowUtc);
  const String todayKey = localNow.length() >= 10 && localNow != "time not set" ? localNow.substring(0, 10) : "";
  if (note_display::abandonSupersededUi()) return;
  drawHomeWeek(todayKey, time(nullptr));
  if (note_display::abandonSupersededUi()) return;
  hline(kInset, kHomeDeliveryRuleY, kContentWidth, BLACK);
  vline(W / 2, 149, 45, BLACK);
  constexpr int left = W / 4, right = 3 * W / 4;
  drawStrC(left, kHomeDeliveryLabelY, "General", 1, BLACK);
  drawHomeDeliveryCount(left, counts.generalOpen);
  if (counts.waiting > 0) {
    drawHomeGoogleLabel(right, HomeDeliveryIcon::Stopwatch, false);
    drawHomeDeliveryCount(right, counts.waiting);
  } else {
    const bool delivered = counts.waiting == 0 && counts.lastDelivery[0];
    drawHomeGoogleLabel(right, delivered ? HomeDeliveryIcon::Check : HomeDeliveryIcon::None,
                        palaUiLanguage() == "sk");
    drawHomeDeliveryValue(right, delivered ? counts.lastDelivery : "-");
  }
  note_display::present(note_display::Refresh::Regular, nullptr, true);
  homeBridgeStatus.drawn(bridgeLink);
}

void resetTodayAgenda() {
  agendaScreen = AgendaScreen{};
}

int todayAgendaPageCount(bool events) {
  const auto& screen = agendaScreen;
  return screen.page >= 0 && screen.events == events
      ? screen.page + (screen.next.filled ? 2 : 1) : 1;
}

void showTodayAgenda(bool events, int page, AgendaNavigation navigation) {
  clearWhite();
  auto& screen = agendaScreen;
  const bool invalid = screen.page < 0 || screen.events != events ||
                       screen.cursor.generation != agendaIndexGeneration() || page < screen.page;
  const bool available = !invalid || beginAgendaScreen(events);
  if (available) {
    while (screen.page < page && screen.next.filled) {
      screen.current = std::move(screen.next);
      screen.next = readAgendaScreenPage();
      ++screen.page;
    }
  }

  char pageLabel[12];
  snprintf(pageLabel, sizeof(pageLabel), "%d", screen.page + 1);
  drawHeader(available && screen.current.filled ? pageLabel : nullptr, BLACK);
  labelLeft(kContentTop, events ? uiText("TERMINE", "EVENTS") : uiText("AUFGABEN", "TASKS"), 1, BLACK, 112);

  if (!available) {
    drawTwoLineHero("INDEX", uiText("AKTUALISIERT", "UPDATING"), 76, BLACK);
  } else if (!screen.current.filled) {
    drawTwoLineHero(events ? uiText("KEINE", "NO") : uiText("NICHTS", "ALL"),
                    events ? uiText("TERMINE", "EVENTS") : uiText("OFFEN", "DONE"),
                    76, BLACK);
  } else {
    labelRight(kContentTop, screen.current.label.c_str(), 1, BLACK, 58);
    for (int i = 0; i < kAgendaLinesPerPage; ++i) {
      const auto& line = screen.current.lines[i];
      if (!line.isEmpty()) drawStr(kInset, 68 + i * 19, line.c_str(), 1, BLACK);
    }
  }

  const bool hasNext = available && screen.next.filled;
  if (navigation != AgendaNavigation::EventsShortcut) {
    if (hasNext || navigation == AgendaNavigation::MoreDisplayPages)
      drawFooter("NEXT", "HOME", BLACK);
    else drawFooterCentered("HOME", BLACK);
  } else if (!hasNext) {
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

void cacheRecordingCaptions() {
  lockedCaption.store(palaUiLanguage() == "sk" ? "ZAMKNUTE" : uiText("GESPERRT", "LOCKED"));
  stopCaption.store(palaUiLanguage() == "sk" ? "STOP: hociktore tl." : uiText("Stop: jede Taste", "Either button stops"));
  savingCaption.store(uiText("SPEICHERN", "SAVING"), std::memory_order_relaxed);
  priorSaveCaption.store(uiText("Speichere vorige", "Saving last note"), std::memory_order_relaxed);
}

void paintPriorSaveIndicator() {
  constexpr int x = kInset, y = PALA_STAGE3 ? 44 : 171, size = 20;
  fillRect(x, y, size, size, WHITE);
  for (int row = 0; row < 4; ++row) hline(x + size - 4 + row, y + row, 4 - row, BLACK);
  fillRect(x + 4, y + 1, 10, 5, BLACK);
  fillRect(x + 10, y + 2, 2, 3, WHITE);
  fillRect(x + 3, y + 10, 14, 8, BLACK);
  hline(x + 5, y + 13, 10, WHITE);
  hline(x + 5, y + 15, 7, WHITE);
  drawStr(x + size + 6, y + 3, priorSaveCaption.load(std::memory_order_relaxed), 1, WHITE);
}

void paintRecordingScreen(bool fallback) {
  fillRect(0, 0, W, H, BLACK);
#if !PALA_STAGE3
  if (fallback) {
    fillCircle(100, 60, 10, WHITE);
    drawStrC(100, 86, "REC", 3, WHITE);
    return;
  }
  drawHeader(uiText("NOTIZ", "NOTE"), WHITE);
  drawStatusHero(uiText("HOERE ZU", "LISTENING"), 67, WHITE);
  fillCircle(kRight - 7, 146, 7, WHITE);
  labelLeft(139, uiText("AUFNAHME", "RECORDING"), 1, WHITE);
#else
  (void)fallback;
  fillCircle(kInset+6,kHeaderY+7,6,WHITE);
  drawStr(kInset+20,kHeaderY,"REC",1,WHITE);
  hline(kInset,kHeaderRuleY,kContentWidth,WHITE);
#endif
}

void paintRecordingProgress(uint32_t elapsedMs) {
#if PALA_STAGE3
  paintRecordingScreen(false);
#else
  fillRect(0, 0, W, H, BLACK);
  drawHeader(uiText("NOTIZ", "NOTE"), WHITE);
  labelLeft(kContentTop, uiText("AUFNAHME", "RECORDING"), 1, WHITE);
  drawTimer(elapsedMs, WHITE);
#endif
}

void paintRecordingStatus(uint32_t elapsed,bool offer,bool locked) {
  // Draw into the panel's frozen copy so a cached first frame has live battery data.
  fillRect(kHomeBatteryX-2,kHeaderY-1,kBatteryWidth+4,kBatteryHeight+5,WHITE);
  const auto battery=readBattery();
  if (battery.percent>=0) drawBattery(battery.percent);
  else {
    const int top=kHomeHeaderCenterY-kBatteryHeight/2;
    strokeRect(kHomeBatteryX,top,kBatteryBodyWidth,kBatteryHeight,1,BLACK);
    fillRect(kHomeBatteryX+kBatteryBodyWidth,top+3,kBatteryContactWidth,6,BLACK);
    drawStrC(kHomeBatteryX+kBatteryBodyWidth/2,kHeaderY,"?",1,BLACK);
  }
  drawTimer(elapsed,WHITE);
  if (locked) {
    drawStrC(W/2,76,lockedCaption.load(),1,WHITE);
    drawStrC(W/2,kFooterTextY,stopCaption.load(),1,WHITE);
  } else if (offer) {
    labelRight(kFooterTextY,"LOCK",1,WHITE);
    hline(kRight+3,kFooterTextY+6,W-kRight-5,WHITE);
  }
}

void paintSavingScreen() {
  fillRect(0, 0, W, H, WHITE);
  drawBattery(readBattery().percent);

  constexpr int size = 76;
  constexpr int x = (W - size) / 2;
  constexpr int y = (H - size) / 2;
  fillRect(x, y, size, size, BLACK);
  for (int row = 0; row < 12; ++row)
    hline(x + size - 12 + row, y + row, 12 - row, WHITE);

  // The metal shutter and the ruled paper label distinguish a 3.5-inch disk.
  fillRect(x + 14, y + 3, 40, 21, WHITE);
  fillRect(x + 38, y + 6, 9, 15, BLACK);
  fillRect(x + 10, y + 39, 56, 33, WHITE);
  fillRect(x + 17, y + 48, 42, 2, BLACK);
  fillRect(x + 17, y + 58, 33, 2, BLACK);

  const char* caption = savingCaption.load(std::memory_order_relaxed);
  const int scale = textW(caption, 2) <= kContentWidth ? 2 : -1;
  drawStrC(W / 2, y + size + 13, caption, scale, BLACK);
}

void showSaved(int noteNumber, uint32_t edge) {
  if (!note_display::beginSaved(edge)) return;
  clearWhite();
  char number[12];
  const auto ordinal = monthly_recordings::sequence(noteNumber);
  snprintf(number, sizeof(number), "#%03d", ordinal ? int(ordinal % 1000) : noteNumber % 1000);
  drawHeader(noteNumber > 0 ? number : "NOTE", BLACK);
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
  completed = constrain(completed, 0, max(total, 0));
  drawModeHeaderWithBattery("SYNC", readBattery().percent);
  if (total > 0) {
    char progress[24];
    snprintf(progress, sizeof(progress), "%d / %d", completed, total);
    labelLeft(kContentTop, progress, 1, BLACK);
  }
  if (const char* transport = backlogTransportLabel(link)) labelRight(kContentTop, transport, 1, BLACK);
  drawStatusHero(memoryPhaseCopy(phase), 78, BLACK);
  strokeRect(kInset, 149, kContentWidth, 12, 1, BLACK);
  const int innerWidth = kContentWidth - 4;
  const int filled = total > 0 ? (innerWidth * completed) / total : 0;
  if (filled > 0) fillRect(kInset + 2, 151, filled, 8, BLACK);
  refresh(serviceConnection);
}

void paintQuestionScreen() {
  fillRect(0, 0, W, H, BLACK);
  drawHeader(uiText("FRAGE", "ASK"), WHITE);
  drawStatusHero(uiText("HOERE ZU", "LISTENING"), 67, WHITE);
  fillCircle(kRight - 7, 146, 7, WHITE);
  labelLeft(139, uiText("AUFNAHME", "RECORDING"), 1, WHITE);
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
  drawSideButtonChoices(nullptr, palaUiLanguage() == "sk" ? "ZRUSIT" : "CANCEL");
  refresh();
}

void showNoteUsbPairingPrompt(const String& peer, const String& id) {
  clearWhite();
  const bool sk = palaUiLanguage() == "sk";
  labelLeft(6, sk ? "Sparovat cez USB?" : "Allow USB pairing?", 2, BLACK);
  hline(kInset, kHeaderRuleY, kContentWidth, BLACK);
  const String name = normalizeForDisplay(peer);
  if (textW(name.c_str(), 2) <= kContentWidth) {
    labelLeft(50, name.isEmpty() ? (sk ? "Pocitac" : "Computer") : name.c_str(), 2, BLACK);
  } else {
    // Peer names are limited to 32 UTF-8 bytes. Preserve wide names and
    // punctuation, especially the suffix distinguishing two computers.
    int start = 0;
    for (int y = 37; start < static_cast<int>(name.length()) && y <= 94; y += 19) {
      int end = name.length();
      while (end > start + 1 && textW(name.substring(start, end).c_str(), 1) > kContentWidth) --end;
      labelLeft(y, name.substring(start, end).c_str(), 1, BLACK);
      start = end;
    }
  }
  const String suffix = id.length() > 8 ? id.substring(id.length() - 8) : id;
  labelLeft(115, (String("ID ") + suffix).c_str(), 1, BLACK);
  drawConfirmationChoices(true);
  refresh();
}

void showNoteRestartPrompt(bool audit) {
  clearWhite();
  const bool sk = palaUiLanguage() == "sk";
  labelLeft(6, sk ? "Restartovat Note?" : "Restart Note?", 2, BLACK);
  hline(kInset, kHeaderRuleY, kContentWidth, BLACK);
  if (audit) {
    labelLeft(43, sk ? "Aj s kontrolou SD?" : "Check storage too?", 1, BLACK);
    labelLeft(64, sk ? "Moze to trvat." : "This can take time.", 1, BLACK);
  }
  labelLeft(91, sk ? "Na odpoved: 30 s" : "Time to reply: 30 s", 1, BLACK);
  drawSideButtonChoices(sk ? "NIE" : "NO", sk ? "ANO" : "YES");
  refresh();
}

void showNoteRestartExpired() {
  const bool sk = palaUiLanguage() == "sk";
  clearWhite();
  labelLeft(18, sk ? "Cas vyprsal" : "Time expired", 2, BLACK);
  hline(kInset, 48, kContentWidth, BLACK);
  labelLeft(73, sk ? "Bez restartu." : "No restart.", 2, BLACK);
  labelLeft(115, sk ? "Navrat o chvilu." : "Returning shortly.", 1, BLACK);
  refresh();
}

void showNoteStorageAuditProgress(const NoteStorageAuditProgress& progress) {
  clearWhite();
  drawHeader("STORAGE", BLACK);
  using namespace note_maintenance;
  drawStrFit(kInset, 54, kContentWidth, auditCaption(progress.phase).phase, 1, BLACK);
  char count[48];
  auditCountText(progress, count, sizeof(count));
  drawStrC(W / 2, 88, count, 1, BLACK);
  constexpr int left = 12, width = 176;
  if (progress.total || progress.phase == NoteStorageAuditPhase::Finished) {
    strokeRect(left, 120, width, 15, 1, BLACK);
    const int filled = static_cast<int>(auditBarPercent(progress) * (width - 4) / 100);
    if (filled) fillRect(left + 2, 122, filled, 11, BLACK);
  }
  snprintf(count, sizeof(count), "%lus   %lu issues", static_cast<unsigned long>(progress.elapsedSeconds),
           static_cast<unsigned long>(progress.problems));
  drawStrC(W / 2, 150, count, 1, BLACK);
  refresh();
}

void showNoteBluetoothPairingNumber(uint32_t code) {
  clearWhite();
  drawHeader("BT PAIRING", BLACK);
  labelLeft(kContentTop, "Compare code on PC", 1, BLACK);
  char digits[7];
  snprintf(digits, sizeof(digits), "%06lu", static_cast<unsigned long>(code % 1000000U));
  drawStrC(W / 2, 82, digits, 3, BLACK);
  drawConfirmationChoices(true);
  refresh();
}

void showTransferConnecting(int batteryPercent) {
  clearWhite();
  drawModeHeaderWithBattery("WEB", batteryPercent);
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

void drawCompanionAddress(const String& ip, const String& hostname) {
  constexpr int maxWidth = kContentWidth - 12;
  const String& host = hostname.isEmpty() ? ip : hostname;
  const String url = String("http://") + host;
  const String& address = textW(url.c_str(), 1) <= maxWidth ? url : host;
  if (textW(address.c_str(), 1) <= maxWidth) {
    fillRect(kInset, kCompanionAddressY, kContentWidth, kCompanionAddressHeight, BLACK);
    const int scale = textW(address.c_str(), 2) <= maxWidth ? 2 : 1;
    drawStrInBox(kInset, kCompanionAddressY, kContentWidth, kCompanionAddressHeight,
                 address.c_str(), scale, WHITE);
    if (!hostname.isEmpty()) labelRight(kCompanionPasswordY, ip.c_str(), 1, BLACK);
    return;
  }

  // Keep the complete .local address visible even for wide letters and name collisions.
  int split = host.lastIndexOf('.');
  if (split <= 0) split = host.length();
  while (split > 1 && textW(host.substring(0, split).c_str(), 1) > maxWidth) --split;
  constexpr int lineHeight = 21;
  fillRect(kInset, kCompanionAddressY, kContentWidth, 2 * lineHeight, BLACK);
  drawStrInBox(kInset, kCompanionAddressY, kContentWidth, lineHeight,
               host.substring(0, split).c_str(), 1, WHITE);
  drawStrInBox(kInset, kCompanionAddressY + lineHeight, kContentWidth, lineHeight,
               host.substring(split).c_str(), 1, WHITE);
  if (!hostname.isEmpty()) labelRight(kCompanionAddressY + 2 * lineHeight + 4, ip.c_str(), 1, BLACK);
}

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

void showTransferMode(CompanionLink link, const String& network, const String& ip, const String& hostname,
                      const String& apPassword, int batteryPercent, int volumeOverlay) {
  clearWhite();
  drawModeHeaderWithBattery("WEB", batteryPercent);
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

    drawCompanionAddress(ip, hostname);
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
  drawStatusHero(message && *message ? translatedUi(message)
                                    : uiText("FEHLER", "ERROR"), 72, BLACK);
  refresh();
}

#if PALA_USB_CARD_SERVICE
void showUsbCardService() {
  const bool sk = palaUiLanguage() == "sk";
  clearWhite();
  labelLeft(9, "USB disk", 2, BLACK);
  hline(kInset, 39, kContentWidth, BLACK);
  labelLeft(64, sk ? "Karta patri PC" : "Card in use by PC", 2, BLACK);
  labelLeft(105, sk ? "REC pozastavene" : "Recording paused", 1, BLACK);
  labelLeft(139, sk ? "Pre navrat vysun disk" : "Eject disk to resume", 1, BLACK);
  refresh();
}
#endif

void showBootError(const char* message) {
  const bool sk = palaUiLanguage() == "sk";
  const bool sd = !strcmp(message, "SD ERROR");
  const bool memory = !strcmp(message, "8/8 ONLY");
  Serial.printf("[UI] startup error: %s\n", message);
  clearWhite();
  labelLeft(9, sk ? "Chyba pri starte" : "Startup error", 2, BLACK);
  hline(kInset, 37, kContentWidth, BLACK);
  if (memory) {
    labelLeft(57, sk ? "Treba 8 MB flash" : "Needs 8 MB flash", 1, BLACK);
    labelLeft(80, sk ? "a 8 MB PSRAM." : "and 8 MB PSRAM.", 1, BLACK);
  } else {
    labelLeft(58, sd ? (sk ? "Chyba SD karty" : "SD card error")
                     : (sk ? "Chyba mikrofonu" : "Microphone error"), 2, BLACK);
    labelLeft(104, sd ? (sk ? "Skontroluj kartu." : "Check the SD card.")
                      : (sk ? "Skontroluj mikrofon." : "Check the mic."), 1, BLACK);
    labelLeft(132, sk ? "Restartuj Note." : "Restart the Note.", 1, BLACK);
  }
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

static int calendarButtonChoice(bool (*serviceConnection)()) {
  return calendar_button::choose(millis, [] {
    return (digitalRead(BTN_REC) == LOW ? 1 : 0) | (digitalRead(BTN_PWR) == LOW ? 2 : 0);
  }, delay, [&] {
    return !recordingRequestedDuringSync() && (!serviceConnection || serviceConnection());
  });
}

static bool parseCalendarMinute(const String& text, struct tm& fields) {
  if (text.length() != 16 || !validUtcTimestamp((text + ":00Z").c_str())) return false;
  fields = {};
  fields.tm_year = text.substring(0, 4).toInt() - 1900;
  fields.tm_mon = text.substring(5, 7).toInt() - 1;
  fields.tm_mday = text.substring(8, 10).toInt();
  fields.tm_hour = text.substring(11, 13).toInt();
  fields.tm_min = text.substring(14, 16).toInt();
  return true;
}

struct CalendarRangeDisplay {
  String startDate;
  String startClock;
  String endDate;
  String endClock;
  String duration;

  bool crossesDate() const { return endDate.length() && startDate != endDate; }
  String clockRange() const {
    return endClock.isEmpty() ? startClock : startClock + " - " + endClock;
  }
};

static String displayCalendarDate(const String& stamp) {
  return stamp.substring(8, 10) + "." + stamp.substring(5, 7) + "." + stamp.substring(0, 4);
}

static CalendarRangeDisplay calendarRangeDisplay(const CalendarProposalTime& time) {
  CalendarRangeDisplay range;
  struct tm start{}, end{};
  if (!parseCalendarMinute(time.start, start)) {
    range.startDate = "?";
    range.startClock = "?";
    return range;
  }
  range.startDate = displayCalendarDate(time.start);
  range.startClock = time.start.substring(11, 16);
  if (!parseCalendarMinute(time.end, end)) return range;
  range.endDate = displayCalendarDate(time.end);
  range.endClock = time.end.substring(11, 16);

  setenv("TZ", LOCAL_TZ_RULE, 1);
  tzset();
  const time_t from = localWallClockEpoch(utcTmToEpoch(start));
  const time_t until = localWallClockEpoch(utcTmToEpoch(end));
  if (from != -1 && until > from) range.duration = String(static_cast<long>((until - from) / 60)) + " min";
  return range;
}

static void drawCalendarHeading(const char* question, const String& title) {
  clearWhite();
  labelLeft(6, question, 2, BLACK);
  hline(kInset, 32, kContentWidth, BLACK);

  const String text = normalizeForDisplay(title);
  if (text.isEmpty()) return;
  if (textW(text.c_str(), 2) <= kContentWidth) {
    labelLeft(39, text.c_str(), 2, BLACK);
    return;
  }
  int lines = 0;
  String second;
  visitAnswerLines(text, [&](const String& line) {
    ++lines;
    if (lines == 1) labelLeft(38, line.c_str(), 1, BLACK);
    if (lines == 2) second = line;
    if (lines < 3) return true;
    while (second.length() && textW((second + "...").c_str(), 1) > kContentWidth)
      second.remove(second.length() - 1);
    second += "...";
    return false;
  });
  if (second.length()) labelLeft(57, second.c_str(), 1, BLACK);
}

static void drawCalendarEndpoint(int y, const char* label, const String& date, const String& clock) {
  // Leave the centre and right of the row for the complete local date and time.
  drawStr(5, y, label, 1, BLACK);
  labelRight(y, (date + " " + clock).c_str(), 1, BLACK);
}

static void drawCalendarRange(const CalendarRangeDisplay& range, bool sharedDate, bool sk,
                              int detailY, int timeY, bool alignToButton) {
  if (range.crossesDate()) {
    drawCalendarEndpoint(detailY, sk ? "Od" : "Start", range.startDate, range.startClock);
    drawCalendarEndpoint(timeY, sk ? "Do" : "End", range.endDate, range.endClock);
    return;
  }
  if (!sharedDate) labelLeft(detailY, range.startDate.c_str(), 1, BLACK);
  if (range.duration.length()) labelRight(detailY, range.duration.c_str(), 1, BLACK);
  const String clocks = range.clockRange();
  if (alignToButton) labelRight(timeY, clocks.c_str(), 2, BLACK);
  else labelLeft(timeY, clocks.c_str(), 2, BLACK);
}

int chooseCalendarEventDate(const String& title, const CalendarProposalTime& first, const CalendarProposalTime& second,
                            bool (*serviceConnection)()) {
  RecordingWindow microphone;
  const bool sk = palaUiLanguage() == "sk";
  const auto upper = calendarRangeDisplay(first);
  const auto lower = calendarRangeDisplay(second);
  const bool sharedDate = upper.startDate == lower.startDate && !upper.crossesDate() && !lower.crossesDate();
  drawCalendarHeading(sk ? "Ktory termin?" : "Which time?", title);
  if (sharedDate) labelLeft(79, upper.startDate.c_str(), 2, BLACK);

  hline(kInset, 104, kContentWidth, BLACK);
  drawCalendarRange(upper, sharedDate, sk, 109, 131, true);
  drawSideButtonCue(137);
  hline(kInset, 154, kContentWidth, BLACK);
  drawCalendarRange(lower, sharedDate, sk, 159, 177, true);
  drawSideButtonCue(181);
  refresh();
  return note_display::lastPresentationOk() ? calendarButtonChoice(serviceConnection) : -1;
}

// 1 accepts, 0 declines, -1 leaves the proposal pending at the Bridge.
int confirmCalendarEvent(const String& title, const CalendarProposalTime& time, int minutes,
                         bool (*serviceConnection)()) {
  RecordingWindow microphone;
  const bool sk = palaUiLanguage() == "sk";
  drawCalendarHeading(sk ? "Ulozit termin?" : "Save event?", title);
  drawCalendarRange(calendarRangeDisplay(time), false, sk, 77, 97, false);
  if (minutes >= 0) {
    labelLeft(119, sk ? "Predstih" : "Alert", 1, BLACK);
    labelRight(119, (String(minutes) + " min").c_str(), 1, BLACK);
  }
  drawConfirmationChoices(false);
  refresh();
  const int button = note_display::lastPresentationOk() ? calendarButtonChoice(serviceConnection) : -1;
  return button < 0 ? -1 : 1 - button;
}
