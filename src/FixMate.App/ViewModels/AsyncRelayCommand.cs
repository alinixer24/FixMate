using System.Windows.Input;

namespace FixMate.App.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Action<Exception> _onError;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Action<Exception> onError)
        : this(_ => execute(), onError)
    {
    }

    public AsyncRelayCommand(Func<object?, Task> execute, Action<Exception> onError)
    {
        _execute = execute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting)
        {
            return;
        }

        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await _execute(parameter);
        }
        catch (Exception exception)
        {
            _onError(exception);
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
