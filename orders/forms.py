from django import forms

from .models import Address, Order


class AddressForm(forms.ModelForm):
    """The map picker on the profile page writes into `street`/`city` and the
    two hidden coordinate fields; everything else the customer types."""

    class Meta:
        model = Address
        fields = [
            'label', 'full_name', 'phone',
            'city', 'street', 'house', 'apartment', 'entrance', 'floor', 'intercom',
            'comment', 'latitude', 'longitude', 'is_default',
        ]
        widgets = {
            'label': forms.TextInput(attrs={'placeholder': 'Дом, Работа…'}),
            'phone': forms.TextInput(attrs={'placeholder': '+7 (___) ___-__-__'}),
            'street': forms.TextInput(attrs={'placeholder': 'Улица или микрорайон'}),
            'comment': forms.Textarea(attrs={'rows': 3, 'placeholder': 'Например: позвонить за 10 минут'}),
            'latitude': forms.HiddenInput(),
            'longitude': forms.HiddenInput(),
        }


class CheckoutForm(forms.ModelForm):
    """Checkout reuses a saved address when the customer picks one; the raw
    fields stay editable for a one-off delivery address."""

    saved_address = forms.ModelChoiceField(
        queryset=Address.objects.none(),
        required=False,
        label='Выбрать сохранённый адрес',
        empty_label='Ввести новый адрес',
    )

    class Meta:
        model = Order
        fields = ['contact_name', 'contact_phone', 'city', 'street_and_details', 'comment']
        widgets = {
            'comment': forms.Textarea(attrs={'rows': 3}),
            'street_and_details': forms.TextInput(attrs={'placeholder': 'Улица, дом, квартира'}),
        }
        labels = {
            'contact_name': 'Имя получателя',
            'contact_phone': 'Телефон',
            'city': 'Город',
            'street_and_details': 'Адрес доставки',
            'comment': 'Комментарий к заказу',
        }

    def __init__(self, *args, user=None, **kwargs):
        super().__init__(*args, **kwargs)
        if user is not None and user.is_authenticated:
            self.fields['saved_address'].queryset = user.addresses.all()
        else:
            del self.fields['saved_address']
