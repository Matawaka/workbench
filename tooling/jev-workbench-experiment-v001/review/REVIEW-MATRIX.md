# Реестр review Jev Workbench Lab

Implementation base: `9e950781b26f144af498e6b28b2cda966ad61428`.
Frozen implementation / review head (H1): `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793`.
Draft PR: https://github.com/Matawaka/workbench/pull/120.

Реестр сохраняет привязку каждого ответа к фактически указанной версии. Исходный внешний target — H1. Ответы Claude и Grok получены через оператора со статусом PARTIAL. Исправление C1 Claude — commit `12ae4eb495899242f17b63e84fbee180cdd4ffa3`; ни один ответ не переносится на него автоматически. Обновлять строки только по фактическому ответу; модель и SHA не выводить из названия диалога.

| Reviewer | Фокус | Статус | Reviewed head SHA | Evidence / результат |
| --- | --- | --- | --- | --- |
| Grok 4.5 (self-report); оператор назвал Grok fast | Заявлен полный static review app/core/tests/fixtures H1 | PARTIAL | `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793`, по ответу | [Ответ и уточнения](GROK-20260922-PARTIAL.md): actionable findings не сообщил; runtime NOT_RUN; exposure EXPOSED. Две неточности UI prose и отсутствие concurrent smoke coverage уточнены root; три прерванные попытки не считаются review. |
| Claude Sonnet 5, версия по переданному ответу; оператор назвал Extra | Semantics/UI; фактически только доступные scaffolding/docs | PARTIAL | H1 запрошен; exact checkout не выполнен | [Переданный ответ и disposition C1](CLAUDE-20260922-PARTIAL.md). UI/core/fixtures/tests недоступны через web; ничего не исполнял. C1 P2 принят и исправлен, внешний recheck PENDING. |
| Internal AI security review | Offline chain/authority boundaries; ограниченный static review | COMPLETED | `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793` | Оставшихся блокеров не выявлено. Перед code commit root сверил неизменность хэшей четырёх проверенных файлов рабочего дерева. Исходные findings и их исправления зафиксированы отдельно; не передаются до первых внешних ответов. |
| Internal implementation verification | Windows build, synthetic tests and in-process WPF smoke | COMPLETED, с указанными ограничениями | `9bc20dff92402bb1aa1f33ff1d1acb25f17fc793` | Root повторно выполнил 112 predecessor tests и 83 importer tests: PASS; build: 0 warnings / 0 errors; WPF smoke: 11 PASS; output guards: 5/5 rejected. Рендеры успешной загрузки и ошибки просмотрены. |

Фактические команды и пределы проверки записаны в публичном `tooling/jev-workbench-experiment-v001/VALIDATION.md` на H1. [Windows CI на H1](https://github.com/Matawaka/workbench/actions/runs/35699013686): `SUCCESS`. Native UI helper не запускался из-за sandbox setup refresh error: внешний интерактивный file dialog и keyboard flow не проверены. WPF smoke исполнялся внутри настоящего приложения; он не заменяет эти сценарии. Linux локально не запускался. Эти ограничения не обозначены как пройденные проверки.

Статусы внешнего review: `NOT_RUN` — результата нет; `PARTIAL` — проверена только часть; `COMPLETED` — reviewer завершил заявленный объём. Статус не кодирует «одобрено» и не заменяет findings. Для исправлений сохранять исходный reviewed SHA, resolution и новый SHA/recheck отдельной записью; не переносить прежнее заключение на новый код автоматически.

Чужие findings передаются внешним reviewers только после их первых независимых ответов. Такая процедура снижает взаимное влияние технических обзоров, но не доказывает статистическую независимость моделей и не является human blinding/adjudication.
