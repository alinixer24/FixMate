namespace FixMate.App.ViewModels;

public class PageViewModel : ViewModelBase
{
    public PageViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }
    public string Description { get; }
}

