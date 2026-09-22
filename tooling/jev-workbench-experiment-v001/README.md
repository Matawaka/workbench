# Workbench Jev Lab — экспериментальное offline-окно

Запускаемое WPF-приложение для двух synthetic-демонстраций и просмотра сохранённого Jev-наблюдения. Это отдельное экспериментальное окно рядом с Workbench; основной процесс Workbench, его MainWindow и выполняемые операции к нему пока не подключены.

Приложение проверяет исходные candidate/receipt, показывает шесть вероятностей ответа «да», точные вопросы и сведения о происхождении. Изменение файлов, неверный digest, неподдерживаемый вопрос, нечисловая вероятность или authority/readback flags приводят к отказу. При новой загрузке или ошибке предыдущие оценки и контекст очищаются. Данные не отправляются провайдеру, параметры доступа и ключи не читаются.

## Запуск

Windows с .NET Desktop Runtime 10 (для сборки — SDK 10):

```powershell
dotnet build tooling/jev-workbench-experiment-v001/app/JevLab.App.csproj -c Release
& tooling/jev-workbench-experiment-v001/app/bin/Release/net10.0-windows/Matawaka.Workbench.JevLab.exe
```

В окне нажмите «Демо: чтение отчёта», затем «Демо: расширение scope». Все ответы этих двух fixtures заданы разработчиком; показанные числа не являются результатом нового Jev-вызова или измерением качества модели.

Для сохранённого наблюдения выберите локальный `inputs.json` или введите его абсолютный путь. Формат manifest:

```json
{
  "schema": "matawaka.jev-lab-inputs/v0.1",
  "caseId": "operator-assigned-id",
  "sampleClass": "SANITIZED_REAL_SHADOW",
  "artifacts": {
    "candidate": { "path": "candidate.json", "rawSha256": "sha256:<actual lowercase 64 hex digits>" },
    "receipt": { "path": "receipt.json", "rawSha256": "sha256:<actual lowercase 64 hex digits>" }
  }
}
```

Manifest — новый явно производный индекс над сохранёнными оригиналами. Он не становится packet, corpus record или reference evidence и не заменяет отсутствующие оригиналы. `caseId` и `sampleClass` — декларации из manifest, не удостоверенная идентичность случая. Не изменяйте frozen candidate/receipt ради импорта и не используйте placeholders вместо фактических хэшей. Для изначально синтетического стимула укажите SYNTHETIC_CONTROL даже если исторический ответ получен от настоящего Jev.

Пример открытия при запуске: `Matawaka.Workbench.JevLab.exe --manifest <absolute-local-inputs.json>`. Реальный case-0001 остаётся HOLD / ADJUDICATION_PENDING; для него receipt пока недоступен в переданной локальной папке. Работающий synthetic-прототип этого gate не меняет.

## Что означает проверка

Проверяются raw SHA-256 обоих файлов, canonical candidate/request digests, отдельный adapter request digest, согласованность моделей и consumption record, точный набор шести Noul и двух диагностических вопросов, примитивы ответов и finite/range constraints. BOM допускается один и сохраняется в raw hash. Duplicate JSON keys, extra/missing contract fields, unsupported rubric, сетевые/device-пути и reparse ancestry отвергаются. Один файл ограничен 1 MiB и JSON depth 16.

Текущий importer принимает только оригинальный rubric с четырьмя строковыми полями состояния. Canonical candidate/request domain не содержит чисел; реализован совместимый с JavaScript порядок ключей и escaping строк. Другие схемы, nullable usage и изменённые формулировки вопросов явно не поддерживаются. Это узкий совместимый импорт, а не общий .NET↔JavaScript JSON canonicalizer.

Соответствие байтов и связей не удостоверяет автора, дату, blinding, происхождение ответа или полноту evidence. Отдельные оригиналы response, lease, source, human labels и provenance эта поверхность не принимает и не проверяет. Поэтому `ReferenceStatus=NOT_ASSESSED`, метрики калибровки не вычисляются, production thresholds отсутствуют. `operationalSpecificity` всегда RESEARCH_ONLY. Условность hasReliableRollback не превращается автоматически в N/A из вероятности causesExternalMutation.

Приложение не является OS/network sandbox. Проверки пути отвергают существующие сетевые и reparse пути, но не дают транзакционной защиты от привилегированного процесса, который одновременно меняет файловую систему. Выбирайте локальные файлы в контролируемом каталоге. Приватные файлы и screenshots с реальными данными сохраняйте вне public repo; обычный UI сам их не экспортирует и не логирует.

## Проверки

```powershell
dotnet run --project tooling/jev-workbench-experiment-v001/test/JevLab.Tests.csproj -c Release
& tooling/jev-workbench-experiment-v001/app/bin/Release/net10.0-windows/Matawaka.Workbench.JevLab.exe --smoke <new-absolute-directory-outside-git>
```

Smoke открывает настоящее WPF-окно, вызывает тот же async loading handler, проверяет empty/demo/replacement/error/recovery/clear и сохраняет PNG-рендеры с `smoke.json` и OUTPUT-MANIFEST.json. Новый output directory должен иметь существующего локального родителя вне Git checkout; existing output и overwrite запрещены. Это инструментальная UI-проверка, не человеческая оценка и не внешний AI review. Интерактивный desktop helper на исходном хосте возвращал sandbox setup refresh error, поэтому его клики и file-dialog traversal не проверялись.

Независимые задания для Grok и Claude находятся в [review/README.md](review/README.md). [Ответ Claude](review/CLAUDE-20260922-PARTIAL.md) — PARTIAL: доступ к UI/core был ограничен, замечание к охвату CI guard принято. [Ответ Grok](review/GROK-20260922-PARTIAL.md) — PARTIAL: заявлен полный static review H1 без actionable findings, runtime не запускался; неточности UI prose уточнены по исходникам. Повторная проверка Claude ожидается. Технический AI review не является HUMAN_ADJUDICATED reference.

CI source guard проверяет шесть явно запрещённых токенов во всех поддерживаемых исходниках пакета: app/core/test, корневых scripts, project/build files, package.json и самих checks. Markdown/fixture data и generated directories исключены. Это лексическая регрессия, не network sandbox и не анализ транзитивных зависимостей. Запуск: `node tooling/jev-workbench-experiment-v001/checks/offline-source-guard.mjs`; negative tests: `node --test tooling/jev-workbench-experiment-v001/checks/offline-source-guard.test.mjs`.

Следующий практический gate после этого прототипа: оператор проверяет интерфейс, возвращает внешние review; затем отдельным этапом определяется и проверяется live-pilot scope с явной отправкой, обработкой ошибок и полным сохранением evidence. Этот проект не включает новый provider call или подключение Jev к permit/deny.
