
namespace UpdateInvTypes
{
    using System;
    using System.Windows.Forms;

    internal static class UpdateInvTypes
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UpdateInvTypesUI());
        }
    }
}