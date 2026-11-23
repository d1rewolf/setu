using Setu.Core.Server;
using Setu.Mac;

// Run by launchd as the LaunchAgent com.greatworx.setu. Everything that can throw is built inside
// SetuStartup's failure handling (logged, exit code 1), and the launcher writes to the startup's own log.
var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var data = Path.Combine(home, "Library", "Application Support", "setu");

return SetuStartup.Run(data, log => MacPlatform.Create(home, data, log));
