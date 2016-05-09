using System;
using System.IO;
using System.Reflection;
using Castle.Windsor;
using Castle.Windsor.Installer;
using PoMo.Data;
using Topshelf;
using Topshelf.HostConfigurators;

namespace PoMo.Server
{
    internal static class Program
    {
        private static void Main()
        {
            string fileName;
            ConnectionStringMethods.GetConnectionString(out fileName);
            if (!File.Exists(fileName))
            {
                Console.WriteLine("Can not locate database, be sure to run the Loader!");
                Environment.Exit(1);
            }
            try
            {
                using (IWindsorContainer container = new WindsorContainer())
                {
                    container.Install(FromAssembly.InThisApplication());
                    HostFactory.Run(Program.TopshelfConfiguration(container));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.ReadKey();
            }
        }

        private static Action<HostConfigurator> TopshelfConfiguration(IWindsorContainer container)
        {
            return hostConfig =>
            {
                hostConfig.Service<IApplication>(config =>
                {
                    config.ConstructUsing(container.Resolve<IApplication>);
                    config.WhenStarted(application => application.Start());
                    config.WhenStopped(application => application.Stop());
                });
                string serviceName = Assembly.GetExecutingAssembly().GetName().Name;
                hostConfig.SetServiceName(serviceName);
                AssemblyProductAttribute productAttribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>();
                string displayName = string.IsNullOrEmpty(productAttribute?.Product) ? serviceName : productAttribute.Product;
                hostConfig.SetDisplayName(displayName);
                AssemblyDescriptionAttribute descriptionAttribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyDescriptionAttribute>();
                string description = string.IsNullOrEmpty(descriptionAttribute?.Description) ? displayName : descriptionAttribute.Description;
                hostConfig.SetDescription(description);
                hostConfig.RunAsLocalSystem();
                hostConfig.StartAutomaticallyDelayed();
            };
        }
    }
}