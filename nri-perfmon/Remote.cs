using System;
using System.Security;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.Runtime.ConstrainedExecution;
using System.Management;

namespace NewRelic
{
    public class RemoteUser
    {
        private const int LOGON_TYPE = 9;
        private const int LOGON_PROVIDER = 3;
        private SafeTokenHandle safeToken;
        private ConnectionOptions connectionOptions;
        private bool logonSuccess = false;
        private string username;
        private string password;
        private string domainname;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(String lpszUsername, String lpszDomain, String lpszPassword,
            int dwLogonType, int dwLogonProvider, out SafeTokenHandle phToken);

        public RemoteUser(Options options)
        {
            username = options.UserName;
            password = options.Password;
            domainname = options.DomainName;
        }

        public ConnectionOptions getConnectionOptions()
        {
            if (connectionOptions == null)
            {
                connectionOptions = new ConnectionOptions();
                if (!String.IsNullOrEmpty(username) && !String.IsNullOrEmpty(password) && !String.IsNullOrEmpty(domainname))
                {
                    connectionOptions.Password = password;
                    connectionOptions.Username = username;
                    connectionOptions.Authority = "ntlmdomain:" + domainname;
                }
            }
            return connectionOptions;
        }

        public T RunAsRemoteUser<T>(Func<T> funk)
        {
            if ((safeToken == null) && !String.IsNullOrEmpty(username) && !String.IsNullOrEmpty(password) && !String.IsNullOrEmpty(domainname))
            {
                if (Util.IsLinuxOrMacOS)
                {
                    Log.WriteLog("Remote Logon for Perf Counters does not work from a Linux or MacOS host.\nPlease disregard this message if you are only running WMI Queries.", Log.LogLevel.WARN);
                }
                else
                {
                    logonSuccess = LogonUser(username, domainname, password, LOGON_TYPE, LOGON_PROVIDER, out safeToken);
                    if (!logonSuccess)
                    {
                        Log.WriteLog(String.Format("Logon User failed: {0}\\{1}", username, password), Log.LogLevel.ERROR);
                        Environment.Exit(1);
                    }
                }
            }

            if (logonSuccess && !safeToken.IsInvalid) {
                T result = default(T);
                try
                {
                    using (WindowsIdentity identity = new WindowsIdentity(safeToken.DangerousGetHandle()))
                    {
                        using (WindowsImpersonationContext context = identity.Impersonate())
                        {
                            result = funk();
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.WriteLog(String.Format("User impersonation failed: {0}", e.Message), Log.LogLevel.ERROR);
                }
                return result;
            }
            return funk();
        }
    }

    public sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeTokenHandle()
            : base(true)
        {
        }

        [DllImport("kernel32.dll")]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        [SuppressUnmanagedCodeSecurity]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }
}
