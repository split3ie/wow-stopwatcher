namespace WowStopwatcher.App.Shell;

/// <summary>Version of the running app.</summary>
public interface IAppVersion
{
    /// <summary>Version without any "+commit" suffix, for example "0.1.0".</summary>
    string Version { get; }

    /// <summary>"v" + version for official builds, otherwise "local build".</summary>
    string DisplayText { get; }

    /// <summary>True for release builds made by the release workflow (not local builds).</summary>
    bool IsOfficialBuild { get; }
}
