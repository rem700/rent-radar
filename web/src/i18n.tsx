import { ConfigProvider } from 'antd'
import deDE from 'antd/locale/de_DE'
import enGB from 'antd/locale/en_GB'
import ruRU from 'antd/locale/ru_RU'
import dayjs from 'dayjs'
import 'dayjs/locale/de'
import 'dayjs/locale/en-gb'
import 'dayjs/locale/ru'
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { theme } from './theme'

export type Lang = 'ru' | 'en' | 'de'
export const LANGS: { value: Lang; label: string }[] = [
  { value: 'ru', label: 'RU' },
  { value: 'en', label: 'EN' },
  { value: 'de', label: 'DE' },
]

// Тексты интерфейса. {n}, {x} — подстановки. Ключи с _one/_few/_many/_other — формы множественного числа.
const ru = {
  appTitle: 'Поиск аренды',
  newSearch: 'Новый поиск',
  backendDown: 'Нет связи с сервером. Запустите start.bat и обновите страницу.',
  variants_one: 'вариант', variants_few: 'варианта', variants_many: 'вариантов', variants_other: 'варианта',
  updating: 'Обновляю…',
  updatedAgo: 'Обновлено {x}',
  neverUpdated: 'Ещё не обновлялось',
  markViewed: 'Отметить {n} новых как просмотренные',
  sourceFailed: '{x} не ответил',
  modeSplit: 'Список и карта', modeList: 'Список', modeMap: 'Карта',
  checkNow: 'Проверить сайты сейчас',
  refresh: 'Обновить',
  searchSettings: 'Настройки поиска',
  deleteSearch: 'Удалить поиск',
  deleteConfirm: 'Удалить поиск вместе с найденными вариантами?',
  delete: 'Удалить', cancel: 'Отмена',
  collecting: 'Собираю объявления, это займёт минуту',
  nothingFound: 'Под эти фильтры ничего не подходит. Попробуйте расширить цену или площадь.',
  emptyTitle: 'Где ищем жильё?',
  emptyText: 'Укажите страну и город. Сайты аренды для этой страны подключатся сами.',
  searchSaved: 'Поиск сохранён',
  searchCreated: 'Поиск создан, собираю объявления',
  // фильтры
  price: 'Цена, €', rooms: 'Комнат', area: 'Площадь, м²', from: 'от', to: 'до',
  moreFilters: 'Ещё фильтры', filters: 'Фильтры',
  sortNewest: 'Сначала новые', sortCheap: 'Сначала дешёвые', sortExpensive: 'Сначала дорогие',
  sortArea: 'Сначала просторные', sortPerM2: 'Дешевле за м²',
  resetFilters: 'Как в настройках поиска', show: 'Показать',
  district: 'Район', districtPh: 'Например, XIV. kerület',
  keywords: 'В объявлении есть слова', keywordsHint: 'Через запятую, нужны все', keywordsPh: 'балкон, лифт',
  exclude: 'Скрыть, если есть слова', excludeHint: 'Через запятую, хватит любого', excludePh: 'посуточно, комната',
  pets: 'Животные', any: 'Не важно', petsYes: 'Только где явно можно', petsNo: 'Убрать, где явно нельзя',
  furniture: 'Мебель', furnYes: 'С мебелью', furnNo: 'Без мебели',
  onlyNew: 'Только новые', onlyFav: 'Только избранное', showHidden: 'Показывать скрытые',
  showInactive: 'Показывать снятые с публикации',
  // карточка
  roomsShort: '{n} комн.', floorN: '{x} этаж', noPhoto: 'нет фото', isNew: 'новое',
  petsOk: 'можно с животными', petsNotOk: 'без животных', furnished: 'с мебелью', inactive: 'снято с публикации',
  priceDown: 'цена снижена', priceUp: 'цена выросла', foundAgo: 'найдено {x}',
  favAdd: 'В избранное', favRemove: 'Убрать из избранного', favorite: 'Избранное',
  hide: 'Скрыть', unhide: 'Вернуть в выдачу',
  approxPlace: 'Место примерное', openOn: 'Открыть на {x}',
  // форма поиска
  otherCountry: 'Другая страна (только демо-данные)',
  country: 'Страна', chooseCountry: 'Выберите страну', city: 'Город', enterCity: 'Введите город',
  cityPh: 'Например: Budapest, București, Bratislava', isoCode: 'ISO-код страны (2 буквы)',
  nameOpt: 'Название (необязательно)', namePh: 'Например: Будапешт 2-комн. до 900 €',
  sources: 'Источники', defaultFilters: 'Фильтры по умолчанию',
  pricePerMonth: 'Цена, € в месяц', districtPart: 'Район (часть названия)',
  updates: 'Обновление', autoUpdate: 'Автообновление', everyMin: 'Каждые, мин',
  pagesPerSite: 'Страниц с сайта', pagesHint: 'Сколько страниц выдачи читать с каждого сайта за один проход',
  save: 'Сохранить', createRun: 'Создать и запустить',
  language: 'Язык',
  demoBanner: 'Демо-версия с примерными данными. Настоящий поиск по сайтам работает при запуске сервиса на своём компьютере.',
  demoReadonly: 'В демо-версии это недоступно',
  sourceCode: 'Код на GitHub',
}
type Dict = typeof ru

