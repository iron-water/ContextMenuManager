using Rmemu.Methods;
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Rmemu.Methods
{
    internal static class TrustedRecoveryStore
    {
        private static readonly SecurityIdentifier Administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private const AccessControlSections Sections = AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access;

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

        internal static string GetJournalPath(string configDirectory)
        {
            using(WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if(identity.User == null) throw new IOException(AppString.W("RecoveryUserUnknown"));
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Rmemu", "Recovery",
                    Hash(identity.User.Value), Hash(Path.GetFullPath(configDirectory).TrimEnd('\\').ToUpperInvariant()), "ChangeHistory.json");
            }
        }

        internal static DirectorySecurity CreateDirectorySecurity()
        {
            var security = new DirectorySecurity();
            security.SetOwner(Administrators);
            security.SetGroup(Administrators);
            security.SetAccessRuleProtection(true, false);
            foreach(SecurityIdentifier identity in new[] { Administrators, SystemAccount })
                security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            return security;
        }

        internal static void Ensure(string configDirectory)
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            for(DirectoryInfo ancestor = new DirectoryInfo(programData); ancestor != null; ancestor = ancestor.Parent)
                RejectReparsePoint(ancestor);
            string leaf = Path.GetDirectoryName(GetJournalPath(configDirectory));
            string path = programData;
            foreach(string component in Path.GetRelativePath(programData, leaf).Split(Path.DirectorySeparatorChar))
            {
                path = Path.Combine(path, component);
                EnsureTrustedDirectory(path);
            }
            ValidateFileIfPresent(Path.Combine(leaf, "ChangeHistory.json"));
        }

        internal static void EnsureTrustedDirectory(string path)
        {
            var directory = new DirectoryInfo(path);
            // Pass security during creation, never repair the owner or ACL of an existing path.
            if(!directory.Exists) FileSystemAclExtensions.Create(directory, CreateDirectorySecurity());
            ValidateDirectory(directory.FullName);
        }

        internal static void ValidateDirectory(string path)
        {
            var directory = new DirectoryInfo(path);
            RejectReparsePoint(directory);
            ValidateSecurity(FileSystemAclExtensions.GetAccessControl(directory, Sections), true);
        }

        internal static void ValidateFileIfPresent(string path)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch(FileNotFoundException) { return; }
            catch(DirectoryNotFoundException) { return; }
            if((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException(AppString.W("RecoveryNotFile", path));
            ValidateSecurity(FileSystemAclExtensions.GetAccessControl(new FileInfo(path), Sections), false);
        }

        private static void RejectReparsePoint(FileSystemInfo entry)
        {
            if((File.GetAttributes(entry.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(AppString.W("RecoveryReparse", entry.FullName));
        }

        internal static void ValidateSecurity(FileSystemSecurity security, bool directory)
        {
            if(!Administrators.Equals(security.GetOwner(typeof(SecurityIdentifier))))
                throw new IOException(AppString.W("RecoveryOwner"));
            if(directory && !security.AreAccessRulesProtected)
                throw new IOException(AppString.W("RecoveryUnprotected"));
            bool administrators = false, system = false;
            foreach(FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                bool adminRule = Administrators.Equals(rule.IdentityReference), systemRule = SystemAccount.Equals(rule.IdentityReference);
                if(rule.AccessControlType != AccessControlType.Allow || !adminRule && !systemRule || rule.FileSystemRights != FileSystemRights.FullControl
                    || rule.PropagationFlags != PropagationFlags.None || directory && rule.InheritanceFlags != (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit))
                    throw new IOException(AppString.W("RecoveryUntrusted"));
                administrators |= adminRule;
                system |= systemRule;
            }
            if(!administrators || !system) throw new IOException(AppString.W("RecoveryMissingAdmin"));
        }
    }
}
