from django.contrib.auth import views as auth_views
from django.urls import path

from . import views

app_name = 'accounts'

urlpatterns = [
    path('login/', auth_views.LoginView.as_view(template_name='accounts/login.html'), name='login'),
    path('logout/', auth_views.LogoutView.as_view(), name='logout'),
    path('register/', views.register, name='register'),
    path('profile/', views.profile, name='profile'),
    path('profile/addresses/', views.address_list, name='address_list'),
    path('profile/addresses/add/', views.address_edit, name='address_add'),
    path('profile/addresses/<int:pk>/', views.address_edit, name='address_edit'),
    path('profile/addresses/<int:pk>/delete/', views.address_delete, name='address_delete'),
]
