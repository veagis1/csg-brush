---
type: system
status: implemented
updated: 2026-11-24
sources:
  - "repo:AGENTS.md"
  - "repo:Development~/Sandbox/Packages/manifest.json"
  - "repo:Development~/Sandbox/ProjectSettings/ProjectVersion.txt"
---
# Фоновая работа с Unity

Проверки пакета проходят в отдельном Unity-проекте Sandbox, версия редактора 6000.6.8f1. В форке пакет подключён из корня репозитория. Путь относительно каталога `Development~/Sandbox/Packages` — `file:../../..`. В манифесте закреплены Pipeline 1.0.0 и Test Framework 1.8.0, в Standalone определён символ `CSG_BRUSH_TESTS`.

Рабочий Sandbox использует URP 17.6.0 и Input System 1.20.1. Префаб «Игрок» с `PlayerInput`, контроллером и действиями `Exploration` находится в `Assets/_Project/Runtime/FirstPerson`. Рендерер и конфигурация URP находятся в `Assets/_Project/Rendering`.

Используйте абсолютный путь стенда для всех вызовов CLI. Запускайте редактор в `--batchmode`, дожидайтесь `ready` и записывайте курсор консоли до проверки. Передавайте `--caller plugin --skill unity-pipeline --format json` на вызовы `unity command` по навыку Pipeline. Сопоставляйте каталог тестов с итогом прогона. В Play Mode проверяйте продвижение кадров. Закрывайте собственный редактор через CLI и проверяйте освобождение блокировки.
