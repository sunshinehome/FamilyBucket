﻿using Bucket.ServiceDiscovery;
using Grpc.Core;
using MagicOnion.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using GRpcServer = Grpc.Core.Server;
namespace Bucket.Gprc.Extensions
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseGrpcRegisterService(this IApplicationBuilder app, IConfiguration configuration)
        {
            RpcServiceDiscoveryOptions serviceDiscoveryOption = new RpcServiceDiscoveryOptions();
            configuration.GetSection("ServiceDiscovery").Bind(serviceDiscoveryOption);
            app.UseGrpcRegisterService(serviceDiscoveryOption);
            return app;
        }
        public static IApplicationBuilder UseGrpcRegisterService(this IApplicationBuilder app, RpcServiceDiscoveryOptions serviceDiscoveryOption)
        {
            var applicationLifetime = app.ApplicationServices.GetRequiredService<IApplicationLifetime>() ??
                throw new ArgumentException("Missing Dependency", nameof(IApplicationLifetime));

            var serviceDiscovery = app.ApplicationServices.GetRequiredService<IServiceDiscovery>() ?? 
                throw new ArgumentException("Missing Dependency", nameof(IServiceDiscovery));

            if (string.IsNullOrEmpty(serviceDiscoveryOption.ServiceName))
                throw new ArgumentException("service name must be configure", nameof(serviceDiscoveryOption.ServiceName));

            IEnumerable<Uri> addresses = null;
            if (serviceDiscoveryOption.Endpoints != null && serviceDiscoveryOption.Endpoints.Length > 0)
            {
                addresses = serviceDiscoveryOption.Endpoints.Select(p => new Uri(p));
            }
            else
            {
                var features = app.Properties["server.Features"] as FeatureCollection;
                addresses = features.Get<IServerAddressesFeature>().Addresses.Select(p => new Uri(p)).ToArray();
            }
            // 以默认第一个地址开启rpc服务
            var grpcServer = InitializeGrpcServer(addresses.FirstOrDefault());

            foreach (var address in addresses)
            {
                UriBuilder myUri = new UriBuilder(address.Scheme, address.Host, address.Port);

                var serviceID = GetRpcServiceId(serviceDiscoveryOption.ServiceName, myUri.Uri);

                Uri healthCheck = null;
                if (!string.IsNullOrEmpty(serviceDiscoveryOption.HealthCheckTemplate))
                {
                    healthCheck = new Uri(myUri.Uri, serviceDiscoveryOption.HealthCheckTemplate);
                }

                var registryInformation = serviceDiscovery.RegisterServiceAsync(serviceDiscoveryOption.ServiceName, 
                    serviceDiscoveryOption.Version, 
                    myUri.Uri, 
                    healthCheckUri: healthCheck, 
                    tags: new[] { $"urlprefix-/{serviceDiscoveryOption.ServiceName}"
                    }).Result;

                applicationLifetime.ApplicationStopping.Register(() =>
                {
                    try
                    {
                        grpcServer.ShutdownAsync().Wait();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"grpcServer had shutown {ex}");
                    }
                    serviceDiscovery.DeregisterServiceAsync(registryInformation.Id);
                });
            }

            return app;
        }
        private static string GetRpcServiceId(string serviceName, Uri uri)
        {
            return $"GRPC_{serviceName}_{uri.Host.Replace(".", "_")}_{uri.Port}";
        }
        private static GRpcServer InitializeGrpcServer(Uri addresses)
        {
            var grpcServer = new GRpcServer
            {
                Ports = { new ServerPort(addresses.Host, addresses.Port, ServerCredentials.Insecure) },
                Services = { MagicOnionEngine.BuildServerServiceDefinition() }
            };
            grpcServer.Start();
            return grpcServer;
        }
    }
}
