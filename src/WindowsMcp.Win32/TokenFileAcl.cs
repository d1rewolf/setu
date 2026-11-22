using System.Security.AccessControl;
using System.Security.Principal;
using WindowsMcp.Core.Server;

namespace WindowsMcp.Win32;

/// <summary>Restricts a file to the current user: inheritance off, one FullControl rule, nothing else.</summary>
public sealed class TokenFileAcl : ITokenFileProtector
{
    public void Protect(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User
            ?? throw new InvalidOperationException("Cannot protect the token file: the current Windows user has no SID.");

        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));

        // Only the DACL was modified on this FileSecurity, so only the DACL is written; the owner stays as is.
        new FileInfo(path).SetAccessControl(security);
    }
}