const en: Dict = {
  appTitle: 'Rental search',
  newSearch: 'New search',
  backendDown: 'No connection to the server. Run start.bat and reload the page.',
  variants_one: 'listing', variants_few: 'listings', variants_many: 'listings', variants_other: 'listings',
  updating: 'Updating…',
  updatedAgo: 'Updated {x}',
  neverUpdated: 'Not updated yet',
  markViewed: 'Mark {n} new as seen',
  sourceFailed: '{x} did not respond',
  modeSplit: 'List and map', modeList: 'List', modeMap: 'Map',
  checkNow: 'Check the sites now',
  refresh: 'Refresh',
  searchSettings: 'Search settings',
  deleteSearch: 'Delete search',
  deleteConfirm: 'Delete this search and everything it found?',
  delete: 'Delete', cancel: 'Cancel',
  collecting: 'Collecting listings, this takes about a minute',
  nothingFound: 'Nothing matches these filters. Try widening the price or size.',
  emptyTitle: 'Where are you looking?',
  emptyText: 'Choose a country and city. Rental sites for that country are added automatically.',
  searchSaved: 'Search saved',
  searchCreated: 'Search created, collecting listings',
  price: 'Price, €', rooms: 'Rooms', area: 'Size, m²', from: 'from', to: 'to',
  moreFilters: 'More filters', filters: 'Filters',
  sortNewest: 'Newest first', sortCheap: 'Cheapest first', sortExpensive: 'Most expensive first',
  sortArea: 'Largest first', sortPerM2: 'Cheapest per m²',
  resetFilters: 'Reset to search defaults', show: 'Show',
  district: 'District', districtPh: 'e.g. XIV. kerület',
  keywords: 'Listing contains words', keywordsHint: 'Comma-separated, all required', keywordsPh: 'balcony, lift',
  exclude: 'Hide if it contains words', excludeHint: 'Comma-separated, any one is enough', excludePh: 'daily, room',
  pets: 'Pets', any: 'Any', petsYes: 'Only where explicitly allowed', petsNo: 'Hide where explicitly not allowed',
  furniture: 'Furniture', furnYes: 'Furnished', furnNo: 'Unfurnished',
  onlyNew: 'New only', onlyFav: 'Favourites only', showHidden: 'Show hidden',
  showInactive: 'Show removed listings',
  roomsShort: '{n} rm', floorN: 'floor {x}', noPhoto: 'no photo', isNew: 'new',
  petsOk: 'pets allowed', petsNotOk: 'no pets', furnished: 'furnished', inactive: 'removed',
  priceDown: 'price dropped', priceUp: 'price went up', foundAgo: 'found {x}',
  favAdd: 'Add to favourites', favRemove: 'Remove from favourites', favorite: 'Favourite',
  hide: 'Hide', unhide: 'Show again',
  approxPlace: 'Approximate location', openOn: 'Open on {x}',
  otherCountry: 'Other country (demo data only)',
  country: 'Country', chooseCountry: 'Choose a country', city: 'City', enterCity: 'Enter a city',
  cityPh: 'e.g. Budapest, București, Bratislava', isoCode: 'Country ISO code (2 letters)',
  nameOpt: 'Name (optional)', namePh: 'e.g. Budapest 2 rooms up to €900',
  sources: 'Sources', defaultFilters: 'Default filters',
  pricePerMonth: 'Price, € per month', districtPart: 'District (part of the name)',
  updates: 'Updates', autoUpdate: 'Auto-update', everyMin: 'Every, min',
  pagesPerSite: 'Pages per site', pagesHint: 'How many result pages to read from each site per run',
  save: 'Save', createRun: 'Create and run',
  language: 'Language',
  demoBanner: 'Demo with sample data. Real searches across the sites run when you start the service on your own computer.',
  demoReadonly: 'Not available in the demo',
  sourceCode: 'Code on GitHub',
}

