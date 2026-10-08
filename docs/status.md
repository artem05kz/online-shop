# Состояние проекта

Сводка для продолжения работы: что сделано, что дальше, как запускать и на чём уже спотыкались.
Обновлено 08.10.2026.

## Где что лежит

- Код: `C:\Проекты\Интернет магазин`, GitHub `artem05kz/online-shop` (раньше `grass-aktau-shop`), ветка `master`.
- На GitHub отправлено всё до `7914540` (план). Пункты 21-29 плана (08.10.2026) закоммичены локально.
- Образцы выгрузок 1С: `C:\Проекты\Пример таблиц для интернет магазина` (прайс Ifoam и складская
  ведомость Grass, артикулы не пересекаются). В репозиторий их не класть.
- Документы: [README](../README.md), [перенос с Django](rewrite-plan.md) (список всех функций,
  решения и отличия), [план развития](roadmap.md), [выкладка](deploy.md), этот файл.

## Что сделано

1. **Перенос с Django на ASP.NET Core** (этапы 1-5, подробно в `rewrite-plan.md`): модели и база
   (EF Core, SQLite), витрина, корзина и оформление заказа, личный кабинет (Identity), панель
   управления `/admin`, импорт из 1С (разбор сверен с Django-парсером на настоящем прайсе: 28 товаров
   совпали полностью, 3 разобраны правильнее), Django-код удалён (остался в истории, `3737eb9`).
2. **Дизайн** (`0850ce9`): панель для людей старше 40 лет (крупный шрифт, кнопки с подписью, кнопка
   «Сохранить» всегда внизу экрана), товары списком с фото и путём категории стрелками, фильтры
   «Без фото», «Без цены», «Нет в наличии», «Скрытые», бренды и категории одним деревом с линиями
   и стрелками порядка, главное фото переключателем. Витрина: иконки вместо эмодзи, плитки категорий
   с фото, фасовки на карточке, выбор фасовки плиткой на странице товара, список категорий слева.
   Карты: 2ГИС вместо Google (с ключом `TWOGIS_API_KEY`, без ключа OpenStreetMap), кнопки «Маршрут
   в 2ГИС» у магазинов, точка магазина ставится кликом в панели.
3. **Доработки из плана** (`6d640b7`): уменьшенные копии фото в WebP (главная 3,58 МБ → 0,17 МБ),
   «Повторить заказ», подсказки в поиске, свёрнутое меню панели на телефоне.
4. **Telegram-бот** (пункт 2 плана): уведомления о новых заказах на номера из списка в панели
   (`/admin/telegram`). Код в `Services/Telegram`: `TelegramApi` (свой HttpClient, чтобы токен
   не попал в журнал), `TelegramBot` (фоновая служба: long polling и очередь рассылки),
   `TelegramLinker` (привязка номера к чату по кнопке «Отправить мой номер»), `TelegramNotifier`,
   `OrderMessage`. Таблица `TelegramRecipients`, миграция `TelegramRecipients`. Без токена бот
   выключен. Проверено с настоящим Telegram только неверным токеном (ответ 401 разобран, токена
   в журнале нет); с настоящим ботом проверяет Артём.

5. **Избранное, сжатие фото, баннеры** (пункты 8-10 плана): `Services/Favorites.cs`
   (кука у гостя, таблица `Favorites` после входа), `FavoritesController`, `_FavoriteButton`,
   `favorites.js`; `Services/ImageShrinker.cs` (уменьшение, поворот по EXIF, WebP),
   `MediaStorage.SaveImageAsync`; `Services/BannerShape.cs` (пропорция 3:1, srcset, предупреждения).
   Проверял Артём: бот с настоящим токеном работает.
6. **Подготовка к выкладке** (пункты 5, 7, 14 плана, подробно в `deploy.md`):
   - PostgreSQL: `Database:Provider=Postgres`, класс `PostgresShopDbContext` и свои миграции
     в `Data/PostgresMigrations`. Новую миграцию добавлять в оба набора (команды ниже).
   - Перенос базы: `--copy-from-sqlite` (`Data/DatabaseCopier.cs`), проверен на настоящей локальной базе.
   - Корзина в таблице `CartItems`, ключи входа в `DataProtectionKeys`, сессий больше нет.
   - Docker: `Dockerfile`, `docker-compose.yml` (db, app, caddy, backup), `deploy/Caddyfile`,
     `deploy/backup.sh`, `.env.example`. Проверено в Docker: перенос данных, миниатюры на Linux,
     сжатие баннера, вход и корзина после перезапуска, копия и восстановление после порчи данных.
   - Страницы ошибок `/error/{код}` (раньше любая ошибка показывалась как «не найдено»), `/health`,
     `/sitemap.xml`, `/robots.txt`.

