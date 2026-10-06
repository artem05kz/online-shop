namespace OnlineShop.Web.ViewModels;

/// <summary>Кнопка с сердечком: на карточке только значок, на странице товара значок с подписью.</summary>
public record FavoriteButtonModel(int ProductId, bool Active, bool WithLabel = false);
