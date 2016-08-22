﻿using Autofac;
using Xpressive.Home.Contracts.Automation;
using Xpressive.Home.Contracts.Gateway;
using Xpressive.Home.Contracts.Messaging;

namespace Xpressive.Home.Plugins.PhilipsHue
{
    public class PhilipsHueModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<PhilipsHueScriptObjectProvider>().As<IScriptObjectProvider>();

            builder.RegisterType<PhilipsHueGateway>()
                .As<IGateway>()
                .As<IPhilipsHueGateway>()
                .As<IMessageQueueListener<CommandMessage>>()
                .SingleInstance()
                .OnActivated(async h => await h.Instance.ObserveBulbStatusAsync());

            builder.RegisterType<PhilipsHueDeviceDiscoveringService>()
                .As<IPhilipsHueDeviceDiscoveringService>()
                .SingleInstance();

            builder.RegisterType<PhilipsHueBridgeDiscoveringService>()
                .As<IPhilipsHueBridgeDiscoveringService>()
                .SingleInstance();

            base.Load(builder);
        }
    }
}
