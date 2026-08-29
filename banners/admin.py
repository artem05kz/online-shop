from django.contrib import admin

from catalog.admin_style import StyledAdminMixin
from .models import Banner


@admin.register(Banner)
class BannerAdmin(StyledAdminMixin, admin.ModelAdmin):
    list_display = ['title', 'order', 'is_active', 'starts_at', 'ends_at']
    list_editable = ['order', 'is_active']