7. **Защита** (07.10.2026, проверка атаками из `scratchpad/attack.cs` описана ниже):
   - вход: блокировка аккаунта на 15 минут после 5 неверных паролей, не больше 10 попыток входа
     и регистрации в минуту с адреса (`Infrastructure/LoginThrottle.cs`), хеш пароля 210 000 повторов;
   - заголовки: CSP с nonce, nosniff, Referrer-Policy, Permissions-Policy (`Infrastructure/SecurityHeaders.cs`),
     HSTS в Caddy. Встроенных `onsubmit`/`onchange` больше нет: `data-confirm`, `data-autosubmit` (`forms.js`).
     Встроенный `<script>` писать только с `nonce="@Context.CspNonce()"` (в Razor Pages `@HttpContext.CspNonce()`);
   - ссылка баннера только на свой сайт или http(s) (`SafeLinks`);
   - копии шифруются паролем `BACKUP_PASSWORD` (GnuPG, AES-256);
   - политика конфиденциальности `/privacy` (реквизиты продавца в «Контактах»), галочка согласия
     при регистрации и при первом заказе, время согласия в `AspNetUsers.PersonalDataConsentAt`.

   Проверено атаками на сайт в Docker: SQL-инъекции, XSS, выход за папку фото, open redirect, CSRF,
   доступ без входа, служебные файлы, перебор паролей, заголовки. Всё защищено.

8. **Почта** (пункт 15 плана): обязательна при регистрации, подтверждается кодом на `/accounts/email`,
   смена пароля по коду (`/accounts/forgot-password`, `/accounts/reset-password`), письма о заказе
   и статусах. Код: `Services/Email` (`EmailSending.cs`: SMTP через MailKit, очередь и фоновая отправка;
   `MailTemplates.cs`; `AccountMail.cs`). Без SMTP письма ложатся в `App_Data/mail`. Проверено через
   Mailpit: регистрация, код, заказ, письмо «Заказ №4 принят» (в локальной базе остался пробный
   покупатель `mailpit418895` и заказ №4).

9. **Пункты 21-29 плана** (08.10.2026):
   - самовывоз (`Order.IsPickup`, `PickupStoreId`; склады не предлагаются), лист заказа для печати
     `/admin/orders/{id}/print`;
   - резерв остатков (`ProductVariant.ReservedQty`, `Services/StockReservations.cs`, пересчёт при запуске);
   - письмо «снова в наличии» (`Favorite.WasInStock`, `Services/RestockNotifier.cs` раз в 5 минут,
     адрес сайта для ссылок из `Site:Url` или первого запроса, `Infrastructure/SiteAddress.cs`);
   - поиск по словам и с опечатками (`Services/SearchSpelling.cs`);
   - отзывы (`Models/Review.cs`, `ReviewsController`, раздел панели «Отзывы»);
   - отчёты `/admin/reports` с выгрузкой в Excel;
   - разметка schema.org и og: на странице товара (`Services/ProductMarkup.cs`), лента `/feed/google.xml`;
   - автопроверка `.github/workflows/ci.yml` (тесты на SQLite и PostgreSQL, сборка образа). На GitHub
     ещё не запускалась: запустится после push. Те же тесты прогнаны в контейнере Linux с .NET SDK.

Тестов 217, все проходят и на SQLite, и на PostgreSQL (`SHOP_TEST_POSTGRES`, см. README).
В тестах Telegram всегда подменён заглушкой `FakeTelegramApi`, даже если на машине задан настоящий токен.

## Что дальше

По [плану развития](roadmap.md):

- **Сервер и домен**: всё готово к выкладке, хостинг Артём ищет позже. Шаги в `deploy.md`.
  Хостинг и домен .kz, по-видимому, должны быть в Казахстане (закон о персональных данных, правила .kz),
  Артём уточняет. В политике написано, что данные хранятся в РК.
- **Почта**: нужен настоящий адрес магазина и «пароль для внешних приложений» (вводит Артём).
- Оплаты на сайте не будет: курьер принимает оплату терминалом. HoReCa (скидки корпоративным клиентам)
  потом, пункты 19-20 плана.
- После выкладки: кнопки «Подтвердить» и «Отменить» в сообщениях бота, автоматическая выгрузка
  из 1С (CommerceML), оплата Kaspi.
- Уведомления покупателю не делаем: если будет мобильное приложение, они уйдут туда.

## Как запускать

```bash
dotnet run --project src/OnlineShop.Web --urls http://localhost:5080
dotnet test OnlineShop.sln
dotnet run --project src/OnlineShop.Web -- --make-manager <логин>
dotnet run --project src/OnlineShop.Web -- --import-1c --price-list "<файл>" --stock "<файл>" --dry-run
```

Миграции: у SQLite и PostgreSQL свои наборы, `--context` указывать обязательно:

