"""Shared styling hook for every ModelAdmin across the project — one small
custom stylesheet (bigger, calmer text; green accent instead of Django's
default blue; no page-wide horizontal scrolling from wide tables) plus a
script that highlights rows edited but not yet saved. Added the documented
Django way, via ModelAdmin.Media, so there's no risk of the admin
template-override recursion that comes with overriding admin/base.html."""


class StyledAdminMixin:
    class Media:
        css = {'all': ('css/admin_custom.css',)}
        js = ('js/admin_edit_highlight.js',)
