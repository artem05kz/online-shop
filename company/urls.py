from django.urls import path

from . import views

app_name = 'company'

urlpatterns = [
    path('addresses/', views.store_list, name='store_list'),
]
