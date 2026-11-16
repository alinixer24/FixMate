using System.Collections.ObjectModel;
using System.Windows.Input;

namespace FixMate.App.ViewModels;

public sealed class TroubleshootingViewModel : ViewModelBase
{
    private static readonly IReadOnlyDictionary<string, ErrorCodeEntry> ErrorCodes =
        new Dictionary<string, ErrorCodeEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["80070002"] = new("Windows could not find a required file.", "Check the file or installation path, then rerun the operation. For Windows Update, restart and retry before using the Windows Update troubleshooter."),
            ["80070003"] = new("Windows could not find a required path.", "Verify the target folder exists and that the drive is available. If this occurred during an update, restart and retry."),
            ["80070005"] = new("Access was denied.", "Check the account and file permissions involved. Use administrator elevation only when you trust the application and the task requires it."),
            ["80070020"] = new("Another process is using the file.", "Close applications that may be using the file, restart Windows if needed, and retry. Avoid deleting files from active system folders."),
            ["80070057"] = new("A parameter or setting was invalid.", "Review the selected path, file name, and configuration values, then retry with valid settings."),
            ["80070422"] = new("A required Windows service is disabled.", "Identify the service named by the operation and check its startup configuration in Services. Do not enable unfamiliar services without confirming their purpose."),
            ["80072EE7"] = new("A server name could not be resolved.", "Check the internet connection and DNS settings, then retry. A VPN, proxy, or network policy may affect name resolution."),
            ["80072EFE"] = new("The connection to a server was interrupted.", "Check the connection and retry. If it persists, check VPN, proxy, firewall, or service availability."),
            ["800F081F"] = new("Windows could not find the source files needed for servicing.", "Confirm Windows Update can reach Microsoft update services. Managed devices may require an approved repair source from their administrator."),
            ["800F0922"] = new("Windows could not complete an update operation.", "Restart and check for the update again. Check available system-reserved partition space and network policy; consult the update history for the specific failure."),
            ["C0000005"] = new("An application attempted to access memory it could not use.", "Update or repair the affected application and its drivers. If one app is involved, check its vendor support information and the related Application event."),
            ["0000001A"] = new("Windows reported a memory-management stop error.", "Record the stop code and any referenced driver. Install trusted Windows and driver updates, and run memory diagnostics if the error recurs."),
            ["0000007B"] = new("Windows could not access the boot device.", "If storage or firmware settings recently changed, review those changes. Back up important data where possible and seek support before changing boot or storage settings."),
            ["000000EF"] = new("A critical Windows process terminated.", "Record the stop code and recent driver or software changes. Check for trusted updates and use Windows recovery options if Windows cannot start normally.")
        };

    private static readonly IReadOnlyList<TroubleshootingTopic> Topics =
    [
        new(
            "Internet or Wi-Fi is not working",
            "Work from the simplest checks toward the network connection. These steps do not change system configuration.",
            [
                "Check that Wi-Fi is enabled or the Ethernet cable is connected; verify other devices can reach the same network.",
                "Disconnect and reconnect to the network, then try opening a different website.",
                "If only one application is affected, check that application's proxy, VPN, and offline settings.",
                "Run the Network connectivity check on the Diagnostics page. If name resolution fails, note any VPN or managed-network restrictions."
            ]),
        new(
            "The PC feels slow",
            "Determine whether the slowdown is constant or began after a specific change before making changes.",
            [
                "Restart the PC and check whether the slowdown returns after sign-in.",
                "Use Task Manager to identify applications with sustained high CPU, memory, or disk use.",
                "Check free space in File Explorer and review the Live monitoring page while the slowdown occurs.",
                "If one application is responsible, update it from its trusted source or review its own settings."
            ]),
        new(
            "Windows Update or installation failed",
            "Use the exact error code from Update history or the installer; different codes can have different causes.",
            [
                "Restart Windows and retry once; make sure the device has a stable connection and sufficient free space.",
                "Open Settings > Windows Update > Update history and record the failing update and error code.",
                "Look up the code in the Error Center below. Check whether a VPN, proxy, or managed-device policy is involved.",
                "For persistent servicing errors, use the Windows integrity check in Diagnostics and review its result before choosing a repair."
            ]),
        new(
            "An application crashes or will not start",
            "Narrow down whether the issue is isolated to one application or affects Windows more broadly.",
            [
                "Restart Windows and reopen the application; note the exact time and any displayed error code.",
                "Install updates for the application from its publisher, then use its repair option if Windows provides one.",
                "Check Event Viewer > Windows Logs > Application for an error at the time of the crash; record the faulting application and module.",
                "If the problem began after an update or driver change, record that change and consult the affected application's support guidance."
            ]),
        new(
            "Disk is nearly full",
            "First identify which drive is low on space. Avoid deleting files you do not recognize, especially in Windows folders.",
            [
                "Open Settings > System > Storage to review which categories use space.",
                "Empty the Recycle Bin only after reviewing its contents; remove personal files you no longer need.",
                "Move large personal media files to a trusted backup or external drive, then verify the copy before removing the original.",
                "Run the Storage health check on the Diagnostics page. Use its disk scan only if you understand the action and approve the elevation prompt."
            ])
    ];

    private string _errorCode = string.Empty;
    private string _errorLookupResult = "Enter a Windows error code, for example 0x80070005 or 0x800F081F.";
    private TroubleshootingTopic _selectedTopic = Topics[0];

    public TroubleshootingViewModel()
    {
        LookupErrorCodeCommand = new RelayCommand(LookupErrorCode);
    }

    public IReadOnlyList<TroubleshootingTopic> TroubleshootingTopics => Topics;
    public ICommand LookupErrorCodeCommand { get; }

    public TroubleshootingTopic SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (ReferenceEquals(_selectedTopic, value))
            {
                return;
            }

            _selectedTopic = value;
            OnPropertyChanged();
        }
    }

    public string ErrorCode
    {
        get => _errorCode;
        set
        {
            _errorCode = value;
            OnPropertyChanged();
        }
    }

    public string ErrorLookupResult
    {
        get => _errorLookupResult;
        private set
        {
            _errorLookupResult = value;
            OnPropertyChanged();
        }
    }

    private void LookupErrorCode()
    {
        var normalizedCode = new string(ErrorCode
            .Where(character => !char.IsWhiteSpace(character))
            .ToArray())
            .ToUpperInvariant();
        if (normalizedCode.StartsWith("0X", StringComparison.Ordinal))
        {
            normalizedCode = normalizedCode[2..];
        }

        if (normalizedCode.Length == 0)
        {
            ErrorLookupResult = "Enter an error code to look it up.";
            return;
        }

        ErrorLookupResult = ErrorCodes.TryGetValue(normalizedCode, out var entry)
            ? $"0x{normalizedCode}: {entry.Meaning}\n\nSuggested next steps: {entry.NextSteps}"
            : $"No offline entry was found for '{ErrorCode.Trim()}'. Check the code in Windows Update history, the application's error details, or Event Viewer. This local lookup does not search the internet.";
    }

    private sealed record ErrorCodeEntry(string Meaning, string NextSteps);
}

public sealed record TroubleshootingTopic(
    string Title,
    string Description,
    IReadOnlyList<string> Steps);
