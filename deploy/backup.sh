#!/usr/bin/env bash
# Резервные копии магазина: база (pg_dump) и загруженные фото. Работает в контейнере backup.
#
#   без аргументов     раз в сутки в BACKUP_HOUR делать копию, хранить KEEP_DAYS дней;
#   now                сделать копию сейчас;
#   list               показать, какие копии есть;
#   restore <метка>    восстановить базу и фото из копии с этой меткой, например 2026-10-07_0300.
#
# Копии зашифрованы паролем BACKUP_PASSWORD (AES-256, формат GnuPG): в них телефоны и адреса покупателей,
# а копии скачивают с сервера к себе. Без пароля копию не открыть, поэтому его хранят не только в .env
# на сервере: если сервер пропадёт вместе с .env, копии без пароля бесполезны.
#
# Восстановление заменяет текущие данные целиком, поэтому перед ним сайт останавливают:
#   docker compose stop app && docker compose run --rm backup restore <метка> && docker compose start app
set -euo pipefail

BACKUP_HOUR="${BACKUP_HOUR:-3}"
KEEP_DAYS="${KEEP_DAYS:-14}"
DIR=/backups
mkdir -p "$DIR"
# gpg хранит служебные файлы в домашней папке, в контейнере её может не быть.
export GNUPGHOME=/tmp/gnupg
mkdir -p -m 700 "$GNUPGHOME"

require_password() {
    if [[ -z "${BACKUP_PASSWORD:-}" ]]; then
        echo "Не задан BACKUP_PASSWORD в .env: копии без шифрования не делаются."
        exit 1
    fi
}

encrypt() {
    gpg --batch --yes --quiet --pinentry-mode loopback --passphrase-fd 3 \
        --symmetric --cipher-algo AES256 --output "$1" 3<<<"$BACKUP_PASSWORD"
}

decrypt() {
    gpg --batch --quiet --pinentry-mode loopback --passphrase-fd 3 --decrypt "$1" 3<<<"$BACKUP_PASSWORD"
}

backup_now() {
    require_password
    local stamp
    stamp=$(date +%Y-%m-%d_%H%M)
    # Шифруем на лету: незашифрованная копия на диск не попадает. Сначала во временный файл,
    # чтобы недописанная копия не выглядела готовой.
    pg_dump --format=custom | encrypt "$DIR/shop_$stamp.dump.gpg.tmp"
    mv "$DIR/shop_$stamp.dump.gpg.tmp" "$DIR/shop_$stamp.dump.gpg"
    # Уменьшенные копии фото (_thumbs) не храним: сайт сделает их заново.
    tar --create --gzip --exclude=media/_thumbs -C /data media | encrypt "$DIR/media_$stamp.tar.gz.gpg.tmp"
    mv "$DIR/media_$stamp.tar.gz.gpg.tmp" "$DIR/media_$stamp.tar.gz.gpg"
    find "$DIR" -maxdepth 1 \( -name 'shop_*.dump*' -o -name 'media_*.tar.gz*' \) -mtime +"$((KEEP_DAYS - 1))" -delete
    echo "Копия $stamp готова: $(du -h "$DIR/shop_$stamp.dump.gpg" | cut -f1) база, $(du -h "$DIR/media_$stamp.tar.gz.gpg" | cut -f1) фото, зашифрованы"
}

restore() {
    local stamp="${1:?Укажите метку копии, список: docker compose run --rm backup list}"
    local dump="$DIR/shop_$stamp.dump.gpg" media="$DIR/media_$stamp.tar.gz.gpg"
    [[ -f "$dump" ]] || { echo "Нет файла $dump"; exit 1; }
    [[ -f "$media" ]] || { echo "Нет файла $media"; exit 1; }
    require_password
    # Сначала проверяем пароль на копии фото: при неверном пароле база останется нетронутой.
    decrypt "$media" > /dev/null || { echo "Не удалось расшифровать: неверный BACKUP_PASSWORD или копия повреждена"; exit 1; }
    echo "Восстанавливаю базу из $dump"
    # --clean удаляет текущие таблицы перед загрузкой: после восстановления база ровно как в копии.
    decrypt "$dump" | pg_restore --clean --if-exists --no-owner --single-transaction --dbname="$PGDATABASE"
    echo "Восстанавливаю фото из $media"
    find /data/media -mindepth 1 -delete
    decrypt "$media" | tar --extract --gzip -C /data
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
        require_password
        echo "Копии каждый день в $BACKUP_HOUR:00, хранятся $KEEP_DAYS дней, зашифрованы"
        while true; do
            sleep "$(seconds_until_backup)"
            backup_now || echo "Копию сделать не удалось, следующая попытка через сутки"
        done
        ;;
    *) echo "Неизвестная команда: $1"; exit 1 ;;
esac
