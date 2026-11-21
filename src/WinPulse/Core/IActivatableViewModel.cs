namespace WinPulse.Core;

/// <summary>Implemented by tab view models that want to pause timers when hidden.</summary>
public interface IActivatableViewModel
{
    void OnActivated();
    void OnDeactivated();
}
