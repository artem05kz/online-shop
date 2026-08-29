from django.urls import path

from . import views

app_name = 'catalog'

urlpatterns = [
    path('', views.home, name='home'),
    # Category/product names are in Russian, so slugs contain Cyrillic
    # characters — the built-in `slug` converter is ASCII-only, `str` isn't.
    path('catalog/<str:brand_slug>/<str:category_slug>/', views.category_detail, name='category'),
    path('product/<str:slug>/', views.product_detail, name='product'),
]
