// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using Eventuous.SignalR.Server;
using Microsoft.AspNetCore.SignalR;

namespace Microsoft.Extensions.DependencyInjection;

public static class SignalRGatewayRegistrations {
    public static IServiceCollection AddSignalRSubscriptionGateway<THub>(
        this IServiceCollection services,
        Action<IServiceProvider, SignalRGatewayOptions> configure
    ) where THub : Hub {
        services.AddSingleton(sp => {
            var options = new SignalRGatewayOptions { SubscriptionFactory = null! };
            configure(sp, options);
            return options;
        });
        services.AddSingleton<SignalRProducer<THub>>();
        services.AddSingleton<SubscriptionGateway<THub>>();
        return services;
    }
}
