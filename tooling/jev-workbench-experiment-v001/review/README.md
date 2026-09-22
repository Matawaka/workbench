# Независимый технический review Jev Workbench Lab

Этот публичный пакет предназначен для передачи оператором в два отдельных диалога: Grok и Claude. Он не содержит private evidence. Подготовка промптов не означает, что модели уже получили код или выполнили review; исходный статус обоих — `NOT_RUN`.

## Проверяемая версия

| Поле | Значение |
| --- | --- |
| Public repository | https://github.com/Matawaka/workbench |
| Implementation base SHA | `9e950781b26f144af498e6b28b2cda966ad61428` |
| Target branch | `codex/jev-workbench-lab-v0.1` |
| Frozen implementation / review head (H1) | `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793` |
| New Draft PR | https://github.com/Matawaka/workbench/pull/120 |
| Verified compare URL | https://github.com/Matawaka/workbench/compare/9e950781b26f144af498e6b28b2cda966ad61428...9bc20dff92402bb1aa1f33ff1d1acb25f17fc793 |

Проверяемый код заморожен на H1. Финализированные review briefs добавляются последующим отдельным commit; они отсутствуют в дереве H1 и не меняют app, core или tests. Оператор передаёт brief вместе с шаблоном ответа из этого пакета, а reviewer проверяет публичную реализацию на H1. `main`, имя ветки и страница PR сами по себе не фиксируют проверяемую версию. Если после H1 меняется код, прежние выводы относятся к H1; нужны проверка изменений и отдельная запись результата для нового SHA.

## Публичный обзор

Цель изменения — отдельное экспериментальное WPF-приложение Jev Lab рядом с Workbench. Оно должно позволить оператору открыть синтетический пример или явно выбранный manifest сохранённых candidate/receipt, проверить файлы и их связи, затем просмотреть шесть вероятностных наблюдений. Проверка исходных файлов не является reference-admission, доказательством точности модели или разрешением операции.

В этот шаг входят offline import, проверка hash/chain, проверка чисел и authority flags, диагностическое отображение и synthetic smoke. Проверьте исполнение этого контракта по коду: текст здесь описывает требуемое поведение и не заменяет доказательств. Production `MainWindow`, `CommandRouter`, `AgentHost`, permit/deny и provider authority должны оставаться вне изменения. `operationalSpecificity` имеет только статус `RESEARCH_ONLY`.

Сетевой вызов Jev/TypeSafe, отправка данных, capture реальных операций, API keys, lease activation, производство human labels, live pilot и production probability thresholds не входят в этот шаг. Наличие механики в историческом коде не разрешает её запуск во время review.

## Передача и независимость

1. Оператор передаёт [GROK-PROMPT.md](GROK-PROMPT.md) в один диалог, [CLAUDE-PROMPT.md](CLAUDE-PROMPT.md) — в другой, вместе с [RESPONSE-TEMPLATE.md](RESPONSE-TEMPLATE.md) и одинаковым точным публичным H1 SHA. Эти файлы берутся из последующего brief-only commit или текущего локального пакета, не из дерева H1.
2. Обоим доступен публичный код и synthetic fixtures на этом SHA. При отсутствии доступа оператор может предоставить public-only diff или архив этого commit. Не подменять его снимком `main`.
3. До первого зафиксированного ответа каждый reviewer работает без findings другого reviewer и внутренних review. Любое фактическое знакомство с чужими выводами указать в ответе.
4. Результаты возвращаются оператору по [RESPONSE-TEMPLATE.md](RESPONSE-TEMPLATE.md). После получения можно сопоставлять findings, воспроизводить ошибки и фиксировать решения.
5. [REVIEW-MATRIX.md](REVIEW-MATRIX.md) обновляется только по фактическим ответам: модель/версия, SHA, выполненные команды, ограничения и ссылка на результат. Подготовленный brief, запущенный диалог и отсутствие ответа не являются пройденным review.

Не отправлять private evidence, реальные labels/receipts, operational context, локальные пути оператора, ключи или результаты private diagnostic CLI. Для воспроизведения использовать лишь public synthetic данные; проверять содержимое любых прикладываемых логов и снимков экрана.

Grok и Claude выполняют технический AI review. Они не становятся независимыми человеческими reviewer, не создают `HUMAN_ADJUDICATED` и не меняют admission реальных случаев. Даже два согласных ответа не устанавливают человеческую независимость, blinding, calibration или достаточность выборки.