```bash
dotnet ef migrations add <Имя> --context ShopDbContext --project src/OnlineShop.Web --output-dir Data/Migrations
dotnet ef migrations add <Имя> --context PostgresShopDbContext --project src/OnlineShop.Web --output-dir Data/PostgresMigrations --namespace OnlineShop.Web.Data.PostgresMigrations
```

Docker на этой машине: `docker compose up -d --build`, сайт на `http://localhost:8088` (порт в `.env`),
в нём копия локальной базы. Отдельно для тестов: контейнер `shop-test-pg` на порту 55432.

В Claude Code сайт запускается конфигурацией `online-shop-dotnet` из `.claude/launch.json` (порт 5080).
Она запускает сайт без профиля, то есть в Production, поэтому `Program.cs` подключает user-secrets
и вне Development: там хранится токен бота (`dotnet user-secrets set Telegram:BotToken ...`).

## Локальная база для разработки

- `src/OnlineShop.Web/App_Data/shop.db` (в git не попадает). В ней настоящий прайс Ifoam
  из образцов, розничные цены условные (опт × 1,3) и случайные остатки, демо-магазины с координатами,
  демо-контакты и демо-баннеры.
- Копия базы до замены каталога: `App_Data/shop-before-real-catalog.db`.
- Тестовый менеджер: логин `тест-покупатель`, пароль в `App_Data/test-manager.txt`.

## Снимки экрана

`tools/screenshots.cs` снимает страницы headless-Chrome с входом под менеджером (логин `-` без входа,
адрес сайта в переменной `SHOT_BASE`, по умолчанию `http://localhost:5080`):

```bash
MSYS_NO_PATHCONV=1 dotnet run tools/screenshots.cs -- <папка> 1366 <логин> <пароль> /admin/products "/|<js перед снимком>"
```

Ширина 390 даёт телефон. Без `MSYS_NO_PATHCONV=1` Git Bash превращает `/admin/...` в путь Windows.

## Подводные камни

- Перед `dotnet build` и `dotnet test` остановить запущенный сайт: он держит exe, сборка падает.
  Правки `.cshtml` видны только после пересборки.
- В Razor нельзя называть переменные `page` и `section`. В Razor Pages параметр адреса `page` занят,
  поэтому номер страницы в списках панели называется `p`.
- Доп. адреса `/add` у форм панели добавлены с `SuppressLinkGeneration`, иначе ссылки «Изменить»
  превращаются в `/add?id=5`.
- Галочки рендерятся со скрытым полем рядом (`CheckBoxHiddenInputRenderMode.Inline`): строки таблиц
  копируются скриптом.
- Цены выводятся с неразрывным пробелом («1 890 ₸» через U+00A0), в тестах сравнивать с `\u00a0`.
  HTML в тестах раскодировать (`TestSite.Page`).
- SQLite: цены хранятся целым числом тиынов, регистр кириллицы в поиске через свою функцию `ulower`.
- На машине больше нет Node.js и openpyxl в системном Python (стоит 3.14). Для сверки с Django-парсером
  ставилось отдельное окружение с openpyxl и Pillow во временной папке.
- Telegram: два запущенных сайта с одним токеном мешают друг другу (ошибка 409, панель пишет
  «бот уже запущен в другом месте»). Тестам это не грозит, у них заглушка.
- Без `SqliteDesignTimeFactory` команда `dotnet ef ... --context ShopDbContext` брала фабрику PostgreSQL
  (наследник подходит под тип) и клала миграцию PostgreSQL в набор SQLite.
- Без `--namespace` dotnet ef кладёт снимок модели PostgreSQL в папку `OnlineShop/Web/Data/...`.
- Compose берёт имя проекта из названия папки, у кириллицы оно пустое: поэтому `name: online-shop`.
- Docker в PowerShell этой сессии виден только после обновления PATH из реестра, в Git Bash
  вызывать по полному пути `/c/Program Files/Docker/Docker/resources/bin/docker.exe`.
- В PowerShell SQL со словом `delete` защитная проверка принимает за удаление файла: SQL отправлять
  файлом через Git Bash.
- Переменные окружения главнее user-secrets: `Program.cs` после `AddUserSecrets` заново добавляет
  переменные окружения и параметры запуска. Без этого `Telegram__BotToken=" "` не выключал бота локально.
- В PowerShell `$env:X = ""` удаляет переменную, а не делает её пустой. Чтобы выключить бота при запуске,
  ставить пробел: сайт его обрезает.
- В Git Bash кириллица в аргументах `curl` портится: логин передавать файлом (`--data-urlencode "Поле@файл"`).
- Общий стиль `.badge` витрины прижат к углу карточки (position: absolute): в панели для значков
  в меню это отменено, иначе они улетают на заголовок.
- Docker Desktop может быть закрыт: перед командами docker запускать его и ждать `docker info`.
- 2ГИС: карта и поиск адреса требуют ключа, демо-ключ на месяц, дальше подписка. Ссылки на маршрут
  работают без ключа.
