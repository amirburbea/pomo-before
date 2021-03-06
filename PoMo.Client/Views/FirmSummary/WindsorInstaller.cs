﻿using Castle.MicroKernel.Registration;
using Castle.MicroKernel.SubSystems.Configuration;
using Castle.Windsor;

namespace PoMo.Client.Views.FirmSummary
{
    public sealed class WindsorInstaller : IWindsorInstaller
    {
        void IWindsorInstaller.Install(IWindsorContainer container, IConfigurationStore store)
        {
            container.Register(Component.For<FirmSummaryView>().LifestyleTransient());
        }
    }
}