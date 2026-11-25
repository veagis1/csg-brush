---
type: system
status: implemented
updated: 2026-11-25
sources:
  - "repo:Development~/Sandbox/Packages/manifest.json"
  - "repo:Development~/Sandbox/Packages/packages-lock.json"
  - "repo:Development~/Sandbox/ProjectSettings/ProjectVersion.txt"
  - "repo:.gitignore"
  - "repo:Development~/Sandbox/Assets/Samples/.gitignore"
---
# Chisel в Sandbox

Chisel установлен в `Sandbox` для знакомства с отдельным редактором CSG. Стенд использует Unity `6000.6.8f1` и URP `17.6.0`. Unity Package Manager восстанавливает две прямые зависимости по полным хешам Git.

| Пакет | Версия | Ревизия |
| --- | --- | --- |
| `com.chisel` | `0.9.5-preview` | `96e8f7ceaa0efca9cf90c746c0a89f74c126bcf4` |
| `com.custom_asset_metadata` | `1.3.0` | `68b5022e92c056a42d0725083de9e0d07c3c1ca5` |

## Просмотр

В текущем локальном стенде пример уже импортирован. В окне **Project** дважды щёлкните `Assets/Samples/Chisel 3D Level Editor/0.9.5-preview/Sample scene/sample scene.unity`. В новом клоне импортируйте пример через **Window → Package Manager → Chisel 3D Level Editor → Samples → Sample scene → Import**. В Git отслеживаются ссылки Package Manager на закреплённые ревизии. Импортированные файлы, их `.meta` и настройки Chisel из `ProjectSettings/Packages/com.chisel` остаются локальными.

Для пустой сцены создайте модель через **GameObject → Chisel → Create Model** и добавьте форму через **GameObject → Chisel → Create Generators**. Chisel хранит авторские параметры в собственных компонентах, а сгенерированные меши под объектом модели. Первое построение может занять больше времени из-за компиляции Burst. [Начало работы с Chisel](https://github.com/RadicalCSG/com.chisel/blob/96e8f7ceaa0efca9cf90c746c0a89f74c126bcf4/GETTING_STARTED.md) · [Инструкция по установке](https://github.com/RadicalCSG/com.chisel/blob/96e8f7ceaa0efca9cf90c746c0a89f74c126bcf4/INSTALLATION.md)

## Проверка и ограничения

В фоновом Unity `6000.6.8f1` пример импортирован штатным API `Sample.Import` и открыт дополнительно к исходной сцене. Найдено 382 компонента Chisel, включая 4 модели, и 75 непустых `MeshFilter` под моделями после обновления редактора. Отсутствующих скриптов и новых ошибок при открытии нет. Burst выдал предупреждение `Synchronous compilation was requested` для `DisposeListChildrenBlobAssetReferenceJob<SubMeshTriangleLookup>`. Открытие и закрытие сцены завершились. Вид и управление в окне Scene графически не проверены.

При первом импорте Unity сообщила о трёх общих GUID с CSG Brush+ для `LICENSE`, `README.md` и `package.json`. UPM установил Chisel успешно. Unity пропустила импорт этих файлов Chisel в `AssetDatabase`. Исходные файлы присутствуют в пакете, а сравнение `.meta` двух пакетов с локальным пакетом подтвердило ровно три общих GUID. При проверке клона форка 25 ноября журнал повторного запуска содержал предупреждения о трёх общих GUID. Сохранились исходные GUID пакета CSG Brush. Ошибок компиляции и активных ошибок консоли в этом сеансе не было. При открытии стенда Chisel сам добавляет локальные ChiselPaintSettings и ChiselContentsList в список предварительной загрузки Player Settings.
