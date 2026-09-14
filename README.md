# SSnetGUI

Удобный графический интерфейс для [sing-box](https://github.com/SagerNet/sing-box) на Windows: запуск/остановка, управление режимами, редактор правил маршрутизации, авто-регенерация WARP и обновление «по воздуху».

> A Windows GUI for sing-box — start/stop control, routing-rules editor, one-click WARP account regeneration, and over-the-air updates.

---

## Возможности

- **Управление сервисом** — Start / Stop / Restart sing-box одной кнопкой, индикатор состояния в окне и в трее.
- **Режимы (settings.json)** — переключатели TUN / Proxy / Logging и выбор финального маршрута (direct / proxy).
- **Редактор правил** — группы `inline` (домены и имена процессов), `remote` (внешние `.srs`) и `local` (локальные списки). Кнопка «Сохранить и применить» пересобирает конфиг через `SSnetCli`.
- **Локальные списки** — скачивание `.srs` через настраиваемое зеркало (обход блокировки GitHub), чтобы sing-box не зависел от сети при старте.
- **Обновление WARP** — регистрация свежего аккаунта Cloudflare WARP в один клик (лечит «протух ключ — перестало работать»).
- **Автозапуск** — запуск свёрнутым в трей при входе в Windows через планировщик задач.
- **Авто-перезапуск после гибернации/сна** — при пробуждении системы sing-box перезапускается автоматически.
- **Обновления по воздуху (OTA)** — приложение проверяет новую версию на GitHub Releases (через зеркало), проверяет SHA256 и тихо обновляется.
- **Тёмная тема**, системный трей, работа от прав администратора без повторных UAC.

## Установка

1. Скачай последний **`SSnet-Setup.exe`** со страницы [Releases](../../releases/latest).
2. Запусти установщик (нужны права администратора). Если на ПК нет .NET 10 — он доустановится автоматически.
3. Открой SSnet и нажми **«Обновить WARP-ключи»** — зарегистрируется твой личный WARP-аккаунт.
4. *(опционально)* Если используешь geo-туннель — вставь свой WireGuard-конфиг в **Конфиги → geo.conf** и нажми «Сохранить и применить».

Установщик не содержит чьих-либо приватных ключей — WARP/geo настраиваются на месте.

## Сборка из исходников

Нужен [.NET SDK 8+](https://dotnet.microsoft.com/download).

```bash
cd ui
dotnet build -c Debug
# или self-contained сборка:
dotnet publish -c Release -r win-x64 --self-contained true
```

Приложение ожидает структуру папок:

```
<корень>/
  SSnetCli.exe          # генератор конфига (из проекта SSnet)
  settings.json
  update.json           # { "repo": "...", "mirror": "..." }
  build/                # sing-box.exe, config.json, скрипты
  data/                 # rules.json, warp.conf, geo.conf, rulesets/
  ui/SSnetUI.exe        # это приложение
```

## Обновления по воздуху

Приложение читает `update.json` (репозиторий + зеркало), тянет `version.json` из последнего релиза и, если версия новее, скачивает `SSnet-Setup.exe`, сверяет `SHA256` и запускает тихую установку.

```json
{
  "repo": "Vexorter42/SSnetGUI",
  "mirror": "https://ghproxy.net/"
}
```

Если зеркало перестало работать — поменяй `mirror` (например `https://ghfast.top/`) без пересборки.

## Поддержать

Проект развивается в свободное время. Поддержать можно здесь: **https://www.donationalerts.com/r/vexorter** ❤

## Благодарности

- [sing-box](https://github.com/SagerNet/sing-box) — движок.
- [itdoginfo/allow-domains](https://github.com/itdoginfo/allow-domains) — списки правил.

## Лицензия

[MIT](LICENSE). Бандлящиеся компоненты (sing-box, списки правил) распространяются под своими лицензиями.
