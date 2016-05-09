using System;
using Castle.Facilities.TypedFactory;
using Castle.MicroKernel.Registration;
using Castle.Windsor;

namespace PoMo.Common.Windsor
{
    public interface IComponentScope<out TComponent> : IDisposable
    {
        TComponent Component
        {
            get;
        }
    }

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
        public static IComponentScope<TComponent> CreateScope<TComponent>(this IFactory<TComponent> factory)
        {
            return factory == null ? null : new ComponentScope<TComponent>(factory.Create(), factory.Release);
        }

        public static IComponentScope<TComponent> CreateScope<TParameter, TComponent>(this IFactory<TParameter, TComponent> factory, TParameter parameter)
        {
            return factory == null ? null : new ComponentScope<TComponent>(factory.Create(parameter), factory.Release);
        }

        public static void RegisterFactories(IWindsorContainer container)
        {
            container
                .AddFacility<TypedFactoryFacility>()
                .Register(Component.For(typeof(IFactory<>)).AsFactory())
                .Register(Component.For(typeof(IFactory<,>)).AsFactory());
        }

        private sealed class ComponentScope<TComponent> : IComponentScope<TComponent>
        {
            private readonly Action<TComponent> _release;
            private bool _isReleased;

            public ComponentScope(TComponent component, Action<TComponent> release)
            {
                this.Component = component;
                this._release = release;
            }

            public TComponent Component
            {
                get;
                private set;
            }

            public void Dispose()
            {
                if (this._isReleased)
                {
                    return;
                }
                this._release(this.Component);
                this.Component = default(TComponent);
                this._isReleased = true;
            }
        }
    }
}