from django.urls import path

from . import views

app_name = 'orders'

urlpatterns = [
    path('cart/', views.cart_detail, name='cart'),
    path('cart/add/<int:variant_id>/', views.cart_add, name='cart_add'),
    path('cart/update/<int:variant_id>/', views.cart_update, name='cart_update'),
    path('cart/remove/<int:variant_id>/', views.cart_remove, name='cart_remove'),
    path('checkout/', views.checkout, name='checkout'),
    path('my/', views.order_list, name='order_list'),
    path('my/<int:order_id>/', views.order_detail, name='order_detail'),
]
