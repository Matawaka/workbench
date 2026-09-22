# Ответ независимого AI reviewer

Цель этого пакета — frozen implementation H1 `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793` поверх base `9e950781b26f144af498e6b28b2cda966ad61428`, [Draft PR #120](https://github.com/Matawaka/workbench/pull/120). В полях ниже укажи фактически проверенные SHA; недоступный commit не отмечай как проверенный. Этот шаблон передаётся вместе с brief и отсутствует в дереве H1.

## Идентификация и полнота

- Reviewer/model/version: `<фактическое значение или UNKNOWN>`
- Дата review: `<ISO 8601>`
- Base SHA: `<40 hex>`
- Reviewed head SHA: `<40 hex>`
- PR / public commit URL: `<URL>`
- Статус: `NOT_RUN | PARTIAL | COMPLETED`
- Доступные материалы и реально прочитанные пути: `<список>`
- Что недоступно/не проверено: `<список>`
- Знакомство с чужими findings до первого ответа: `NONE_DECLARED | EXPOSED | UNKNOWN`; пояснение: `<факты>`
- Роль: `AI_TECHNICAL_REVIEW`; это не human adjudication/reference admission.

## Фактическая проверка

| Команда / действие | Среда | Фактический результат | Evidence / limitation |
| --- | --- | --- | --- |
| `<offline command или UI sequence>` | `<OS/runtime>` | `<exit code, passed/failed либо NOT_RUN>` | `<sanitized public result>` |

Не вставлять private данные, пути оператора, ключи или полные operational logs. Не заявлять исполнение команды, если анализировался только её код. Для UI указать источник вывода: живой запуск, screenshot или исходники.

## Findings

Повторить блок только для конкретных actionable findings. Если подтверждённых дефектов нет, написать это явно; ограничения проверки остаются обязательными.

### [P0/P1/P2/P3] Краткое название

- Location: `<repo-relative file>:<verified line на reviewed head>`
- Статус evidence: `REPRODUCED | CODE_SUPPORTED | HYPOTHESIS`
- Нарушенный контракт: `<что обещает проверяемая версия>`
- Минимальный synthetic input / шаги: `<достаточно для воспроизведения>`
- Ожидалось: `<результат>`
- Наблюдалось: `<результат; для непроверенного — NOT_RUN и вывод из кода>`
- Практическое влияние и предпосылки: `<кто и когда затронут>`
- Предложение исправления: `<узкое направление, без расширения scope>`
- Regression test: `<что обязан доказать тест>`

## Заключение в пределах scope

- Offline demo/import: `<вывод и его evidence>`
- Неразрешённые риски / неизвестное: `<список>`
- Следующий узкий инженерный gate: `<что проверить или исправить>`

`COMPLETED` означает завершённый технический review заявленного объёма. Он не означает отсутствие дефектов, production readiness, разрешение live send, истинность вероятностей или допуск человеческого эталона.
