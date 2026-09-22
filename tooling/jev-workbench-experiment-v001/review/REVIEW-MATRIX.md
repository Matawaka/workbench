# Реестр review Jev Workbench Lab

Implementation base: `9e950781b26f144af498e6b28b2cda966ad61428`.
Frozen implementation / review head (H1): `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793`.
Draft PR: https://github.com/Matawaka/workbench/pull/120.

Реестр фиксирует полученные внутренние результаты и отсутствие внешних ответов. Финализированные briefs и этот реестр добавляются после H1 отдельным commit, без изменений app/core/tests; объект внешнего review остаётся H1. Обновлять строки только по фактическому ответу; модель и SHA не выводить из названия диалога.

| Reviewer | Фокус | Статус | Reviewed head SHA | Evidence / результат |
| --- | --- | --- | --- | --- |
| Grok; model/version UNKNOWN | Adversarial inputs, hashes, path and resource boundaries | NOT_RUN | — | Brief подготовлен; ответ не получен |
| Claude; model/version UNKNOWN | Semantics, UI, stale display, claims and evidence distinctions | NOT_RUN | — | Brief подготовлен; ответ не получен |
| Internal AI security review | Offline chain/authority boundaries; ограниченный static review | COMPLETED | `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793` | Оставшихся блокеров не выявлено. Перед code commit root сверил неизменность хэшей четырёх проверенных файлов рабочего дерева. Исходные findings и их исправления зафиксированы отдельно; не передаются до первых внешних ответов. |
| Internal implementation verification | Windows build, synthetic tests and in-process WPF smoke | COMPLETED, с указанными ограничениями | `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793` | Root повторно выполнил 112 predecessor tests и 83 importer tests: PASS; build: 0 warnings / 0 errors; WPF smoke: 11 PASS; output guards: 5/5 rejected. Рендеры успешной загрузки и ошибки просмотрены. |

Фактические команды и пределы проверки записаны в публичном `tooling/jev-workbench-experiment-v001/VALIDATION.md` на H1. [Windows CI на H1](https://github.com/Matawaka/workbench/actions/runs/35699013686): `SUCCESS`. Native UI helper не запускался из-за sandbox setup refresh error: внешний интерактивный file dialog и keyboard flow не проверены. WPF smoke исполнялся внутри настоящего приложения; он не заменяет эти сценарии. Linux локально не запускался. Эти ограничения не обозначены как пройденные проверки.

Статусы внешнего review: `NOT_RUN` — результата нет; `PARTIAL` — проверена только часть; `COMPLETED` — reviewer завершил заявленный объём. Статус не кодирует «одобрено» и не заменяет findings. Для исправлений сохранять исходный reviewed SHA, resolution и новый SHA/recheck отдельной записью; не переносить прежнее заключение на новый код автоматически.

Чужие findings передаются внешним reviewers только после их первых независимых ответов. Такая процедура снижает взаимное влияние технических обзоров, но не доказывает статистическую независимость моделей и не является human blinding/adjudication.
