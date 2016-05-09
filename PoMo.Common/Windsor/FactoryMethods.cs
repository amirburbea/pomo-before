using System;
using Castle.Facilities.TypedFactory;
using Castle.MicroKernel.Registration;
using Castle.Windsor;

namespace PoMo.Common.Windsor
{
    public interface IFactory<TComponent> : IFactoryRelease<TComponent>
    {
        TComponent Create();
    }

    public interface IFactory<in TParameter, TComponent> : IFactoryRelease<TComponent>
    {
        TComponent Create(TParameter parameter);
    }

    public interface IFactoryRelease<in TComponent>
    {
        void Release(TComponent component);
    }

    public static class FactoryMethods
    {
        public static void RegisterFactories(IWindsorContainer container)
        {
            container
                .AddFacility<TypedFactoryFacility>()
                .Register(Component.For(typeof(IFactory<>)).AsFactory())
                .Register(Component.For(typeof(IFactory<,>)).AsFactory());
        }
    }
}