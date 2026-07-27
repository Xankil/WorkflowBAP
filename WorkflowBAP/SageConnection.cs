using System;
using Objets100cLib;

namespace WorkflowBAP.Sage
{
    public sealed class SageConnection : IDisposable
    {
        private BSCPTAApplication100c _application;

        public BSCPTAApplication100c Application
        {
            get
            {
                if (_application == null || !_application.IsOpen)
                {
                    throw new InvalidOperationException(
                        "La connexion Sage n'est pas ouverte.");
                }

                return _application;
            }
        }

        public void Open(
            string companyFile,
            string username,
            string password)
        {
            if (string.IsNullOrWhiteSpace(companyFile))
            {
                throw new ArgumentException(
                    "Le chemin du fichier société Sage est obligatoire.",
                    nameof(companyFile));
            }

            _application = new BSCPTAApplication100c();

            _application.Name = companyFile;

            _application.Loggable.UserName = username;
            _application.Loggable.UserPwd = password;

            _application.Open();
        }

        public void Dispose()
        {
            if (_application == null)
                return;

            try
            {
                if (_application.IsOpen)
                    _application.Close();
            }
            finally
            {
                _application = null;
            }
        }
    }
}