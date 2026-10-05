namespace OnlineShop.Web.Infrastructure;

/// <summary>Склонение слов после чисел: 1 товар, 2 товара, 5 товаров.</summary>
public static class Plural
{
    public static string Choose(int n, string one, string few, string many)
    {
        var lastTwo = Math.Abs(n) % 100;
        var last = lastTwo % 10;
        if (lastTwo is >= 11 and <= 14)
        {
            return many;
        }
        return last switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    public static string Products(int n) => Choose(n, "товар", "товара", "товаров");
}
