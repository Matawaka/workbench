# Проверка offline Jev Lab, 2026-09-22

Проверена реализация поверх `9e950781b26f144af498e6b28b2cda966ad61428`, без изменений predecessor, handoff checkpoint и production. Финальные base/head SHA фиксируются в новом Draft PR; внешний review получает отдельный неизменяемый implementation SHA.

## Фактические результаты Windows

| Проверка | Результат |
| --- | --- |
| Исторические adapter / send lease / corpus / labels suites | 20 + 4 + 7 + 5 = 36 PASS |
| Reference-admission bridge | 76 PASS |
| Новый .NET importer, negative и compatibility cases | 83/83 PASS |
| WPF Release build | 0 warnings, 0 errors |
| Настоящее WPF-окно, in-process synthetic smoke | 11/11 PASS, четыре PNG |
| Output guards: existing, Git checkout, slash UNC, backslash UNC, ADS | 5/5 rejected; existing manifest unchanged |
| Внутренний параллельный AI review | Завершён ограниченный static review, оставшихся блокеров нет |
| Grok / Claude | NOT_RUN — ожидаются фактические ответы через оператора |

83 теста включают исторический публичный candidate/TypeSafe receipt, различие requested alias и observed model, независимый Node canonical Unicode vector, BOM с исходным raw hash, tampering, duplicate keys, неизвестные схемы/вопросы/поля, неверные digests и authority, finite/range constraints, depth/size, UNC/device/ADS и настоящие symlink/ancestor cases. Windows-only ADS case даёт 83; на Linux harness содержит 82. Linux в этом шаге локально не запускался.

Smoke проверяет empty → read-only demo → scope-expansion demo → ошибка отсутствующего файла → recovery → clear. Проверены шесть значений, RESEARCH_ONLY, удаление старых данных после ошибки и неизменность шести исходных demo-файлов. Root agent просмотрел рендеры успешной загрузки и ошибки; внутренний reviewer изучал UI по коду. Это не прохождение file dialog или keyboard flow через внешнюю desktop automation: helper исходного хоста не смог запуститься из-за sandbox setup refresh error.

## Команды

Из корня repository:

```powershell
$packages = @('jev-system-one-judgment-adapter', 'jev-shadow-one-shot-send-v003', 'jev-shadow-evidence-corpus-v001', 'jev-shadow-label-semantics-v002', 'jev-shadow-reference-admission-v002')
foreach ($package in $packages) {
  npm --prefix "tooling/$package" test
  if ($LASTEXITCODE -ne 0) { throw "Suite failed: $package" }
}
dotnet run --project tooling/jev-workbench-experiment-v001/test/JevLab.Tests.csproj -c Release
dotnet build tooling/jev-workbench-experiment-v001/app/JevLab.App.csproj -c Release
```

Для smoke задать `$smokeDir` — новый абсолютный путь под существующим локальным родителем вне любого Git checkout:

```powershell
$exe = (Resolve-Path 'tooling/jev-workbench-experiment-v001/app/bin/Release/net10.0-windows/Matawaka.Workbench.JevLab.exe').Path
$p = Start-Process -FilePath $exe -ArgumentList '--smoke', ('"' + $smokeDir + '"') -WindowStyle Hidden -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Smoke failed' }
Get-Content (Join-Path $smokeDir 'smoke.json')
```

Output guard checks запускались тем же `--smoke`: повторный `$smokeDir`, новый путь внутри текущего checkout, `//localhost/jev-lab-unavailable/share`, `\\localhost\jev-lab-unavailable\share`, локальный путь с `:invalid` после имени. Каждый возвратил exit 1. SHA-256 существующего OUTPUT-MANIFEST.json не изменился; запрещённый каталог в repository не появился. Мapped network drive локально не создавался; отказ по DriveType.Network проверен по коду.

Новый Windows CI повторяет 112 прежних тестов, importer, WPF build и synthetic smoke на exact PR head; проверяет ancestry, allowlist diff и чистоту checkout. Фактический CI status и URL фиксируются в PR после запуска, этот файл их не предсказывает.

## Границы результата

Jev Lab запускается отдельным процессом. Новый provider call, production integration, live capture, человеческая adjudication и calibration отсутствуют. Saved-file import проверяет candidate/request/receipt/consumption, но не полный packet/labels/provenance/reference chain. Для полного reference-admission нужен predecessor bridge и фактические оригиналы. `ReferenceStatus=NOT_ASSESSED`; никакая вероятность не устанавливает permit/deny.

Schema support намеренно узкий: исторический rubric, четыре строковых поля контекста, обязательные числовые usage. Path checks не являются OS sandbox против привилегированной гонки переименования. Нативный интерактивный file dialog и ручной keyboard flow остаются для оператора. Synthetic demo позволяет испытать UX сейчас; live pilot требует отдельного ограниченного задания после технического review. HUMAN_ADJUDICATED и независимые исходные случаи остаются отдельным evidence gate.
