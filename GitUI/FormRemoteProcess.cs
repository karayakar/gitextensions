﻿using System;
using System.Windows.Forms;
using GitCommands;
using GitCommands.Config;

using GitUI.UserControls;

using ResourceManager;

namespace GitUI
{
    /// <summary>
    /// Form that handles Plink exceptions
    /// </summary>
    public partial class FormRemoteProcess : FormProcess
    {
        #region Translation
        private readonly TranslationString _fingerprintNotRegistredText =
            new TranslationString("The fingerprint of this host is not registered by PuTTY." + Environment.NewLine + "This causes this process to hang, and that why it is automatically stopped." + Environment.NewLine + Environment.NewLine + "When the connection is opened detached from Git and GitExtensions, the host's fingerprint can be registered." + Environment.NewLine + "You could also manually add the host's fingerprint or run Test Connection from the remotes dialog." + Environment.NewLine + Environment.NewLine + "Do you want to register the host's fingerprint and restart the process?");
        private readonly TranslationString _fingerprintNotRegistredTextCaption =
            new TranslationString("Host Fingerprint not registered");
        #endregion

        public bool Plink { get; set; }
        private bool _restart;
        protected readonly GitModule Module;

        // only for translation
        protected FormRemoteProcess()
        {
        }

        public FormRemoteProcess(GitModule module, string process, string arguments)
            : base(process, arguments, module.WorkingDir, null, true)
        {
            Module = module;
        }

        public FormRemoteProcess(GitModule module, string arguments)
            : base(null, arguments, module.WorkingDir, null, true)
        {
            Module = module;
        }

        public static new bool ShowDialog(GitModuleForm owner, string arguments)
        {
            return ShowDialog(owner, owner.Module, arguments);
        }

        public static new bool ShowDialog(IWin32Window owner, GitModule module, string arguments)
        {
            using (var formRemoteProcess = new FormRemoteProcess(module, arguments))
            {
                formRemoteProcess.ShowDialog(owner);
                return !formRemoteProcess.ErrorOccurred();
            }
        }

        private string _urlTryingToConnect = string.Empty;

        /// <summary>
        /// When cloning a remote using putty, sometimes an error occurs that the fingerprint is not known.
        /// This is fixed by trying to connect from the command line, and choose yes when asked for storing
        /// the fingerprint. Just a dirty fix...
        /// </summary>
        public void SetUrlTryingToConnect(string url)
        {
            _urlTryingToConnect = url;
        }

        protected override void BeforeProcessStart()
        {
            _restart = false;
            Plink = GitCommandHelpers.Plink();
            base.BeforeProcessStart();
        }

        protected override bool HandleOnExit(ref bool isError)
        {
            if (_restart)
            {
                Retry();
                return true;
            }

            // An error occurred!
            if (isError && Plink)
            {
                // there might be another error, this condition is too weak
                /*
                if (GetOutputString().Contains("successfully authenticated"))
                {
                    isError = false;
                    return false;
                }
                */

                // If the authentication failed because of a missing key, ask the user to supply one.
                if (GetOutputString().Contains("FATAL ERROR") && GetOutputString().Contains("authentication"))
                {
                    if (FormPuttyError.AskForKey(this, out var loadedKey))
                    {
                        // To prevent future authentication errors, save this key for this remote.
                        if (!string.IsNullOrEmpty(loadedKey) && !string.IsNullOrEmpty(Remote) &&
                            string.IsNullOrEmpty(Module.GetSetting("remote.{0}.puttykeyfile")))
                        {
                            Module.SetPathSetting(string.Format("remote.{0}.puttykeyfile", Remote), loadedKey);
                        }

                        // Retry the command.
                        Retry();
                        return true;
                    }
                }

                if (GetOutputString().ToLower().Contains("the server's host key is not cached in the registry"))
                {
                    string remoteUrl;

                    if (string.IsNullOrEmpty(_urlTryingToConnect))
                    {
                        remoteUrl = Module.GetSetting(string.Format(SettingKeyString.RemoteUrl, Remote));
                        if (string.IsNullOrEmpty(remoteUrl))
                        {
                            remoteUrl = Remote;
                        }
                    }
                    else
                    {
                        remoteUrl = _urlTryingToConnect;
                    }

                    if (AskForCacheHostkey(this, Module, remoteUrl))
                    {
                        Retry();
                        return true;
                    }
                }
            }

            return base.HandleOnExit(ref isError);
        }

        public static bool AskForCacheHostkey(IWin32Window owner, GitModule module, string remoteUrl)
        {
            if (!remoteUrl.IsNullOrEmpty() && MessageBoxes.CacheHostkey(owner))
            {
                remoteUrl = GitCommandHelpers.GetPlinkCompatibleUrl(remoteUrl);

                module.RunExternalCmdShowConsole(
                    "cmd.exe",
                    string.Format("/k \"\"{0}\" -T {1}\"", AppSettings.Plink, remoteUrl));

                return true;
            }

            return false;
        }

        protected override void DataReceived(object sender, TextEventArgs e)
        {
            if (Plink)
            {
                if (e.Text.Contains("If you trust this host, enter \"y\" to add the key to"))
                {
                    if (MessageBox.Show(this, _fingerprintNotRegistredText.Text, _fingerprintNotRegistredTextCaption.Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        string remoteUrl;
                        if (string.IsNullOrEmpty(_urlTryingToConnect))
                        {
                            remoteUrl = Module.GetSetting(string.Format(SettingKeyString.RemoteUrl, Remote));
                            remoteUrl = string.IsNullOrEmpty(remoteUrl) ? Remote : remoteUrl;
                        }
                        else
                        {
                            remoteUrl = _urlTryingToConnect;
                        }

                        remoteUrl = GitCommandHelpers.GetPlinkCompatibleUrl(remoteUrl);

                        Module.RunExternalCmdShowConsole("cmd.exe", string.Format("/k \"\"{0}\" {1}\"", AppSettings.Plink, remoteUrl));

                        _restart = true;
                        Reset();
                    }
                    else
                    {
                        KillProcess();
                    }
                }
            }

            base.DataReceived(sender, e);
        }
    }
}
