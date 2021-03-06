﻿
/*///////////////////////////////////////////////////////////////////
<EasyFarm, general farming utility for FFXI.>
Copyright (C) <2013>  <Zerolimits>

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
*/
///////////////////////////////////////////////////////////////////

using EasyFarm.Debugging;
using EasyFarm.FarmingTool;
using EasyFarm.State;
using EasyFarm.ViewModels;
using EasyFarm.Views;
using FFACETools;
using Microsoft.Practices.Prism.PubSubEvents;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using ZeroLimits.FarmingTool;
using System.Linq;

namespace EasyFarm
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static FFACE _fface;
        private static FarmingTools _farmingTools;

        public static FarmingTools FarmingTools
        {
            get { return _farmingTools ?? (_farmingTools = FarmingTools.GetInstance(_fface)); }
            set { _farmingTools = value; }
        }

        public static IEventAggregator EventAggregator;

        public static GameEngine GameEngine { get; set; }

        public static readonly String[] resources = { "spells.xml", "abils.xml" };

        public const String resourcesUrl = "http://www.ffevo.net/topic/2923-ashita-and-ffacetools-missing-resource-files/";

        public App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Check if the resource files exist.
            if (!Directory.Exists("resources"))
            {
                MessageBox.Show(
                    String.Format("You're missing the resources folder. You can download it from : {0}", resourcesUrl
                    ));
                Environment.Exit(0);
            }

            if (resources.Any(x => !File.Exists(Path.Combine("resources", x))))
            {
                String[] missing = resources.Where(x => !File.Exists(Path.Combine("resources", x))).ToArray();

                String message = String.Join(" , ", missing);

                MessageBox.Show(
                    String.Format("You're missing {0} resource files. You can download them from : {1}.",
                    message, resourcesUrl));

                Environment.Exit(0);
            }

            // Let user select ffxi process
            frmStartup ProcessSelectionScreen = new frmStartup();
            ProcessSelectionScreen.ShowDialog();

            // Validate the selection
            var m_process = ProcessSelectionScreen.SelectedProcess;

            // Check if the user made a selection. 
            if (m_process == null)
            {
                System.Windows.Forms.MessageBox.Show("No valid process was selected: Exiting now.");
                Environment.Exit(0);
            }

            // Set up the event aggregator for updates to the status bar from 
            // multiple view models.
            EventAggregator = new EventAggregator();

            // Save the selected fface instance. 
            _fface = ProcessSelectionScreen.SelectedSession;

            // Set up the game engine if valid.
            FarmingTools.LoadSettings();

            // Set up UnitService to use this mob filter instead of its
            // default mob filter. 
            FarmingTools.UnitService.UnitFilter = UnitFilters.MobFilter(_fface);

            // Create a new game engine to control our character. 
            GameEngine = new GameEngine(_fface);

            // new DebugSpellCasting(_fface).Show();

            // new DebugCreatures(_fface, FarmingTools.UnitService).Show();

            // var dbc = new DebugCreatures(FarmingTools.FFACE, FarmingTools.UnitService);
            // dbc.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            FarmingTools.SaveSettings();
        }

        /// <summary>
        /// Update the user on what's happening.
        /// </summary>
        /// <param name="message">The message to display in the statusbar</param>
        public static void InformUser(String message, params object[] values)
        {
            EventAggregator.GetEvent<StatusBarUpdateEvent>().Publish(String.Format(message, values));
        }

        // Thanks to atom0s for assembly embedding code!

        /// <summary>
        /// Assembly resolve event callback to load embedded libraries.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private static Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var dllName = args.Name.Contains(",") ? args.Name.Substring(0, args.Name.IndexOf(",", System.StringComparison.InvariantCultureIgnoreCase)) : args.Name.Replace(".dll", "");
            if (dllName.ToLower().EndsWith(".resources"))
                return null;

            var fullName = string.Format("EasyFarm.Embedded.{0}.dll", new AssemblyName(args.Name).Name);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fullName))
            {
                if (stream == null)
                    return null;

                var data = new byte[stream.Length];
                stream.Read(data, 0, (int)stream.Length);
                return Assembly.Load(data);
            }
        }
    }
}
