# Запуск двухузлового proof: краткая инструкция

Цель — проверить недоказанную часть #106: блокируется ли у zero-capability Windows AppContainer доступ к **реальному отдельному private-network peer**, причём Windows должен классифицировать отказ именно как `NETISO_ERROR_TYPE_PRIVATE_NETWORK (1)`.

Это не тест Qwen и не разрешение production provider.

## Что понадобится

1. **x64 Windows-компьютер** для proof-node.
   - Канонический вариант: Windows Server 2025 x64, build family 26100.
   - Если на основном ПК Windows 10/11 — используйте временную VM Windows Server 2025. Обычный Windows 10/11 можно использовать только для диагностики, но не для закрытия #106.
2. **MacBook M2** — synthetic peer.
3. Желательно отдельный USB/USB-C Ethernet-адаптер для Mac и отдельный Ethernet-порт/адаптер для Windows.
4. Обычный Ethernet-кабель. Современные адаптеры обычно не требуют crossover-кабеля.
5. .NET 10 SDK на Mac и Windows Server VM/host.

## Схема

```text
Windows Server 2025 x64                  MacBook M2
10.77.0.1/30                             10.77.0.2/30
       |                                      |
       +------ отдельный Ethernet-линк -------+

TCP 41060 = management
TCP 41061 = proof target
mask = 255.255.255.252
gateway = отсутствует
DNS = отсутствует на тестовом интерфейсе
```

Обычный Интернет может оставаться на другом интерфейсе (например, Wi-Fi). Тестовый Ethernet не должен быть default route.

## Если Windows Server 2025 запускается как Hyper-V VM

Лучший вариант:

1. Подключить выделенный Ethernet-адаптер Windows-PC напрямую к Ethernet-адаптеру Mac.
2. В Hyper-V создать **External Virtual Switch** на этом выделенном адаптере.
3. Отключить **Allow management operating system to share this network adapter**.
4. Подключить test-vNIC Windows Server VM к этому external switch.
5. Внутри VM задать `10.77.0.1/30`, без gateway и DNS.
6. Обычный доступ VM в Интернет, если нужен для установки .NET, держать на отдельном втором vNIC. Перед proof убедиться, что тестовый vNIC не имеет default route.

## Шаг 1. Mac

Открыть Terminal в checkout ветки:

```bash
git switch feat/workbench-private-peer-qualification-executor-v1
git pull
cd integrations/model-invocation/windows-private-peer-proof-v1/executor
chmod +x mac-peer.sh
./mac-peer.sh inspect
```

Скрипт покажет default interface и список интерфейсов. Выберите **выделенный Ethernet**, не Wi-Fi/default interface.

Например, если выделенный адаптер — `en7`:

```bash
./mac-peer.sh configure en7
./mac-peer.sh build en7
```

`configure` присвоит только тестовому интерфейсу `10.77.0.2/30`. `build` проверит frozen SHA-256 исходника/контракта и выполнит pure-controls до запуска сети.

Пока listener **не запускайте**, пока Windows-сторона не подготовлена.

## Шаг 2. Windows Server 2025

Внутри Windows Server настройте выделенный test-interface:

```text
IP:      10.77.0.1
Mask:    255.255.255.252 (/30)
Gateway: пусто
DNS:     пусто
```

Затем в PowerShell из checkout той же ветки:

```powershell
git switch feat/workbench-private-peer-qualification-executor-v1
git pull
Set-ExecutionPolicy -Scope Process Bypass
.\integrations\model-invocation\windows-private-peer-proof-v1\executor\Prepare-WindowsProofNode.ps1
```

`Prepare`:

- проверит Windows Server 2025 / build 26100;
- проверит `10.77.0.1/30` и отсутствие `10.77.0.2` среди локальных адресов;
- проверит отсутствие default route и DNS на test-interface;
- сверит frozen SHA/hashes;
- соберёт coordinator;
- создаст новый закрытый `trial` из ровно 7 файлов;
- создаст `manifest.json`, `windows-proof-provisioning.json`, `executor-state.json`;
- **не выполнит ни одного сетевого обращения к Mac**.

Сохраните путь `state=...`, который скрипт напечатает.

## Шаг 3. Старт peer на Mac

На Mac:

```bash
./mac-peer.sh serve en7
```

После появления `MAC_PEER_READY` **ничем не проверяйте порты 41060/41061**. Не использовать `nc`, `telnet`, `curl`, port scanner или `Test-NetConnection`. Любое лишнее соединение может правильно перевести одноразовую сессию в FAIL_CLOSED.

## Шаг 4. Реальный proof на Windows

Сразу после старта Mac listener:

```powershell
.\integrations\model-invocation\windows-private-peer-proof-v1\executor\Run-WindowsPrivatePeerTrial.ps1 `
  -StatePath "C:\...\executor-state.json" `
  -ExecuteQualifiedTrial
```

`-ExecuteQualifiedTrial` обязателен: подготовка файлов сама по себе не является разрешением на сетевой trial.

Скрипт не делает предварительный port probe. Первым management-соединением будет канонический `BEGIN` координатора.

## Что будет считаться GREEN

Только одновременно:

```text
Windows child DiagnoseInfoType = 1 (PRIVATE_NETWORK)
DiagnosticClassificationUsedAsProof = true
SocketBehaviorUsedAsProof = false
SocketTimeoutPromotedToProof = false
Peer TargetAcceptsTotal = 1
Peer ChildWindowAcceptedConnections = 0
Peer ProtocolSatisfied = true
CleanupSucceeded = true
ProductionProviderRegistered = false
```

Финальный theorem:

```text
OS_PRIVATE_NETWORK_PATH_NOT_AUTHORIZED_PROVEN
```

`NONE(0)`, `INTERNET_CLIENT(2)`, `INTERNET_CLIENT_SERVER(3)`, отсутствие receipt, лишнее соединение или ошибка cleanup = FAIL_CLOSED.

## Какие файлы прислать обратно после опыта

С Mac:

- `mac-peer-provisioning.json`
- `peer-session.jsonl`

С Windows:

- `windows-proof-provisioning.json`
- `proof-evidence.json`
- `coordinator-stdout.jsonl`
- `manifest.json`
- `executor-state.json`

Можно просто упаковать две папки evidence в ZIP и загрузить в чат. По ним можно будет провести финальную проверку #106 и решить, действительно ли допустимо объявить private-network isolation доказанной.
