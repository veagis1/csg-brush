---
type: guide
status: current
updated: 2026-11-22
sources:
  - "repo:AGENTS.md"
  - "repo:Development~/Tools/Caveman/package-lock.json"
  - "repo:Development~/Tools/Caveman/caveman.cmd"
  - "repo:Development~/Tools/Caveman/read-log.cmd"
  - "repo:Development~/Tools/Caveman/read-log.mjs"
  - "repo:Development~/Tools/Caveman/blocks.cmd"
  - "repo:Development~/Tools/Firefox/package-lock.json"
  - "repo:Development~/Tools/Firefox/mcp.cmd"
  - "repo:.blocks/INDEX.md"
  - "repo:.agents/skills/caveman/SKILL.md"
  - "repo:.agents/skills/ultracave/SKILL.md"
  - "repo:.agents/skills/megacave/SKILL.md"
  - "repo:.agents/skills/caveman-learn/SKILL.md"
---
# Caveman в Codex

Открывайте CSG Brush+ от корня с `AGENTS.md`. В доверенном проекте Codex читает проектный `.codex/config.toml`. Там подключены Firefox MCP и Caveman MCP для восстановления исходного вывода. Конфиг зависит от расположения проекта и исключён из Git. [Настройки проекта Codex](https://learn.chatgpt.com/docs/config-file/config-basic).

## Стиль ответа

Штатные навыки Caveman 4.1.0 лежат в `.agents/skills/`. Обычный режим задаётся правилами проекта. В чате можно попросить «выключи Caveman», «включи Caveman» или выбрать `ultracave` и `megacave`. Навык задаёт стиль текста. Точные имена, числа, причины ошибок и установленные ограничения сохраняются. [Навыки Codex](https://learn.chatgpt.com/docs/build-skills).

## Чтение логов

При первом просмотре лога используйте проектную обёртку. Лог до 200 строк и 32 КиБ включительно выводится побайтно. При превышении порога обёртка пытается оставить компактную сводку и выдать идентификатор восстановления `ccr_…`. При размере больше 8 МиБ, известной ошибке или отказе сжатия найдите нужную строку и прочитайте диапазон оригинала. Для точных стеков, диффов и доказательств используйте оригинал.

```powershell
.\Development~\Tools\Caveman\read-log.cmd 'путь\к\логу.txt'
.\Development~\Tools\Caveman\read-log.cmd --raw 'путь\к\логу.txt'
.\Development~\Tools\Caveman\caveman.cmd retrieve ccr_ПОДСТАВЬТЕ_ИДЕНТИФИКАТОР
```

CLI и MCP используют состояние `Development~/Tools/Caveman/.state/`. Вызов `caveman_retrieve` принимает тот же идентификатор. Удаление состояния делает прежние идентификаторы недоступными. Codex применяет правило чтения через явный вызов обёртки. Встроенный поиск, shell и ответы других MCP работают в обычном режиме.

## Проверенные скрипты

Индекс находится в `.blocks/INDEX.md`. Если подходит проверенный блок, запускайте его через `Development~/Tools/Caveman/blocks.cmd run`. В Codex блоки запускаются явно. Проверка из PowerShell:

```powershell
.\Development~\Tools\Caveman\blocks.cmd verify --all --check
.\Development~\Tools\Caveman\blocks.cmd run first-error --path .blocks/fixtures/first-error/build.log
.\Development~\Tools\Caveman\blocks.cmd run json-peek --path .blocks/fixtures/json-peek/sample.json
```

## Firefox

`Development~/Tools/Firefox/mcp.cmd` запускает Firefox MCP 0.11.0 в фоновом режиме через отдельный профиль под `Development~/Tools/Firefox/.state/session/`. Для первого осмотра используйте `take_snapshot` с коротким `maxLines`. При необходимости полного снимка указывайте путь под `Development~/Tools/Firefox/.state/`. Caveman Browse требует Chromium, поэтому команды `caveman.cmd browse` и `caveman.cmd tools browse` завершаются с кодом 2. Действия с браузером выполняет MCP `firefox`.

## Воспроизведение в другом клоне

Нужны Windows x64, Node.js 22.13 или новее, npm, Python 3.10 или новее, Git и Firefox. Обёртки используют проектный runtime, если он есть, либо программы из PATH. Из корня клона установите зависимости из lock-файлов:

```powershell
npm.cmd --prefix .\Development~\Tools\Caveman ci --ignore-scripts --no-audit --no-fund
npm.cmd --prefix .\Development~\Tools\Firefox ci --ignore-scripts --no-audit --no-fund
.\Development~\Tools\Caveman\caveman.cmd setup --install --json
.\Development~\Tools\Caveman\caveman.cmd setup --skip routing,browse --agents none --dry-run
.\Development~\Tools\Caveman\caveman.cmd setup --skip routing,browse --agents none --yes
.\Development~\Tools\Caveman\caveman.cmd off browse --yes
```

Локальный драйвер `geckodriver.exe` версии 0.37.1 ставится в `Development~/Tools/Firefox/.state/bin/`. SHA-256 архива Windows x64 `dfed9315abe8d2fbc1b6161a2ee8002452e79cf05ee92fdc653a4e26bc35edd8`, исполняемого файла `e95b4eac7960ffcd5acbfd92bb7d49d48f99c1d01a20ddd297fef8c80821020d`. [Выпуск geckodriver](https://github.com/mozilla/geckodriver/releases/tag/v0.37.1).

Создайте в корне проекта `.codex/config.toml`. Подставьте абсолютный корень вместо `ROOT`, сохраняя одиночные кавычки TOML и отдельные аргументы `cmd.exe`. Пути с пробелом поддерживаются.

```toml
[mcp_servers.firefox]
command = 'cmd.exe'
args = ['/d', '/c', 'call', 'ROOT\Development~\Tools\Firefox\mcp.cmd']
cwd = 'ROOT'
startup_timeout_sec = 20
tool_timeout_sec = 60

[mcp_servers.caveman]
command = 'ROOT\Development~\Tools\Caveman\.state\bin\caveman-mcp.exe'
startup_timeout_sec = 20
tool_timeout_sec = 60
env = { DO_NOT_TRACK = '1', CAVEMAN_HOME = 'ROOT\Development~\Tools\Caveman\.state' }
```

После изменения регистрации откройте новый чат. `codex mcp list --json` из корня показывает `firefox` и `caveman`, вне проекта их регистраций нет. При проверке Firefox нужен доступ к локальному сокету WebDriver.

## Фактическая проверка 22 ноября 2026

- Из 8Circles перенесены четыре неизменённых навыка, профильные скрипты и блоки. Новый локальный процесс Codex распознал все четыре навыка проекта. Нужные файлы среды взяты с этого ПК, зависимости установлены по lock-файлам. История, логи и существующие профили не копировались. Глобальные настройки Codex и OpenCode не менялись.
- CLI 4.1.0 показывает включённые `output`, `input`, `scripts` и `waste-fixes`, выключенные `routing` и `browse`. Нативная интеграция Caveman с Codex не установлена. Экономия квоты и автоматическое сжатие контекста не измерялись.
- Подтверждены границы 200/201 строк и 32 768/32 769 байт, отказ на файле больше 8 МиБ и при выключенном `input`, точный вывод `--raw`, восстановление оригинала через CLI и через MCP, сохранение кода выхода 7. На искусственном логе из 1001 строки объём вывода сократился с 80 027 до 616 байт. Оба восстановления совпали со своими оригиналами побайтно.
- Все пять Blocks прошли проверку при разрешённом локальном соединении. Без разрешения соединение Firefox с WebDriver было недоступно. Firefox MCP в отдельном профиле выполнил `navigate_page` и `take_snapshot`, показал русский заголовок и завершился после закрытия stdin. Временный профиль и его процессы удалены. Команда Codex подтвердила проектную область обеих регистраций. Вызовы в текущем уже открытом чате не проверялись.
