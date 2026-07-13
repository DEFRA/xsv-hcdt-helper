using Microsoft.Extensions.DependencyInjection;

namespace XsvHcdtHelper;

public interface IXsvHcdtBuilder
{
    IServiceCollection Services { get; }
}

internal sealed class XsvHcdtBuilder : IXsvHcdtBuilder
{
    public IServiceCollection Services { get; }
    public XsvHcdtBuilder(IServiceCollection services) => Services = services;
}

public static class XsvHcdtServiceCollectionExtensions
{
    /// <summary>
    /// Registers IXsvHcdtNormaliser and IXsvHcdtReader with sensible defaults.
    /// </summary>
    public static IXsvHcdtBuilder AddXsvHcdtHelper(this IServiceCollection services)
    {
        services.AddOptions<XsvHcdtOptions>();

        services.AddSingleton<IXsvHcdtNormaliser, XsvHcdtNormaliser>();
        services.AddSingleton<IXsvHcdtReader, XsvHcdtReader>();

        return new XsvHcdtBuilder(services);
    }

    /// <summary>
    /// Registers XSV Helper services and configures options via a delegate.
    /// </summary>
    public static IXsvHcdtBuilder AddXsvHcdtHelper(
        this IServiceCollection services,
        Action<XsvHcdtOptions> configure)
    {
        var builder = services.AddXsvHcdtHelper();
        services.Configure(configure);
        return builder;
    }
}
