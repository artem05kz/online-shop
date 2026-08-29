from django import forms

from .models import Order


class CheckoutForm(forms.ModelForm):
    class Meta:
        model = Order
        fields = ['contact_name', 'contact_phone', 'city', 'street_and_details', 'comment']
        widgets = {
            'comment': forms.Textarea(attrs={'rows': 3}),
        }
        labels = {
            'contact_name': 'Имя получателя',
            'contact_phone': 'Телефон',
            'city': 'Город',
            'street_and_details': 'Улица, дом, квартира',
            'comment': 'Комментарий к заказу',
        }
