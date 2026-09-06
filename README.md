# Кооп-демо на FishNet + Facepunch.Steamworks

Учебный проект: минимальная кооперативная игра, в которой видно, как устроен весь путь
от «нажал кнопку в меню» до «бегаем по одной сцене с другом из списка друзей Steam».

* Лобби Steam с приглашениями через оверлей.
* Server-authoritative движение с клиентским предсказанием (CSP) и реконсиляцией.
* Сетевые сцены: меню → лобби → матч → лобби.
* Fallback на прямое IP-подключение (Tugboat), когда Steam недоступен.

**Подробный разбор реализации: [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md).**

---

## Требования

| Компонент | Версия | Как установлен |
|---|---|---|
| Unity | 6000.6.0f1 (URP) | — |
| FishNet | 4.7.3 | UPM, git-ссылка в `Packages/manifest.json` |
| Facepunch.Steamworks | 2.5.2 | DLL в `Assets/Plugins/Facepunch.Steamworks` |
| FishyFacepunch | main (2024-04) | исходники в `Assets/Plugins/FishyFacepunch` |

Player Settings уже настроены: **Api Compatibility Level = .NET Framework**
(Facepunch собран под net4.x и с `.NET Standard 2.1` не соберётся).

## Быстрый старт

1. Запустите клиент Steam и войдите в аккаунт.
2. Откройте сцену `Assets/_Project/Scenes/Bootstrap.unity` и нажмите Play.
3. «Создать игру» → вы в лобби.
4. «Пригласить друзей» → откроется оверлей Steam → выберите друга.
   Друг принимает приглашение и попадает в ваше лобби.
5. Когда все нажали «Готов», хост нажимает «Начать матч».

Управление в матче: `WASD` — движение, `Shift` — бег, `Space` — прыжок,
мышь — камера, `Esc` — меню паузы.

## Локальное тестирование без второго компьютера

Через Steam P2P нельзя подключиться к самому себе одним аккаунтом, поэтому для проверки
«двух игроков на одной машине» есть LAN-режим на транспорте Tugboat:

1. В `Assets/_Project/Settings/AppConfig.asset` включите `Force Lan Mode`.
2. Запустите Play в редакторе и нажмите «Создать игру».
3. Соберите билд и запустите его так:

```bash
CoopDemo.exe -lan -autojoin 127.0.0.1
```

Флаги командной строки (`Assets/_Project/Scripts/App/CommandLineOptions.cs`):

| Флаг | Действие |
|---|---|
| `-autohost` | сразу создать сессию |
| `-autojoin <адрес>` | сразу подключиться (IP для Tugboat, SteamID64 лобби для Steam) |
| `-lan` | принудительный LAN-режим, даже если Steam доступен |

## Структура

```
Assets/_Project/
  Scenes/      Bootstrap → MainMenu → Lobby → Game
  Prefabs/     Network/ (NetworkManager, PlayerCharacter, PlayerSession, SessionCoordinator)
  Scripts/
    Core/        абстракции и конфиг, ни от чего не зависят
    Steam/       обёртка над Facepunch.Steamworks
    Networking/  сессия, лобби-логика, спавн (FishNet)
    Gameplay/    движение с предсказанием, камера, внешний вид
    UI/          «глупые» вью
    App/         composition root
    Editor/      валидация Build Settings (меню Coop/)
```

## Полезные пункты меню

* `Coop → Validate Build Settings` — проверить, что все сцены на месте и в правильном порядке.
* `Coop → Fix Build Settings` — починить порядок автоматически.
* `Fish-Networking → Refresh Default Prefabs` — пересобрать коллекцию сетевых префабов
  (нужно, если префабы создавались скриптом или переносились между папками).

## Известные ограничения

Проект учебный и намеренно не содержит: аутентификации, античита, host-migration,
голосового чата, пула объектов под нагрузкой, интерполяции чужих персонажей поверх
предсказания. Что и как добавлять — в конце [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md).