const de: Dict = {
  appTitle: 'Mietwohnungssuche',
  newSearch: 'Neue Suche',
  backendDown: 'Keine Verbindung zum Server. Starten Sie start.bat und laden Sie die Seite neu.',
  variants_one: 'Angebot', variants_few: 'Angebote', variants_many: 'Angebote', variants_other: 'Angebote',
  updating: 'Wird aktualisiert…',
  updatedAgo: 'Aktualisiert {x}',
  neverUpdated: 'Noch nicht aktualisiert',
  markViewed: '{n} neue als gesehen markieren',
  sourceFailed: '{x} antwortet nicht',
  modeSplit: 'Liste und Karte', modeList: 'Liste', modeMap: 'Karte',
  checkNow: 'Websites jetzt prüfen',
  refresh: 'Aktualisieren',
  searchSettings: 'Sucheinstellungen',
  deleteSearch: 'Suche löschen',
  deleteConfirm: 'Suche samt allen gefundenen Angeboten löschen?',
  delete: 'Löschen', cancel: 'Abbrechen',
  collecting: 'Angebote werden gesammelt, das dauert etwa eine Minute',
  nothingFound: 'Keine Treffer für diese Filter. Erweitern Sie Preis oder Fläche.',
  emptyTitle: 'Wo suchen Sie?',
  emptyText: 'Wählen Sie Land und Stadt. Die Mietportale dieses Landes werden automatisch eingebunden.',
  searchSaved: 'Suche gespeichert',
  searchCreated: 'Suche erstellt, Angebote werden gesammelt',
  price: 'Preis, €', rooms: 'Zimmer', area: 'Fläche, m²', from: 'von', to: 'bis',
  moreFilters: 'Weitere Filter', filters: 'Filter',
  sortNewest: 'Neueste zuerst', sortCheap: 'Günstigste zuerst', sortExpensive: 'Teuerste zuerst',
  sortArea: 'Größte zuerst', sortPerM2: 'Günstigste pro m²',
  resetFilters: 'Wie in den Sucheinstellungen', show: 'Anzeigen',
  district: 'Bezirk', districtPh: 'z. B. XIV. kerület',
  keywords: 'Anzeige enthält Wörter', keywordsHint: 'Durch Komma getrennt, alle erforderlich', keywordsPh: 'Balkon, Aufzug',
  exclude: 'Ausblenden bei Wörtern', excludeHint: 'Durch Komma getrennt, eines genügt', excludePh: 'tageweise, Zimmer',
  pets: 'Haustiere', any: 'Egal', petsYes: 'Nur wo ausdrücklich erlaubt', petsNo: 'Ausblenden, wo ausdrücklich verboten',
  furniture: 'Möbliert', furnYes: 'Möbliert', furnNo: 'Unmöbliert',
  onlyNew: 'Nur neue', onlyFav: 'Nur Favoriten', showHidden: 'Ausgeblendete anzeigen',
  showInactive: 'Entfernte Angebote anzeigen',
  roomsShort: '{n} Zi.', floorN: '{x}. Stock', noPhoto: 'kein Foto', isNew: 'neu',
  petsOk: 'Haustiere erlaubt', petsNotOk: 'keine Haustiere', furnished: 'möbliert', inactive: 'entfernt',
  priceDown: 'Preis gesenkt', priceUp: 'Preis gestiegen', foundAgo: 'gefunden {x}',
  favAdd: 'Zu Favoriten', favRemove: 'Aus Favoriten entfernen', favorite: 'Favorit',
  hide: 'Ausblenden', unhide: 'Wieder anzeigen',
  approxPlace: 'Ungefähre Lage', openOn: 'Auf {x} öffnen',
  otherCountry: 'Anderes Land (nur Demodaten)',
  country: 'Land', chooseCountry: 'Land wählen', city: 'Stadt', enterCity: 'Stadt eingeben',
  cityPh: 'z. B. Budapest, București, Bratislava', isoCode: 'ISO-Ländercode (2 Buchstaben)',
  nameOpt: 'Name (optional)', namePh: 'z. B. Budapest 2 Zimmer bis 900 €',
  sources: 'Quellen', defaultFilters: 'Standardfilter',
  pricePerMonth: 'Preis, € pro Monat', districtPart: 'Bezirk (Teil des Namens)',
  updates: 'Aktualisierung', autoUpdate: 'Automatisch aktualisieren', everyMin: 'Alle, Min.',
  pagesPerSite: 'Seiten pro Website', pagesHint: 'Wie viele Ergebnisseiten pro Website und Durchlauf gelesen werden',
  save: 'Speichern', createRun: 'Erstellen und starten',
  language: 'Sprache',
  demoBanner: 'Demo mit Beispieldaten. Die echte Suche über die Portale läuft, wenn Sie den Dienst auf Ihrem eigenen Computer starten.',
  demoReadonly: 'In der Demo nicht verfügbar',
  sourceCode: 'Code auf GitHub',
}

