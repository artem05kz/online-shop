from django.contrib import admin

from catalog.admin_style import StyledAdminMixin
from .models import Address, Order, OrderItem


class OrderItemInline(admin.TabularInline):
    model = OrderItem
    extra = 0
    fields = ['variant', 'product_name', 'pack_label', 'price', 'quantity']
    readonly_fields = ['product_name', 'pack_label', 'price']


@admin.register(Order)
class OrderAdmin(StyledAdminMixin, admin.ModelAdmin):
    list_display = ['order_number', 'user', 'status', 'contact_phone', 'city', 'created_at']
    list_editable = ['status']
    list_filter = ['status']
    search_fields = ['contact_phone', 'contact_name', 'user__username']
    inlines = [OrderItemInline]

    @admin.display(description='Номер заказа', ordering='id')
    def order_number(self, obj):
        return obj.pk


@admin.register(Address)
class AddressAdmin(StyledAdminMixin, admin.ModelAdmin):
    list_display = ['user', 'full_name', 'phone', 'city', 'is_default']
    search_fields = ['full_name', 'phone', 'user__username']
