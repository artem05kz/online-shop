# Сборка сайта в образ Docker. Запуск вместе с базой, Caddy и копиями: docker compose up -d (см. docs/deploy.md).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Сначала только файл проекта: пока зависимости не менялись, Docker берёт восстановленные пакеты из кеша.
COPY src/OnlineShop.Web/OnlineShop.Web.csproj src/OnlineShop.Web/
RUN dotnet restore src/OnlineShop.Web/OnlineShop.Web.csproj
COPY src/OnlineShop.Web/ src/OnlineShop.Web/
# Без указания платформы: образ работает и на обычном сервере, и на ARM.
RUN dotnet publish src/OnlineShop.Web/OnlineShop.Web.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Фото и служебные файлы вне папки программы: при обновлении образа они остаются на месте (тома в compose).
RUN mkdir -p /data/media /data/app && chown -R app:app /data
# Порт 8080 задан в самом образе aspnet (ASPNETCORE_HTTP_PORTS).
ENV Database__Provider=Postgres \
    Media__Root=/data/media \
    Storage__AppData=/data/app
# Не от имени администратора: ошибка в сайте не даёт доступа ко всему контейнеру.
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "OnlineShop.Web.dll"]
