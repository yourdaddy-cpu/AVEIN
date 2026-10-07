namespace AVEIN
{
    public interface IModule
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsEnabled { get; }
        void Init();
        void Shutdown();
    }
}
