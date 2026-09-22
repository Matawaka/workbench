# Промпт для следующего сеанса Codex

Продолжи Jev / TypeSafe shadow research в Matawaka/workbench с сохранённой точки, а не с нуля. Общайся со мной по-русски. Выполни один законченный инженерный шаг: provenance-aware reference admission / adjudication bridge для corpus v0.1 и label semantics v0.2. Не выполняй новые вызовы Jev, реальные операции Workbench или автоматическое принятие человеческого эталона.

## 1. Восстанови состояние

Прочитай существующие AGENTS.md в области работы, затем `docs/jev-codex-handoff-20260922/HANDOFF.md` и этот файл. Handoff-ветка: `handoff/jev-shadow-codex-2026-09-22`. Точный implementation baseline до handoff: `a6e389f1b2910e987f5749186152fb5d31f98eda`, PR #117, ветка `research/jev-shadow-label-semantics-v0.2`. `main` не заменяет эту цепочку research-веток.

Сначала проверь git status, remotes, HEAD и ancestry. На компьютере оператора репозиторий находится в `K:\matawaka\workbench`, remote называется `github-workbench`. В cloud-клоне имя remote может быть другим: обнаружь, не угадывай. Создай отдельную рабочую ветку, например `codex/jev-corpus-reference-admission-v0.2`, от проверенного handoff commit. Не трогай незакоммиченные изменения, не делай reset --hard, force push, merge/rebase/retarget/закрытие старых PR. Публикуй только новый Draft PR при доступном GitHub; иначе сохрани локальный commit/patch и точные команды передачи.

Исходники: `tooling/jev-system-one-judgment-adapter`, `tooling/jev-shadow-one-shot-send-v003`, `tooling/jev-shadow-evidence-corpus-v001`, `tooling/jev-shadow-real-case-lab-v001`, `tooling/jev-shadow-label-semantics-v002`. Историю решений смотри в PR #108–117, особенно #115–117 и комментариях о происхождении reviewer.

## 2. Зафиксируй границы

`Probabilistic Judgment != Authorization`. Не менять Workbench permit/deny, CommandRouter, AgentHost, production MainWindow, provider implementation, сетевые разрешения и одноразовые lease. Не запускать `send:once`, `qualify:*`, не запрашивать/читать/печатать API key. Все новые тесты — offline/synthetic. Исторические CI, predecessor gates и GREEN/RED evidence не переписывать ради прохождения нового кода.

Реальный case-0001 имеет PRIMARY, SECONDARY, addendum и Jev receipt. SECONDARY получена человеком с помощью ИИ — это уже подтверждено оператором; не спрашивай об этом повторно. Сохранять `HUMAN_SECONDARY` как историческую роль, отдельно учитывать `HUMAN_AI_ASSISTED` как заявленное происхождение. Это не новая уже реализованная разновидность labelKind и не автоматический допуск в human-ground-truth cohort. AI assistance и доступ к Jev — независимые измерения. Не выводить отсутствие знакомства человека с моделью вне отдельного assisting-чата из декларации о контексте этого чата.

Исходные байты immutable. Хэш доказывает совпадение содержимого, не время, авторство или blinding. `labeledAt=null` в SECONDARY не исправлять задним числом. Нет завершённой HUMAN_ADJUDICATED reference; case-0001 остаётся `ADJUDICATION_PENDING` / reference admission HOLD. Ты видишь post-model контекст: не выдавай собственный ответ за HUMAN_SECONDARY или model-blinded HUMAN_ADJUDICATED.

## 3. Приватные материалы

Реальное evidence хранится вне public repo: `K:\matawaka-private-evidence\jev-shadow\cases\case-0001`. Передаваемый архив PRIVATE также содержит derived-аудиты, исходные загруженные файлы и список отсутствующих оригиналов. Читать реальные данные только при применимом разрешении оператора; не публиковать private labels, per-signal comparisons, operational context или JSON в GitHub/CI. Локальный Codex не означает офлайн-обработку: не отправляй необезличенные корпоративные данные модели.

Если приватные файлы недоступны, НЕ блокируй разработку просьбой загрузить их публично. Выполни схемы, валидаторы, offline-тесты и CLI на явно синтетических fixtures; для настоящего case выдай `PRIVATE_INPUTS_UNAVAILABLE` и точный список нужных файлов/проверок. Не реконструируй отсутствующие originals как якобы исходные. Не запускай старый intake поверх замороженной папки: он может перезаписать файлы.

## 4. Реализуй ограниченный successor

Предпочтительно новый отдельный пакет `tooling/jev-shadow-reference-admission-v002/`, не меняющий исторические corpus/label файлы. Сначала воспроизведи проблемы минимальными offline regression-тестами. Раздели:

