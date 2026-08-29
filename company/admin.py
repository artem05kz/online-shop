from django.contrib import admin

from catalog.admin_style import StyledAdminMixin
from .models import SiteContact, Store


@admin.register(Store)
class StoreAdmin(StyledAdminMixin, admin.ModelAdmin):
    list_display = ['address', 'city', 'kind', 'working_hours', 'phone', 'order', 'is_active']
    list_editable = ['order', 'is_active']
    list_filter = ['city', 'kind', 'is_active']
    search_fields = ['address', 'city', 'phone']
    ordering = ['order', 'city']

    fieldsets = [
        ('Адрес точки', {
            'fields': ['city', 'address', 'kind'],
            'description': 'Адрес пишите так, как его должен увидеть покупатель — одной строкой.',
        }),
        ('Режим работы и связь', {
            'fields': ['working_hours', 'lunch_hours', 'phone'],
        }),
        ('Показ на сайте', {
            'fields': ['order', 'is_active'],
        }),
        ('Карта (необязательно)', {
            'fields': ['map_url', 'latitude', 'longitude'],
            'classes': ['collapse'],
            'description': 'Нужно, только если хотите кнопку «Как добраться».',
        }),
    ]


@admin.register(SiteContact)
class SiteContactAdmin(StyledAdminMixin, admin.ModelAdmin):
    list_display = ['title', 'phone', 'email', 'whatsapp', 'instagram', 'is_active']
    list_editable = ['is_active']

    fieldsets = [
        ('Что это', {
            'fields': ['title', 'is_active'],
            'description': 'Эти контакты показываются в шапке и внизу каждой страницы сайта. '
                           'Оставьте галочку «Использовать на сайте» только у одного набора.',
        }),
        ('Телефоны и почта', {
            'fields': ['phone', 'phone_extra', 'email'],
        }),
        ('Мессенджеры и соцсети', {
            'fields': ['whatsapp', 'telegram', 'instagram'],
            'description': 'Можно указать номер или имя пользователя — ссылка соберётся сама.',
        }),
        ('Город и часы работы', {
            'fields': ['city', 'working_hours'],
        }),
    ]
