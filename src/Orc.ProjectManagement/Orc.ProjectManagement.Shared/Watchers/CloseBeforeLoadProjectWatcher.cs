﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CloseBeforeLoadProjectWatcher.cs" company="Wild Gums">
//   Copyright (c) 2008 - 2015 Wild Gums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.ProjectManagement
{
    using System.Threading.Tasks;

    public class CloseBeforeLoadProjectWatcher : ProjectWatcherBase
    {
        #region Fields
        private readonly IProjectManager _projectManager;
        #endregion

        #region Constructors
        public CloseBeforeLoadProjectWatcher(IProjectManager projectManager)
            : base(projectManager)
        {
            _projectManager = projectManager;
        }
        #endregion

        protected override async Task OnLoadingAsync(ProjectCancelEventArgs e)
        {
            if (e.Cancel || _projectManager.ActiveProject == null)
            {
                await base.OnLoadingAsync(e).ConfigureAwait(false);
                return;
            }

            e.Cancel = !await _projectManager.CloseAsync().ConfigureAwait(false);

            await base.OnLoadingAsync(e).ConfigureAwait(false);
        }
    }
}