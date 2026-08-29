from django.urls import path

from . import import_views, views

app_name = 'catalog'

urlpatterns = [
    path('', views.home, name='home'),
    # Category/product names are in Russian, so slugs contain Cyrillic
    # characters — the built-in `slug` converter is ASCII-only, `str` isn't.
    path('catalog/<str:brand_slug>/<str:category_slug>/', views.category_detail, name='category'),
    path('product/<str:slug>/', views.product_detail, name='product'),
    # Staff-only: upload a new 1C price-list/stock export from the browser
    # instead of running the management command from a terminal.
    path('import/', import_views.import_upload, name='import_upload'),
    path('import/confirm/', import_views.import_confirm, name='import_confirm'),
]
