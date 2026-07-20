using Microsoft.Extensions.DependencyInjection;

namespace XsvHcdtHelper;

public static class XsvHcdtBuilderExtensions
{
    public static IXsvHcdtBuilder Configure(
        this IXsvHcdtBuilder builder,
        Action<XsvHcdtOptions> configure)
    {
        builder.Services.Configure(configure);
        return builder;
    }

    public static IXsvHcdtBuilder AddOutputSink<TSink>(this IXsvHcdtBuilder builder)
        where TSink : class, IRowSink
    {
        // The sink is constructed per operation via ActivatorUtilities (it needs the output
        // Stream as a constructor argument); no container registration is required.
        builder.Services.Configure<XsvHcdtOptions>(opts => opts.CustomSinkType = typeof(TSink));

        return builder;
    }
}