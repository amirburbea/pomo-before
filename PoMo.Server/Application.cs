using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using PoMo.Common.ServiceModel.Contracts;
using PoMo.Server.Properties;

namespace PoMo.Server
{
    public interface IApplication
    {
        void Start();

        void Stop();
    }

    public sealed class Application : IApplication
    {
        private readonly Binding _binding;
        private readonly IServerContract _instance;
        private ServiceHost _host;

        public Application(IServerContract instance, Binding binding)
        {
            this._instance = instance;
            this._binding = binding;
        }

        void IApplication.Start()
        {
            this._host = new ServiceHost(this._instance);
            this._host.AddServiceEndpoint(typeof(IServerContract), this._binding, Settings.Default.WcfUri);
            this._host.Description.Behaviors.Find<ServiceDebugBehavior>().IncludeExceptionDetailInFaults = true;
            this._host.Open();
        }

        void IApplication.Stop()
        {
            this._host.Close();
        }
    }
}