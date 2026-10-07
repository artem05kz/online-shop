#!/usr/bin/env bash
# Резервные копии магазина: база (pg_dump) и загруженные фото. Работает в контейнере backup.
#
#   без аргументов     раз в сутки в BACKUP_HOUR делать копию, хранить KEEP_DAYS дней;
#   now                сделать копию сейчас;
#   list               показать, какие копии есть;
#   restore <метка>    восстановить базу и фото из копии с этой меткой, например 2026-10-07_0300.
#
# Восстановление заменяет текущие данные целиком, поэтому перед ним сайт останавливают:
#   docker compose stop app && docker compose run --rm backup restore <метка> && docker compose start app
set -euo pipefail

BACKUP_HOUR="${BACKUP_HOUR:-3}"
KEEP_DAYS="${KEEP_DAYS:-14}"
DIR=/backups
mkdir -p "$DIR"

backup_now() {
    local stamp
    stamp=$(date +%Y-%m-%d_%H%M)
    # Сначала во временный файл: недописанная копия не должна выглядеть готовой.
    pg_dump --format=custom --file="$DIR/shop_$stamp.dump.tmp"
    mv "$DIR/shop_$stamp.dump.tmp" "$DIR/shop_$stamp.dump"
    # Уменьшенные копии фото (_thumbs) не храним: сайт сделает их заново.
    tar --create --gzip --file="$DIR/media_$stamp.tar.gz.tmp" --exclude=media/_thumbs -C /data media
    mv "$DIR/media_$stamp.tar.gz.tmp" "$DIR/media_$stamp.tar.gz"
    find "$DIR" -maxdepth 1 \( -name 'shop_*.dump' -o -name 'media_*.tar.gz' \) -mtime +"$((KEEP_DAYS - 1))" -delete
    echo "Копия $stamp готова: $(du -h "$DIR/shop_$stamp.dump" | cut -f1) база, $(du -h "$DIR/media_$stamp.tar.gz" | cut -f1) фото"
}

restore() {
    local stamp="${1:?Укажите метку копии, список: docker compose run --rm backup list}"
    local dump="$DIR/shop_$stamp.dump" media="$DIR/media_$stamp.tar.gz"
    [[ -f "$dump" ]] || { echo "Нет файла $dump"; exit 1; }
    [[ -f "$media" ]] || { echo "Нет файла $media"; exit 1; }
    echo "Восстанавливаю базу из $dump"
    # --clean удаляет текущие таблицы перед загрузкой: после восстановления база ровно как в копии.
    pg_restore --clean --if-exists --no-owner --single-transaction --dbname="$PGDATABASE" "$dump"
    echo "Восстанавливаю фото из $media"
    find /data/media -mindepth 1 -delete
    tar --extract --gzip --file="$media" -C /data
    echo "Готово. Запустите сайт: docker compose start app"
}

seconds_until_backup() {
    local now target
    now=$(date +%s)
    target=$(date -d "today $BACKUP_HOUR:00" +%s)
    if (( target <= now )); then
        target=$(date -d "tomorrow $BACKUP_HOUR:00" +%s)
    fi
    echo $(( target - now ))
}

case "${1:-loop}" in
    now) backup_now ;;
    list) ls -lh "$DIR" | grep -E 'shop_|media_' || echo "Копий пока нет" ;;
    restore) restore "${2:-}" ;;
    loop)
        echo "Копии каждый день в $BACKUP_HOUR:00, хранятся $KEEP_DAYS дней"
        while true; do
            sleep "$(seconds_until_backup)"
            backup_now || echo "Копию сделать не удалось, следующая попытка через сутки"
        done
        ;;
    *) echo "Неизвестная команда: $1"; exit 1 ;;
esac
