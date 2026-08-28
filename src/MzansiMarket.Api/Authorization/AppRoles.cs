namespace MzansiMarket.Api.Authorization;

public static class AppRoles
{
    public const string Customer = "Customer";
    public const string Seller = "Seller";
    public const string ProductAdministrator = "ProductAdministrator";
    public const string FulfilmentEmployee = "FulfilmentEmployee";
    public const string BusinessManager = "BusinessManager";
    public const string SystemAdministrator = "SystemAdministrator";

    public static readonly string[] All =
    [
        Customer,
        Seller,
        ProductAdministrator,
        FulfilmentEmployee,
        BusinessManager,
        SystemAdministrator
    ];
}
