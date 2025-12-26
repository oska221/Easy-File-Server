using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace MiniHttpServer
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Check if the program is running as administrator
            if (!IsAdministrator())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Application.ExecutablePath;
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";

                    Process.Start(psi);
                }
                catch
                {
                    // clicked NO in UAC → shutdown
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
