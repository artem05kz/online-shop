from django import forms
from django.contrib.auth.forms import UserCreationForm
from django.contrib.auth.models import User


class RegisterForm(UserCreationForm):
    class Meta:
        model = User
        fields = ['username', 'email']
        labels = {
            'username': 'Логин',
            'email': 'Электронная почта',
        }


class ProfileForm(forms.ModelForm):
    """Name is optional on purpose — a customer shouldn't be forced to fill
    it in just to browse or reorder."""

    class Meta:
        model = User
        fields = ['first_name', 'last_name', 'email']
        labels = {
            'first_name': 'Имя',
            'last_name': 'Фамилия',
            'email': 'Электронная почта',
        }
        widgets = {
            'first_name': forms.TextInput(attrs={'placeholder': 'Необязательно'}),
            'last_name': forms.TextInput(attrs={'placeholder': 'Необязательно'}),
        }
