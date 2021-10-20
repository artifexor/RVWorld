﻿/******************************************************
 *     ROMVault3 is written by Gordon J.              *
 *     Contact gordon@romvault.com                    *
 *     Copyright 2021                                 *
 ******************************************************/

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using RomVaultCore;
using RomVaultCore.Utils;

namespace ROMVault
{
    public partial class FrmSettings : Form
    {
        public FrmSettings()
        {
            InitializeComponent();

            cboFixLevel.Items.Clear();
            cboFixLevel.Items.Add("Level 1 - Fast copy Match on CRC");
            cboFixLevel.Items.Add("Level 2 - Fast copy if SHA1 scanned");
            cboFixLevel.Items.Add("Level 3 - Uncompress/Hash/Compress");
        }

        private void FrmConfigLoad(object sender, EventArgs e)
        {
            lblDATRoot.Text = Settings.rvSettings.DatRoot;
            cboFixLevel.SelectedIndex = (int)Settings.rvSettings.FixLevel;

            textBox1.Text = "";
            foreach (string file in Settings.rvSettings.IgnoreFiles)
            {
                textBox1.Text += file + Environment.NewLine;
            }
            chkTrrntZip.Checked = Settings.rvSettings.ConvertToTrrntzip;
            chkrv7Zip.Checked = Settings.rvSettings.ConvertToRV7Z;

            chkDetailedReporting.Checked = Settings.rvSettings.DetailedFixReporting;
            chkDoubleCheckDelete.Checked = Settings.rvSettings.DoubleCheckDelete;
            chkCacheSaveTimer.Checked = Settings.rvSettings.CacheSaveTimerEnabled;
            upTime.Value = Settings.rvSettings.CacheSaveTimePeriod;
            chkDebugLogs.Checked = Settings.rvSettings.DebugLogsEnabled;
        }

        private void BtnCancelClick(object sender, EventArgs e)
        {
            Close();
        }

        private void BtnOkClick(object sender, EventArgs e)
        {
            Settings.rvSettings.DatRoot = lblDATRoot.Text;
            Settings.rvSettings.FixLevel = (EFixLevel)cboFixLevel.SelectedIndex;
            string strtxt = textBox1.Text;
            strtxt = strtxt.Replace("\r", "");
            string[] strsplit = strtxt.Split('\n');

            Settings.rvSettings.IgnoreFiles = new List<string>(strsplit);
            for (int i = 0; i < Settings.rvSettings.IgnoreFiles.Count; i++)
            {
                Settings.rvSettings.IgnoreFiles[i] = Settings.rvSettings.IgnoreFiles[i].Trim();
                if (string.IsNullOrEmpty(Settings.rvSettings.IgnoreFiles[i]))
                {
                    Settings.rvSettings.IgnoreFiles.RemoveAt(i);
                    i--;
                }
            }
            Settings.rvSettings.SetRegExRules();

            Settings.rvSettings.DetailedFixReporting = chkDetailedReporting.Checked;
            Settings.rvSettings.DoubleCheckDelete = chkDoubleCheckDelete.Checked;
            Settings.rvSettings.DebugLogsEnabled = chkDebugLogs.Checked;
            Settings.rvSettings.CacheSaveTimerEnabled = chkCacheSaveTimer.Checked;
            Settings.rvSettings.CacheSaveTimePeriod = (int)upTime.Value;

            Settings.rvSettings.ConvertToTrrntzip = chkTrrntZip.Checked;
            Settings.rvSettings.ConvertToRV7Z = chkrv7Zip.Checked;


            Settings.WriteConfig(Settings.rvSettings);
            Close();
        }

        private void BtnDatClick(object sender, EventArgs e)
        {
            FolderBrowserDialog browse = new FolderBrowserDialog
            {
                ShowNewFolderButton = true,
                Description = "Select a folder for DAT Root",
                RootFolder = Environment.SpecialFolder.MyComputer,
                SelectedPath = Settings.rvSettings.DatRoot
            };

            if (browse.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            lblDATRoot.Text = RelativePath.MakeRelative(AppDomain.CurrentDomain.BaseDirectory, browse.SelectedPath);
        }
    }
}