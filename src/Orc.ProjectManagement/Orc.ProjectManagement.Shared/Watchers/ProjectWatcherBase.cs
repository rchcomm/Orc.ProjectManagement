﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ProjectWatcherBase.cs" company="Wild Gums">
//   Copyright (c) 2008 - 2015 Wild Gums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.ProjectManagement
{
    using System;
    using System.Threading.Tasks;
    using Catel;
    using Catel.Data;

    public abstract class ProjectWatcherBase
    {
        #region Fields
        private readonly IProjectManager _projectManager;
        #endregion

        #region Constructors
        protected ProjectWatcherBase(IProjectManager projectManager)
        {
            Argument.IsNotNull(() => projectManager);

            _projectManager = projectManager;

            projectManager.ProjectLocationLoading += OnProjectLocationLoading;
            projectManager.ProjectLocationLoadingFailed += OnProjectLocationLoadingFailed;
            projectManager.ProjectLocationLoadingCanceled += OnProjectLocationLoadingCanceled;
            projectManager.ProjectLoaded += OnProjectLoaded;

            projectManager.ProjectSaving += OnProjectSaving;
            projectManager.ProjectSavingCanceled += OnProjectSavingCanceled;
            projectManager.ProjectSavingFailed += OnProjectSavingFailed;
            projectManager.ProjectSaved += OnProjectSaved;

            projectManager.ProjectClosing += OnProjectClosing;
            projectManager.ProjectClosingCanceled += OnProjectClosingCanceled;
            projectManager.ProjectClosed += OnProjectClosed;

            projectManager.ProjectUpdated += OnProjectUpdated;

            projectManager.ProjectRefreshRequired += OnProjectRefreshRequired;

            projectManager.ActiveProjectChanging += OnActiveProjectChanging;
            projectManager.ActiveProjectChangingCanceled += OnActiveProjectChangingCanceled;
            projectManager.ActiveProjectChangingFailed += OnActiveProjectChangingFailed;
            projectManager.ActiveProjectChanged += OnActiveProjectChanged;
        }
        #endregion

        #region Properties
        protected IProjectManager ProjectManager
        {
            get { return _projectManager; }
        }
        #endregion

        #region Methods
        [ObsoleteEx(ReplacementTypeOrMember = "OnLoading(ProjectLocationCancelEventArgs e)", RemoveInVersion = "1.1.0", TreatAsErrorFromVersion = "1.0.0")]
        protected virtual async Task OnLoading(ProjectCancelEventArgs e)
        {
        }

        protected virtual async Task OnLoading(ProjectLocationCancelEventArgs e)
        {
        }

        protected virtual async Task OnLoadingFailed(string location, Exception exception, IValidationContext validationContext)
        {
        }

        protected virtual async Task OnLoadingCanceled(string location)
        {
        }

        protected virtual async Task OnLoaded(IProject project)
        {
        }

        protected virtual async Task OnSaving(ProjectCancelEventArgs e)
        {
        }

        protected virtual async Task OnSavingCanceled(IProject project)
        {
        }

        protected virtual async Task OnSavingFailed(IProject project, Exception exception)
        {
        }

        protected virtual async Task OnSaved(IProject project)
        {
        }

        protected virtual async Task OnClosing(ProjectCancelEventArgs e)
        {
        }

        protected virtual async Task OnClosed(IProject project)
        {
        }

        protected virtual async Task OnClosingCanceled(IProject project)
        {
        }

        protected virtual void OnUpdated(IProject oldProject, IProject newProject, bool isRefresh)
        {
        }

        protected virtual void OnProjectRefreshRequired()
        {
        }

        protected virtual async Task OnActiveProjectChanged(IProject oldProject, IProject newProject)
        {
        }

        protected virtual async Task OnActiveProjectChangingFailed(IProject project, Exception exception)
        {
        }

        protected virtual async Task OnActiveProjectChangingCanceled(IProject project)
        {
        }

        protected virtual async Task OnActiveProjectChanging(ProjectUpdatedCancelEventArgs e)
        {
        }

        private async Task OnProjectLocationLoading(object sender, ProjectLocationCancelEventArgs e)
        {
            await OnLoading(e);
        }

        private async Task OnProjectLocationLoadingFailed(object sender, ProjectLocationErrorEventArgs e)
        {
            await OnLoadingFailed(e.Location, e.Exception, e.ValidationContext);
        }

        private async Task OnProjectLocationLoadingCanceled(object sender, ProjectLocationEventArgs e)
        {
            await OnLoadingCanceled(e.Location);
        }

        private async Task OnProjectLoaded(object sender, ProjectEventArgs e)
        {
            await OnLoaded(e.Project);
        }

        private async Task OnProjectSaving(object sender, ProjectCancelEventArgs e)
        {
            await OnSaving(e);
        }

        private async Task OnProjectSavingCanceled(object sender, ProjectEventArgs e)
        {
            await OnSavingCanceled(e.Project);
        }

        private async Task OnProjectSavingFailed(object sender, ProjectErrorEventArgs e)
        {
            await OnSavingFailed(e.Project, e.Exception);
        }

        private async Task OnProjectSaved(object sender, ProjectEventArgs e)
        {
            await OnSaved(e.Project);
        }

        private async Task OnProjectClosing(object sender, ProjectCancelEventArgs e)
        {
            await OnClosing(e);
        }

        private async Task OnProjectClosingCanceled(object sender, ProjectEventArgs e)
        {
            await OnClosingCanceled(e.Project);
        }

        private async Task OnProjectClosed(object sender, ProjectEventArgs e)
        {
            await OnClosed(e.Project);
        }

        private void OnProjectUpdated(object sender, ProjectUpdatedEventArgs e)
        {
            OnUpdated(e.OldProject, e.NewProject, e.IsRefresh);
        }

        private void OnProjectRefreshRequired(object sender, EventArgs e)
        {
            OnProjectRefreshRequired();
        }

        private async Task OnActiveProjectChanged(object sender, ProjectUpdatedEventArgs e)
        {
            await OnActiveProjectChanged(e.OldProject, e.NewProject);
        }

        private async Task OnActiveProjectChangingFailed(object sender, ProjectErrorEventArgs e)
        {
            await OnActiveProjectChangingFailed(e.Project, e.Exception);
        }

        private async Task OnActiveProjectChangingCanceled(object sender, ProjectEventArgs e)
        {
            await OnActiveProjectChangingCanceled(e.Project);
        }

        private async Task OnActiveProjectChanging(object sender, ProjectUpdatedCancelEventArgs e)
        {
            await OnActiveProjectChanging(e);
        }
        #endregion
    }
}