- историческую роль (`HUMAN_PRIMARY`, `HUMAN_SECONDARY`, ...);
- фактическое происхождение (`HUMAN_UNASSISTED`, `HUMAN_AI_ASSISTED`, `MODEL_ONLY`, `UNKNOWN`) и основание декларации;
- exposure отдельно для человека, assisting model/session и adjudicator (`UNKNOWN` не превращается в false);
- применимость/определённость (`ASSERTED`, `UNDETERMINED`, `NOT_APPLICABLE`);
- завершённость/подписи-ссылки review, самостоятельность reviewers и допуск reference, а не просто `labelKind`.

Не делай вид, что эти поля сами доказывают авторство или техническую изоляцию. Нужны связанные источники, явные missing reasons и уровни утверждений. Помощь ИИ не запрещать автоматически; выделить отдельный диагностический/assisted cohort и потребовать заранее заданную политику для любого reference-admission. При недостатке сведений закрытый по умолчанию допуск: diagnostic/pending, не calibrated/reference.

Создай безопасные команды preflight, prepare-human-only-adjudication и diagnostic-score. Пакет для adjudicator не должен принимать Jev receipt, содержать предсказания/вероятности, primary-vs-model результаты или ответы, выбранные Codex. Итоговый adjudication draft оставляй незаполненным. Post-model comparison — отдельный private CLI и явно не слепой эталон. Согласие SECONDARY с Jev нельзя использовать для выбора истины.

Проверь полную chain binding: raw SHA-256 исходников; canonical packet digest, пересчитанный из фактического пакета; caseId; packet↔candidate↔request↔receipt; primary/secondary↔packet; supplementary provenance↔exact frozen-label digest. Не доверяй самозаявленному packet.reviewPacketDigest. Unknown schema, несовпадения, пропуски, нечисловые вероятности, NaN/Infinity, выход за [0,1], неизвестные или несовпадающие сигналы — явный отказ, без тихого пропуска.

Scoring: UNDETERMINED и NOT_APPLICABLE исключаются с явными причинами, никогда не преобразуются в false/0. `hasReliableRollback=NOT_APPLICABLE` допускается только при ASSERTED false у causesExternalMutation. `operationalSpecificity` сохраняется research-only, без policy admission. ECE gating считать по уникальным исходным случаям/группам в каждом signal/model/rubric cohort, не по сумме шести вопросов и не по повторным вызовам. Разные caseId одной исходной ситуации не считать независимыми; недостаточно просто `independentSample=true`. Не объединять модели/версии вопросника, assisted/unassisted или synthetic/real без явного разделения. Порог 30 — существующий exploratory default, не доказательство достаточности выборки. При недостатке данных ECE=null с объяснением. Все текущие выводы диагностические; ни одна метрика не устанавливает production threshold.

Новые CLI должны писать только в новый каталог вне public repo, без overwrite. Проверить реальный путь/предков, symlink/junction escape, BOM, Windows пути и повторный запуск. Фиксация source manifest не должна менять оригиналы. Использовать временные синтетические каталоги в тестах и не копировать private evidence в fixtures.

## 5. Критерии завершения одного шага

1. Исходные четыре Node suites повторно запущены (исторически 20+4+7+5=36); отдельно указан фактический результат. Windows/.NET build — при наличии подходящей среды, иначе честно NOT RUN и ссылка на прошлую квалификацию.
2. Новый suite покрывает: digest mismatch; fake self-digest; case/request/receipt mismatch; отсутствие provenance; unknown exposure; assisted-origin без автоматического human-reference admission; разные/неподтверждённые reviewers; попытку выдать migration за независимый review; template как finished review; N/A/undetermined; некорректные числа/сигналы; 5 cases×6 signals не дают 30 independent cases; одинаковый source под разными IDs; несколько model/rubric cohorts; private path escape; отказ overwrite; отсутствие model output в human-only packet.
3. Реальная case-0001 остаётся HOLD, пока нет необходимых человеческих и исходных доказательств. Не создать HUMAN_ADJUDICATED автоматически. При доступном private bundle допустим только append-only audit в новом private каталоге.
4. Draft PR содержит только код, synthetic tests, документацию и новый narrow workflow. Старые production/protected файлы и frozen evidence побайтово неизменны.
5. Верни точные base/head SHAs, изменённые файлы, реальные команды и результаты, manifest/хэши выходов, known limits и один следующий human/evidence gate. Не растягивай работу новыми версиями ради зелёных показателей и не расширяй scope после выполнения этого шага.

Начинай с короткого preflight, затем реализуй этот bounded successor. Используй этот handoff как карту, а текущие файлы и исполненные тесты — как источник проверяемых фактов.
