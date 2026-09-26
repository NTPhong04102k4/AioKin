using Xunit;

namespace AioKin.Tests.Infrastructure;

/// <summary>
/// Mot fixture dung chung cho moi test class: khoi dong app va chay migration mat vai giay,
/// lam lai o tung class thi bo test cham den muc khong ai chay no nua.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
