# Platypus Reclayed — Online Co-op

Неофициальный онлайн-кооператив на двоих для **Platypus Reclayed** (Steam,
Windows): хост приглашает друга через Steam, и они проходят игру вместе.
Устроен как platypus-coop для оригинального Platypus: каждый игрок запускает свою игру, по сети идут только ввод и
проверочные суммы.

*English below.*

Проверено на полной версии **1.4.0** (`1.4.0-26-g2926f6f81`): две копии игры,
бой на первом мире, проверочные суммы обеих игр совпадают на протяжении всего
матча. Приглашение через Steam между двумя разными аккаунтами — см.
[TESTING.md](TESTING.md).

## Установка

1. Скачайте `PlatypusReclayedCoop-<версия>.zip` из
   [Releases](../../releases) и распакуйте в любую папку.
2. Закройте игру и дважды щёлкните **`install.bat`**. Скрипт сам находит игру
   во всех библиотеках Steam, скачивает **BepInEx 5.4.23.3** (загрузчик модов
   для Unity) с GitHub и кладёт мод. Файлы игры не меняются.
3. Запускайте игру **из Steam** — окно приглашения открывается через оверлей Steam.
4. Один раз: **Моды → Дополнения → «Online Co-op»**.

Игра не нашлась автоматически:

```powershell
.\install.ps1 -Game 'D:\SteamLibrary\steamapps\common\Platypus Reclayed'
```

Удаление: `uninstall.bat` (мод) или `.\uninstall.ps1 -RemoveBepInEx` (вернуть
игру в исходное состояние).

```text
Platypus Reclayed/
├── Platypus.exe                          ← игра, не меняется
├── winhttp.dll, doorstop_config.ini      ← загрузчик BepInEx
├── BepInEx/plugins/PlatypusReclayedCoop/PlatypusReclayedCoop.dll
└── Platypus_Data/StreamingAssets/Online Co-op/   ← запись в «Дополнениях» (мод создаёт сам)
```

## Как играть

1. **Хост:** главное меню → **«Совместная игра»** → сложность → мир → выбор корабля.
2. Справа внизу кнопка **«Пригласить друга»** — окно Steam, выберите друга.
3. **Друг** принимает приглашение в Steam — его игра сразу открывает выбор корабля.
4. Каждый выбирает свой корабль (хост слева, друг справа) и жмёт **«Готов»**
   (ещё раз — «Отмена»). Уровень начинается, когда готовы оба.
5. **«Выйти»** на экране кораблей — выход из совместной игры у обоих.

- Обычный «Старт» остаётся одиночной игрой.
- Пауза (Esc) общая. При обрыве связи игра встаёт на паузу и ждёт до 60 с.
- Кнопки чужой панели недоступны: каждый управляет только своим кораблём.
- Тексты мода переведены на все 16 языков игры.
- Обе машины: **одна версия игры** и **одна версия мода**, иначе подключение
  отклоняется с объяснением.

Без Steam (LAN / VPN), для проверки на одном ПК — запуск с параметрами:

```text
Platypus.exe --coop-host 27833              (хост ждёт по TCP 27833)
Platypus.exe --coop-join 192.168.1.10:27833 (второй игрок)
```

В `BepInEx\config\marilynje.platypus.reclayedcoop.cfg` есть `ShowIpJoin = true`
— вернуть в меню кнопки входа по IP.

## Как это устроено

Логика и коллизии игры считаются в `FixedUpdate` (`OnTick`, `CheckCollision`),
а `Update` только сглаживает картинку. Мод через Harmony делает её
детерминированной для двоих (lockstep на 60 Гц, буфер ввода по пингу, четыре
хеша состояния каждый тик — тихой рассинхронизации нет):

| Что недетерминировано в игре | Что делает мод |
| --- | --- |
| `Player.Update` копит ввод каждый кадр отрисовки (`inputPool` в 1.4) | ввод задаётся из сети внутри тика (`UpdateMovement`) |
| Огонь читается из Rewired | заменён на сетевой бит огня |
| Таймеры неуязвимости и респауна на `Time.time` / `WaitForSeconds` | общие «часы симуляции» по номеру тика |
| `UpdateInvincibility`, `MoveIntoBounds` вызываются из `Update` | выполняются только внутри тика |
| Молния игрока (1.4) ставится по экранным координатам | позиция считается из состояния симуляции (`Lightning.cs`) |
| Вход игрока/continue — по локальным контроллерам | по сетевым нажатиям, слот = номер игрока |
| `UnityEngine.Random` общий для игры, звука и UI | у симуляции свой поток |
| Старт уровня из меню | хост шлёт снапшот `Globals` и seed, обе игры грузят уровень с ним |

При расхождении обе игры пишут `BepInEx\desync-tick*.txt` — построчное
состояние всех объектов, которое можно сравнить `diff`'ом.

## Сборка из исходников

Нужны Windows, своя копия игры и компилятор C#: Visual Studio (любой набор),
.NET SDK или `csc.exe` из .NET Framework. Код — C# 5.

```powershell
.\install.ps1      # первый раз: ставит BepInEx в игру
.\build.ps1        # -> build\PlatypusReclayedCoop.dll
.\install.ps1      # копирует собранный мод в игру
.\package.ps1      # -> releases\PlatypusReclayedCoop-<версия>.zip
```

`tools/verify.py` сверяет все цели патчей с вашей сборкой игры — запускайте
после обновлений игры: `python tools/verify.py "<игра>\Platypus_Data\Managed\Platypus.dll"`.

---

## English

Unofficial two-player online co-op for **Platypus Reclayed** (Steam, Windows).
Tested on the full game **1.4.0**.

**Install:** download the zip from [Releases](../../releases), close the game,
double-click `install.bat` (finds the game through Steam, installs BepInEx 5
and the mod). Start the game from Steam, then tick **Mods → Add-ons → Online Co-op**.

**Play:** main menu → **Online Co-op** → difficulty → world → ship screen →
**Invite a Friend** (Steam invite). The friend accepts in Steam and lands on
the same screen. Both press **Ready**; the level starts when both are ready.
Both players need the same game version and mod version.

**Uninstall:** `uninstall.bat`, or `.\uninstall.ps1 -RemoveBepInEx` to restore
the game completely.

License: MIT ([LICENSE](LICENSE)).

Unofficial fan project, not affiliated with Claymatic Games. The game, its
art and trademarks belong to their owners. This repository contains no game files.