const DICTS: Record<Lang, Dict> = { ru, en, de }
const ANTD = { ru: ruRU, en: enGB, de: deDE }
const DAYJS = { ru: 'ru', en: 'en-gb', de: 'de' }
const NUM = { ru: 'ru-RU', en: 'en-GB', de: 'de-DE' }

export type TKey = keyof Dict

interface Ctx {
  lang: Lang
  setLang: (l: Lang) => void
  t: (key: TKey, vars?: Record<string, string | number>) => string
  plural: (base: 'variants', n: number) => string
  country: (code: string) => string
  num: (v: number) => string
  money: (v: number | null, cur: string | null) => string
}

const I18nContext = createContext<Ctx | null>(null)

function initialLang(): Lang {
  try {
    const saved = localStorage.getItem('lang')
    if (saved === 'ru' || saved === 'en' || saved === 'de') return saved
  } catch {
    /* хранилище недоступно — язык по умолчанию */
  }
  return 'ru'
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(initialLang)
  dayjs.locale(DAYJS[lang])

  const setLang = useCallback((l: Lang) => {
    setLangState(l)
    try {
      localStorage.setItem('lang', l)
    } catch {
      /* не страшно, просто не запомним */
    }
  }, [])

  const value = useMemo<Ctx>(() => {
    const dict = DICTS[lang]
    const fill = (s: string, vars?: Record<string, string | number>) =>
      vars ? s.replace(/\{(\w+)\}/g, (_, k) => String(vars[k] ?? '')) : s
    const rules = new Intl.PluralRules(NUM[lang])
    let regions: Intl.DisplayNames | null = null
    try {
      regions = new Intl.DisplayNames([NUM[lang]], { type: 'region' })
    } catch {
      regions = null
    }
    return {
      lang,
      setLang,
      t: (key, vars) => fill(dict[key] ?? ru[key] ?? key, vars),
      plural: (base, n) => {
        const form = rules.select(n) as 'one' | 'few' | 'many' | 'other'
        const key = `${base}_${form}` as TKey
        return dict[key] ?? dict[`${base}_other` as TKey]
      },
      country: (code) => {
        try {
          return regions?.of(code) ?? code
        } catch {
          return code
        }
      },
      num: (v) => Math.round(v).toLocaleString(NUM[lang]),
      money: (v, cur) => (v == null ? '—' : `${Math.round(v).toLocaleString(NUM[lang])} ${cur === 'EUR' ? '€' : cur ?? ''}`.trim()),
    }
  }, [lang, setLang])

  return (
    <I18nContext.Provider value={value}>
      <ConfigProvider locale={ANTD[lang]} theme={theme}>
        {children}
      </ConfigProvider>
    </I18nContext.Provider>
  )
}

export function useI18n(): Ctx {
  const ctx = useContext(I18nContext)
  if (!ctx) throw new Error('useI18n вне I18nProvider')
  return ctx
}
